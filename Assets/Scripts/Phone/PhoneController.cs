using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ContentHouse.UI;   // UIFactory (code-built canvas)

namespace ContentHouse.Phone
{
    /// <summary>
    /// Interactive smartphone (spec: PhoneController.cs). [Tab] toggles.
    /// Apps: Flashlight, Messages, CCTV. Battery drains through Acts I-II and
    /// force-shuts-down at the Act II blackout.
    /// </summary>
    public class PhoneController : MonoBehaviour
    {
        public CCTV.CCTVCameraFeed HallCam;
        public CCTV.CCTVCameraFeed LivingCam;
        public Player.FlashlightController Flashlight;

        public float Battery = Core.GameConfig.PhoneBatteryStart;
        public bool ForcedShutdown { get; private set; }
        public bool BatteryAlive => Battery > 0.5f && !ForcedShutdown;
        public bool IsOpen { get; private set; }
        public float DrainMultiplier = 1f;

        RectTransform canvas;
        RectTransform panel, closedStrip;
        Text batteryText, clockText, appTitle;
        RectTransform homeScreen, flashlightScreen, messagesScreen, cctvScreen;
        RectTransform messagesContent;
        Text flashlightStateText;
        Image batteryFill;
        RawImage cctvImage;
        Text cctvLabel;
        Text badgeText;
        GameObject badge;

        int unread;
        int camIndex;
        float clockMinutes = 23 * 60 + 30;   // 11:30 PM

        public class Message
        {
            public string Sender, Body;
            public bool Urgent;
            public Message(string s, string b, bool u) { Sender = s; Body = b; Urgent = u; }
        }
        readonly List<Message> messages = new List<Message>();

        void Awake()
        {
            UI.UIFactory.EnsureEventSystem();   // phone apps need it too
            BuildUI();
        }

        void Update()
        {
            bool togglePressed = Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.P);
            if (togglePressed && !ForcedShutdown && BatteryAlive && !BlockedByOverlay())
            {
                Debug.Log("[Phone] toggle key pressed -> open=" + !IsOpen);
                SetOpen(!IsOpen);
            }
            else if (togglePressed && BlockedByOverlay())
            {
                Debug.Log("[Phone] toggle blocked (menu or narration on top)");
            }

            if (BatteryAlive)
            {
                float drain = Core.GameConfig.PhonePassiveDrainPerMinute / 60f * DrainMultiplier;
                if (Flashlight != null && Flashlight.IsOn) drain += Core.GameConfig.FlashlightDrainPerMinute / 60f;
                if (IsOpen && cctvScreen.gameObject.activeSelf) drain += 2f / 60f;
                Battery = Mathf.Max(0f, Battery - drain * Time.deltaTime);
                if (Battery <= 0f) Shutdown("WARNING: Battery 0% - Device Shutting Down...");
            }

            UpdateClockAndBatteryUI();
        }

        // ---------- public API ----------

        public void AddMessage(string sender, string body, bool urgent = false)
        {
            messages.Add(new Message(sender, body, urgent));
            RenderMessages();
            if (!IsOpen)
            {
                unread++;
                Audio.ProceduralAudio.PlayPhoneBuzz(transform.position);
                UpdateBadge();
            }
        }

        /// <summary>Act II: battery slams to 1% then powers off.</summary>
        public void ForceShutdown()
        {
            Battery = 1f;
            SetOpen(false);
            ForcedShutdown = true;
            if (Flashlight != null) Flashlight.ForceOff();
            Shutdown("WARNING: Battery 1% - Device Shutting Down...");
        }

        // ---------- internals ----------

        void Shutdown(string line)
        {
            ForcedShutdown = true;
            SetOpen(false);
            closedStrip.gameObject.SetActive(false);
            var gm = Core.GameManager.Instance;
            if (gm != null && gm.HUD != null)
                gm.HUD.QueueSubtitle("", line, 3f);
        }

        /// <summary>Phone is unusable while the menu or narration is on top.</summary>
        bool BlockedByOverlay()
        {
            var narr = UI.NarrationScreen.Instance;
            if (narr != null && narr.IsPlaying) return true;
            var gm = Core.GameManager.Instance;
            return gm != null && gm.Phase == Core.GameManager.GamePhase.Menu;
        }

        public void SetOpen(bool open)
        {
            // Guard: never operate on an unbuilt UI (e.g. edit-mode probing).
            if (homeScreen == null)
            {
                IsOpen = false;
                return;
            }
            IsOpen = open;
            if (panel != null) panel.gameObject.SetActive(open);
            if (closedStrip != null) closedStrip.gameObject.SetActive(!open);
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
            if (open) { unread = 0; UpdateBadge(); ShowApp(0); }
            SetFeedRendering();
        }

        void UpdateClockAndBatteryUI()
        {
            clockMinutes += Time.deltaTime * (60f / 25f);   // 1 game min per 25 real sec
            if (clockText != null)
            {
                float total = clockMinutes % (24 * 60);
                int h24 = Mathf.FloorToInt(total / 60f) % 24;
                int m = Mathf.FloorToInt(total % 60f);
                int h12 = h24 % 12; if (h12 == 0) h12 = 12;
                clockText.text = string.Format("{0}:{1:00} {2}", h12, m, h24 >= 12 ? "PM" : "AM");
            }
            if (batteryText != null)
                batteryText.text = ForcedShutdown ? "0%" : Mathf.CeilToInt(Mathf.Clamp(Battery, 0f, 100f)) + "%";
            if (batteryFill != null)
                batteryFill.fillAmount = Mathf.Clamp01(Battery / 100f);
        }

        void UpdateBadge()
        {
            if (badge == null) return;
            badge.SetActive(unread > 0);
            if (badgeText != null) badgeText.text = unread.ToString();
        }

        void SetFeedRendering()
        {
            bool viewing = IsOpen && cctvScreen.gameObject.activeSelf;
            if (HallCam != null) HallCam.SetViewing(viewing);
            if (LivingCam != null) LivingCam.SetViewing(viewing);
        }

        // ---------- UI construction ----------

        void BuildUI()
        {
            // Sorting 500: above menu/narration/HUD so the phone is never hidden.
            canvas = UIFactory.CanvasRoot("PhoneCanvas", 500);

            // Closed strip (bottom right)
            closedStrip = UIFactory.NewRect("ClosedStrip", canvas);
            closedStrip.anchorMin = closedStrip.anchorMax = new Vector2(1f, 0f);
            closedStrip.anchoredPosition = new Vector2(-190, 54);
            closedStrip.sizeDelta = new Vector2(300, 64);
            var stripBg = UIFactory.Image("StripBg", closedStrip, new Color(0, 0, 0, 0.55f));
            UIFactory.Stretch(stripBg.rectTransform);
            var stripText = UIFactory.Text("StripText", closedStrip, "[TAB]  Phone", 24, TextAnchor.MiddleCenter, Color.white);
            UIFactory.Stretch(stripText.rectTransform);
            var stripBat = UIFactory.Text("StripBat", closedStrip, "", 20, TextAnchor.UpperRight, new Color(1, 1, 1, 0.7f));
            UIFactory.Stretch(stripBat.rectTransform);
            batteryText = stripBat;
            // The closed strip is a passive indicator - never intercept clicks.
            stripBg.raycastTarget = false;
            stripText.raycastTarget = false;
            stripBat.raycastTarget = false;

            // Phone panel
            panel = UIFactory.NewRect("PhonePanel", canvas);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = new Vector2(260, 0);
            panel.sizeDelta = new Vector2(380, 740);
            var bg = UIFactory.Image("PhoneBg", panel, new Color(0.04f, 0.04f, 0.06f, 0.98f));
            UIFactory.Stretch(bg.rectTransform);

            // Status bar
            clockText = UIFactory.Text("Clock", panel, "11:30 PM", 22, TextAnchor.UpperLeft, Color.white);
            clockText.rectTransform.anchorMin = new Vector2(0, 1); clockText.rectTransform.anchorMax = new Vector2(0.5f, 1);
            clockText.rectTransform.anchoredPosition = new Vector2(18, -14);
            clockText.rectTransform.sizeDelta = new Vector2(160, 26);
            var batPct = UIFactory.Text("BatPct", panel, "78%", 22, TextAnchor.UpperRight, Color.white);
            batPct.rectTransform.anchorMin = new Vector2(0.5f, 1); batPct.rectTransform.anchorMax = new Vector2(1, 1);
            batPct.rectTransform.anchoredPosition = new Vector2(-18, -14);
            batPct.rectTransform.sizeDelta = new Vector2(160, 26);
            var batBarBg = UIFactory.Image("BatBarBg", panel, new Color(1, 1, 1, 0.15f));
            batBarBg.rectTransform.anchorMin = batBarBg.rectTransform.anchorMax = new Vector2(1, 1);
            batBarBg.rectTransform.pivot = new Vector2(1, 1);
            batBarBg.rectTransform.anchoredPosition = new Vector2(-110, -14);
            batBarBg.rectTransform.sizeDelta = new Vector2(70, 12);
            batteryFill = UIFactory.Image("BatFill", batBarBg.rectTransform, new Color(0.3f, 0.85f, 0.4f));
            UIFactory.Stretch(batteryFill.rectTransform);
            batteryFill.type = Image.Type.Filled;
            batteryFill.fillMethod = Image.FillMethod.Horizontal;

            appTitle = UIFactory.Text("AppTitle", panel, "HOME", 30, TextAnchor.UpperCenter, new Color(1, 1, 1, 0.85f));
            appTitle.rectTransform.anchorMin = new Vector2(0, 1); appTitle.rectTransform.anchorMax = new Vector2(1, 1);
            appTitle.rectTransform.anchoredPosition = new Vector2(0, -58);
            appTitle.rectTransform.sizeDelta = new Vector2(0, 34);

            homeScreen = UIFactory.NewRect("HomeScreen", panel);
            UIFactory.Stretch(homeScreen);
            homeScreen.offsetMin = new Vector2(18, 18);
            homeScreen.offsetMax = new Vector2(-18, -100);

            flashlightScreen = UIFactory.NewRect("FlashlightScreen", panel);
            UIFactory.Stretch(flashlightScreen);
            flashlightScreen.offsetMin = new Vector2(18, 18);
            flashlightScreen.offsetMax = new Vector2(-18, -100);

            messagesScreen = UIFactory.NewRect("MessagesScreen", panel);
            UIFactory.Stretch(messagesScreen);
            messagesScreen.offsetMin = new Vector2(18, 18);
            messagesScreen.offsetMax = new Vector2(-18, -100);

            cctvScreen = UIFactory.NewRect("CctvScreen", panel);
            UIFactory.Stretch(cctvScreen);
            cctvScreen.offsetMin = new Vector2(18, 18);
            cctvScreen.offsetMax = new Vector2(-18, -100);

            BuildHome();
            BuildFlashlight();
            BuildMessages();
            BuildCCTV();

            ShowApp(0);
            panel.gameObject.SetActive(false);
        }

        void BuildHome()
        {
            var fl = UIFactory.Button("AppFlashlight", homeScreen, "FLASHLIGHT", 26, new Color(0.15f, 0.15f, 0.2f));
            UIFactory.Stretch(fl.image.rectTransform);
            fl.image.rectTransform.anchorMin = new Vector2(0, 1); fl.image.rectTransform.anchorMax = new Vector2(1, 0.72f);
            fl.image.rectTransform.offsetMin = Vector2.zero; fl.image.rectTransform.offsetMax = Vector2.zero;
            fl.onClick.AddListener(() => ShowApp(1));

            var msg = UIFactory.Button("AppMessages", homeScreen, "MESSAGES", 26, new Color(0.15f, 0.15f, 0.2f));
            UIFactory.Stretch(msg.image.rectTransform);
            msg.image.rectTransform.anchorMin = new Vector2(0, 0.72f); msg.image.rectTransform.anchorMax = new Vector2(1, 0.44f);
            msg.image.rectTransform.offsetMin = Vector2.zero; msg.image.rectTransform.offsetMax = Vector2.zero;
            msg.onClick.AddListener(() => ShowApp(2));
            badge = UIFactory.Image("Badge", msg.image.rectTransform, new Color(0.85f, 0.15f, 0.15f)).gameObject;
            var badgeRect = badge.GetComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(1, 1);
            badgeRect.anchorMax = new Vector2(1, 1);
            badgeRect.sizeDelta = new Vector2(34, 34);
            badgeRect.anchoredPosition = new Vector2(-6, -6);
            badgeText = UIFactory.Text("BadgeText", badge.transform, "0", 20, TextAnchor.MiddleCenter, Color.white);
            UIFactory.Stretch(badgeText.rectTransform);
            badge.SetActive(false);

            var cam = UIFactory.Button("AppCCTV", homeScreen, "CCTV", 26, new Color(0.15f, 0.15f, 0.2f));
            UIFactory.Stretch(cam.image.rectTransform);
            cam.image.rectTransform.anchorMin = new Vector2(0, 0.44f); cam.image.rectTransform.anchorMax = new Vector2(1, 0.16f);
            cam.image.rectTransform.offsetMin = Vector2.zero; cam.image.rectTransform.offsetMax = Vector2.zero;
            cam.onClick.AddListener(() => ShowApp(3));
        }

        void BuildFlashlight()
        {
            flashlightStateText = UIFactory.Text("FLState", flashlightScreen, "OFF", 40, TextAnchor.MiddleCenter, new Color(1, 0.85f, 0.4f));
            UIFactory.Stretch(flashlightStateText.rectTransform);
            var btn = UIFactory.Button("FLToggle", flashlightScreen, "TOGGLE LIGHT", 26, new Color(0.2f, 0.18f, 0.1f));
            UIFactory.Stretch(btn.image.rectTransform);
            btn.image.rectTransform.anchorMin = new Vector2(0.25f, 0.25f); btn.image.rectTransform.anchorMax = new Vector2(0.75f, 0.45f);
            btn.image.rectTransform.offsetMin = Vector2.zero; btn.image.rectTransform.offsetMax = Vector2.zero;
            btn.onClick.AddListener(ToggleFlashlightApp);
            var note = UIFactory.Text("FLNote", flashlightScreen, "Drains battery fast.", 20, TextAnchor.LowerCenter, new Color(1, 1, 1, 0.5f));
            UIFactory.Stretch(note.rectTransform);
            note.rectTransform.offsetMin = new Vector2(0, 12); note.rectTransform.offsetMax = new Vector2(0, 40);
        }

        void BuildMessages()
        {
            var viewR = UIFactory.NewRect("MsgView", messagesScreen);
            UIFactory.Stretch(viewR);
            var view = viewR.gameObject.AddComponent<ScrollRect>();
            var vpR = UIFactory.NewRect("MsgViewport", viewR);
            UIFactory.Stretch(vpR);
            vpR.gameObject.AddComponent<RectMask2D>();
            view.viewport = vpR;
            var contentR = UIFactory.NewRect("MsgContent", vpR);
            contentR.anchorMin = new Vector2(0, 1); contentR.anchorMax = new Vector2(1, 1);
            contentR.pivot = new Vector2(0.5f, 1f);
            contentR.sizeDelta = new Vector2(0, 10);
            var layout = contentR.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.spacing = 10;
            layout.padding = new RectOffset(4, 4, 4, 4);
            var fitter = contentR.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            view.content = contentR;
            view.vertical = true;
            view.horizontal = false;
            view.scrollSensitivity = 30;
            view.elasticity = 0.1f;
            view.inertia = false;
            messagesContent = contentR;
        }

        void BuildCCTV()
        {
            cctvImage = UIFactory.RawImage("Feed", cctvScreen);
            UIFactory.Stretch(cctvImage.rectTransform);
            cctvImage.rectTransform.offsetMin = new Vector2(0, 60);
            cctvImage.color = Color.black;
            cctvLabel = UIFactory.Text("CctvLabel", cctvScreen, "", 22, TextAnchor.LowerCenter, new Color(0.6f, 1f, 0.6f));
            UIFactory.Stretch(cctvLabel.rectTransform);
            cctvLabel.rectTransform.offsetMin = new Vector2(0, 30); cctvLabel.rectTransform.offsetMax = new Vector2(0, 60);
            var swap = UIFactory.Button("SwapCam", cctvScreen, "SWITCH FEED", 22, new Color(0.12f, 0.16f, 0.12f));
            UIFactory.Stretch(swap.image.rectTransform);
            swap.image.rectTransform.anchorMin = new Vector2(0.3f, 0.02f); swap.image.rectTransform.anchorMax = new Vector2(0.7f, 0.13f);
            swap.image.rectTransform.offsetMin = Vector2.zero; swap.image.rectTransform.offsetMax = Vector2.zero;
            swap.onClick.AddListener(() => { camIndex = 1 - camIndex; UpdateFeed(); });
        }

        void ShowApp(int index)
        {
            homeScreen.gameObject.SetActive(index == 0);
            flashlightScreen.gameObject.SetActive(index == 1);
            messagesScreen.gameObject.SetActive(index == 2);
            cctvScreen.gameObject.SetActive(index == 3);
            appTitle.text = index == 0 ? "HOME" : index == 1 ? "FLASHLIGHT"
                : index == 2 ? "MESSAGES" : "CCTV";
            if (index == 1) UpdateFlashlightState();
            if (index == 3) UpdateFeed();
            if (index == 2) { unread = 0; UpdateBadge(); }
            SetFeedRendering();
        }

        void UpdateFlashlightState()
        {
            if (flashlightStateText == null) return;
            bool on = Flashlight != null && Flashlight.IsOn;
            flashlightStateText.text = on ? "ON" : "OFF";
            flashlightStateText.color = on ? new Color(1, 0.85f, 0.4f) : new Color(1, 1, 1, 0.5f);
        }

        void ToggleFlashlightApp()
        {
            if (Flashlight == null) return;
            Flashlight.Toggle();
            Audio.ProceduralAudio.PlayClick(transform.position);
            UpdateFlashlightState();
        }

        void UpdateFeed()
        {
            var feed = camIndex == 0 ? HallCam : LivingCam;
            if (feed == null) feed = HallCam;
            if (cctvImage != null)
            {
                cctvImage.texture = feed != null ? feed.CurrentTexture : null;
                cctvImage.color = feed != null && feed.Smashed ? new Color(0.7f, 0.7f, 0.7f) : Color.white;
            }
            if (cctvLabel != null) cctvLabel.text = feed != null ? feed.FeedName + (feed.Smashed ? "  [SIGNAL LOST]" : "  [LIVE]") : "NO FEED";
        }

        void RenderMessages()
        {
            if (messagesContent == null) return;
            for (int i = messagesContent.childCount - 1; i >= 0; i--)
                Destroy(messagesContent.GetChild(i).gameObject);

            foreach (var m in messages)
            {
                var entry = UIFactory.NewRect("Msg", messagesContent);
                var bg = UIFactory.Image("MsgBg", entry, m.Urgent
                    ? new Color(0.25f, 0.08f, 0.08f, 0.9f)
                    : new Color(0.1f, 0.1f, 0.13f, 0.9f));
                UIFactory.Stretch(bg.rectTransform);
                var layout = entry.gameObject.AddComponent<VerticalLayoutGroup>();
                layout.childControlHeight = true; layout.childForceExpandHeight = false;
                layout.childControlWidth = true; layout.childForceExpandWidth = true;
                layout.padding = new RectOffset(10, 10, 8, 8);
                var sender = UIFactory.Text("MWho", entry, m.Sender, 20, TextAnchor.UpperLeft,
                    m.Urgent ? new Color(1, 0.5f, 0.4f) : new Color(0.55f, 0.75f, 1f));
                sender.rectTransform.sizeDelta = new Vector2(0, 22);
                var body = UIFactory.Text("MBody", entry, m.Body, 22, TextAnchor.UpperLeft, Color.white);
                body.rectTransform.sizeDelta = new Vector2(0, 60);
                var le = body.gameObject.AddComponent<LayoutElement>();
                le.minHeight = 44;
            }
        }
    }
}



