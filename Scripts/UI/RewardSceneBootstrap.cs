using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Locackthon.Core;

namespace Locackthon.UI
{
    /// Drives the 3-beat reward sequence in RewardScene, all by TAP on the full-screen Btn_Next:
    ///   STEP 1 (Scatter)  — char1..char4 sit scattered + dialog line. Tap → STEP 2.
    ///   STEP 2 (Merge)    — chars tween toward center, shrinking + fading; Img_MergeOrb fades in
    ///                       and grows at center. Dialog panel hidden this beat. Tap → STEP 3.
    ///   STEP 3 (Reveal)   — orb flashes/expands + fades out, Img_FinalSpirit fades in and scales up.
    ///                       Dialog shows lineBoss. Tap → EnterEnding() (unchanged handoff).
    /// Pure coroutine + Lerp tweens (anchoredPosition / localScale / alpha), no external libs.
    ///
    /// SCALE: the orb and final spirit animate RELATIVE to the localScale you author in the scene —
    /// that authored scale is captured at Start and used as the END (full) size, so the Inspector
    /// size is always the final size. They only grow UP to it (from a fraction); never forced to 1.
    public class RewardSceneBootstrap : MonoBehaviour
    {
        private enum Beat { Scatter, Merging, OrbFormed, Revealing, BossRevealed }

        [Header("Core wiring (existing)")]
        [Tooltip("Full-screen invisible button that advances each beat.")]
        [SerializeField] private Button nextButton;
        [Tooltip("The dialog line label (Txt_RewardLine). Text is swapped per beat.")]
        [SerializeField] private TMP_Text dialogText;
        [Tooltip("The dialog box (Panel_RewardDialog). Hidden during the merge beat, shown for scatter + boss.")]
        [SerializeField] private GameObject dialogPanel;
        [Tooltip("The large background blur glow (Blurr). Shown in step 1; hidden from the merge beat onward so it doesn't clash with the orb glow.")]
        [SerializeField] private GameObject blurOverlay;

        [Header("Step 1 — scattered spirits (existing char1..char4)")]
        [Tooltip("The 4 scattered spirit Images. They converge to the center during the merge.")]
        [SerializeField] private Image[] chars = new Image[4];

        [Header("Step 2 — merge orb (NEW, grey placeholder — assign glow art)")]
        [Tooltip("Img_MergeOrb. Starts inactive/transparent; fades in + grows to its AUTHORED scene scale.")]
        [SerializeField] private CanvasGroup mergeOrb;

        [Header("Step 3 — final boss spirit (NEW, grey placeholder — assign art)")]
        [Tooltip("Img_FinalSpirit. Starts inactive/transparent; fades in + scales up to its AUTHORED scene scale.")]
        [SerializeField] private CanvasGroup finalSpirit;

        [Header("Dialog lines (editable — line breaks are preserved exactly as typed)")]
        [TextArea] [SerializeField] private string lineScatter = "드디어 정령들 다 모았다!\n어엇?? 이게 무슨 일이지?";
        [TextArea] [SerializeField] private string lineBoss = "정조의 눈물이 흩날렸다!\n최종 보스를 향했다!";

        [Header("Merge tween timing")]
        [Tooltip("Where the spirits converge to (anchored position, canvas space).")]
        [SerializeField] private Vector2 mergeCenter = Vector2.zero;
        [Tooltip("Seconds for char1..char4 to travel to center while shrinking + fading.")]
        [SerializeField] private float mergeDuration = 1.5f;
        [Tooltip("Local-scale multiplier each char shrinks to as it reaches the center (relative to its start scale).")]
        [SerializeField] private float charEndScaleMul = 0.2f;
        [Tooltip("Orb starts the merge at this FRACTION of its authored scene scale, then grows UP to 1x authored.")]
        [SerializeField] private float orbStartFraction = 0.2f;

        [Header("Reveal tween timing")]
        [Tooltip("Seconds for the orb to flash/expand and fade out.")]
        [SerializeField] private float orbFlashDuration = 0.4f;
        [Tooltip("Multiplier the orb expands to during the flash, relative to its authored scene scale.")]
        [SerializeField] private float orbFlashScaleMul = 1.6f;
        [Tooltip("Seconds for the final spirit to fade in and scale up.")]
        [SerializeField] private float revealDuration = 1.2f;
        [Tooltip("Final spirit starts the reveal at this FRACTION of its authored scene scale, then grows UP to 1x authored.")]
        [SerializeField] private float finalStartFraction = 0.4f;

        [Header("Auto-advance (default OFF — each beat waits for a tap)")]
        [Tooltip("If true, STEP 2 auto-continues to STEP 3 after the orb forms (instead of waiting for a tap).")]
        [SerializeField] private bool mergeAutoAdvance = false;
        [SerializeField] private float mergeAutoAdvanceDelay = 1.0f;
        [Tooltip("If true, STEP 3 auto-advances to EndingScene after the boss is revealed (instead of waiting for a tap).")]
        [SerializeField] private bool bossAutoAdvance = false;
        [SerializeField] private float bossAutoAdvanceDelay = 1.5f;

        private Beat beat = Beat.Scatter;
        private bool animating;

        // Authored scene localScale of the orb / final spirit = the END (full) size of each tween.
        private Vector3 authoredOrbScale = Vector3.one;
        private Vector3 authoredFinalScale = Vector3.one;

        private void Awake()
        {
            if (nextButton != null) nextButton.onClick.AddListener(OnNextClicked);
            else Debug.LogWarning("[RewardSceneBootstrap] nextButton is not assigned.");
        }

        private void Start()
        {
            // Capture the authored (Inspector) scales FIRST — these are the final sizes the tweens reach.
            if (mergeOrb != null) authoredOrbScale = mergeOrb.transform.localScale;
            if (finalSpirit != null) authoredFinalScale = finalSpirit.transform.localScale;

            // Force the starting state regardless of how the scene was saved.
            if (mergeOrb != null) { mergeOrb.alpha = 0f; mergeOrb.gameObject.SetActive(false); }
            if (finalSpirit != null) { finalSpirit.alpha = 0f; finalSpirit.gameObject.SetActive(false); }
            if (chars != null)
                foreach (var c in chars)
                    if (c != null) { c.gameObject.SetActive(true); SetAlpha(c, 1f); }

            beat = Beat.Scatter;
            animating = false;
            if (dialogPanel != null) dialogPanel.SetActive(true);
            if (blurOverlay != null) blurOverlay.SetActive(true); // visible during scatter
            ApplyDialog(lineScatter);
        }

        public void OnNextClicked()
        {
            if (animating) return; // ignore taps mid-tween

            switch (beat)
            {
                case Beat.Scatter:      StartCoroutine(RunMerge());   break;
                case Beat.OrbFormed:    StartCoroutine(RunReveal());  break;
                case Beat.BossRevealed: GoToEnding();                 break;
                // Merging / Revealing: animating==true already guards this.
            }
        }

        // ---------- STEP 2: merge ----------
        private IEnumerator RunMerge()
        {
            animating = true;
            beat = Beat.Merging;
            // No merge-beat dialog: hide the whole dialog box (fallback: clear the text).
            if (dialogPanel != null) dialogPanel.SetActive(false);
            else if (dialogText != null) dialogText.text = string.Empty;

            // Hide the background blur so its glow doesn't clash with the orb (stays hidden through step 3).
            if (blurOverlay != null) blurOverlay.SetActive(false);

            // Snapshot each char's start anchoredPosition + scale.
            int n = chars != null ? chars.Length : 0;
            var startPos = new Vector2[n];
            var startScale = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                if (chars[i] == null) continue;
                var rt = chars[i].rectTransform;
                startPos[i] = rt.anchoredPosition;
                startScale[i] = rt.localScale;
            }

            Vector3 orbStart = authoredOrbScale * orbStartFraction;
            if (mergeOrb != null)
            {
                mergeOrb.gameObject.SetActive(true);
                mergeOrb.alpha = 0f;
                SetScale(mergeOrb, orbStart);
            }

            float dur = Mathf.Max(0.0001f, mergeDuration);
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / dur;
                float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                for (int i = 0; i < n; i++)
                {
                    if (chars[i] == null) continue;
                    var rt = chars[i].rectTransform;
                    rt.anchoredPosition = Vector2.Lerp(startPos[i], mergeCenter, e);
                    rt.localScale = Vector3.Lerp(startScale[i], startScale[i] * charEndScaleMul, e);
                    SetAlpha(chars[i], 1f - e);
                }
                if (mergeOrb != null)
                {
                    mergeOrb.alpha = e;
                    SetScale(mergeOrb, Vector3.Lerp(orbStart, authoredOrbScale, e)); // ends at authored size
                }
                yield return null;
            }

            // Settle: chars hidden, orb at its authored (full) scale.
            for (int i = 0; i < n; i++)
                if (chars[i] != null) chars[i].gameObject.SetActive(false);
            if (mergeOrb != null) { mergeOrb.alpha = 1f; SetScale(mergeOrb, authoredOrbScale); }

            beat = Beat.OrbFormed;
            animating = false;

            if (mergeAutoAdvance)
            {
                yield return new WaitForSeconds(mergeAutoAdvanceDelay);
                if (beat == Beat.OrbFormed && !animating) StartCoroutine(RunReveal());
            }
        }

        // ---------- STEP 3: boss reveal ----------
        private IEnumerator RunReveal()
        {
            animating = true;
            beat = Beat.Revealing;
            if (dialogPanel != null) dialogPanel.SetActive(true);
            ApplyDialog(lineBoss);

            // Orb flash: expand (relative to authored) + fade out.
            if (mergeOrb != null)
            {
                Vector3 flashTarget = authoredOrbScale * orbFlashScaleMul;
                float fdur = Mathf.Max(0.0001f, orbFlashDuration);
                float t = 0f;
                while (t < 1f)
                {
                    t += Time.deltaTime / fdur;
                    float e = Mathf.Clamp01(t);
                    SetScale(mergeOrb, Vector3.Lerp(authoredOrbScale, flashTarget, e));
                    mergeOrb.alpha = 1f - e;
                    yield return null;
                }
                mergeOrb.alpha = 0f;
                mergeOrb.gameObject.SetActive(false);
            }

            // Final spirit: fade in + scale up to its authored (full) scale.
            if (finalSpirit != null)
            {
                Vector3 finalStart = authoredFinalScale * finalStartFraction;
                finalSpirit.gameObject.SetActive(true);
                finalSpirit.alpha = 0f;
                SetScale(finalSpirit, finalStart);

                float rdur = Mathf.Max(0.0001f, revealDuration);
                float t = 0f;
                while (t < 1f)
                {
                    t += Time.deltaTime / rdur;
                    float e = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
                    finalSpirit.alpha = e;
                    SetScale(finalSpirit, Vector3.Lerp(finalStart, authoredFinalScale, e)); // ends at authored size
                    yield return null;
                }
                finalSpirit.alpha = 1f;
                SetScale(finalSpirit, authoredFinalScale);
            }

            beat = Beat.BossRevealed;
            animating = false;

            if (bossAutoAdvance)
            {
                yield return new WaitForSeconds(bossAutoAdvanceDelay);
                if (beat == Beat.BossRevealed) GoToEnding();
            }
        }

        private void GoToEnding()
        {
            Debug.Log("[RewardSceneBootstrap] Boss revealed → EndingScene.");
            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.EnterEnding();
            }
            else
            {
                Debug.LogWarning("[RewardSceneBootstrap] No GameStateManager — falling back to direct SceneManager load.");
                SceneManager.LoadScene("EndingScene");
            }
        }

        // ---------- helpers ----------
        // Apply dialog text exactly as authored. Real newlines (Enter in the TextArea) pass through
        // untouched; a literal backslash-n typed in the Inspector is also honored as a line break.
        private void ApplyDialog(string text)
        {
            if (dialogText == null) return;
            dialogText.text = text != null ? text.Replace("\\n", "\n") : string.Empty;
        }

        private static void SetAlpha(Image img, float a)
        {
            if (img == null) return;
            var c = img.color; c.a = a; img.color = c;
        }

        private static void SetScale(CanvasGroup g, Vector3 s)
        {
            if (g == null) return;
            g.transform.localScale = s;
        }
    }
}
