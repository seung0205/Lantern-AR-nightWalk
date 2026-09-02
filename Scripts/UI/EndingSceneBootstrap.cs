using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Locackthon.UI
{
    /// Wires runtime behaviour for EndingScene.
    ///
    /// STATE A (Panel_EndingNarrative): the narrative text (Txt_EndingStory) reveals CHARACTER BY
    /// CHARACTER via a coroutine that raises TMP's maxVisibleCharacters (so the authored text /
    /// rich-text / line breaks are untouched — only visibility animates). Tapping Btn_Next while it
    /// is still typing INSTANTLY completes the text; tapping again (after it's fully shown) advances
    /// to STATE B.
    ///
    /// STATE B (Panel_EndingFinal): the END screen. Btn_ShareSNS opens Android's native share sheet
    /// (ACTION_SEND, text/plain) with an editable caption. In the Editor it just logs the caption.
    public class EndingSceneBootstrap : MonoBehaviour
    {
        [Header("Panels & buttons (authored in scene)")]
        [SerializeField] private GameObject panelNarrative;
        [SerializeField] private GameObject panelFinal;
        [SerializeField] private Button nextButton;
        [SerializeField] private Button shareSnsButton;

        [Header("Typewriter")]
        [Tooltip("The narrative TMP label (Txt_EndingStory). Its authored text is revealed char-by-char.")]
        [SerializeField] private TMP_Text narrativeText;
        [Tooltip("Reveal speed in characters per second. Higher = faster.")]
        [SerializeField] private float charsPerSecond = 30f;

        [Header("SNS share")]
        [Tooltip("Caption shared to the native Android share sheet (text-only for now).")]
        [TextArea]
        [SerializeField] private string shareCaption = "등불야행에서 수원의 정령을 모두 모았어요! #등불야행 #수원화성";

        private Coroutine typeRoutine;
        private bool isTyping;     // true while characters are still being revealed
        private bool textComplete; // true once the full narrative is shown

        private void Awake()
        {
            if (nextButton     != null) nextButton.onClick.AddListener(OnNextClicked);
            else Debug.LogWarning("[EndingSceneBootstrap] nextButton is not assigned.");

            if (shareSnsButton != null) shareSnsButton.onClick.AddListener(OnShareClicked);
            else Debug.LogWarning("[EndingSceneBootstrap] shareSnsButton is not assigned.");
        }

        private void Start()
        {
            // Force the starting state in case the scene was saved with both panels active.
            ShowNarrative();
        }

        public void ShowNarrative()
        {
            if (panelNarrative != null) panelNarrative.SetActive(true);
            if (panelFinal     != null) panelFinal.SetActive(false);

            // Begin the char-by-char reveal of the authored narrative text.
            if (typeRoutine != null) StopCoroutine(typeRoutine);
            if (narrativeText != null) typeRoutine = StartCoroutine(TypeNarrative());
            else { isTyping = false; textComplete = true; }
        }

        private IEnumerator TypeNarrative()
        {
            isTyping = true;
            textComplete = false;

            // Build the glyph layout without changing the authored string, then hide all glyphs.
            narrativeText.ForceMeshUpdate();
            int total = narrativeText.textInfo.characterCount;
            narrativeText.maxVisibleCharacters = 0;

            float shown = 0f;
            float cps = Mathf.Max(0.0001f, charsPerSecond);
            while (shown < total)
            {
                shown += cps * Time.deltaTime;
                narrativeText.maxVisibleCharacters = Mathf.Min(total, Mathf.FloorToInt(shown));
                yield return null;
            }

            CompleteText();
        }

        // Show every character immediately (used when the player taps mid-typing, or at the end).
        private void CompleteText()
        {
            if (typeRoutine != null) { StopCoroutine(typeRoutine); typeRoutine = null; }
            if (narrativeText != null) narrativeText.maxVisibleCharacters = int.MaxValue;
            isTyping = false;
            textComplete = true;
        }

        // Btn_Next gate: tap while typing finishes the text; tap after it's complete advances to END.
        public void OnNextClicked()
        {
            if (isTyping) { CompleteText(); return; }
            if (textComplete) ShowFinal();
        }

        public void ShowFinal()
        {
            Debug.Log("[EndingSceneBootstrap] Narrative complete → final panel.");
            if (panelNarrative != null) panelNarrative.SetActive(false);
            if (panelFinal     != null) panelFinal.SetActive(true);
        }

        // ---------- SNS share (text-only) ----------
        public void OnShareClicked()
        {
            ShareText(shareCaption);
        }

        /// Opens the native Android share sheet (ACTION_SEND chooser) with text only. Structured so
        /// an image path can be added later (switch to EXTRA_STREAM + a content:// Uri via
        /// FileProvider, setType image/*).
        private void ShareText(string caption)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // NOTE: do NOT wrap these AndroidJavaObjects in `using`. startActivity is dispatched
                // to the Android UI thread below (see runOnUiThread), so the chooser/activity must
                // still be alive when that runnable actually runs — disposing them here would race
                // the dispatch and the sheet would silently fail to appear.
                var intentClass  = new AndroidJavaClass("android.content.Intent");
                var intentObject = new AndroidJavaObject("android.content.Intent");
                intentObject.Call<AndroidJavaObject>("setAction", intentClass.GetStatic<string>("ACTION_SEND"));
                intentObject.Call<AndroidJavaObject>("setType", "text/plain");
                intentObject.Call<AndroidJavaObject>("putExtra", intentClass.GetStatic<string>("EXTRA_TEXT"), caption);
                // TODO (image keepsake): also putExtra(EXTRA_STREAM, uri) + setType("image/*")
                //                        once a screenshot is captured and exposed via FileProvider.

                // Wrap in a chooser so the app picker (카톡/Instagram/Facebook/…) appears every time
                // instead of defaulting to a previously-chosen app.
                var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intentObject, "공유하기");
                chooser.Call<AndroidJavaObject>("addFlags", intentClass.GetStatic<int>("FLAG_ACTIVITY_NEW_TASK"));

                var unityPlayer     = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");

                // CRITICAL: startActivity for a chooser must run on the Android UI thread. Calling it
                // from Unity's thread is what makes the share sheet silently not appear on many
                // devices (it looks like "nothing happens / log only"). Dispatch it explicitly.
                currentActivity.Call("runOnUiThread", new AndroidJavaRunnable(() =>
                {
                    try
                    {
                        currentActivity.Call("startActivity", chooser);
                        Debug.Log("[EndingSceneBootstrap] Android share sheet launched (chooser).");
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogError($"[EndingSceneBootstrap] startActivity (UI thread) failed: {e.Message}");
                    }
                }));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[EndingSceneBootstrap] Android share failed: {e.Message}");
            }
#else
            // No Android intent off-device — log the caption as a preview.
            Debug.Log($"[EndingSceneBootstrap] (Editor preview) Share text:\n{caption}");
#endif
        }
    }
}
