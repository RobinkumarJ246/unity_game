using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ContentHouse.Interaction
{
    /// <summary>
    /// Base class for hold-[E] interactions: a radial progress ring shows at
    /// screen center while holding, with a label. Release early = reset.
    /// The ring sprite is generated procedurally (no assets needed).
    /// </summary>
    public abstract class HoldInteractable : InteractableObject
    {
        public float HoldSeconds = 2.5f;
        public string HoldLabel = "Holding...";

        protected bool holding;
        protected float holdT;

        /// <summary>Test hook: pretend [E] is held (the editor MCP can't press keys).</summary>
        public static bool SimulateHeld;

        void Update()
        {
            if (!holding || used) return;
            if (!(Input.GetKey(KeyCode.E) || SimulateHeld)) { holding = false; holdT = 0f; ProgressCircle.Hide(); OnHoldCancel(); return; }

            holdT += Time.deltaTime;
            ProgressCircle.Show(HoldLabel, holdT / HoldSeconds);
            OnHoldProgress(holdT / HoldSeconds);

            if (holdT >= HoldSeconds)
            {
                holding = false;
                holdT = 0f;
                ProgressCircle.Hide();
                OnHoldComplete();
            }
        }

        public override void Interact(Player.PlayerInteraction player)
        {
            if (used) return;
            if (!CanStartHold(player)) return;
            holding = true;
            holdT = 0f;
            OnHoldStart();
        }

        protected virtual bool CanStartHold(Player.PlayerInteraction player) => true;
        protected virtual void OnHoldStart() { }
        /// <summary>[E] released before the ring filled.</summary>
        protected virtual void OnHoldCancel() { }
        protected virtual void OnHoldProgress(float t) { }
        protected abstract void OnHoldComplete();
    }

    /// <summary>
    /// Center-screen radial progress ring (single instance, built in code).
    /// </summary>
    public class ProgressCircle : MonoBehaviour
    {
        static ProgressCircle instance;
        RectTransform root;
        Image ring;
        Text label;
        Sprite ringSprite;

        public static void Show(string text, float t01)
        {
            Ensure();
            if (instance == null) return;
            instance.root.gameObject.SetActive(true);
            instance.label.text = text;
            instance.ring.fillAmount = Mathf.Clamp01(t01);
        }

        public static void Hide()
        {
            if (instance == null) return;
            instance.root.gameObject.SetActive(false);
        }

        static void Ensure()
        {
            if (instance != null) return;
            var go = new GameObject("ProgressCircle");
            instance = go.AddComponent<ProgressCircle>();
            DontDestroyOnLoad(go);
            instance.Build();
        }

        void Build()
        {
            ringSprite = MakeRingSprite();

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 350;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            root = canvasGo.GetComponent<RectTransform>();

            var bgGo = new GameObject("RingBg", typeof(Image));
            var bgR = bgGo.GetComponent<RectTransform>();
            bgR.SetParent(root, false);
            bgR.anchoredPosition = new Vector2(0, -220);
            bgR.sizeDelta = new Vector2(92, 92);
            var bgImg = bgGo.GetComponent<Image>();
            bgImg.sprite = ringSprite;
            bgImg.color = new Color(1, 1, 1, 0.18f);
            bgImg.type = Image.Type.Filled;
            bgImg.fillMethod = Image.FillMethod.Radial360;
            bgImg.fillAmount = 1f;

            var ringGo = new GameObject("Ring", typeof(Image));
            var ringR = ringGo.GetComponent<RectTransform>();
            ringR.SetParent(root, false);
            ringR.anchoredPosition = new Vector2(0, -220);
            ringR.sizeDelta = new Vector2(92, 92);
            ring = ringGo.GetComponent<Image>();
            ring.sprite = ringSprite;
            ring.color = new Color(0.45f, 0.95f, 0.55f);
            ring.type = Image.Type.Filled;
            ring.fillMethod = Image.FillMethod.Radial360;
            ring.fillOrigin = (int)Image.Origin360.Top;
            ring.fillAmount = 0f;

            var labelGo = new GameObject("Label", typeof(Text));
            var labelR = labelGo.GetComponent<RectTransform>();
            labelR.SetParent(root, false);
            labelR.anchoredPosition = new Vector2(0, -280);
            labelR.sizeDelta = new Vector2(600, 34);
            label = labelGo.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 26;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(1, 1, 1, 0.9f);

            var shadow = labelGo.AddComponent<Shadow>();
            shadow.effectColor = new Color(0, 0, 0, 0.85f);

            canvasGo.SetActive(false);
            root = canvasGo.GetComponent<RectTransform>();
            canvasGo.SetActive(true);
            // Hide initially
            canvasGo.SetActive(false);
        }

        /// <summary>Procedural ring sprite: white annulus, 360-radial fillable.</summary>
        static Sprite MakeRingSprite()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float outer = size * 0.48f;
            float inner = size * 0.34f;
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - size * 0.5f + 0.5f;
                    float dy = y - size * 0.5f + 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = (r <= outer && r >= inner) ? 1f : 0f;
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
