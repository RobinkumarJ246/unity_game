using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ContentHouse.UI;   // UIFactory (code-built canvas)

namespace ContentHouse.Phone
{
    /// <summary>
    /// Interactive smartphone (spec: PhoneController.cs). [Tab]/[P] toggles.
    /// Apps: Flashlight, Messages, CCTV. Battery drains through Acts I-II and
    /// force-shuts-down at the Act II blackout.
    /// Navigation: tiles on the home screen, "&lt;" button / home bar / Backspace / Esc go back,
    /// keys 1-3 open the apps directly.
    /// </summary>
    public partial class PhoneController : MonoBehaviour
    {
        // Screens: 0 home, 1 flashlight, 2 messages (conversation LIST), 3 CCTV, 4 contacts (phone app),
        // 5 active call, 6 one contact's message THREAD.
        // Contacts + threads: PhoneMessages.cs. Calls + story replies: PhoneCalls.cs. Free typing: PhoneTyping.cs.
        const int ScreenCount = 7;
        public CCTV.CCTVCameraFeed HallCam;
        public CCTV.CCTVCameraFeed LivingCam;
        public Player.FlashlightController Flashlight;

        public float Battery = Core.GameConfig.PhoneBatteryStart;
        public bool ForcedShutdown { get; private set; }
        public bool BatteryAlive => Battery > 0.5f && !ForcedShutdown;
        public bool IsOpen { get; private set; }
        public float DrainMultiplier = 1f;

        // ---- palette ----
        static readonly Color ScreenBase = new Color(0.05f, 0.06f, 0.10f, 1f);
        static readonly Color Amber = new Color(1f, 0.78f, 0.30f);
        static readonly Color Blue = new Color(0.30f, 0.55f, 1f);
        static readonly Color Green = new Color(0.25f, 0.78f, 0.50f);
        static readonly Color Soft = new Color(1f, 1f, 1f, 0.55f);

        RectTransform canvas;
        RectTransform panel, closedStrip;
        CanvasGroup panelGroup;
        Vector2 panelBasePos;
        float openT;

        Text batteryText, stripBatteryText, clockText, appTitle;
        RectTransform homeScreen, flashlightScreen, messagesScreen, cctvScreen;
        CanvasGroup[] screenGroups = new CanvasGroup[ScreenCount];
        RectTransform[] screens = new RectTransform[ScreenCount];
        RectTransform messagesContent;
        ScrollRect messagesScroll;
        Text messagesEmpty;
        Text flashlightStateText;
        Image flButton, flGlow, flIconRing, flIconHole, flIconGap, flIconBar;
        float flOnT;
        Image batteryFill;
        Text greeting;
        RawImage cctvImage;
        Text cctvLabel;
        Image liveDot;
        Text badgeText;
        GameObject badge;
        GameObject backButton;

        int camIndex;
        int currentApp;
        float appAnimT = 1f;
        float clockMinutes = 23 * 60 + 30;   // 11:30 PM

        public class Message
        {
            public string Sender, Body, Stamp;
            public bool Urgent, Mine;   // Mine = sent by the player (right-aligned blue bubble)
            public bool Note;           // centred grey log line (e.g. "Call with Manager  01:12")
            public string To;           // for Mine messages: who it was sent to
            public Message(string s, string b, bool u, string stamp, bool mine = false) { Sender = s; Body = b; Urgent = u; Stamp = stamp; Mine = mine; }
        }
        void Awake()
        {
            UI.UIFactory.EnsureEventSystem();   // phone apps need it too
            BuildUI();
        }

        void Update()
        {
            bool togglePressed = Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.P);
            // can't pocket the phone mid-call, and a typed "p" must not close it
            if (togglePressed && (InCall || Typing)) { }
            else if (togglePressed && !ForcedShutdown && BatteryAlive && !BlockedByOverlay())
            {
                Debug.Log("[Phone] toggle key pressed -> open=" + !IsOpen);
                SetOpen(!IsOpen);
            }
            else if (togglePressed && BlockedByOverlay())
            {
                Debug.Log("[Phone] toggle blocked (menu or narration on top)");
            }

            if (IsOpen && !ForcedShutdown) HandlePhoneKeys();

            // the little "[TAB] Phone" strip: not over the main menu or narration cards, not when the phone is dead/open
            if (closedStrip != null)
            {
                bool wantStrip = !IsOpen && !ForcedShutdown && !BlockedByOverlay();
                if (closedStrip.gameObject.activeSelf != wantStrip) closedStrip.gameObject.SetActive(wantStrip);
            }
            UpdateTyping();
            UpdateMessageScrollKeys();
            UpdateCallInput();

            if (BatteryAlive)
            {
                float drain = Core.GameConfig.PhonePassiveDrainPerMinute / 60f * DrainMultiplier;
                if (Flashlight != null && Flashlight.IsOn) drain += Core.GameConfig.FlashlightDrainPerMinute / 60f;
                if (IsOpen && cctvScreen.gameObject.activeSelf) drain += 2f / 60f;
                Battery = Mathf.Max(0f, Battery - drain * Time.deltaTime);
                if (Battery <= 0f) Shutdown("WARNING: Battery 0% - Device Shutting Down...");
            }

            UpdateClockAndBatteryUI();
            AnimateUI();
        }

        void HandlePhoneKeys()
        {
            var dlg = UI.DialogueScreen.Instance;
            if (dlg != null && dlg.IsPlaying) return;
            if (InCall) return;                         // the call screen handles its own input
            if (Typing) return;                         // typing: Backspace/digits/letters are text, not shortcuts
            if (HandleComposeKeys()) return;            // 1-3 pick a text reply while one is pending
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Backspace))
            {
                if (currentApp != 0) GoBack(); else SetOpen(false);
            }
            else if (currentApp == 2 && OpenConversationByDigit()) { }   // in the conversation list, 1-9 open that chat
            else if (Input.GetKeyDown(KeyCode.Alpha1)) GoApp(1);
            else if (Input.GetKeyDown(KeyCode.Alpha2)) GoApp(2);
            else if (Input.GetKeyDown(KeyCode.Alpha3)) GoApp(3);
            else if (Input.GetKeyDown(KeyCode.Alpha4)) GoApp(4);
        }

        // ---------- public API ----------

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
            if (!open && InCall) return;   // hang-up is the only way out of a call
            if (!open) Defocus();
            IsOpen = open;
            if (closedStrip != null) closedStrip.gameObject.SetActive(!open);
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
            if (open)
            {
                // a text waiting for your reply takes you straight to the thread
                if (HasPendingText && pendingContact != null) OpenThread(pendingContact); else ShowApp(0);
                Audio.ProceduralAudio.PlayPhoneOpen(transform.position);
                if (panel != null) panel.gameObject.SetActive(true);   // slide-in handled in AnimateUI
            }
            else
            {
                Audio.ProceduralAudio.PlayPhoneClose(transform.position);   // slide-out handled in AnimateUI
            }
            SetFeedRendering();
        }

        string ClockString()
        {
            float total = clockMinutes % (24 * 60);
            int h24 = Mathf.FloorToInt(total / 60f) % 24;
            int m = Mathf.FloorToInt(total % 60f);
            int h12 = h24 % 12; if (h12 == 0) h12 = 12;
            return string.Format("{0}:{1:00} {2}", h12, m, h24 >= 12 ? "PM" : "AM");
        }

        void UpdateClockAndBatteryUI()
        {
            clockMinutes += Time.deltaTime * (60f / 25f);   // 1 game min per 25 real sec
            if (clockText != null) clockText.text = ClockString();
            float pct = ForcedShutdown ? 0f : Mathf.Clamp(Battery, 0f, 100f);
            bool low = pct < 15f;
            if (batteryText != null)
            {
                batteryText.text = ForcedShutdown ? "0%" : Mathf.CeilToInt(pct) + "%";
                batteryText.color = low ? new Color(1f, 0.4f, 0.35f) : Color.white;
            }
            if (stripBatteryText != null) stripBatteryText.text = ForcedShutdown ? "0%" : Mathf.CeilToInt(pct) + "%";
            if (batteryFill != null)
            {
                var a = batteryFill.rectTransform.anchorMax; a.x = Mathf.Clamp01(pct / 100f);
                batteryFill.rectTransform.anchorMax = a;
                batteryFill.color = low ? new Color(0.95f, 0.3f, 0.28f) : pct < 30f ? Amber : Green;
            }
        }

        /// <summary>Home-screen badge = unread messages across ALL conversations.</summary>
        void UpdateBadge()
        {
            if (badge == null) return;
            int total = TotalUnread();
            badge.SetActive(total > 0);
            if (badgeText != null) badgeText.text = total.ToString();
        }

        void SetFeedRendering()
        {
            bool viewing = IsOpen && cctvScreen.gameObject.activeSelf;
            if (HallCam != null) HallCam.SetViewing(viewing);
            if (LivingCam != null) LivingCam.SetViewing(viewing);
        }

        // ---------- animation ----------

        void AnimateUI()
        {
            if (panel == null) return;

            // open / close: fade + slide + slight scale
            openT = Mathf.MoveTowards(openT, IsOpen ? 1f : 0f, Time.unscaledDeltaTime * 7f);
            float k = Mathf.SmoothStep(0f, 1f, openT);
            panelGroup.alpha = k;
            panelGroup.blocksRaycasts = IsOpen;
            panel.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, k);
            panel.anchoredPosition = panelBasePos + new Vector2((1f - k) * 80f, 0f);
            bool shouldShow = openT > 0.001f;
            if (panel.gameObject.activeSelf != shouldShow) panel.gameObject.SetActive(shouldShow);
            if (!shouldShow) return;

            // app switch: new screen fades/slides up
            if (appAnimT < 1f)
            {
                appAnimT = Mathf.MoveTowards(appAnimT, 1f, Time.unscaledDeltaTime * 6f);
                float e = Mathf.SmoothStep(0f, 1f, appAnimT);
                screenGroups[currentApp].alpha = e;
                screens[currentApp].anchoredPosition = new Vector2(0f, (1f - e) * -26f);
            }

            // flashlight button glow
            bool on = Flashlight != null && Flashlight.IsOn;
            flOnT = Mathf.MoveTowards(flOnT, on ? 1f : 0f, Time.unscaledDeltaTime * 8f);
            if (flButton != null)
            {
                flButton.color = Color.Lerp(new Color(0.15f, 0.16f, 0.22f), Amber, flOnT);
                var ic = Color.Lerp(new Color(1f, 1f, 1f, 0.65f), new Color(0.22f, 0.15f, 0.0f), flOnT);
                var bgc = flButton.color;
                flIconRing.color = ic; flIconBar.color = ic;
                flIconHole.color = bgc; flIconGap.color = bgc;
                float pulse = 1f + 0.04f * Mathf.Sin(Time.unscaledTime * 3f) * flOnT;
                flGlow.color = new Color(Amber.r, Amber.g, Amber.b, 0.22f * flOnT);
                flGlow.rectTransform.localScale = Vector3.one * pulse;
            }

            // live dot blink
            if (liveDot != null && cctvScreen.gameObject.activeSelf)
            {
                var feed = camIndex == 0 ? HallCam : LivingCam;
                bool lost = feed != null && feed.Smashed;
                liveDot.color = lost ? new Color(0.5f, 0.5f, 0.5f, 1f)
                    : new Color(1f, 0.2f, 0.2f, 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 5f));
            }
        }

        // ---------- UI construction ----------

        static void Anchor(RectTransform r, Vector2 anchor, Vector2 size, Vector2 pos)
        {
            r.anchorMin = r.anchorMax = anchor;
            r.pivot = anchor;
            r.sizeDelta = size;
            r.anchoredPosition = pos;
        }

        Button AddButton(Graphic g, UnityEngine.Events.UnityAction onClick)
        {
            var b = g.gameObject.AddComponent<Button>();
            b.targetGraphic = g;
            UIFactory.StyleButton(b);
            b.onClick.AddListener(() => { Audio.ProceduralAudio.PlayClick(transform.position); });
            if (onClick != null) b.onClick.AddListener(onClick);
            return b;
        }

        void BuildUI()
        {
            // Sorting 500: above menu/narration/HUD so the phone is never hidden.
            canvas = UIFactory.CanvasRoot("PhoneCanvas", 500);

            // Closed strip (bottom right) - passive indicator
            closedStrip = UIFactory.NewRect("ClosedStrip", canvas);
            closedStrip.anchorMin = closedStrip.anchorMax = new Vector2(1f, 0f);
            closedStrip.anchoredPosition = new Vector2(-190, 54);
            closedStrip.sizeDelta = new Vector2(300, 64);
            var stripBg = UIFactory.Round("StripBg", closedStrip, new Color(0, 0, 0, 0.55f), 20);
            UIFactory.Stretch(stripBg.rectTransform);
            var stripText = UIFactory.Text("StripText", closedStrip, "[TAB]  Phone", 24, TextAnchor.MiddleCenter, Color.white);
            UIFactory.Stretch(stripText.rectTransform);
            var stripBat = UIFactory.Text("StripBat", closedStrip, "", 20, TextAnchor.UpperRight, new Color(1, 1, 1, 0.7f));
            UIFactory.Stretch(stripBat.rectTransform);
            stripBat.rectTransform.offsetMax = new Vector2(-14, -6);
            stripBatteryText = stripBat;
            stripBg.raycastTarget = false;
            stripText.raycastTarget = false;
            stripBat.raycastTarget = false;

            // ---- phone body ----
            panel = UIFactory.NewRect("PhonePanel", canvas);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panelBasePos = new Vector2(300, -20);
            panel.anchoredPosition = panelBasePos;
            panel.sizeDelta = new Vector2(470, 930);
            panelGroup = panel.gameObject.AddComponent<CanvasGroup>();

            var bezel = UIFactory.Round("Bezel", panel, new Color(0.015f, 0.015f, 0.02f, 1f), 60);
            UIFactory.Stretch(bezel.rectTransform);
            var rim = UIFactory.Round("Rim", panel, new Color(1f, 1f, 1f, 0.07f), 60);   // faint edge light
            UIFactory.Stretch(rim.rectTransform);
            rim.raycastTarget = false;
            var inner = UIFactory.Round("BezelInner", panel, new Color(0.015f, 0.015f, 0.02f, 1f), 58);
            UIFactory.Stretch(inner.rectTransform);
            inner.rectTransform.offsetMin = new Vector2(2, 2); inner.rectTransform.offsetMax = new Vector2(-2, -2);
            inner.raycastTarget = false;

            var screen = UIFactory.Round("Screen", panel, ScreenBase, 48);
            UIFactory.Stretch(screen.rectTransform);
            screen.rectTransform.offsetMin = new Vector2(12, 12); screen.rectTransform.offsetMax = new Vector2(-12, -12);
            screen.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var scr = screen.rectTransform;

            var wall = UIFactory.Image("Wallpaper", scr, Color.white);
            wall.sprite = UIFactory.VerticalGradient(new Color(0.13f, 0.15f, 0.30f), new Color(0.04f, 0.04f, 0.08f));
            UIFactory.Stretch(wall.rectTransform);
            wall.raycastTarget = false;

            // camera pill
            var pill = UIFactory.Round("Pill", scr, new Color(0, 0, 0, 1f), 13);
            Anchor(pill.rectTransform, new Vector2(0.5f, 1f), new Vector2(112, 26), new Vector2(0, -12));
            pill.raycastTarget = false;

            // status bar
            clockText = UIFactory.Text("Clock", scr, "11:30 PM", 22, TextAnchor.UpperLeft, Color.white);
            Anchor(clockText.rectTransform, new Vector2(0f, 1f), new Vector2(160, 28), new Vector2(34, -16));
            clockText.fontStyle = FontStyle.Bold;
            clockText.raycastTarget = false;

            var batOutline = UIFactory.Round("BatOutline", scr, new Color(1, 1, 1, 0.85f), 7);
            Anchor(batOutline.rectTransform, new Vector2(1f, 1f), new Vector2(46, 22), new Vector2(-40, -18));
            batOutline.raycastTarget = false;
            var batNub = UIFactory.Round("BatNub", scr, new Color(1, 1, 1, 0.85f), 2);
            Anchor(batNub.rectTransform, new Vector2(1f, 1f), new Vector2(4, 9), new Vector2(-36, -25));
            batNub.raycastTarget = false;
            var batInner = UIFactory.Round("BatInner", batOutline.rectTransform, new Color(0.07f, 0.08f, 0.12f), 5);
            UIFactory.Stretch(batInner.rectTransform);
            batInner.rectTransform.offsetMin = new Vector2(2, 2); batInner.rectTransform.offsetMax = new Vector2(-2, -2);
            batInner.raycastTarget = false;
            batteryFill = UIFactory.Round("BatFill", batInner.rectTransform, Green, 4);
            UIFactory.Stretch(batteryFill.rectTransform);
            batteryFill.rectTransform.offsetMin = new Vector2(1, 1); batteryFill.rectTransform.offsetMax = new Vector2(-1, -1);
            batteryFill.raycastTarget = false;
            var batPct = UIFactory.Text("BatPct", scr, "78%", 20, TextAnchor.UpperRight, Color.white);
            Anchor(batPct.rectTransform, new Vector2(1f, 1f), new Vector2(90, 26), new Vector2(-92, -18));
            batPct.raycastTarget = false;
            batteryText = batPct;

            // header: back button + title
            var back = UIFactory.Circle("Back", scr, new Color(1, 1, 1, 0.13f), 46);
            Anchor(back.rectTransform, new Vector2(0f, 1f), new Vector2(46, 46), new Vector2(26, -62));
            var backLabel = UIFactory.Text("BackLabel", back.rectTransform, "<", 30, TextAnchor.MiddleCenter, Color.white);
            UIFactory.Stretch(backLabel.rectTransform);
            backLabel.fontStyle = FontStyle.Bold;
            backLabel.raycastTarget = false;
            AddButton(back, GoBack);   // thread -> conversation list -> home
            backButton = back.gameObject;

            appTitle = UIFactory.Text("AppTitle", scr, "", 32, TextAnchor.MiddleCenter, Color.white);
            Anchor(appTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(260, 46), new Vector2(0, -62));
            appTitle.fontStyle = FontStyle.Bold;
            appTitle.raycastTarget = false;

            // content area
            var content = UIFactory.NewRect("Content", scr);
            UIFactory.Stretch(content);
            content.offsetMin = new Vector2(22, 62); content.offsetMax = new Vector2(-22, -118);

            for (int i = 0; i < ScreenCount; i++)
            {
                var s = UIFactory.NewRect("Screen" + i, content);
                UIFactory.Stretch(s);
                screens[i] = s;
                screenGroups[i] = s.gameObject.AddComponent<CanvasGroup>();
            }
            homeScreen = screens[0]; flashlightScreen = screens[1]; messagesScreen = screens[2]; cctvScreen = screens[3];
            contactsScreen = screens[4]; callScreen = screens[5]; threadScreen = screens[ThreadScreen];

            // home bar (tap = go home)
            var homeHit = UIFactory.Image("HomeHit", scr, new Color(1, 1, 1, 0.001f));
            Anchor(homeHit.rectTransform, new Vector2(0.5f, 0f), new Vector2(200, 40), new Vector2(0, 8));
            var homeBar = UIFactory.Round("HomeBar", homeHit.rectTransform, new Color(1, 1, 1, 0.6f), 3);
            Anchor(homeBar.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(140, 6), Vector2.zero);
            homeBar.raycastTarget = false;
            var hb = homeHit.gameObject.AddComponent<Button>();
            hb.targetGraphic = homeHit; hb.transition = Selectable.Transition.None;
            hb.onClick.AddListener(() => { if (currentApp != 0) GoApp(0); });

            BuildHome();
            BuildFlashlight();
            RegisterDefaultContacts();
            BuildConversationList();   // Messages app = list of conversations
            BuildMessages();           // ... and one contact's thread (screen 6)
            BuildCCTV();
            BuildContacts();
            BuildCall();

            ShowApp(0);
            panelGroup.alpha = 0f;
            panel.gameObject.SetActive(false);
        }

        // ----- home -----

        Image MakeTile(Transform grid, string label, Color color, int app, System.Action<RectTransform> glyph)
        {
            var cell = UIFactory.NewRect("Cell_" + label, grid);
            var tile = UIFactory.Round("Tile_" + label, cell, color, 42);
            Anchor(tile.rectTransform, new Vector2(0.5f, 1f), new Vector2(150, 150), new Vector2(0, 0));
            glyph(tile.rectTransform);
            var txt = UIFactory.Text("Label", cell, label, 22, TextAnchor.UpperCenter, new Color(1, 1, 1, 0.9f));
            Anchor(txt.rectTransform, new Vector2(0.5f, 1f), new Vector2(190, 30), new Vector2(0, -160));
            txt.raycastTarget = false;
            AddButton(tile, () => GoApp(app));
            return tile;
        }

        void BuildHome()
        {
            greeting = UIFactory.Text("Greeting", homeScreen, "Hi, " + Core.GameConfig.PlayerName, 40, TextAnchor.UpperLeft, Color.white);
            Anchor(greeting.rectTransform, new Vector2(0f, 1f), new Vector2(380, 50), new Vector2(8, -6));
            greeting.fontStyle = FontStyle.Bold;
            greeting.raycastTarget = false;
            var sub = UIFactory.Text("Sub", homeScreen, "Tap an app, or press 1  2  3  4", 21, TextAnchor.UpperLeft, Soft);
            Anchor(sub.rectTransform, new Vector2(0f, 1f), new Vector2(380, 28), new Vector2(10, -58));
            sub.raycastTarget = false;

            var gridRt = UIFactory.NewRect("Grid", homeScreen);
            Anchor(gridRt, new Vector2(0.5f, 1f), new Vector2(390, 520), new Vector2(0, -120));
            var grid = gridRt.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(190, 215);
            grid.spacing = new Vector2(10, 12);
            grid.childAlignment = TextAnchor.UpperCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;

            // Flashlight icon: torch body + head + beam
            MakeTile(gridRt, "Flashlight", new Color(0.95f, 0.65f, 0.15f), 1, t =>
            {
                var head = UIFactory.Round("Head", t, Color.white, 6);
                Anchor(head.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(52, 20), new Vector2(0, 32));
                var body = UIFactory.Round("Body", t, new Color(1, 1, 1, 0.95f), 8);
                Anchor(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(34, 62), new Vector2(0, -8));
                var btn = UIFactory.Round("Btn", t, new Color(0.95f, 0.65f, 0.15f), 3);
                Anchor(btn.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(8, 16), new Vector2(0, 0));
                foreach (var g in new[] { head, body, btn }) g.raycastTarget = false;
            });

            // Messages icon: speech bubble with three dots
            var msgTile = MakeTile(gridRt, "Messages", Blue, 2, t =>
            {
                var bub = UIFactory.Round("Bubble", t, Color.white, 20);
                Anchor(bub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(84, 58), new Vector2(0, 6));
                var tail = UIFactory.Round("Tail", t, Color.white, 4);
                Anchor(tail.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(18, 18), new Vector2(-20, -28));
                tail.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
                bub.raycastTarget = false; tail.raycastTarget = false;
                for (int i = -1; i <= 1; i++)
                {
                    var dot = UIFactory.Circle("Dot", t, Blue, 11);
                    Anchor(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(11, 11), new Vector2(i * 20, 6));
                    dot.raycastTarget = false;
                }
            });
            badge = UIFactory.Circle("Badge", msgTile.rectTransform, new Color(0.95f, 0.2f, 0.2f), 40).gameObject;
            Anchor(badge.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(40, 40), new Vector2(8, 8));
            badge.GetComponent<Image>().raycastTarget = false;
            badgeText = UIFactory.Text("BadgeText", badge.transform, "0", 22, TextAnchor.MiddleCenter, Color.white);
            UIFactory.Stretch(badgeText.rectTransform);
            badgeText.fontStyle = FontStyle.Bold;
            badgeText.raycastTarget = false;
            badge.SetActive(false);

            // CCTV icon: camera lens
            MakeTile(gridRt, "CCTV", Green, 3, t =>
            {
                var outer = UIFactory.Circle("Outer", t, Color.white, 72);
                Anchor(outer.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(72, 72), Vector2.zero);
                var mid = UIFactory.Circle("Mid", t, new Color(0.10f, 0.35f, 0.25f), 44);
                Anchor(mid.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(44, 44), Vector2.zero);
                var shine = UIFactory.Circle("Shine", t, new Color(1, 1, 1, 0.9f), 12);
                Anchor(shine.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(12, 12), new Vector2(7, 7));
                foreach (var g in new[] { outer, mid, shine }) g.raycastTarget = false;
            });

            // Phone icon: a handset (earpiece + mouthpiece joined by a tilted bar)
            MakeTile(gridRt, "Phone", new Color(0.20f, 0.72f, 0.42f), 4, t =>
            {
                // handset: an arch of dots (the grip) with a heavier blob at each end, tilted like a phone icon
                var hs = UIFactory.NewRect("Handset", t);
                Anchor(hs, new Vector2(0.5f, 0.5f), new Vector2(10, 10), new Vector2(0, -4));
                hs.localRotation = Quaternion.Euler(0, 0, -40);
                const float radius = 36f;
                for (int i = 0; i <= 8; i++)
                {
                    float ang = Mathf.Lerp(20f, 160f, i / 8f) * Mathf.Deg2Rad;
                    bool end = i == 0 || i == 8;
                    var dot = UIFactory.Circle("D" + i, hs, Color.white, end ? 30f : 20f);
                    Anchor(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(end ? 30f : 20f, end ? 30f : 20f),
                        new Vector2(Mathf.Cos(ang) * radius, Mathf.Sin(ang) * radius * 0.8f - (end ? 6f : 0f)));
                    dot.raycastTarget = false;
                }
            });
        }

        // ----- flashlight -----

        void BuildFlashlight()
        {
            flGlow = UIFactory.Circle("Glow", flashlightScreen, new Color(Amber.r, Amber.g, Amber.b, 0f), 330);
            Anchor(flGlow.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(330, 330), new Vector2(0, 60));
            flGlow.raycastTarget = false;

            flButton = UIFactory.Circle("FLToggle", flashlightScreen, new Color(0.15f, 0.16f, 0.22f), 230);
            Anchor(flButton.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(230, 230), new Vector2(0, 60));
            AddButton(flButton, ToggleFlashlightApp);

            // power icon: ring with a gap at the top and a bar
            flIconRing = UIFactory.Circle("Ring", flButton.rectTransform, new Color(1, 1, 1, 0.65f), 92);
            Anchor(flIconRing.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(92, 92), new Vector2(0, -4));
            flIconHole = UIFactory.Circle("Hole", flButton.rectTransform, flButton.color, 70);
            Anchor(flIconHole.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(70, 70), new Vector2(0, -4));
            flIconGap = UIFactory.Image("Gap", flButton.rectTransform, flButton.color);
            Anchor(flIconGap.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(26, 40), new Vector2(0, 40));
            flIconGap.raycastTarget = false;
            flIconBar = UIFactory.Round("Bar", flButton.rectTransform, new Color(1, 1, 1, 0.65f), 5);
            Anchor(flIconBar.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(10, 46), new Vector2(0, 14));
            flIconRing.raycastTarget = false; flIconHole.raycastTarget = false; flIconBar.raycastTarget = false;

            flashlightStateText = UIFactory.Text("FLState", flashlightScreen, "OFF", 44, TextAnchor.MiddleCenter, Soft);
            Anchor(flashlightStateText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(300, 56), new Vector2(0, -130));
            flashlightStateText.fontStyle = FontStyle.Bold;
            flashlightStateText.raycastTarget = false;

            var hint = UIFactory.Text("FLHint", flashlightScreen, "Tap the button to switch the light", 21, TextAnchor.MiddleCenter, Soft);
            Anchor(hint.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(380, 28), new Vector2(0, -178));
            hint.raycastTarget = false;
            var note = UIFactory.Text("FLNote", flashlightScreen, "Drains the battery fast.", 20, TextAnchor.LowerCenter, new Color(1, 1, 1, 0.4f));
            Anchor(note.rectTransform, new Vector2(0.5f, 0f), new Vector2(380, 28), new Vector2(0, 8));
            note.raycastTarget = false;
        }

        // ----- messages -----

        void BuildMessages()
        {
            var viewR = UIFactory.NewRect("MsgView", threadScreen);
            UIFactory.Stretch(viewR);
            messagesScroll = viewR.gameObject.AddComponent<ScrollRect>();
            var vpR = UIFactory.NewRect("MsgViewport", viewR);
            UIFactory.Stretch(vpR);
            vpR.gameObject.AddComponent<RectMask2D>();
            messagesScroll.viewport = vpR;
            var contentR = UIFactory.NewRect("MsgContent", vpR);
            contentR.anchorMin = new Vector2(0, 1); contentR.anchorMax = new Vector2(1, 1);
            contentR.pivot = new Vector2(0.5f, 1f);
            contentR.sizeDelta = new Vector2(0, 10);
            var layout = contentR.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.spacing = 14;
            layout.padding = new RectOffset(2, 14, 6, 14);   // right gap so bubbles clear the scrollbar
            var fitter = contentR.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            messagesScroll.content = contentR;
            messagesScroll.vertical = true;
            messagesScroll.horizontal = false;
            messagesScroll.movementType = ScrollRect.MovementType.Clamped;
            messagesScroll.scrollSensitivity = 40;
            messagesScroll.inertia = true;
            messagesScroll.decelerationRate = 0.12f;
            messagesContent = contentR;

            // slim scrollbar on the right edge (auto-hides when the whole thread fits)
            var track = UIFactory.Round("ScrollTrack", viewR, new Color(1f, 1f, 1f, 0.07f), 4);
            var trt = track.rectTransform;
            trt.anchorMin = new Vector2(1f, 0f); trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(1f, 0.5f);
            trt.sizeDelta = new Vector2(8f, 0f); trt.anchoredPosition = Vector2.zero;
            var handle = UIFactory.Round("ScrollHandle", trt, new Color(1f, 1f, 1f, 0.38f), 4);
            UIFactory.Stretch(handle.rectTransform);
            var bar = track.gameObject.AddComponent<Scrollbar>();
            bar.handleRect = handle.rectTransform;
            bar.targetGraphic = handle;
            bar.direction = Scrollbar.Direction.BottomToTop;
            messagesScroll.verticalScrollbar = bar;
            messagesScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            messagesScroll.verticalScrollbarSpacing = 0f;

            BuildTypeBar();   // free typing (secret replies) lives under the thread

            messagesEmpty = UIFactory.Text("Empty", threadScreen, "No messages yet", 26, TextAnchor.MiddleCenter, new Color(1, 1, 1, 0.35f));
            UIFactory.Stretch(messagesEmpty.rectTransform);
            messagesEmpty.raycastTarget = false;
        }

        /// <summary>Rebuild the open thread (the current contact's messages only).</summary>
        void RenderMessages()
        {
            if (messagesContent == null) return;
            for (int i = messagesContent.childCount - 1; i >= 0; i--)
                Destroy(messagesContent.GetChild(i).gameObject);
            var thread = currentContact != null ? currentContact.Thread : null;
            if (messagesEmpty != null)
            {
                messagesEmpty.gameObject.SetActive(thread == null || thread.Count == 0);
                messagesEmpty.text = currentContact != null ? "No messages with " + currentContact.Name + " yet" : "No messages yet";
            }
            if (thread == null) return;

            foreach (var m in thread)
            {
                if (m.Note)
                {
                    var note = UIFactory.NewRect("Note", messagesContent);
                    var nl = UIFactory.Text("NoteText", note, m.Body + "   " + m.Stamp, 19, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.42f));
                    nl.raycastTarget = false;
                    UIFactory.Stretch(nl.rectTransform);
                    note.gameObject.AddComponent<LayoutElement>().minHeight = 34;
                    continue;
                }
                var entry = UIFactory.NewRect("Msg", messagesContent);
                var entryLayout = entry.gameObject.AddComponent<VerticalLayoutGroup>();
                entryLayout.childControlHeight = true; entryLayout.childForceExpandHeight = false;
                entryLayout.childControlWidth = true; entryLayout.childForceExpandWidth = true;
                entryLayout.spacing = 4;
                // my texts sit on the right with a gap on the left, theirs on the left
                entryLayout.padding = m.Mine ? new RectOffset(70, 0, 0, 0) : new RectOffset(0, 70, 0, 0);

                var sender = UIFactory.Text("MWho", entry,
                    m.Mine ? (string.IsNullOrEmpty(m.To) ? "You" : "You  >  " + m.To) : m.Sender, 20,
                    m.Mine ? TextAnchor.LowerRight : TextAnchor.LowerLeft,
                    m.Mine ? new Color(0.7f, 0.85f, 1f) : (m.Urgent ? new Color(1f, 0.55f, 0.45f) : new Color(0.62f, 0.78f, 1f)));
                sender.fontStyle = FontStyle.Bold;
                sender.raycastTarget = false;
                sender.gameObject.AddComponent<LayoutElement>().minHeight = 24;

                var bubble = UIFactory.Round("Bubble", entry, m.Mine
                    ? new Color(0.20f, 0.45f, 0.92f, 0.98f)
                    : m.Urgent
                        ? new Color(0.42f, 0.12f, 0.14f, 0.96f)
                        : new Color(0.17f, 0.20f, 0.30f, 0.96f), 22);
                var bl = bubble.gameObject.AddComponent<VerticalLayoutGroup>();
                bl.childControlHeight = true; bl.childForceExpandHeight = false;
                bl.childControlWidth = true; bl.childForceExpandWidth = true;
                bl.padding = new RectOffset(18, 18, 14, 14);
                var body = UIFactory.Text("MBody", bubble.transform, m.Body, 23, TextAnchor.UpperLeft, Color.white);
                body.raycastTarget = false;
                body.gameObject.AddComponent<LayoutElement>().minHeight = 30;

                var stamp = UIFactory.Text("MTime", entry, m.Stamp, 17, m.Mine ? TextAnchor.UpperRight : TextAnchor.UpperLeft, new Color(1, 1, 1, 0.38f));
                stamp.raycastTarget = false;
                stamp.gameObject.AddComponent<LayoutElement>().minHeight = 20;
            }
            StartCoroutine(ScrollToNewest());
        }

        IEnumerator ScrollToNewest()
        {
            yield return null;
            if (messagesContent == null || messagesScroll == null) yield break;
            LayoutRebuilder.ForceRebuildLayoutImmediate(messagesContent);
            messagesScroll.verticalNormalizedPosition = 0f;
        }

        // ----- CCTV -----

        void BuildCCTV()
        {
            var frame = UIFactory.Round("Frame", cctvScreen, new Color(0f, 0f, 0f, 0.92f), 24);
            UIFactory.Stretch(frame.rectTransform);
            frame.rectTransform.offsetMin = new Vector2(0, 84);
            frame.raycastTarget = false;
            cctvImage = UIFactory.RawImage("Feed", frame.rectTransform);
            UIFactory.Stretch(cctvImage.rectTransform);
            cctvImage.rectTransform.offsetMin = new Vector2(6, 6); cctvImage.rectTransform.offsetMax = new Vector2(-6, -6);
            cctvImage.color = Color.black;
            cctvImage.raycastTarget = false;

            liveDot = UIFactory.Circle("LiveDot", frame.rectTransform, new Color(1f, 0.2f, 0.2f), 14);
            Anchor(liveDot.rectTransform, new Vector2(0f, 1f), new Vector2(14, 14), new Vector2(22, -24));
            liveDot.raycastTarget = false;
            cctvLabel = UIFactory.Text("CctvLabel", frame.rectTransform, "", 21, TextAnchor.UpperLeft, new Color(0.7f, 1f, 0.75f));
            Anchor(cctvLabel.rectTransform, new Vector2(0f, 1f), new Vector2(360, 28), new Vector2(46, -18));
            cctvLabel.raycastTarget = false;

            var swap = UIFactory.Round("SwapCam", cctvScreen, new Color(0.22f, 0.45f, 0.9f), 24);
            Anchor(swap.rectTransform, new Vector2(0.5f, 0f), new Vector2(280, 56), new Vector2(0, 10));
            var swapLabel = UIFactory.Text("SwapLabel", swap.rectTransform, "SWITCH FEED", 22, TextAnchor.MiddleCenter, Color.white);
            UIFactory.Stretch(swapLabel.rectTransform);
            swapLabel.fontStyle = FontStyle.Bold;
            swapLabel.raycastTarget = false;
            AddButton(swap, () => { camIndex = 1 - camIndex; UpdateFeed(); });
        }

        // ----- navigation -----

        void GoApp(int index)
        {
            if (InCall) return;   // no wandering off during a call
            if (index == currentApp && index == 0) return;
            ShowApp(index);
        }

        static readonly string[] AppTitles = { "", "Flashlight", "Messages", "CCTV", "Phone", "", "" };

        void ShowApp(int index)
        {
            currentApp = index;
            if (index != ThreadScreen) Defocus();
            for (int i = 0; i < ScreenCount; i++) screens[i].gameObject.SetActive(i == index);
            appAnimT = 0f;
            screenGroups[index].alpha = 0f;
            appTitle.text = index == ThreadScreen && currentContact != null ? currentContact.Name : AppTitles[index];
            if (backButton != null) backButton.SetActive(index != 0 && index != 5);
            if (index == 1) UpdateFlashlightState();
            if (index == 3) UpdateFeed();
            if (index == 2) RefreshConversationList();
            if (index == ThreadScreen) OnThreadShown();
            SetFeedRendering();
        }

        void UpdateFlashlightState()
        {
            if (flashlightStateText == null) return;
            bool on = Flashlight != null && Flashlight.IsOn;
            flashlightStateText.text = on ? "ON" : "OFF";
            flashlightStateText.color = on ? Amber : Soft;
        }

        void ToggleFlashlightApp()
        {
            if (Flashlight == null) return;
            Flashlight.Toggle();
            UpdateFlashlightState();
        }

        void UpdateFeed()
        {
            var feed = camIndex == 0 ? HallCam : LivingCam;
            if (feed == null) feed = HallCam;
            if (cctvImage != null)
            {
                var tex = feed != null ? feed.CurrentTexture : null;
                cctvImage.texture = tex;
                cctvImage.color = feed != null && feed.Smashed ? new Color(0.7f, 0.7f, 0.7f) : Color.white;
                // aspect-fill: centre-crop the feed instead of stretching it into the portrait frame
                var r = cctvImage.rectTransform.rect;
                if (tex != null && r.height > 1f)
                {
                    float fa = r.width / r.height, ta = tex.width / (float)tex.height;
                    cctvImage.uvRect = ta > fa
                        ? new Rect((1f - fa / ta) * 0.5f, 0f, fa / ta, 1f)
                        : new Rect(0f, (1f - ta / fa) * 0.5f, 1f, ta / fa);
                }
            }
            if (cctvLabel != null) cctvLabel.text = feed != null ? feed.FeedName + (feed.Smashed ? "   SIGNAL LOST" : "   LIVE") : "NO FEED";
        }
    }
}
