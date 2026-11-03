using System.Collections.Generic;
using UnityEngine;

namespace ContentHouse.Player
{
    /// <summary>
    /// Dish-washing animation in first person: a plate held low in front of you, a sponge scrubbing it in
    /// circles, soap bubbles rising, and the grime on the plate fading as the task progresses.
    /// Primitives only (no assets), parented to the camera. Driven by SinkWashInteractable:
    /// Begin() -> SetProgress(0..1) every frame -> End().
    /// </summary>
    public class ScrubAnimation : MonoBehaviour
    {
        Transform plate, sponge;
        readonly List<Transform> grime = new List<Transform>();
        readonly List<Transform> bubbles = new List<Transform>();
        readonly List<float> bubbleAge = new List<float>();
        float progress, t, nextBubble;
        Material foam;

        static Material Mat(Color c, bool unlit = false)
        {
            var sh = Shader.Find(unlit ? "Unlit/Color" : "Standard");
            return new Material(sh) { color = c, hideFlags = HideFlags.HideAndDontSave };
        }

        static Transform Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material m)
        {
            var g = GameObject.CreatePrimitive(type);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos; g.transform.localScale = scale;
            var r = g.GetComponent<Renderer>(); r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return g.transform;
        }

        public static ScrubAnimation Begin(Camera cam)
        {
            if (cam == null) return null;
            var old = cam.GetComponentInChildren<ScrubAnimation>();
            if (old != null) Destroy(old.gameObject);
            var go = new GameObject("ScrubAnimation");
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0f, -0.20f, 0.55f);
            go.transform.localRotation = Quaternion.Euler(-28f, 0f, 0f);   // plate tipped up so you see its face
            go.transform.localScale = Vector3.one * 0.78f;
            var a = go.AddComponent<ScrubAnimation>();
            a.Build();
            return a;
        }

        void Build()
        {
            foam = Mat(new Color(0.96f, 0.98f, 1f));
            // the plate: white disc with a blue rim, flat face toward you (child of an angled root)
            plate = new GameObject("Plate").transform;
            plate.SetParent(transform, false);
            Part(PrimitiveType.Cylinder, plate, Vector3.zero, new Vector3(0.26f, 0.008f, 0.26f), Mat(new Color(0.20f, 0.42f, 0.78f)));
            Part(PrimitiveType.Cylinder, plate, new Vector3(0f, 0.005f, 0f), new Vector3(0.235f, 0.008f, 0.235f), Mat(new Color(0.93f, 0.93f, 0.90f)));
            // grime: dark smudges that shrink away as you scrub
            var grimeMat = Mat(new Color(0.30f, 0.22f, 0.12f));
            var spots = new[] { new Vector2(-0.05f, 0.04f), new Vector2(0.06f, 0.02f), new Vector2(0.01f, -0.06f), new Vector2(-0.07f, -0.04f), new Vector2(0.07f, -0.06f) };
            foreach (var s in spots)
            {
                var g = Part(PrimitiveType.Cylinder, plate, new Vector3(s.x, 0.011f, s.y), new Vector3(0.07f, 0.002f, 0.06f), grimeMat);
                grime.Add(g);
            }
            // the sponge: yellow body, green scouring pad
            sponge = new GameObject("Sponge").transform;
            sponge.SetParent(transform, false);
            Part(PrimitiveType.Cube, sponge, Vector3.zero, new Vector3(0.095f, 0.035f, 0.065f), Mat(new Color(0.95f, 0.82f, 0.18f)));
            Part(PrimitiveType.Cube, sponge, new Vector3(0f, -0.019f, 0f), new Vector3(0.097f, 0.012f, 0.067f), Mat(new Color(0.18f, 0.55f, 0.25f)));
            t = 0f;
        }

        public void SetProgress(float p) { progress = Mathf.Clamp01(p); }

        public static void End(Camera cam)
        {
            if (cam == null) return;
            foreach (var a in cam.GetComponentsInChildren<ScrubAnimation>()) Destroy(a.gameObject);
        }

        void Update()
        {
            t += Time.deltaTime;
            float slide = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.3f));            // slide up into view
            plate.localPosition = new Vector3(0f, -(1f - slide) * 0.18f, 0f);
            plate.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * 3f) * 4f, 0f);

            // sponge scrubs in circles over the plate, a little faster toward the end
            float speed = 7f + progress * 3f;
            float x = Mathf.Cos(t * speed) * 0.055f, z = Mathf.Sin(t * speed * 1.15f) * 0.045f;
            sponge.localPosition = plate.localPosition + new Vector3(x, 0.03f, z);
            sponge.localRotation = Quaternion.Euler(0f, Mathf.Sin(t * speed) * 25f, Mathf.Cos(t * speed) * 6f);

            // grime fades with progress (staggered so the plate gets clean gradually)
            for (int i = 0; i < grime.Count; i++)
            {
                float k = Mathf.Clamp01((progress - i * 0.12f) / 0.45f);
                float s = 1f - k;
                grime[i].localScale = new Vector3(0.07f * s, 0.002f, 0.06f * s);
                grime[i].gameObject.SetActive(s > 0.02f);
            }

            // bubbles
            nextBubble -= Time.deltaTime;
            if (nextBubble <= 0f && slide > 0.9f)
            {
                nextBubble = 0.07f;
                var b = Part(PrimitiveType.Sphere, transform, plate.localPosition + new Vector3(Random.Range(-0.1f, 0.1f), 0.03f, Random.Range(-0.1f, 0.1f)),
                             Vector3.one * Random.Range(0.012f, 0.03f), foam);
                bubbles.Add(b); bubbleAge.Add(0f);
            }
            for (int i = bubbles.Count - 1; i >= 0; i--)
            {
                bubbleAge[i] += Time.deltaTime;
                float a = bubbleAge[i];
                if (a > 1.1f) { Destroy(bubbles[i].gameObject); bubbles.RemoveAt(i); bubbleAge.RemoveAt(i); continue; }
                bubbles[i].localPosition += new Vector3(Mathf.Sin(a * 5f + i) * 0.0006f, Time.deltaTime * 0.07f, Time.deltaTime * -0.03f);
                float shrink = Mathf.Max(0.004f, bubbles[i].localScale.x * (1f - Time.deltaTime * 0.9f));   // pop away slowly
                bubbles[i].localScale = Vector3.one * shrink;
            }
        }
    }
}
