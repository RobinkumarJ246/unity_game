using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ContentHouse.UI
{
    /// <summary>
    /// All head-up feedback: objective, subtitles/dialogue queue, interaction
    /// prompt, held item, noise meter (while hiding), screen fades, red flash,
    /// QTE prompt, CCTV fullscreen overlay and death text.
    /// </summary>
    public class HUDController : MonoBehaviour
    {
        Text objective;
        Text objectiveLabel;
        Text subtitle;
        Text centerPrompt;
        Text heldItem;
        Text qtePrompt;
        Text deathText;
        Image noiseFill;
        RectTransform noiseRoot;
        Text noiseHint;
        Image fade;
        Image redFlash;
        Image feedOverlayDim;
        RawImage feedOverlay;
        Text feedLabel;

        readonly Queue<Sub> subs = new Queue<Sub>();
        class Sub { public string Who, Line; public float Dur; }

        void Awake()
        {
            var canvas = UIFactory.CanvasRoot("HUDCanvas", 100);
            Build(canvas);
        }

        void Build(RectTransform canvas)
        {
            objectiveLabel = UIFactory.Text("ObjLabel", canvas, "OBJECTIVE", 22, TextAnchor.UpperLeft, new Color(1, 0.8f, 0.3f, 0.9f));
            objectiveLabel.rectTransform.anchorMin = objectiveLabel.rectTransform.anchorMax = new Vector2(0, 1);
            objectiveLabel.rectTransform.pivot = new Vector2(0f, 1f);          // top-left pivot: box grows right/down
            objectiveLabel.rectTransform.anchoredPosition = new Vector2(28, -20);
            objectiveLabel.rectTransform.sizeDelta = new Vector2(700, 24);

            objective = UIFactory.Text("Obj", canvas, "", 30, TextAnchor.UpperLeft, Color.white);
            objective.rectTransform.anchorMin = objective.rectTransform.anchorMax = new Vector2(0, 1);
            objective.rectTransform.pivot = new Vector2(0f, 1f);               // top-left pivot
            objective.rectTransform.anchoredPosition = new Vector2(28, -52);
            objective.rectTransform.sizeDelta = new Vector2(700, 120);

            subtitle = UIFactory.Text("Subtitle", canvas, "", 34, TextAnchor.MiddleCenter, Color.white);
            subtitle.rectTransform.anchorMin = subtitle.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            subtitle.rectTransform.anchoredPosition = new Vector2(0, 130);
            subtitle.rectTransform.sizeDelta = new Vector2(1300, 150);

            centerPrompt = UIFactory.Text("CenterPrompt", canvas, "", 28, TextAnchor.MiddleCenter, new Color(1, 0.95f, 0.75f));
            centerPrompt.rectTransform.anchorMin = centerPrompt.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            centerPrompt.rectTransform.anchoredPosition = new Vector2(0, -160);
            centerPrompt.rectTransform.sizeDelta = new Vector2(760, 34);

            heldItem = UIFactory.Text("Held", canvas, "", 24, TextAnchor.LowerLeft, new Color(1, 1, 1, 0.75f));
            heldItem.rectTransform.anchorMin = heldItem.rectTransform.anchorMax = new Vector2(0, 0);
            heldItem.rectTransform.pivot = new Vector2(0f, 0f);               // bottom-left pivot
            heldItem.rectTransform.anchoredPosition = new Vector2(28, 24);
            heldItem.rectTransform.sizeDelta = new Vector2(700, 28);

            // Crosshair dot
            var dot = UIFactory.Image("Dot", canvas, new Color(1, 1, 1, 0.6f));
            dot.rectTransform.sizeDelta = new Vector2(5, 5);
            dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);

            // Noise meter (hidden until hiding)
            noiseRoot = UIFactory.NewRect("NoiseMeter", canvas);
            noiseRoot.anchorMin = noiseRoot.anchorMax = new Vector2(1, 0.5f);
            noiseRoot.anchoredPosition = new Vector2(-110, 0);
            noiseRoot.sizeDelta = new Vector2(46, 340);
            var meterBg = UIFactory.Image("MeterBg", noiseRoot, new Color(0, 0, 0, 0.6f));
            UIFactory.Stretch(meterBg.rectTransform);
            noiseFill = UIFactory.Image("MeterFill", noiseRoot, new Color(0.3f, 0.9f, 0.4f));
            noiseFill.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            noiseFill.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            noiseFill.rectTransform.pivot = new Vector2(0.5f, 0f);
            noiseFill.rectTransform.sizeDelta = new Vector2(30, 330);
            noiseHint = UIFactory.Text("NoiseHint", noiseRoot, "STAY SILENT", 20, TextAnchor.LowerCenter, new Color(1, 0.4f, 0.4f));
            noiseHint.rectTransform.anchorMin = new Vector2(0, 0); noiseHint.rectTransform.anchorMax = new Vector2(1, 0);
            noiseHint.rectTransform.anchoredPosition = new Vector2(0, -34);
            noiseHint.rectTransform.sizeDelta = new Vector2(0, 26);
            noiseRoot.gameObject.SetActive(false);

            // Separate top canvas for full-screen effects
            var fx = UIFactory.CanvasRoot("HUDFxCanvas", 400);

            feedOverlayDim = UIFactory.Image("FeedDim", fx, new Color(0, 0, 0, 0.85f));
            UIFactory.Stretch(feedOverlayDim.rectTransform);
            feedOverlay = UIFactory.RawImage("Feed", fx);
            UIFactory.Stretch(feedOverlay.rectTransform);
            feedOverlay.rectTransform.offsetMin = new Vector3(300, 160);
            feedOverlay.rectTransform.offsetMax = new Vector3(-300, -160);
            feedOverlay.color = Color.black;   // never white while textureless
            feedLabel = UIFactory.Text("FeedLabel", fx, "LIVE", 34, TextAnchor.UpperCenter, new Color(0.6f, 1f, 0.6f));
            feedLabel.rectTransform.anchorMin = feedLabel.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            feedLabel.rectTransform.anchoredPosition = new Vector2(0, -120);
            feedLabel.rectTransform.sizeDelta = new Vector2(0, 40);
            // Hidden until the Act IV cutscene shows the GoPro feed.
            feedOverlay.gameObject.SetActive(false);
            feedOverlayDim.gameObject.SetActive(false);
            feedLabel.gameObject.SetActive(false);

            redFlash = UIFactory.Image("RedFlash", fx, new Color(0.7f, 0.05f, 0.05f, 0f));
            UIFactory.Stretch(redFlash.rectTransform);

            qtePrompt = UIFactory.Text("QTE", fx, "", 42, TextAnchor.MiddleCenter, new Color(1, 0.9f, 0.5f));
            qtePrompt.rectTransform.anchorMin = qtePrompt.rectTransform.anchorMax = new Vector2(0.5f, 0.35f);
            qtePrompt.rectTransform.sizeDelta = new Vector2(1100, 120);

            fade = UIFactory.Image("Fade", fx, new Color(0, 0, 0, 0f));
            UIFactory.Stretch(fade.rectTransform);

            deathText = UIFactory.Text("Death", fx, "", 60, TextAnchor.MiddleCenter, new Color(0.9f, 0.1f, 0.1f));
            deathText.rectTransform.anchorMin = deathText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            deathText.rectTransform.sizeDelta = new Vector2(1100, 200);

            // The HUD is purely informational: its full-screen overlays must
            // never intercept clicks meant for the phone or menu canvases.
            var fxCaster = fx.GetComponent<GraphicRaycaster>();
            if (fxCaster != null) fxCaster.enabled = false;
            var hudCaster = canvas.GetComponent<GraphicRaycaster>();
            if (hudCaster != null) hudCaster.enabled = false;
        }

        void Update()
        {
            if (centerPrompt != null)
            {
                promptAlpha = Mathf.MoveTowards(promptAlpha, promptTarget, 8f * Time.deltaTime);
                var pc = centerPrompt.color; pc.a = promptAlpha; centerPrompt.color = pc;
            }
            // Noise meter live update while hiding.
            if (noiseRoot.gameObject.activeSelf && Mic.MicLoudnessDetection.Instance != null)
            {
                var mic = Mic.MicLoudnessDetection.Instance;
                float lvl = Mathf.Clamp01(mic.Level / 0.25f);
                noiseFill.rectTransform.sizeDelta = new Vector2(30, 330 * lvl);
                Color c = mic.Zone == 2 ? new Color(0.9f, 0.15f, 0.15f)
                    : mic.Zone == 1 ? new Color(0.95f, 0.75f, 0.2f)
                    : new Color(0.3f, 0.9f, 0.4f);
                noiseFill.color = Color.Lerp(noiseFill.color, c, 12f * Time.deltaTime);
                noiseHint.text = mic.Zone == 2 ? "EXPOSED - HE CAN HEAR YOU"
                    : mic.Zone == 1 ? "WHISPER QUIET" : "STAY SILENT";
            }
        }

        // ---------- API ----------

        public void SetObjective(string text) { if (objective != null) objective.text = text; }
        public void SetObjectiveLabel(string text) { if (objectiveLabel != null) objectiveLabel.text = text; }

        // Prompt fades in/out instead of popping; last text stays visible while fading out.
        string promptShown = "";
        float promptTarget, promptAlpha;

        public void SetCenterPrompt(string text)
        {
            if (!string.IsNullOrEmpty(text)) { promptShown = text; promptTarget = 1f; }
            else promptTarget = 0f;
            if (centerPrompt != null) centerPrompt.text = promptShown;
        }

        public void SetHeldItem(string item)
        {
            if (heldItem != null)
                heldItem.text = string.IsNullOrEmpty(item) ? "" : "Carrying: " + item;
        }

        public void ShowNoiseMeter(bool show)
        {
            if (noiseRoot != null) noiseRoot.gameObject.SetActive(show);
        }

        public void SetQTEPrompt(string text)
        {
            if (qtePrompt != null) qtePrompt.text = text ?? "";
        }

        public void ShowFeedOverlay(bool show, CCTV.CCTVCameraFeed feed)
        {
            if (feedOverlay == null) return;
            feedOverlay.gameObject.SetActive(show);
            if (feedOverlayDim != null) feedOverlayDim.gameObject.SetActive(show);
            if (feedLabel != null) feedLabel.gameObject.SetActive(show);
            if (show && feed != null)
            {
                feedOverlay.texture = feed.CurrentTexture;
                feed.SetViewing(true);
            }
        }

        public void QueueSubtitle(string who, string line, float duration = 3.2f)
        {
            subs.Enqueue(new Sub { Who = who, Line = line, Dur = duration });
            if (!processing) StartCoroutine(ProcessSubs());
        }

        public void ClearSubtitles() { subs.Clear(); subtitle.text = ""; }

        bool processing;
        IEnumerator ProcessSubs()
        {
            processing = true;
            while (subs.Count > 0)
            {
                var s = subs.Dequeue();
                subtitle.text = string.IsNullOrEmpty(s.Who) ? s.Line : "<b>" + s.Who + "</b>:  " + s.Line;
                yield return new WaitForSeconds(s.Dur);
            }
            subtitle.text = "";
            processing = false;
        }

        public IEnumerator FadeTo(float alpha, float seconds)
        {
            float start = fade.color.a;
            for (float t = 0; t < 1f; t += Time.deltaTime / Mathf.Max(0.01f, seconds))
            {
                fade.color = new Color(0, 0, 0, Mathf.Lerp(start, alpha, t));
                yield return null;
            }
            fade.color = new Color(0, 0, 0, alpha);
        }

        public IEnumerator FlashRed(float hold = 0.35f)
        {
            redFlash.color = new Color(0.7f, 0.05f, 0.05f, 0.55f);
            yield return new WaitForSeconds(hold);
            for (float t = 0; t < 1f; t += Time.deltaTime / 0.4f)
            {
                redFlash.color = new Color(0.7f, 0.05f, 0.05f, 0.55f * (1 - t));
                yield return null;
            }
            redFlash.color = new Color(0.7f, 0.05f, 0.05f, 0f);
        }

        public void ShowDeathText(bool show, string text = "HE GOT YOU")
        {
            if (deathText != null) { deathText.gameObject.SetActive(show); deathText.text = text; }
        }
    }
}
