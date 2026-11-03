using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ContentHouse.Player
{
    /// <summary>
    /// Shows what the player is carrying in the lower right of the view: the Gamer-Juice can and the
    /// pizza (your generated models, Resources/Generated). The pizza is cut into 8 wedges so each bite
    /// removes one. Parented to the camera; visual only (no colliders).
    /// Driven by GameManager.SetHeldItem; TvEatInteractable calls Bite().
    /// </summary>
    public class HeldItemView : MonoBehaviour
    {
        public const string Can = "Gamer-Juice can", Pizza = "leftover pizza", Package = "package";

        GameObject prop;
        string shown = "";
        float t;
        float biteT = 1f;
        readonly List<Transform> slices = new List<Transform>();
        static AudioClip crunch;

        public static HeldItemView Of(Camera cam)
        {
            if (cam == null) return null;
            var v = cam.GetComponentInChildren<HeldItemView>();
            if (v == null)
            {
                var go = new GameObject("HeldItemView");
                go.transform.SetParent(cam.transform, false);
                v = go.AddComponent<HeldItemView>();
            }
            return v;
        }

        /// <summary>Set (or clear, with null / "") the carried item for this camera.</summary>
        public static void Show(Camera cam, string item)
        {
            var v = Of(cam);
            if (v != null) v.Set(item ?? "");
        }

        public int SlicesLeft { get { int n = 0; foreach (var s in slices) if (s != null && s.gameObject.activeSelf) n++; return n; } }
        public int SliceTotal => slices.Count;

        static Material Fallback(Color c)
        {
            return new Material(Shader.Find("Standard")) { color = c, hideFlags = HideFlags.HideAndDontSave };
        }

        void Set(string item)
        {
            if (item == shown) return;
            shown = item;
            if (prop != null) Destroy(prop);
            prop = null; slices.Clear();
            if (item == "") return;

            if (item == Can)
            {
                prop = Spawn("Generated/juice_can", 0.20f);              // mesh is 1 unit tall -> 20 cm can
                transform.localPosition = new Vector3(0.22f, -0.22f, 0.5f);
                transform.localRotation = Quaternion.Euler(-8f, 18f, 6f);
            }
            else if (item == Pizza)
            {
                prop = Spawn("Generated/pizza_slices", 0.21f);           // ~1 unit wide -> 21 cm pizza
                transform.localPosition = new Vector3(0.15f, -0.25f, 0.60f);
                transform.localRotation = Quaternion.Euler(-52f, 12f, 0f);   // tipped up toward you so you see the toppings
                if (prop != null)
                    foreach (Transform c in prop.transform) slices.Add(c);
                // eat from the near edge first: nearest to the front (-Z in prop space) goes first
                slices.Sort((a, b) => Score(a).CompareTo(Score(b)));
            }
            else if (item == Package)
            {
                prop = Spawn("Generated/package_box", 0.7f);              // prefab is 38 cm; carried a bit smaller
                transform.localPosition = new Vector3(0.17f, -0.30f, 0.62f);
                transform.localRotation = Quaternion.Euler(-10f, -16f, 0f);
            }
            else
            {
                prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(prop.GetComponent<Collider>());
                prop.GetComponent<Renderer>().sharedMaterial = Fallback(new Color(0.6f, 0.6f, 0.65f));
                prop.transform.localScale = Vector3.one * 0.12f;
                transform.localPosition = new Vector3(0.22f, -0.24f, 0.5f);
                transform.localRotation = Quaternion.Euler(-10f, 20f, 0f);
            }
            if (prop != null) prop.transform.SetParent(transform, false);
            restPos = transform.localPosition; restRot = transform.localRotation;   // where the item rests in your view
            t = 0f; biteT = 1f; eatK = 0f; eatTarget = 0f;
        }

        static float Score(Transform s)
        {
            var mf = s.GetComponent<MeshFilter>();
            var c = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds.center : Vector3.zero;
            return Vector2.Distance(new Vector2(c.x, c.z), new Vector2(0f, -0.5f));   // distance to the near edge
        }

        GameObject Spawn(string path, float size)
        {
            var prefab = Resources.Load<GameObject>(path);
            GameObject go;
            if (prefab != null) go = Instantiate(prefab);
            else
            {   // missing asset: a plain placeholder so the game still works
                go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.GetComponent<Renderer>().sharedMaterial = Fallback(Color.grey);
                go.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            }
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localScale = Vector3.one * size;
            return go;
        }

        // ---- eating: the pizza is slowly brought up to your mouth while the hold ring fills ----
        float eatK, eatTarget;
        Vector3 restPos; Quaternion restRot;

        /// <summary>0..1 while the hold ring fills: how far the pizza has travelled toward your mouth.</summary>
        public void SetBiteProgress(float p) { if (shown == Pizza) eatTarget = Mathf.Clamp01(p); }

        /// <summary>The bite is done: the nearest wedge is gone and the pizza comes back down. Returns slices left.</summary>
        public int FinishBite()
        {
            if (shown != Pizza) return 0;
            foreach (var s in slices)
                if (s != null && s.gameObject.activeSelf)
                {
                    s.gameObject.SetActive(false);
                    break;
                }
            eatTarget = 0f;      // lower the pizza again for the next slice
            return SlicesLeft;
        }

        /// <summary>Let go early: the pizza goes back down, nothing eaten.</summary>
        public void CancelBite() { eatTarget = 0f; }

        static void EnsureCrunch()
        {
            if (crunch != null) return;
            const int rate = 22050; int n = (int)(rate * 0.28f);
            var d = new float[n]; var rng = new System.Random(11);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float tt = i / (float)rate;
                // two or three sharp crackles with a fast decay each, over a soft low thud
                float env = Mathf.Exp(-tt * 38f) + 0.8f * Mathf.Exp(-Mathf.Max(0f, tt - 0.07f) * 45f) * (tt > 0.07f ? 1f : 0f)
                          + 0.6f * Mathf.Exp(-Mathf.Max(0f, tt - 0.15f) * 50f) * (tt > 0.15f ? 1f : 0f);
                float w = (float)(rng.NextDouble() * 2 - 1);
                lp += (w - lp) * 0.55f;
                d[i] = (w * 0.6f + lp * 0.4f) * env;
            }
            float peak = 0.0001f; foreach (var x in d) peak = Mathf.Max(peak, Mathf.Abs(x));
            for (int i = 0; i < n; i++) d[i] *= 0.9f / peak;
            crunch = AudioClip.Create("bite_crunch", n, 1, rate, false);
            crunch.SetData(d, 0);
        }

        void Update()
        {
            if (prop == null) return;
            t += Time.deltaTime;
            float slide = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.25f));
            var pos = new Vector3(0f, Mathf.Sin(Time.time * 1.6f) * 0.004f - (1f - slide) * 0.12f, 0f);
            // smooth follow of the hold progress (and a gentle chewing wobble near the end)
            eatK = Mathf.Lerp(eatK, eatTarget, 1f - Mathf.Exp(-9f * Time.deltaTime));
            float k = Mathf.SmoothStep(0f, 1f, eatK);
            prop.transform.localPosition = pos;                            // slide-in + sway, in the prop's own space

            // the eating move happens on the holder, which lives in CAMERA space (x right, y up, z forward):
            // from its resting spot at the lower right up to just below your mouth, close to the lens
            var mouth = new Vector3(0.03f, -0.10f, 0.30f);
            var mouthRot = Quaternion.Euler(-12f, 0f, 0f);
            Vector3 p = Vector3.Lerp(restPos, mouth, k);
            if (eatK > 0.82f) p.y += Mathf.Sin(Time.time * 26f) * 0.004f;  // nibble
            transform.localPosition = p;
            transform.localRotation = Quaternion.Slerp(restRot, mouthRot, k);
        }
    }
}
