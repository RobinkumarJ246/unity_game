using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace ContentHouse.UI
{
    /// <summary>
    /// Epilogue overlay (spec section 6): typewriter text over black, then
    /// title card and restart / quit.
    /// </summary>
    public class EpilogueUI : MonoBehaviour
    {
        RectTransform root;
        Text body, card;
        Button restart, quit;

        void Awake()
        {
            var canvas = UIFactory.CanvasRoot("EpilogueCanvas", 300);
            root = canvas;

            var bg = UIFactory.Image("Bg", canvas, Color.black);
            UIFactory.Stretch(bg.rectTransform);

            body = UIFactory.Text("Body", canvas, "", 30, TextAnchor.MiddleLeft, new Color(0.9f, 0.9f, 0.95f));
            body.rectTransform.anchorMin = body.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            body.rectTransform.sizeDelta = new Vector2(1100, 700);
            body.rectTransform.anchoredPosition = Vector2.zero;

            card = UIFactory.Text("Card", canvas, "", 46, TextAnchor.MiddleCenter, new Color(1, 0.45f, 0.3f));
            card.rectTransform.anchorMin = card.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            card.rectTransform.sizeDelta = new Vector2(1200, 200);
            card.rectTransform.anchoredPosition = new Vector2(0, 0);

            restart = UIFactory.Button("Restart", canvas, "PLAY AGAIN", 26, new Color(0.4f, 0.12f, 0.1f));
            var rr = restart.GetComponent<RectTransform>();
            rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0f);
            rr.anchoredPosition = new Vector2(-140, 60);
            rr.sizeDelta = new Vector2(250, 60);

            quit = UIFactory.Button("Quit", canvas, "QUIT", 26, new Color(0.15f, 0.15f, 0.18f));
            var qr = quit.GetComponent<RectTransform>();
            qr.anchorMin = qr.anchorMax = new Vector2(0.5f, 0f);
            qr.anchoredPosition = new Vector2(140, 60);
            qr.sizeDelta = new Vector2(250, 60);
            quit.onClick.AddListener(Application.Quit);
            restart.onClick.AddListener(Restart);

            root.gameObject.SetActive(false);
        }

        void Restart()
        {
            Time.timeScale = 1f;
            UnityEngine.SceneManagement.SceneManager.LoadScene(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>Play the epilogue lines from the spec, then show the card.</summary>
        public IEnumerator PlayEpilogue()
        {
            string[] lines =
            {
                "The intruder was identified as " + Core.GameConfig.IntruderName + ", an unhinged viewer who had been tracking " + Core.GameConfig.StreamerName + "'s location for months.",
                "The clip of " + Core.GameConfig.StreamerName + " live-streaming our real-time home invasion went viral, gaining over 25 million views in 48 hours.",
                Core.GameConfig.StreamerName + " bought a mansion in Los Angeles with the ad revenue. I quit my job as his manager the very next morning.",
                "I still watch his streams sometimes... but now I keep a baseball bat right next to my bed.",
            };

            root.gameObject.SetActive(true);
            body.text = "";
            card.text = "";
            restart.gameObject.SetActive(false);
            quit.gameObject.SetActive(false);

            foreach (var line in lines)
            {
                for (int i = 1; i <= line.Length; i += 2)
                {
                    body.text = line.Substring(0, i);
                    yield return new WaitForSeconds(0.02f);
                }
                body.text = line;
                yield return new WaitForSeconds(1.6f);
                body.text += "\n\n";
            }

            body.gameObject.SetActive(false);
            card.text = Core.GameConfig.GameTitle;
            Audio.ProceduralAudio.PlayThunder(Vector3.zero, 0.5f);
            yield return new WaitForSeconds(2.2f);
            restart.gameObject.SetActive(true);
            quit.gameObject.SetActive(true);
        }
    }
}
