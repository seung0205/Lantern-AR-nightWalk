using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Locackthon.Core;

namespace Locackthon.AR
{
    [RequireComponent(typeof(Button))]
    public class BackToLanternButton : MonoBehaviour
    {
        [Tooltip("Scene loaded as a fallback if no GameStateManager singleton exists (e.g. when AR scene was opened directly in the editor).")]
        [SerializeField] private string fallbackLanternScene = "LanternPrototype";

        private void Awake() => GetComponent<Button>().onClick.AddListener(OnClick);

        private void OnClick()
        {
            Debug.Log("[BackToLanternButton] Clicked.");
            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.ReturnToLantern(null);
            }
            else
            {
                Debug.LogWarning($"[BackToLanternButton] No GameStateManager instance — falling back to SceneManager.LoadScene(\"{fallbackLanternScene}\").");
                SceneManager.LoadScene(fallbackLanternScene);
            }
        }
    }
}
