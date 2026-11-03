using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using ContentHouse.UI;

namespace ContentHouse.Phone
{
    /// <summary>
    /// Free typing at the bottom of every thread: type, press Enter or Send. The text goes to whoever's
    /// thread is open. Nobody answers - except for the hidden phrases in Resources/text_replies.txt
    /// (see PhoneTextReplies; a rule's "to:" is the contact's Key).
    /// Same class as PhoneController.cs (partial).
    /// </summary>
    public partial class PhoneController
    {
        const float TypeBarH = 76f;

        Image typeBar;
        InputField typeField;
        PhoneTextReplies secretReplies;
        float nextSendAllowed;

        /// <summary>True while the player is typing in the message box (phone shortcuts must stand down).</summary>
        public bool Typing => typeField != null && typeField.isFocused;

        void BuildTypeBar()
        {
            secretReplies = new PhoneTextReplies();

            typeBar = UIFactory.Round("TypeBar", threadScreen, new Color(0.08f, 0.10f, 0.17f, 0.99f), 22);
            var rt = typeBar.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, TypeBarH);
            rt.anchoredPosition = Vector2.zero;

            typeField = UIFactory.InputField("TypeField", rt, "Type a message...");
            var fr = typeField.GetComponent<RectTransform>();
            fr.anchorMin = new Vector2(0f, 0f); fr.anchorMax = new Vector2(1f, 1f);
            fr.offsetMin = new Vector2(10, 10); fr.offsetMax = new Vector2(-104, -10);
            typeField.characterLimit = 90;
            typeField.lineType = InputField.LineType.SingleLine;
            typeField.textComponent.fontSize = 22;
            var ph = typeField.placeholder as Text; if (ph != null) ph.fontSize = 20;
            var fimg = typeField.GetComponent<Image>();
            fimg.sprite = UIFactory.Rounded(18); fimg.type = Image.Type.Sliced;

            var send = UIFactory.Round("SendBtn", rt, new Color(0.20f, 0.45f, 0.92f), 20);
            Anchor(send.rectTransform, new Vector2(1f, 0.5f), new Vector2(86, 56), new Vector2(-10, 0));
            var sl = UIFactory.Text("Label", send.rectTransform, "Send", 22, TextAnchor.MiddleCenter, Color.white);
            UIFactory.Stretch(sl.rectTransform); sl.fontStyle = FontStyle.Bold; sl.raycastTarget = false;
            AddButton(send, SendTyped);

            // the thread sits above the type bar
            var msgView = threadScreen.Find("MsgView") as RectTransform;
            if (msgView != null) msgView.offsetMin = new Vector2(0f, TypeBarH + 8f);
        }

        /// <summary>Placeholder names the person you're texting.</summary>
        void RefreshTypePlaceholder()
        {
            if (typeField == null) return;
            var ph = typeField.placeholder as Text;
            if (ph != null) ph.text = currentContact != null ? "Message " + currentContact.Name + "..." : "Type a message...";
            typeField.text = "";
        }

        /// <summary>Drop keyboard focus (so letters go back to being plain keys).</summary>
        void Defocus()
        {
            if (typeField != null && typeField.isFocused && UnityEngine.EventSystems.EventSystem.current != null)
                UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(null);
        }

        /// <summary>Per-frame: Enter sends, Esc leaves the box (a second Esc goes back as usual).</summary>
        void UpdateTyping()
        {
            if (typeField == null || !typeField.isFocused) return;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) SendTyped();
            else if (Input.GetKeyDown(KeyCode.Escape)) Defocus();
        }

        void SendTyped()
        {
            if (typeField == null || currentContact == null) return;
            string text = typeField.text.Trim();
            if (text.Length == 0) return;
            if (Time.unscaledTime < nextSendAllowed) return;      // no hammering Enter
            nextSendAllowed = Time.unscaledTime + 0.6f;

            typeField.text = "";
            typeField.ActivateInputField();
            var to = currentContact;

            AddMessage(Core.GameConfig.PlayerName, text, false, true, to.Name);
            Audio.ProceduralAudio.PlayClick(transform.position);

            // Secret replies only: everything else is simply left on read.
            string reply; float delay;
            if (secretReplies != null && secretReplies.TryReply(to.Key, text, Time.unscaledTime, out reply, out delay))
                StartCoroutine(SecretReply(to.Name, reply, delay));
        }

        IEnumerator SecretReply(string from, string text, float delay)
        {
            yield return new WaitForSeconds(delay);
            AddMessage(from, text);   // goes to that contact's thread; buzz + badge if you're not looking at it
        }
    }
}
