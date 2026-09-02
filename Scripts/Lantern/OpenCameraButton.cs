using UnityEngine;
using UnityEngine.UI;
using Locackthon.Core;
using Locackthon.Data;

namespace Locackthon.Lantern
{
    // The "Open Camera" UI button on the Lantern screen.
    //
    // Subscribes to ProximityWatcher's enter/leave events and shows/hides itself based on
    // whether a spirit is in capture range. On click, it asks GameStateManager to enter the
    // AR scene with that spirit as the target.
    //
    // If `panel` is wired in the Inspector, the whole panel is toggled SetActive on/off.
    // If not, the button hides by disabling its Image + Button + child Graphics (the
    // GameObject stays active so this script keeps listening to ProximityWatcher events).
    [RequireComponent(typeof(Button))]
    public class OpenCameraButton : MonoBehaviour
    {
        [Header("Inputs")]
        [SerializeField] private ProximityWatcher watcher;

        [Header("Visibility")]
        [Tooltip("Optional panel to toggle on/off based on capture-range state. If null, the button hides itself by disabling its Image + Button (the GameObject stays active so this script keeps listening).")]
        [SerializeField] private GameObject panel;

        private Button button;
        private Image image;

        private void Awake()
        {
            button = GetComponent<Button>();
            image  = GetComponent<Image>();
            button.onClick.AddListener(OnClick);

            if (watcher != null)
            {
                watcher.OnSpiritEntersCaptureRange.AddListener(HandleEnter);
                watcher.OnSpiritLeavesCaptureRange.AddListener(HandleLeave);
            }
            else
            {
                Debug.LogWarning("[OpenCameraButton] No ProximityWatcher assigned — button will never appear.");
            }

            // If a spirit is already in range at scene load (it shouldn't be on first poll, but defensive), reflect it.
            bool alreadyInRange = watcher != null && watcher.NearestInRange != null;
            SetVisible(alreadyInRange);
        }

        private void OnDestroy()
        {
            if (watcher == null) return;
            watcher.OnSpiritEntersCaptureRange.RemoveListener(HandleEnter);
            watcher.OnSpiritLeavesCaptureRange.RemoveListener(HandleLeave);
        }

        private void HandleEnter(Spirit s) => SetVisible(true);
        private void HandleLeave(Spirit s) => SetVisible(false);

        private void SetVisible(bool visible)
        {
            if (panel != null)
            {
                panel.SetActive(visible);
                return;
            }
            // Keep the GameObject active so this MonoBehaviour stays subscribed; only hide the visual + clicks.
            if (image  != null) image.enabled = visible;
            if (button != null) button.interactable = visible;

            // Also toggle every child Graphic (TMP labels, sub-icons) — otherwise the
            // child label keeps rendering "Open Camera" with no button rectangle behind it.
            var childGraphics = GetComponentsInChildren<Graphic>(includeInactive: true);
            for (int i = 0; i < childGraphics.Length; i++)
            {
                var g = childGraphics[i];
                if (g == image) continue; // already handled above
                g.enabled = visible;
            }
        }

        private void OnClick()
        {
            Spirit target = watcher != null ? watcher.NearestInRange : null;
            if (target == null) return;
            if (GameStateManager.Instance == null)
            {
                Debug.LogWarning("[OpenCameraButton] No GameStateManager in scene; cannot enter AR.");
                return;
            }
            GameStateManager.Instance.EnterARCapture(target);
        }
    }
}
