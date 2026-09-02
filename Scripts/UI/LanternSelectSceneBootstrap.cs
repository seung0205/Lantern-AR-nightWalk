using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Locackthon.Core;

namespace Locackthon.UI
{
    /// Wires runtime behaviour for the LanternSelectScene UI. Three panels drive the flow:
    ///   A: Panel_LanternCard       — 3-lantern carousel. 좌/우 arrows browse, 선택 confirms.
    ///   B: Panel_LanternGot        — lantern obtained card, → advances
    ///   C: Panel_SpiritsAppear     — spirits hint, → loads LanternPrototype
    ///
    /// CAROUSEL: each lantern is its OWN object in the Hierarchy (LanternCard_1/2/3), each
    /// holding its own image + name + description authored directly in the scene. The script
    /// NEVER writes text or sprites — it only toggles which card is active. Whatever you type
    /// or assign in the Hierarchy is exactly what shows in Play.
    ///
    /// Left/right arrows move currentIndex (clamped 0..count-1, no wrap) and grey out
    /// (interactable=false) at the ends. The 선택 button records the chosen index into
    /// GameStateManager and advances to the Got panel. The Got → SpiritsAppear →
    /// EnterLanternMain forward flow is unchanged.
    public class LanternSelectSceneBootstrap : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject panelGrid;
        [SerializeField] private GameObject panelGot;
        [SerializeField] private GameObject panelSpiritsAppear;

        [Header("Carousel cards (each is a full lantern authored in the Hierarchy)")]
        [Tooltip("The 3 lantern card objects. Only the one at currentIndex is active; the script toggles SetActive and never touches their contents.")]
        [SerializeField] private GameObject[] lanternCards = new GameObject[3];

        [Header("Selected-lantern display on Got / SpiritsAppear panels")]
        [Tooltip("Lantern_0/1/2 children on Panel_LanternGot's Img_Lantern. Index matches SelectedLanternIndex; only the matching one is shown. Sprites are authored in the Hierarchy — never written at runtime.")]
        [SerializeField] private GameObject[] gotLanterns = new GameObject[3];
        [Tooltip("Lantern_0/1/2 children on Panel_SpiritsAppear's Img_Lantern. Index matches SelectedLanternIndex; only the matching one is shown.")]
        [SerializeField] private GameObject[] spiritLanterns = new GameObject[3];
        [Tooltip("Text_0/1/2 message variants on Panel_LanternGot. Index matches SelectedLanternIndex; only the matching one is shown. Wording is authored in the Hierarchy — never written at runtime.")]
        [SerializeField] private GameObject[] gotTexts = new GameObject[3];

        [Header("Carousel navigation (Panel_LanternCard)")]
        [SerializeField] private Button leftArrow;    // Btn_LeftArrow  — previous lantern
        [SerializeField] private Button rightArrow;   // Btn_RightArrow — next lantern
        [SerializeField] private Button selectButton; // Btn_Select     — 선택 confirm

        [Header("Existing forward-flow arrows")]
        [SerializeField] private Button nextArrowA; // legacy Grid→Got arrow (optional now)
        [SerializeField] private Button nextArrowB;
        [SerializeField] private Button nextArrowC;

        private int currentIndex = 0;

        private void Awake()
        {
            // Carousel navigation.
            if (leftArrow != null) leftArrow.onClick.AddListener(MovePrev);
            else Debug.LogWarning("[LanternSelectSceneBootstrap] leftArrow is not assigned.");

            if (rightArrow != null) rightArrow.onClick.AddListener(MoveNext);
            else Debug.LogWarning("[LanternSelectSceneBootstrap] rightArrow is not assigned.");

            if (selectButton != null) selectButton.onClick.AddListener(ConfirmSelection);
            else Debug.LogWarning("[LanternSelectSceneBootstrap] selectButton is not assigned — cannot confirm a lantern.");

            // Legacy forward-flow arrow A still advances to Got if it's wired, but the
            // confirm path now runs through the dedicated 선택 button.
            if (nextArrowA != null) nextArrowA.onClick.AddListener(ShowGot);

            if (nextArrowB != null) nextArrowB.onClick.AddListener(ShowSpiritsAppear);
            else Debug.LogWarning("[LanternSelectSceneBootstrap] nextArrowB is not assigned.");

            if (nextArrowC != null) nextArrowC.onClick.AddListener(OnFinalArrowClicked);
            else Debug.LogWarning("[LanternSelectSceneBootstrap] nextArrowC is not assigned.");

            // Force the starting state in case the scene was saved with multiple panels active.
            ShowGrid();
            ShowCurrentCard();
        }

        public void ShowGrid()
        {
            SetState(showGrid: true, showGot: false, showSpirits: false);
        }

        public void ShowGot()
        {
            Debug.Log("[LanternSelectSceneBootstrap] → LanternGot panel.");
            SetState(showGrid: false, showGot: true, showSpirits: false);
            ShowSelectedLantern(gotLanterns, hideParentImage: true);
            // Texts live under Txt_LanternGot, whose Image IS the cream rounded text box —
            // never disable that parent, or the box vanishes. So hideParentImage stays false.
            ShowSelectedLantern(gotTexts, hideParentImage: false);
        }

        public void ShowSpiritsAppear()
        {
            Debug.Log("[LanternSelectSceneBootstrap] ArrowB → SpiritsAppear panel.");
            SetState(showGrid: false, showGot: false, showSpirits: true);
            ShowSelectedLantern(spiritLanterns, hideParentImage: true);
        }

        // Activates only the Lantern_N matching GameStateManager.SelectedLanternIndex and
        // deactivates the others. Pure SetActive — never touches any sprite.
        // Activates only the element matching SelectedLanternIndex and deactivates the others.
        // hideParentImage: ONLY for the lantern arrays (gotLanterns/spiritLanterns), whose shared
        // parent is Img_Lantern carrying a leftover 등불.png placeholder — disabling that parent
        // Image stops the fixed 등불 sitting behind the toggled children. NEVER pass true for the
        // text arrays: their parent (Txt_LanternGot) IS the cream rounded text-box frame, and
        // disabling it makes the box disappear. Default false so text calls are always safe.
        private void ShowSelectedLantern(GameObject[] lanterns, bool hideParentImage = false)
        {
            if (lanterns == null || lanterns.Length == 0) return;

            int idx = GameStateManager.Instance != null ? GameStateManager.Instance.SelectedLanternIndex : 0;
            idx = Mathf.Clamp(idx, 0, lanterns.Length - 1);

            for (int i = 0; i < lanterns.Length; i++)
            {
                if (lanterns[i] != null) lanterns[i].SetActive(i == idx);
            }

            if (!hideParentImage) return;

            // Disable ONLY the lantern placeholder parent Image (Img_Lantern). Toggles the
            // component's `enabled` only — never changes a sprite; per-lantern child art untouched.
            var firstParent = lanterns[0] != null ? lanterns[0].transform.parent : null;
            if (firstParent != null)
            {
                var parentImage = firstParent.GetComponent<Image>();
                if (parentImage != null) parentImage.enabled = false;
            }
        }

        public void OnFinalArrowClicked()
        {
            Debug.Log("[LanternSelectSceneBootstrap] ArrowC → LanternPrototype.");
            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.EnterLanternMain();
            }
            else
            {
                Debug.LogWarning("[LanternSelectSceneBootstrap] No GameStateManager — falling back to direct SceneManager load.");
                SceneManager.LoadScene("LanternPrototype");
            }
        }

        // --- Carousel ---------------------------------------------------------------------

        private void MovePrev()
        {
            currentIndex = Mathf.Clamp(currentIndex - 1, 0, CardCount - 1);
            ShowCurrentCard();
        }

        private void MoveNext()
        {
            currentIndex = Mathf.Clamp(currentIndex + 1, 0, CardCount - 1);
            ShowCurrentCard();
        }

        private int CardCount => lanternCards != null ? lanternCards.Length : 0;

        // Shows ONLY the current card (SetActive), and greys out the end arrows. Never
        // touches any text or sprite — the cards' authored content is left exactly as-is.
        private void ShowCurrentCard()
        {
            for (int i = 0; i < CardCount; i++)
            {
                if (lanternCards[i] != null)
                {
                    lanternCards[i].SetActive(i == currentIndex);
                }
            }

            // Grey out (do NOT hide) the arrows at the ends — keeps layout stable.
            if (leftArrow != null) leftArrow.interactable = currentIndex > 0;
            if (rightArrow != null) rightArrow.interactable = currentIndex < CardCount - 1;
        }

        // 선택 confirm: record the chosen lantern, then advance exactly like the old ArrowA.
        private void ConfirmSelection()
        {
            Debug.Log($"[LanternSelectSceneBootstrap] 선택 confirmed lantern index {currentIndex}.");
            if (GameStateManager.Instance != null)
            {
                GameStateManager.Instance.SetSelectedLanternIndex(currentIndex);
            }
            else
            {
                Debug.LogWarning("[LanternSelectSceneBootstrap] No GameStateManager — selected lantern index NOT recorded.");
            }
            ShowGot();
        }

        private void SetState(bool showGrid, bool showGot, bool showSpirits)
        {
            if (panelGrid          != null) panelGrid.SetActive(showGrid);
            if (panelGot           != null) panelGot.SetActive(showGot);
            if (panelSpiritsAppear != null) panelSpiritsAppear.SetActive(showSpirits);
        }
    }
}
