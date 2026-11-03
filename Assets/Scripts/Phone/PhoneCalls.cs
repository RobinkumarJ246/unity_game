using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ContentHouse.UI;

namespace ContentHouse.Phone
{
    /// <summary>
    /// Phone features added for the Day-1 evening: sending a text (pick a reply, dialogue-style),
    /// the contacts app and phone calls that play a conversation from dialogues.txt on the call screen.
    /// Same class as PhoneController.cs (partial).
    /// </summary>
    public partial class PhoneController
    {
        // ------------------------------------------------------------------ state
        RectTransform contactsScreen, callScreen;

        // texting
        Image composeBar;
        string[] pendingOptions;
        System.Action<string> pendingSent;
        public bool HasPendingText => pendingOptions != null;
        bool messageBuzzedThisFrame => lastBuzzFrame == Time.frameCount;
        int lastBuzzFrame = -1;

        // calls
        public bool InCall { get; private set; }
        /// <summary>Raised when the player taps a contact in the Phone app (argument = contact name).</summary>
        public event System.Action<string> CallRequested;
        Text callName, callStatus, callSpeaker, callLine, callHint, callAvatarLetter;
        Image callAvatar;
        RectTransform callChoices;
        readonly List<Button> callButtons = new List<Button>();
        bool callAdvance, callTimerOn;
        float callLastAdvance, callTime;
        int callPick = -1;

        static readonly Color TheirColor = new Color(1f, 0.72f, 0.35f);
        static readonly Color MyColor = new Color(0.55f, 0.75f, 1f);

        // ------------------------------------------------------------------ thread scrolling

        /// <summary>Scroll the Messages thread by pixels (+ = towards older messages / up).</summary>
        public void ScrollMessages(float pixels)
        {
            if (messagesScroll == null || messagesScroll.content == null || messagesScroll.viewport == null) return;
            float range = messagesScroll.content.rect.height - messagesScroll.viewport.rect.height;
            if (range <= 1f) return;
            messagesScroll.velocity = Vector2.zero;
            messagesScroll.verticalNormalizedPosition = Mathf.Clamp01(messagesScroll.verticalNormalizedPosition + pixels / range);
        }

        /// <summary>0 = newest at the bottom, 1 = very first message at the top.</summary>
        public void ScrollMessagesTo(float normalized)
        {
            if (messagesScroll == null) return;
            messagesScroll.velocity = Vector2.zero;
            messagesScroll.verticalNormalizedPosition = Mathf.Clamp01(normalized);
        }

        /// <summary>Keys for the thread: Up/Down or W/S (hold), PageUp/PageDown, Home (first message), End (newest). Mouse wheel + drag are the ScrollRect's own.</summary>
        void UpdateMessageScrollKeys()
        {
            if (!IsOpen || currentApp != ThreadScreen || InCall || messagesScroll == null) return;
            float dt = Time.unscaledDeltaTime;
            float px = 0f;
            bool letters = !Typing;   // W/S are text while typing; the arrows still scroll
            if (Input.GetKey(KeyCode.UpArrow) || (letters && Input.GetKey(KeyCode.W))) px += 700f * dt;
            if (Input.GetKey(KeyCode.DownArrow) || (letters && Input.GetKey(KeyCode.S))) px -= 700f * dt;
            if (px != 0f) ScrollMessages(px);
            float page = messagesScroll.viewport != null ? messagesScroll.viewport.rect.height * 0.85f : 300f;
            if (Input.GetKeyDown(KeyCode.PageUp)) ScrollMessages(page);
            if (Input.GetKeyDown(KeyCode.PageDown)) ScrollMessages(-page);
            if (Input.GetKeyDown(KeyCode.Home)) ScrollMessagesTo(1f);
            if (Input.GetKeyDown(KeyCode.End)) ScrollMessagesTo(0f);
        }

        // ------------------------------------------------------------------ texting

        /// <summary>First "line:" of a dialogues.txt section (used for scripted incoming texts).</summary>
        public string FirstLine(string convoId)
        {
            var dlg = DialogueScreen.Instance;
            List<string[]> steps;
            if (dlg != null && dlg.TryGetSteps(convoId, out steps))
                foreach (var s in steps) if (s[0] == "line") return s[1];
            Debug.LogWarning("[Phone] no line in dialogues.txt section '" + convoId + "'");
            return "...";
        }

        /// <summary>
        /// Ask the player to send a text to a contact (default Mr. IG). The reply options are the first
        /// "choice:" of the dialogues.txt section. The phone buzzes and shows an unread badge; the options
        /// appear in THAT contact's thread. onSent receives the text the player picked.
        /// </summary>
        public void OfferText(string convoId, System.Action<string> onSent, string contactName = null)
        {
            var dlg = DialogueScreen.Instance;
            List<string[]> steps;
            if (dlg == null || !dlg.TryGetSteps(convoId, out steps)) { Debug.LogWarning("[Phone] missing text section " + convoId); return; }
            string[] choice = null;
            foreach (var s in steps) if (s[0] == "choice") { choice = s; break; }
            if (choice == null) { Debug.LogWarning("[Phone] text section has no choice: " + convoId); return; }
            int n = int.Parse(choice[1]);
            pendingOptions = new string[n];
            for (int i = 0; i < n; i++) pendingOptions[i] = choice[2 + i];
            pendingSent = onSent;
            pendingContact = GetOrCreateContact(contactName ?? Core.GameConfig.StreamerName);
            pendingContact.Activity = ++activityCounter;

            if (!IsViewing(pendingContact))
            {
                if (pendingContact.Unread == 0) pendingContact.Unread = 1;   // at least one: a pending reply, not an extra message
                UpdateBadge();
                if (!messageBuzzedThisFrame) Audio.ProceduralAudio.PlayPhoneBuzz(transform.position);
            }
            RefreshCompose();
            if (IsOpen && currentApp == 2) RefreshConversationList();
        }

        void EnsureComposeBar()
        {
            if (composeBar != null) return;
            composeBar = UIFactory.Round("ComposeBar", threadScreen, new Color(0.08f, 0.10f, 0.17f, 0.99f), 22);
            var rt = composeBar.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, TypeBarH + 6f);   // sits just above the typing bar
        }

        void RefreshCompose()
        {
            var msgView = threadScreen != null ? threadScreen.Find("MsgView") as RectTransform : null;
            // the reply picker only shows inside the thread of the contact it is for
            bool show = pendingOptions != null && currentApp == ThreadScreen && currentContact == pendingContact;
            if (!show)
            {
                if (composeBar != null) composeBar.gameObject.SetActive(false);
                if (msgView != null) msgView.offsetMin = new Vector2(0f, TypeBarH + 8f);
                return;
            }
            EnsureComposeBar();
            var bar = composeBar.rectTransform;
            for (int i = bar.childCount - 1; i >= 0; i--) Destroy(bar.GetChild(i).gameObject);
            composeBar.gameObject.SetActive(true);

            int n = pendingOptions.Length;
            float h = 40f + n * 74f + 6f;
            bar.sizeDelta = new Vector2(0f, h);
            if (msgView != null) msgView.offsetMin = new Vector2(0f, TypeBarH + h + 14f);

            var title = UIFactory.Text("ComposeTitle", bar, "Choose a reply  (tap, or press 1-" + n + ")", 19,
                TextAnchor.UpperLeft, Soft);
            Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(380, 26), new Vector2(16, -10));
            title.raycastTarget = false;

            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var b = UIFactory.Round("Reply" + i, bar, new Color(0.20f, 0.45f, 0.92f), 20);
                Anchor(b.rectTransform, new Vector2(0.5f, 1f), new Vector2(372, 66), new Vector2(0, -40 - i * 74));
                var lbl = UIFactory.Text("Label", b.rectTransform, (i + 1) + ".  " + pendingOptions[i], 21, TextAnchor.MiddleLeft, Color.white);
                UIFactory.Stretch(lbl.rectTransform);
                lbl.rectTransform.offsetMin = new Vector2(16, 0); lbl.rectTransform.offsetMax = new Vector2(-12, 0);
                lbl.raycastTarget = false;
                AddButton(b, () => PickText(idx));
            }
            StartCoroutine(ScrollToNewest());
        }

        /// <summary>1-3 pick a reply while a text is waiting and the Messages app is open.</summary>
        bool HandleComposeKeys()
        {
            if (pendingOptions == null || currentApp != ThreadScreen || currentContact != pendingContact || Typing) return false;
            for (int i = 0; i < pendingOptions.Length && i < 3; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) { PickText(i); return true; }
            return false;
        }

        void PickText(int i)
        {
            if (pendingOptions == null || i < 0 || i >= pendingOptions.Length) return;
            string text = pendingOptions[i];
            var cb = pendingSent;
            var to = pendingContact;
            pendingOptions = null; pendingSent = null; pendingContact = null;
            AddMessage(Core.GameConfig.PlayerName, text, false, true, to != null ? to.Name : Core.GameConfig.StreamerName);   // right-aligned bubble
            Audio.ProceduralAudio.PlayClick(transform.position);
            RefreshCompose();
            StartCoroutine(AfterSent(text, cb));
        }

        IEnumerator AfterSent(string text, System.Action<string> cb)
        {
            yield return new WaitForSeconds(0.5f);   // let the bubble land before the story reacts
            if (cb != null) cb(text);
        }

        // ------------------------------------------------------------------ toast (small notice inside the phone)

        CanvasGroup toastGroup;
        Text toastText;
        float toastUntil;

        void BuildToast()
        {
            var bg = UIFactory.Round("Toast", panel, new Color(0.02f, 0.02f, 0.05f, 0.94f), 20);
            Anchor(bg.rectTransform, new Vector2(0.5f, 0f), new Vector2(400, 64), new Vector2(0, 74));
            bg.raycastTarget = false;
            toastGroup = bg.gameObject.AddComponent<CanvasGroup>();
            toastGroup.alpha = 0f; toastGroup.blocksRaycasts = false; toastGroup.interactable = false;
            toastText = UIFactory.Text("ToastText", bg.rectTransform, "", 21, TextAnchor.MiddleCenter, Color.white);
            UIFactory.Stretch(toastText.rectTransform);
            toastText.rectTransform.offsetMin = new Vector2(14, 4); toastText.rectTransform.offsetMax = new Vector2(-14, -4);
            toastText.raycastTarget = false;
        }

        /// <summary>Short notice shown over the bottom of the phone screen.</summary>
        public void ShowToast(string text, float seconds = 2.8f)
        {
            if (toastGroup == null) return;
            toastText.text = text;
            toastUntil = Time.unscaledTime + seconds;
        }

        void UpdateToast()
        {
            if (toastGroup == null) return;
            float target = Time.unscaledTime < toastUntil ? 1f : 0f;
            toastGroup.alpha = Mathf.MoveTowards(toastGroup.alpha, target, Time.unscaledDeltaTime * 6f);
        }

        // ------------------------------------------------------------------ contacts (Phone app)

        void BuildContacts()
        {
            BuildToast();
            // one row per callable contact from the shared registry (see PhoneMessages.cs)
            int row = 0;
            foreach (var c in contacts)
                if (c.Callable) AddContactRow(row++, c.Name, c.Sub, c.Color);
        }

        void AddContactRow(int index, string name, string sub, Color col)
        {
            var row = UIFactory.Round("Row_" + name, contactsScreen, new Color(1f, 1f, 1f, 0.08f), 26);
            Anchor(row.rectTransform, new Vector2(0.5f, 1f), new Vector2(402, 104), new Vector2(0, -10 - index * 116));

            var av = UIFactory.Circle("Avatar", row.rectTransform, col, 70);
            Anchor(av.rectTransform, new Vector2(0f, 0.5f), new Vector2(70, 70), new Vector2(16, 0));
            av.raycastTarget = false;
            var letter = UIFactory.Text("Letter", av.rectTransform, name.Substring(0, 1), 34, TextAnchor.MiddleCenter, Color.white);
            UIFactory.Stretch(letter.rectTransform); letter.fontStyle = FontStyle.Bold; letter.raycastTarget = false;

            var nameT = UIFactory.Text("Name", row.rectTransform, name, 28, TextAnchor.MiddleLeft, Color.white);
            Anchor(nameT.rectTransform, new Vector2(0f, 0.5f), new Vector2(230, 36), new Vector2(102, 14));
            nameT.fontStyle = FontStyle.Bold; nameT.raycastTarget = false;
            var subT = UIFactory.Text("Sub", row.rectTransform, sub, 19, TextAnchor.MiddleLeft, Soft);
            Anchor(subT.rectTransform, new Vector2(0f, 0.5f), new Vector2(240, 28), new Vector2(102, -20));
            subT.raycastTarget = false;

            var call = UIFactory.Circle("CallBtn", row.rectTransform, new Color(0.20f, 0.72f, 0.42f), 58);
            Anchor(call.rectTransform, new Vector2(1f, 0.5f), new Vector2(58, 58), new Vector2(-16, 0));
            call.raycastTarget = false;
            var cl = UIFactory.Text("CallLabel", call.rectTransform, "Call", 18, TextAnchor.MiddleCenter, Color.white);
            UIFactory.Stretch(cl.rectTransform); cl.fontStyle = FontStyle.Bold; cl.raycastTarget = false;

            AddButton(row, () => { if (CallRequested != null) CallRequested(name); });
        }

        // ------------------------------------------------------------------ call screen

        void BuildCall()
        {
            callAvatar = UIFactory.Circle("Avatar", callScreen, new Color(0.88f, 0.42f, 0.25f), 120);
            Anchor(callAvatar.rectTransform, new Vector2(0.5f, 1f), new Vector2(120, 120), new Vector2(0, -6));
            callAvatar.raycastTarget = false;
            callAvatarLetter = UIFactory.Text("Letter", callAvatar.rectTransform, "M", 56, TextAnchor.MiddleCenter, Color.white);
            UIFactory.Stretch(callAvatarLetter.rectTransform); callAvatarLetter.fontStyle = FontStyle.Bold; callAvatarLetter.raycastTarget = false;

            callName = UIFactory.Text("CallName", callScreen, "", 36, TextAnchor.MiddleCenter, Color.white);
            Anchor(callName.rectTransform, new Vector2(0.5f, 1f), new Vector2(380, 44), new Vector2(0, -136));
            callName.fontStyle = FontStyle.Bold; callName.raycastTarget = false;
            callStatus = UIFactory.Text("CallStatus", callScreen, "", 22, TextAnchor.MiddleCenter, Soft);
            Anchor(callStatus.rectTransform, new Vector2(0.5f, 1f), new Vector2(380, 30), new Vector2(0, -184));
            callStatus.raycastTarget = false;

            callSpeaker = UIFactory.Text("CallSpeaker", callScreen, "", 20, TextAnchor.UpperLeft, TheirColor);
            Anchor(callSpeaker.rectTransform, new Vector2(0f, 1f), new Vector2(400, 26), new Vector2(6, -236));
            callSpeaker.fontStyle = FontStyle.Bold; callSpeaker.raycastTarget = false;
            callLine = UIFactory.Text("CallLine", callScreen, "", 26, TextAnchor.UpperLeft, Color.white);
            Anchor(callLine.rectTransform, new Vector2(0f, 1f), new Vector2(398, 250), new Vector2(6, -266));
            callLine.raycastTarget = false;

            callChoices = UIFactory.NewRect("CallChoices", callScreen);
            Anchor(callChoices, new Vector2(0.5f, 1f), new Vector2(402, 340), new Vector2(0, -268));

            callHint = UIFactory.Text("CallHint", callScreen, "", 19, TextAnchor.LowerCenter, Soft);
            Anchor(callHint.rectTransform, new Vector2(0.5f, 0f), new Vector2(380, 28), new Vector2(0, 6));
            callHint.raycastTarget = false;
        }

        void ClearCallChoices()
        {
            foreach (var b in callButtons) if (b != null) Destroy(b.gameObject);
            callButtons.Clear();
        }

        /// <summary>Per-frame call input: advance keys + the call timer.</summary>
        void UpdateCallInput()
        {
            UpdateToast();
            if (!InCall) return;
            if (callTimerOn)
            {
                callTime += Time.deltaTime;
                int s = (int)callTime;
                callStatus.text = (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
            }
            bool pressed = Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)
                        || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.E)
                        || Input.GetMouseButtonDown(0);
            if (pressed && Time.unscaledTime >= callLastAdvance + 0.15f)
            {
                callAdvance = true;
                callLastAdvance = Time.unscaledTime;
            }
        }

        /// <summary>
        /// Place a call: rings, connects, then plays the dialogues.txt section on the call screen
        /// (lines type out, choices + reactions work like the in-world dialogue; nothing auto-advances).
        /// onEnded fires after hang-up.
        /// </summary>
        public void StartCall(string contact, string convoId, System.Action onEnded)
        {
            if (InCall) return;
            StartCoroutine(CallRoutine(contact, convoId, onEnded));
        }

        IEnumerator CallRoutine(string contact, string convoId, System.Action onEnded)
        {
            var dlg = DialogueScreen.Instance;
            List<string[]> steps = null;
            if (dlg == null || !dlg.TryGetSteps(convoId, out steps))
            {
                Debug.LogWarning("[Phone] no call script '" + convoId + "'");
                if (onEnded != null) onEnded();
                yield break;
            }

            InCall = true;
            if (!IsOpen) SetOpen(true);
            ShowApp(5);
            callName.text = contact;
            callAvatarLetter.text = contact.Substring(0, 1);
            callAvatar.color = contact == "Manager" ? new Color(0.88f, 0.42f, 0.25f) : Blue;
            callLine.text = ""; callSpeaker.text = ""; callHint.text = "";
            ClearCallChoices();
            callTimerOn = false; callTime = 0f;

            // ringing
            for (int r = 0; r < 3; r++)
            {
                callStatus.text = "calling" + new string('.', r + 1);
                Audio.ProceduralAudio.PlayPhoneBuzz(transform.position);
                yield return new WaitForSeconds(1.1f);
            }
            callStatus.text = "00:00"; callTimerOn = true;
            Audio.ProceduralAudio.PlayClick(transform.position);
            yield return new WaitForSeconds(0.4f);

            foreach (var step in steps)
            {
                if (step[0] == "line")
                {
                    yield return StartCoroutine(CallShowLine(contact, TheirColor, step[1]));
                }
                else if (step[0] == "choice")
                {
                    int n = int.Parse(step[1]);
                    callLine.text = "";
                    callSpeaker.text = "YOUR REPLY  (tap, or press 1-" + n + ")";
                    callSpeaker.color = MyColor;
                    callHint.text = "";
                    ClearCallChoices();
                    for (int c = 0; c < n; c++)
                    {
                        int idx = c;
                        var b = UIFactory.Round("Opt" + c, callChoices, new Color(0.20f, 0.45f, 0.92f), 22);
                        Anchor(b.rectTransform, new Vector2(0.5f, 1f), new Vector2(402, 96), new Vector2(0, -c * 106));
                        var lbl = UIFactory.Text("Label", b.rectTransform, (c + 1) + ".  " + step[2 + c], 22, TextAnchor.MiddleLeft, Color.white);
                        UIFactory.Stretch(lbl.rectTransform);
                        lbl.rectTransform.offsetMin = new Vector2(16, 0); lbl.rectTransform.offsetMax = new Vector2(-12, 0);
                        lbl.raycastTarget = false;
                        var btn = AddButton(b, () => { callPick = idx; });
                        callButtons.Add(btn);
                    }
                    callPick = -1;
                    while (callPick < 0)   // never auto-picks
                    {
                        for (int c = 0; c < n && c < 3; c++)
                            if (Input.GetKeyDown(KeyCode.Alpha1 + c) || Input.GetKeyDown(KeyCode.Keypad1 + c)) callPick = c;
                        yield return null;
                    }
                    int pick = callPick; callPick = -1;
                    callAdvance = false; callLastAdvance = Time.unscaledTime;   // the pick must not also skip the reply
                    ClearCallChoices();

                    yield return StartCoroutine(CallShowLine(Core.GameConfig.PlayerName, MyColor, step[2 + pick]));
                    string react = step[2 + n + pick];
                    if (!string.IsNullOrEmpty(react))
                        yield return StartCoroutine(CallShowLine(contact, TheirColor, react));
                }
            }

            // hang up
            callTimerOn = false;
            int total = (int)callTime;
            AddNote("Call with " + contact + "  " + (total / 60).ToString("00") + ":" + (total % 60).ToString("00"), contact);
            callStatus.text = "call ended";
            callLine.text = ""; callSpeaker.text = ""; callHint.text = "";
            Audio.ProceduralAudio.PlayClick(transform.position);
            yield return new WaitForSeconds(1.2f);
            InCall = false;
            callAdvance = false;
            ShowApp(0);
            if (onEnded != null) onEnded();
        }

        IEnumerator CallShowLine(string who, Color color, string text)
        {
            callSpeaker.text = who.ToUpperInvariant();
            callSpeaker.color = color;
            callHint.text = "";
            int shown = 0; float acc = 0f;
            callAdvance = false;
            while (shown < text.Length)
            {
                if (TakeCallAdvance()) break;
                acc += Time.unscaledDeltaTime * 60f;
                int n = (int)acc; acc -= n;
                shown = Mathf.Min(text.Length, shown + n);
                callLine.text = text.Substring(0, shown);
                yield return null;
            }
            callLine.text = text;
            callHint.text = "[Space] / click  to continue";
            while (!TakeCallAdvance()) yield return null;   // waits for the player, however long
            callHint.text = "";
        }

        bool TakeCallAdvance()
        {
            if (!callAdvance) return false;
            callAdvance = false;
            return true;
        }
    }
}
