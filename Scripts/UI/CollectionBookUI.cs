using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Locackthon.Core;
using Locackthon.Data;

namespace Locackthon.UI
{
    /// 도감 (Collection Book) view. ALL visuals are real serialized scene objects (authored in
    /// LanternPrototype, editable in the Inspector). This script only DRIVES them at runtime:
    ///   - fills each of the 4 grid cells from registry[i] + caught state (color vs silhouette + "???"),
    ///   - sets the header count,
    ///   - opens/closes the detail view and fills its image/name/description/stat/위치/일시.
    /// It never creates GameObjects, so layout/fonts/colors/sprites are tweakable in the editor.
    /// devUnlockAll only changes what is DISPLAYED as caught — CollectionDatabase JSON is untouched.
    public class CollectionBookUI : MonoBehaviour
    {
        // One grid cell = its Button + the icon Image + the name label. Cell index maps to
        // registry index (Cell 0 → registry.All[0], etc.).
        [System.Serializable]
        public class SpiritCell
        {
            public Button button;
            public Image icon;
            public TMP_Text nameLabel;
        }

        [Header("Grid view (scene objects)")]
        [Tooltip("The whole Collection overlay (Panel_Collection). Toggled by Show/Hide.")]
        [SerializeField] private GameObject root;
        [Tooltip("DECORATION only (not a button): large featured spirit image at the top. Bound to the last-caught spirit at runtime.")]
        [SerializeField] private Image featuredImage;
        [Tooltip("DECORATION only: featured spirit NAME label (e.g. Img_Name/Spirit_Name). Bound to the SAME spirit as featuredImage so image and name always match.")]
        [SerializeField] private TMP_Text featuredNameText;
        [Tooltip("Header label — set to \"N/4개 수집\" at runtime.")]
        [SerializeField] private TMP_Text counterText;
        [SerializeField] private Button closeButton;
        [Tooltip("The 4 grid cells, in registry order (0..3).")]
        [SerializeField] private SpiritCell[] cells = new SpiritCell[4];

        [Header("Detail view (scene objects)")]
        [SerializeField] private GameObject detailRoot;
        [SerializeField] private Image detailSpiritImage;
        [SerializeField] private TMP_Text detailName;
        [SerializeField] private TMP_Text detailDescription;
        [SerializeField] private Image detailStatImage;
        [SerializeField] private TMP_Text detailLocationValue;
        [SerializeField] private TMP_Text detailDateValue;
        [SerializeField] private Button detailBackButton;

        [Header("Data-driven styling (the only visual bits the script overrides)")]
        [SerializeField] private Color capturedIconTint = Color.white;
        [SerializeField] private Color silhouetteColor  = new Color(0.22f, 0.18f, 0.28f, 1f);
        [SerializeField] private Color capturedNameColor = new Color(1f, 0.95f, 0.85f, 1f);
        [SerializeField] private Color uncaughtNameColor = new Color(0.6f, 0.55f, 0.65f, 1f);
        [Tooltip("Color applied to the featured spirit NAME at runtime. Code overrides the Inspector value on the label itself, so tweak this field instead. Default is the warm dark used by the cell names.")]
        [SerializeField] private Color featuredNameColor = new Color(0.345f, 0.161f, 0f, 1f);
        [SerializeField] private string uncaughtLabel = "???";

        private SpiritRegistry registry;
        private bool devUnlockAll;
        private bool wired;

        public bool IsOpen => root != null && root.activeSelf;

        /// Called by CollectionBookBootstrap with the registry and the dev flag. Wires the
        /// buttons once and forces the overlay (and detail) hidden.
        public void Initialize(SpiritRegistry registry, bool devUnlockAll)
        {
            this.registry = registry;
            this.devUnlockAll = devUnlockAll;
            WireButtons();
            if (detailRoot != null) detailRoot.SetActive(false);
            if (root != null) root.SetActive(false);
        }

        private void WireButtons()
        {
            if (wired) return;
            wired = true;

            if (closeButton != null) { closeButton.onClick.RemoveListener(Hide); closeButton.onClick.AddListener(Hide); }
            if (detailBackButton != null) { detailBackButton.onClick.RemoveListener(ShowGrid); detailBackButton.onClick.AddListener(ShowGrid); }

            if (cells != null)
            {
                for (int i = 0; i < cells.Length; i++)
                {
                    if (cells[i] == null || cells[i].button == null) continue;
                    int idx = i; // capture
                    cells[i].button.onClick.RemoveAllListeners();
                    cells[i].button.onClick.AddListener(() => OnCellClicked(idx));
                }
            }
        }

        public void Show()
        {
            if (root == null) return;
            root.SetActive(true);
            root.transform.SetAsLastSibling();
            ShowGrid();
            Refresh();
        }

        public void Hide()
        {
            if (root != null) root.SetActive(false);
        }

        private void ShowGrid()
        {
            if (detailRoot != null) detailRoot.SetActive(false);
        }

        public void Refresh()
        {
            if (registry == null || cells == null) return;
            var db = CollectionDatabase.Instance;
            int total = registry.Count;
            int captured = 0;

            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                if (cell == null) continue;
                Spirit spirit = i < total ? registry.All[i] : null;
                bool isCaptured = spirit != null && (devUnlockAll || (db != null && db.HasCaptured(spirit)));
                if (isCaptured) captured++;
                ApplyCell(cell, spirit, isCaptured);
            }

            if (counterText != null) counterText.text = $"{captured}/{total}개 수집";

            ApplyFeatured(db, total);
        }

        // Featured (decorative) image = the LAST CAUGHT spirit. If nothing caught yet: in demo
        // (devUnlockAll) fall back to registry[0] so it isn't empty; otherwise a grey silhouette.
        private void ApplyFeatured(CollectionDatabase db, int total)
        {
            // ONE shared source for BOTH the featured image and the featured name, so they match.
            Spirit featured = db != null ? db.LastCaptured : null;
            if (featured == null && devUnlockAll && total > 0) featured = registry.All[0];

            // 0-caught fallback source for the featured image: the first spirit's silhouette art.
            Spirit firstSpirit = total > 0 ? registry.All[0] : null;

            if (featuredImage != null)
            {
                if (featured != null && featured.sprite != null)
                {
                    // A caught (or dev-unlocked) spirit: full-color artwork.
                    featuredImage.sprite = featured.sprite;
                    featuredImage.color = capturedIconTint;
                }
                else if (featured != null)
                {
                    featuredImage.sprite = null;
                    featuredImage.color = featured.accentColor;
                }
                else if (firstSpirit != null && firstSpirit.silhouetteSprite != null)
                {
                    // Nothing caught yet: show the first spirit's grey 'shadow' silhouette art at
                    // full color (no tint), instead of a blank square.
                    featuredImage.sprite = firstSpirit.silhouetteSprite;
                    featuredImage.color = Color.white;
                }
                else
                {
                    // No silhouette art assigned — last-resort solid silhouette square.
                    featuredImage.sprite = null;
                    featuredImage.color = silhouetteColor;
                }
            }

            if (featuredNameText != null)
            {
                featuredNameText.text = featured != null ? featured.displayName : uncaughtLabel;
                // Force centering + the configurable color every bind — Edit-mode values get
                // undone at runtime, so set them explicitly here.
                featuredNameText.alignment = TextAlignmentOptions.Center; // horizontal Center + vertical Middle
                featuredNameText.color = featuredNameColor;
            }
        }

        // Only touches the data-driven bits: icon sprite+tint, name text+color, button interactable.
        // Everything else on the cell (background, sizes, fonts) is whatever you authored in the scene.
        private void ApplyCell(SpiritCell cell, Spirit spirit, bool captured)
        {
            if (cell.icon != null)
            {
                if (captured && spirit != null && spirit.sprite != null)
                {
                    cell.icon.sprite = spirit.sprite;
                    cell.icon.color = capturedIconTint;
                }
                else if (captured && spirit != null)
                {
                    // Caught but no sprite asset — fall back to a solid accent-colored cell.
                    cell.icon.sprite = null;
                    cell.icon.color = spirit.accentColor;
                }
                else if (spirit != null && spirit.silhouetteSprite != null)
                {
                    // Uncaught: show the dedicated grey 'shadow' silhouette art at full color (no
                    // tint), so the cell is spirit-shaped instead of a blank square.
                    cell.icon.sprite = spirit.silhouetteSprite;
                    cell.icon.color = Color.white;
                }
                else
                {
                    // Uncaught with no silhouette art assigned — last-resort solid silhouette square.
                    cell.icon.sprite = null;
                    cell.icon.color = silhouetteColor;
                }
            }
            if (cell.nameLabel != null)
            {
                cell.nameLabel.text = (captured && spirit != null) ? spirit.displayName : uncaughtLabel;
                cell.nameLabel.color = captured ? capturedNameColor : uncaughtNameColor;
            }
            if (cell.button != null) cell.button.interactable = captured; // uncaught cells ignore taps
        }

        private void OnCellClicked(int index)
        {
            if (registry == null || index < 0 || index >= registry.Count) return;
            var spirit = registry.All[index];
            var db = CollectionDatabase.Instance;
            bool isCaptured = spirit != null && (devUnlockAll || (db != null && db.HasCaptured(spirit)));
            if (!isCaptured) return; // uncaught → never opens detail
            OpenDetail(spirit);
        }

        private void OpenDetail(Spirit spirit)
        {
            if (spirit == null || detailRoot == null) return;

            var bigSprite = spirit.capturedSprite != null ? spirit.capturedSprite : spirit.sprite;
            if (detailSpiritImage != null)
            {
                detailSpiritImage.sprite = bigSprite;
                detailSpiritImage.color = bigSprite != null ? Color.white : silhouetteColor;
            }
            if (detailName != null) detailName.text = spirit.displayName;
            if (detailDescription != null) detailDescription.text = spirit.description;
            if (detailStatImage != null)
            {
                detailStatImage.sprite = spirit.statImage;
                detailStatImage.color = spirit.statImage != null ? Color.white : silhouetteColor;
            }
            if (detailLocationValue != null) detailLocationValue.text = spirit.foundLocation;
            if (detailDateValue != null) detailDateValue.text = spirit.foundDate;

            detailRoot.SetActive(true);
            detailRoot.transform.SetAsLastSibling();
        }
    }
}
