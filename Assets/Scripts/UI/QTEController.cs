using System.Collections;
using UnityEngine;

namespace ContentHouse.UI
{
    /// <summary>
    /// Quick-time events (spec Act IV: shove desk / smash window).
    /// Shows a pulsing prompt and waits for [E] within a time window.
    /// </summary>
    public class QTEController : MonoBehaviour
    {
        public bool LastResult { get; private set; }

        HUDController HUD => Core.GameManager.Instance != null ? Core.GameManager.Instance.HUD : null;

        /// <summary>Run a QTE. Check LastResult after the coroutine.</summary>
        public IEnumerator Run(string prompt, float window, KeyCode key = KeyCode.E)
        {
            if (HUD != null) HUD.SetQTEPrompt(prompt + "   [ " + key + " ]");
            LastResult = false;
            float t = 0f;
            while (t < window)
            {
                t += Time.deltaTime;
                if (Input.GetKeyDown(key)) { LastResult = true; break; }
                yield return null;
            }
            if (HUD != null) HUD.SetQTEPrompt("");
        }
    }
}
