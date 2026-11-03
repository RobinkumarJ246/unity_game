using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ContentHouse.UI
{
    /// <summary>
    /// Full-screen effects drawn above the world and HUD but below the phone:
    ///  - eyelids (SetEyelids 0 = open .. 1 = shut): two black lids that close from top and bottom,
    ///  - a colour flash (jumpscare hit),
    ///  - a short camera shake helper.
    /// Built lazily in code; one instance. Also hosts the demo end card.
    /// </summary>
    public class ScreenFx : MonoBehaviour
    {
        static ScreenFx inst;
        RectTransform top, bottom;
        Image flash;
        float lids, lidsTarget, lidSpeed = 6f;

        public static ScreenFx Instance
        {
            get
            {
                if (inst == null)
                {
                    var go = new GameObject("ScreenFx");
                    inst = go.AddComponent<ScreenFx>();
                    inst.Build();
                }
                return inst;
            }
        }

        void Build()
        {
            var root = UIFactory.CanvasRoot("ScreenFxCanvas", 480);
            root.SetParent(transform, false);
            var caster = root.GetComponent<GraphicRaycaster>(); if (caster != null) caster.enabled = false;

            top = MakeLid(root, true); bottom = MakeLid(root, false);
            flash = UIFactory.Image("Flash", root, new Color(1f, 1f, 1f, 0f));
            UIFactory.Stretch(flash.rectTransform); flash.raycastTarget = false;
            Apply();
        }

        static RectTransform MakeLid(RectTransform root, bool isTop)
        {
            var img = UIFactory.Image(isTop ? "LidTop" : "LidBottom", root, Color.black);
            img.raycastTarget = false;
            var r = img.rectTransform;
            r.anchorMin = new Vector2(0f, isTop ? 1f : 0f); r.anchorMax = new Vector2(1f, isTop ? 1f : 0f);
            r.pivot = new Vector2(0.5f, isTop ? 1f : 0f);
            r.sizeDelta = new Vector2(0f, 0f);
            return r;
        }

        /// <summary>0 = eyes open, 1 = shut. Follows smoothly unless snap = true.</summary>
        public void SetEyelids(float closed, bool snap = false, float speed = 6f)
        {
            lidsTarget = Mathf.Clamp01(closed); lidSpeed = speed;
            if (snap) { lids = lidsTarget; Apply(); }
        }

        public float Eyelids => lids;

        void Apply()
        {
            // each lid covers up to 52% of the height (a hair of overlap so no gap shows at the middle)
            float h = ((RectTransform)top.parent).rect.height * 0.52f * Mathf.SmoothStep(0f, 1f, lids);
            top.sizeDelta = new Vector2(0f, h); bottom.sizeDelta = new Vector2(0f, h);
        }

        void Update()
        {
            if (!Mathf.Approximately(lids, lidsTarget))
            {
                lids = Mathf.MoveTowards(lids, lidsTarget, lidSpeed * Time.unscaledDeltaTime);
                Apply();
            }
        }

        /// <summary>Instant colour hit that fades out.</summary>
        public IEnumerator Flash(Color c, float hold = 0.06f, float fade = 0.5f)
        {
            flash.color = c;
            yield return new WaitForSeconds(hold);
            for (float t = 0f; t < fade; t += Time.unscaledDeltaTime)
            {
                var k = flash.color; k.a = c.a * (1f - t / fade); flash.color = k;
                yield return null;
            }
            var z = flash.color; z.a = 0f; flash.color = z;
        }

        /// <summary>Shake a camera's local position for a moment (the camera must not be driven by the controller meanwhile).</summary>
        public static IEnumerator Shake(Transform cam, float seconds, float amount)
        {
            var basePos = cam.localPosition;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                float k = 1f - t / seconds;
                cam.localPosition = basePos + new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), 0f) * amount * k;
                yield return null;
            }
            cam.localPosition = basePos;
        }
    }
}
