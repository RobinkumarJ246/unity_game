using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ContentHouse.UI;

namespace ContentHouse.Phone
{
    /// <summary>
    /// Contacts + per-contact message threads. The Messages app shows a LIST of conversations; tapping one
    /// opens that contact's own thread (history, unread count, story reply picker, typing box).
    /// To add a person: one RegisterContact(...) call (or just send/receive a message from a new name -
    /// unknown senders such as "UNKNOWN" get a conversation automatically).
    /// Same class as PhoneController.cs (partial).
    /// </summary>
    public partial class PhoneController
    {
        public const int ThreadScreen = 6;

        public class Contact
        {
            public string Key, Name, Sub;
            public Color Color;
            public bool Callable;                                   // appears in the Phone app
            public readonly List<Message> Thread = new List<Message>();
            public int Unread;
            public int Activity;                                    // bumped on every message: sorts the list
        }

        readonly List<Contact> contacts = new List<Contact>();
        Contact currentContact;       // the thread being viewed
        Contact pendingContact;       // whose thread has a story reply waiting
        int activityCounter;
        RectTransform threadScreen, listContent;
        ScrollRect listScroll;
        readonly List<Contact> listOrder = new List<Contact>();     // what the list currently shows (for 1-9 keys)

        // ------------------------------------------------------------------ registry

        void RegisterDefaultContacts()
        {
            if (contacts.Count > 0) return;
            RegisterContact("ig", Core.GameConfig.StreamerName, "Streaming - do not disturb", Blue, true);
            RegisterContact("manager", "Manager", "Work", new Color(0.88f, 0.42f, 0.25f), true);
        }

        /// <summary>Add (or fetch) a contact. Key is what text_replies.txt rules refer to ("to: ig").</summary>
        public Contact RegisterContact(string key, string name, string sub, Color color, bool callable = false)
        {
            var existing = FindContact(key);
            if (existing != null) return existing;
            var c = new Contact { Key = key, Name = name, Sub = sub, Color = color, Callable = callable };
            contacts.Add(c);
            return c;
        }

        /// <summary>Find by display name or key (case-insensitive); null if unknown.</summary>
        public Contact FindContact(string nameOrKey)
        {
            if (string.IsNullOrEmpty(nameOrKey)) return null;
            foreach (var c in contacts)
                if (string.Equals(c.Name, nameOrKey, System.StringComparison.OrdinalIgnoreCase)
                    || string.Equals(c.Key, nameOrKey, System.StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        /// <summary>Unknown senders (e.g. the creepy "UNKNOWN" texts) get a conversation of their own.</summary>
        Contact GetOrCreateContact(string name)
        {
            var c = FindContact(name);
            if (c != null) return c;
            string key = (name ?? "?").Trim().ToLowerInvariant().Replace(' ', '_');
            return RegisterContact(key, name, "", new Color(0.55f, 0.58f, 0.66f));
        }

        int TotalUnread()
        {
            int n = 0;
            foreach (var c in contacts) n += c.Unread;
            return n;
        }

        // ------------------------------------------------------------------ adding messages

        /// <summary>
        /// Incoming: goes to the sender's thread. Mine=true: goes to the "to" contact's thread
        /// (default Mr. IG). Buzz + unread only if you aren't looking at that thread.
        /// </summary>
        public void AddMessage(string sender, string body, bool urgent = false, bool mine = false, string to = null)
        {
            var c = GetOrCreateContact(mine ? (to ?? Core.GameConfig.StreamerName) : sender);
            c.Thread.Add(new Message(sender, body, urgent, ClockString(), mine) { To = to });
            c.Activity = ++activityCounter;

            bool viewing = IsViewing(c);
            if (viewing) RenderMessages();
            if (!viewing && !mine)
            {
                c.Unread++;
                lastBuzzFrame = Time.frameCount;   // OfferText right after this must not buzz a second time
                Audio.ProceduralAudio.PlayPhoneBuzz(transform.position);
            }
            UpdateBadge();
            if (IsOpen && currentApp == 2) RefreshConversationList();
        }

        /// <summary>Centred log line in a contact's thread (e.g. a call record). No buzz, no unread.</summary>
        public void AddNote(string text, string contactName)
        {
            var c = GetOrCreateContact(contactName);
            c.Thread.Add(new Message("", text, false, ClockString()) { Note = true });
            c.Activity = ++activityCounter;
            if (IsViewing(c)) RenderMessages();
            if (IsOpen && currentApp == 2) RefreshConversationList();
        }

        bool IsViewing(Contact c) => IsOpen && currentApp == ThreadScreen && currentContact == c;

        // ------------------------------------------------------------------ navigation

        public void OpenThread(Contact c)
        {
            if (c == null) return;
            currentContact = c;
            c.Unread = 0;
            UpdateBadge();
            ShowApp(ThreadScreen);
        }

        /// <summary>Thread -> conversation list -> home.</summary>
        void GoBack()
        {
            if (InCall) return;
            if (currentApp == ThreadScreen) ShowApp(2);
            else if (currentApp != 0) ShowApp(0);
        }

        /// <summary>Called by ShowApp when the thread screen appears.</summary>
        void OnThreadShown()
        {
            RenderMessages();
            StartCoroutine(ScrollToNewest());
            RefreshCompose();
            RefreshTypePlaceholder();
        }

        // ------------------------------------------------------------------ conversation list

        void BuildConversationList()
        {
            var viewR = UIFactory.NewRect("ListView", messagesScreen);
            UIFactory.Stretch(viewR);
            listScroll = viewR.gameObject.AddComponent<ScrollRect>();
            var vp = UIFactory.NewRect("ListViewport", viewR);
            UIFactory.Stretch(vp);
            vp.gameObject.AddComponent<RectMask2D>();
            listScroll.viewport = vp;
            listContent = UIFactory.NewRect("ListContent", vp);
            listContent.anchorMin = new Vector2(0, 1); listContent.anchorMax = new Vector2(1, 1);
            listContent.pivot = new Vector2(0.5f, 1f);
            listContent.sizeDelta = new Vector2(0, 10);
            var lay = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            lay.childControlHeight = true; lay.childForceExpandHeight = false;
            lay.childControlWidth = true; lay.childForceExpandWidth = true;
            lay.spacing = 10; lay.padding = new RectOffset(0, 0, 6, 10);
            listContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            listScroll.content = listContent;
            listScroll.horizontal = false; listScroll.vertical = true;
            listScroll.movementType = ScrollRect.MovementType.Clamped;
            listScroll.scrollSensitivity = 40; listScroll.inertia = true; listScroll.decelerationRate = 0.12f;
        }

        static string Preview(Contact c)
        {
            // newest real message; failing that, the newest log line (e.g. "Call with Manager  00:48")
            for (int pass = 0; pass < 2; pass++)
                for (int i = c.Thread.Count - 1; i >= 0; i--)
                {
                    var m = c.Thread[i];
                    if (m.Note != (pass == 1)) continue;
                    string t = (m.Mine ? "You: " : "") + m.Body.Replace('\n', ' ');
                    return t.Length > 27 ? t.Substring(0, 26) + "..." : t;   // short: the unread badge sits at the right
                }
            return "No messages yet";
        }

        static string LastStamp(Contact c)
        {
            for (int i = c.Thread.Count - 1; i >= 0; i--) return c.Thread[i].Stamp;   // newest item, message or log line
            return "";
        }

        void RefreshConversationList()
        {
            if (listContent == null) return;
            for (int i = listContent.childCount - 1; i >= 0; i--) Destroy(listContent.GetChild(i).gameObject);

            listOrder.Clear();
            listOrder.AddRange(contacts);
            // most recent conversation first; never-used ones keep registration order
            var order = new Dictionary<Contact, int>();
            for (int i = 0; i < contacts.Count; i++) order[contacts[i]] = i;
            listOrder.Sort((a, b) => a.Activity != b.Activity ? b.Activity.CompareTo(a.Activity) : order[a].CompareTo(order[b]));

            for (int i = 0; i < listOrder.Count; i++)
            {
                var c = listOrder[i];
                var row = UIFactory.Round("Conv_" + c.Key, listContent, new Color(1f, 1f, 1f, 0.08f), 24);
                row.gameObject.AddComponent<LayoutElement>().preferredHeight = 98;

                var av = UIFactory.Circle("Avatar", row.rectTransform, c.Color, 64);
                Anchor(av.rectTransform, new Vector2(0f, 0.5f), new Vector2(64, 64), new Vector2(14, 0));
                av.raycastTarget = false;
                var letter = UIFactory.Text("Letter", av.rectTransform, c.Name.Substring(0, 1).ToUpperInvariant(), 30, TextAnchor.MiddleCenter, Color.white);
                UIFactory.Stretch(letter.rectTransform); letter.fontStyle = FontStyle.Bold; letter.raycastTarget = false;

                var name = UIFactory.Text("Name", row.rectTransform, c.Name, 26, TextAnchor.MiddleLeft, Color.white);
                Anchor(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(200, 34), new Vector2(92, 15));
                name.fontStyle = FontStyle.Bold; name.raycastTarget = false;

                bool pending = HasPendingText && pendingContact == c;
                var prev = UIFactory.Text("Preview", row.rectTransform, pending ? "Tap to reply" : Preview(c), 19,
                    TextAnchor.MiddleLeft, pending ? Amber : (c.Unread > 0 ? Color.white : Soft));
                Anchor(prev.rectTransform, new Vector2(0f, 0.5f), new Vector2(250, 28), new Vector2(92, -18));
                prev.horizontalOverflow = HorizontalWrapMode.Overflow;
                prev.raycastTarget = false;

                var stamp = UIFactory.Text("Time", row.rectTransform, LastStamp(c), 16, TextAnchor.MiddleRight, new Color(1, 1, 1, 0.38f));
                Anchor(stamp.rectTransform, new Vector2(1f, 1f), new Vector2(110, 24), new Vector2(-14, -10));
                stamp.raycastTarget = false;

                if (c.Unread > 0)
                {
                    var dot = UIFactory.Circle("Unread", row.rectTransform, new Color(0.95f, 0.2f, 0.2f), 30);
                    Anchor(dot.rectTransform, new Vector2(1f, 0.5f), new Vector2(30, 30), new Vector2(-16, -14));
                    dot.raycastTarget = false;
                    var n = UIFactory.Text("N", dot.rectTransform, c.Unread.ToString(), 18, TextAnchor.MiddleCenter, Color.white);
                    UIFactory.Stretch(n.rectTransform); n.fontStyle = FontStyle.Bold; n.raycastTarget = false;
                }

                var contact = c;
                AddButton(row, () => OpenThread(contact));
            }
        }

        /// <summary>In the conversation list, keys 1-9 open that row.</summary>
        bool OpenConversationByDigit()
        {
            for (int i = 0; i < 9 && i < listOrder.Count; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    OpenThread(listOrder[i]);
                    return true;
                }
            return false;
        }
    }
}
