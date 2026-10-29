using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ContentHouse.UI
{
    /// <summary>
    /// Code-built uGUI helpers so every panel can construct itself at runtime
    /// without serialized scene UI (vibe-coding friendly, zero prefabs).
    /// </summary>
    public static class UIFactory
    {
        /// <summary>
        /// uGUI is dead without an EventSystem; the code-built UI never has one
        /// in the scene, so create it once, idempotently.
        /// </summary>
        public static void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        public static Font Font
        {
            get
            {
                if (f == null) f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return f;
            }
        }
        static Font f;

        public static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var r = go.GetComponent<RectTransform>();
            r.SetParent(parent, false);
            return r;
        }

        public static RectTransform CanvasRoot(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var c = go.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = sortingOrder;
            var sc = go.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080);
            sc.matchWidthOrHeight = 0.5f;
            return go.GetComponent<RectTransform>();
        }

        /// <summary>Full-screen stretch rect.</summary>
        public static RectTransform Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.sizeDelta = Vector2.zero;
            r.anchoredPosition = Vector2.zero;
            return r;
        }

        public static Image Image(string name, Transform parent, Color color)
        {
            var r = NewRect(name, parent);
            var img = r.gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        public static Text Text(string name, Transform parent, string content, int size,
            TextAnchor anchor, Color color)
        {
            var r = NewRect(name, parent);
            var t = r.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = content;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            // Readability: soft shadow on every text by default.
            var sh = r.gameObject.AddComponent<Shadow>();
            sh.effectColor = new Color(0f, 0f, 0f, 0.85f);
            sh.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }

        public static Button Button(string name, Transform parent, string label, int size, Color block)
        {
            var r = NewRect(name, parent);
            var img = r.gameObject.AddComponent<Image>();
            img.color = block;
            var b = r.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            var txt = Text(name + "_label", r, label, size, TextAnchor.MiddleCenter, Color.white);
            Stretch(txt.rectTransform);
            return b;
        }

        public static RawImage RawImage(string name, Transform parent)
        {
            var r = NewRect(name, parent);
            return r.gameObject.AddComponent<RawImage>();
        }

        public static InputField InputField(string name, Transform parent, string placeholder)
        {
            var r = NewRect(name, parent);
            var img = r.gameObject.AddComponent<Image>();
            img.color = new Color(0.08f, 0.08f, 0.1f, 0.95f);
            var inf = r.gameObject.AddComponent<InputField>();
            inf.targetGraphic = img;
            var ph = Text(name + "_ph", r, placeholder, 26, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.35f));
            Stretch(ph.rectTransform);
            ph.rectTransform.offsetMin = new Vector2(14, 0);
            ph.rectTransform.offsetMax = new Vector2(-14, 0);
            var val = Text(name + "_val", r, "", 26, TextAnchor.MiddleLeft, Color.white);
            Stretch(val.rectTransform);
            val.rectTransform.offsetMin = new Vector2(14, 0);
            val.rectTransform.offsetMax = new Vector2(-14, 0);
            inf.placeholder = ph;
            inf.textComponent = val;
            inf.caretColor = Color.white;
            return inf;
        }

        public static Toggle Toggle(string name, Transform parent, string label, bool startOn)
        {
            var r = NewRect(name, parent);
            var t = r.gameObject.AddComponent<Toggle>();
            var box = Image(name + "_box", r, new Color(0.1f, 0.1f, 0.12f, 1f));
            box.rectTransform.sizeDelta = new Vector2(30, 30);
            box.rectTransform.anchoredPosition = new Vector2(15, 0);
            var txt = Text(name + "_label", r, label, 22, TextAnchor.MiddleLeft, Color.white);
            txt.rectTransform.sizeDelta = new Vector2(500, 30);
            txt.rectTransform.anchoredPosition = new Vector2(290, 0);
            t.graphic = box;
            t.targetGraphic = box;
            t.isOn = startOn;
            t.onValueChanged.AddListener(v => box.color = v ? new Color(0.2f, 0.7f, 0.3f) : new Color(0.1f, 0.1f, 0.12f, 1f));
            t.onValueChanged.Invoke(startOn);
            return t;
        }
    }
}
