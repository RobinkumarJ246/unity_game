using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ContentHouse.UI
{
    /// <summary>
    /// "End of demo" card shown after Day 1. Fades in over black, then waits: Enter / Space / click returns to
    /// the main menu (reloads the scene), Esc does too.
    /// </summary>
    public class DemoEndScreen : MonoBehaviour
    {
        public static IEnumerator Show(MonoBehaviour host)
        {
            var go = new GameObject("DemoEndScreen");
            var s = go.AddComponent<DemoEndScreen>();
            yield return host.StartCoroutine(s.Run());
        }

        IEnumerator Run()
        {
            var root = UIFactory.CanvasRoot("DemoEndCanvas", 600);
            root.SetParent(transform, false);
            var bg = UIFactory.Image("Bg", root, Color.black); UIFactory.Stretch(bg.rectTransform);
            var group = root.gameObject.AddComponent<CanvasGroup>(); group.alpha = 0f;

            var title = UIFactory.Text("Title", root, "CONTENT HOUSE", 78, TextAnchor.MiddleCenter, new Color(0.95f, 0.92f, 0.85f));
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 0.6f);
            title.rectTransform.sizeDelta = new Vector2(1400, 110); title.fontStyle = FontStyle.Bold;

            var sub = UIFactory.Text("Sub", root, "END OF DEMO", 40, TextAnchor.MiddleCenter, new Color(1f, 0.72f, 0.35f));
            sub.rectTransform.anchorMin = sub.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            sub.rectTransform.sizeDelta = new Vector2(1200, 60);

            var thanks = UIFactory.Text("Thanks", root, "Thanks for playing. Whatever was on that tape, the night isn't over.", 28,
                TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.65f));
            thanks.rectTransform.anchorMin = thanks.rectTransform.anchorMax = new Vector2(0.5f, 0.4f);
            thanks.rectTransform.sizeDelta = new Vector2(1400, 50);

            var hint = UIFactory.Text("Hint", root, "[Enter]  back to the main menu", 24, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.45f));
            hint.rectTransform.anchorMin = hint.rectTransform.anchorMax = new Vector2(0.5f, 0.12f);
            hint.rectTransform.sizeDelta = new Vector2(1000, 40);

            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            for (float t = 0f; t < 2.5f; t += Time.unscaledDeltaTime) { group.alpha = t / 2.5f; yield return null; }
            group.alpha = 1f;
            yield return new WaitForSeconds(1.0f);

            while (!(Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)
                     || Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(0)))
            {
                var c = hint.color; c.a = 0.3f + 0.2f * Mathf.Sin(Time.unscaledTime * 3f); hint.color = c;
                yield return null;
            }
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0 ? SceneManager.GetActiveScene().buildIndex : 0);
            // not in build settings (editor test): reload by path
            if (SceneManager.GetActiveScene().buildIndex < 0)
                SceneManager.LoadScene(SceneManager.GetActiveScene().path);
        }
    }
}
