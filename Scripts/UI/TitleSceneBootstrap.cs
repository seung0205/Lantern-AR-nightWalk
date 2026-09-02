using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Locackthon.Core;

namespace Locackthon.UI
{
    /// Wires runtime behaviour for the TitleScene UI. The Canvas + Img_Background are
    /// authored directly in the scene. Two groups drive a simple splash → title flow:
    ///   Panel_Splash — shown first for splashDuration seconds (spirit/logo splash)
    ///   Panel_Title  — the 로티니 GO logo + 시작하기 button (+ language button later)
    /// On Start the splash shows and the title hides; after splashDuration the swap
    /// reverses. Plain SetActive, no fade. The Btn_Start click handler (unchanged) asks
    /// GameStateManager to load LanternSelectScene.
    public class TitleSceneBootstrap : MonoBehaviour
    {
        [Header("Buttons")]
        [SerializeField] private Button startButton;

        [Header("Splash → Title flow")]
        [SerializeField] private GameObject panelSplash;
        [SerializeField] private GameObject panelTitle;
        [Tooltip("Seconds the splash stays up before the title appears.")]
        [SerializeField] private float splashDuration = 2.5f;

        private void Awake()
        {
            if (startButton != null) startButton.onClick.AddListener(OnStartClicked);
            else Debug.LogWarning("[TitleSceneBootstrap] startButton is not assigned.");
        }

        private void Start()
        {
            // Splash first, title hidden.
            if (panelSplash != null) panelSplash.SetActive(true);
            else Debug.LogWarning("[TitleSceneBootstrap] panelSplash is not assigned.");

            if (panelTitle != null) panelTitle.SetActive(false);
            else Debug.LogWarning("[TitleSceneBootstrap] panelTitle is not assigned.");

            StartCoroutine(SplashThenTitle());
        }

        private IEnumerator SplashThenTitle()
        {
            yield return new WaitForSeconds(splashDuration);

            if (panelSplash != null) panelSplash.SetActive(false);
            if (panelTitle != null) panelTitle.SetActive(true);
            Debug.Log("[TitleSceneBootstrap] Splash finished → Title panel shown.");
        }

        public void OnStartClicked()
        {
            Debug.Log("[TitleSceneBootstrap] Start clicked → LanternSelectScene.");
            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.EnterLanternSelect();
            }
            else
            {
                Debug.LogWarning("[TitleSceneBootstrap] No GameStateManager — falling back to direct SceneManager load.");
                SceneManager.LoadScene("LanternSelectScene");
            }
        }
    }
}
