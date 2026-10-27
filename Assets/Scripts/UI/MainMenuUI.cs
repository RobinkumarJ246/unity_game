using UnityEngine;
using UnityEngine.UI;

namespace ContentHouse.UI
{
    /// <summary>
    /// Main menu overlay: player name customization (default: Steve),
    /// microphone toggle for the hiding mechanic, controls reference.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        InputField nameField;
        Toggle micToggle;
        RectTransform root;

        void Awake()
        {
            UIFactory.EnsureEventSystem();   // buttons dead without one
            var canvas = UIFactory.CanvasRoot("MainMenuCanvas", 200);
            root = canvas;

            var bg = UIFactory.Image("Bg", canvas, new Color(0.01f, 0.01f, 0.03f, 1f));
            UIFactory.Stretch(bg.rectTransform);

            var title = UIFactory.Text("Title", canvas, Core.GameConfig.GameTitle, 84, TextAnchor.MiddleCenter, Color.white);
            title.rectTransform.anchorMin = title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            title.rectTransform.anchoredPosition = new Vector2(0, -140);
            title.rectTransform.sizeDelta = new Vector2(1200, 100);

            var sub = UIFactory.Text("Sub", canvas, Core.GameConfig.GameSubtitle, 28, TextAnchor.MiddleCenter, new Color(1, 0.5f, 0.3f, 0.9f));
            sub.rectTransform.anchorMin = sub.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            sub.rectTransform.anchoredPosition = new Vector2(0, -230);
            sub.rectTransform.sizeDelta = new Vector2(1200, 40);

            var nameLabel = UIFactory.Text("NameLabel", canvas, "Your name (you play as his assistant):", 26, TextAnchor.MiddleLeft, new Color(1, 1, 1, 0.8f));
            nameLabel.rectTransform.anchorMin = nameLabel.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            nameLabel.rectTransform.pivot = new Vector2(0.5f, 0f);
            nameLabel.rectTransform.anchoredPosition = new Vector2(0, -30);
            nameLabel.rectTransform.sizeDelta = new Vector2(560, 30);

            nameField = UIFactory.InputField("NameField", canvas, Core.GameConfig.DefaultPlayerName);
            nameField.text = PlayerPrefs.GetString(Core.GameConfig.PlayerNameKey, Core.GameConfig.DefaultPlayerName);
            nameField.characterLimit = 20;
            nameField.textComponent.fontSize = 30;
            var fr = nameField.GetComponent<RectTransform>();
            fr.anchorMin = fr.anchorMax = new Vector2(0.5f, 0.5f);
            fr.pivot = new Vector2(0.5f, 1f);
            fr.anchoredPosition = new Vector2(0, -64);
            fr.sizeDelta = new Vector2(560, 56);

            micToggle = UIFactory.Toggle("MicToggle", canvas, "Use microphone while hiding (off = hold-breath buttons)", Core.GameConfig.UseMic);
            var mr = micToggle.GetComponent<RectTransform>();
            mr.anchorMin = mr.anchorMax = new Vector2(0.5f, 0.5f);
            mr.anchoredPosition = new Vector2(0, -150);
            mr.sizeDelta = new Vector2(640, 34);

            var start = UIFactory.Button("Start", canvas, "START THE SUBATHON", 32, new Color(0.55f, 0.12f, 0.1f));
            var sr = start.GetComponent<RectTransform>();
            sr.anchorMin = sr.anchorMax = new Vector2(0.5f, 0.5f);
            sr.anchoredPosition = new Vector2(0, -230);
            sr.sizeDelta = new Vector2(480, 72);
            start.onClick.AddListener(BeginGame);

            var quit = UIFactory.Button("Quit", canvas, "QUIT", 22, new Color(0.15f, 0.15f, 0.18f));
            var qr = quit.GetComponent<RectTransform>();
            qr.anchorMin = qr.anchorMax = new Vector2(0.5f, 0f);
            qr.anchoredPosition = new Vector2(0, 40);
            qr.sizeDelta = new Vector2(240, 48);
            quit.onClick.AddListener(Application.Quit);

            var controls = UIFactory.Text("Controls", canvas,
                "WASD move   |   Mouse look   |   [E] interact   |   [C] crouch   |   SHIFT run\n[TAB] phone   |   [SPACE] loud breathing (mic off)",
                22, TextAnchor.LowerCenter, new Color(1, 1, 1, 0.55f));
            controls.rectTransform.anchorMin = controls.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            controls.rectTransform.anchoredPosition = new Vector2(0, 110);
            controls.rectTransform.sizeDelta = new Vector2(1400, 70);

            var warning = UIFactory.Text("Warning", canvas,
                "Headphones recommended. The game listens to your microphone while you hide.",
                20, TextAnchor.LowerCenter, new Color(1, 0.6f, 0.2f, 0.7f));
            warning.rectTransform.anchorMin = warning.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            warning.rectTransform.anchoredPosition = new Vector2(0, 76);
            warning.rectTransform.sizeDelta = new Vector2(1400, 26);
        }

        void BeginGame()
        {
            var name = nameField.text != null ? nameField.text.Trim() : "";
            PlayerPrefs.SetString(Core.GameConfig.PlayerNameKey, string.IsNullOrEmpty(name) ? Core.GameConfig.DefaultPlayerName : name);
            PlayerPrefs.SetInt(Core.GameConfig.UseMicKey, micToggle != null && micToggle.isOn ? 1 : 0);
            PlayerPrefs.Save();
            Audio.ProceduralAudio.PlayClick(Vector3.zero);

            root.gameObject.SetActive(false);
            var gm = Core.GameManager.Instance;
            if (gm != null && gm.Story != null)
            {
                if (NarrationScreen.Instance != null)
                    gm.StartCoroutine(IntroThenStory());
                else
                    gm.Story.BeginGame();
            }
        }

        /// <summary>Fears-to-Fathom opening narration plays before gameplay begins.</summary>
        System.Collections.IEnumerator IntroThenStory()
        {
            yield return StartCoroutine(NarrationScreen.Instance.Play("intro"));
            var gm = Core.GameManager.Instance;
            if (gm != null && gm.Story != null) gm.Story.BeginGame();
        }
    }
}
