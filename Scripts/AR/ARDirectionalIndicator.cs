using UnityEngine;
using UnityEngine.UI;

namespace Locackthon.AR
{
    // Edge-of-screen arrow that points toward the spirit while it is off-camera.
    // When the spirit is inside the camera view, the arrow hides itself.
    [RequireComponent(typeof(RectTransform))]
    public class ARDirectionalIndicator : MonoBehaviour
    {
        [SerializeField] private ARCaptureController controller;
        [SerializeField] private Camera arCamera;
        [Tooltip("Padding from the screen edge in canvas pixels.")]
        [SerializeField, Min(0f)] private float edgePadding = 80f;

        private RectTransform self;
        private Image image;
        private Canvas canvas;

        private void Awake()
        {
            self = (RectTransform)transform;
            image = GetComponent<Image>();
            canvas = GetComponentInParent<Canvas>();
            // Default to hidden so we never flash on top of the spirit before the first projection check.
            if (image != null) image.enabled = false;
        }

        private void LateUpdate()
        {
            if (arCamera == null) arCamera = Camera.main;
            Transform target = controller != null ? controller.SpiritAnchor : null;
            if (target == null || arCamera == null || canvas == null) { Show(false); return; }

            Vector3 viewport = arCamera.WorldToViewportPoint(target.position);
            bool behind = viewport.z < 0f;
            bool onScreen = !behind && viewport.x > 0.05f && viewport.x < 0.95f && viewport.y > 0.05f && viewport.y < 0.95f;
            if (onScreen) { Show(false); return; }

            Show(true);

            // If the target is behind the camera, mirror the viewport vector through the centre so
            // the arrow points to the correct side instead of in the opposite direction.
            Vector2 fromCenter = new Vector2(viewport.x - 0.5f, viewport.y - 0.5f);
            if (behind) fromCenter = -fromCenter;
            if (fromCenter.sqrMagnitude < 0.0001f) fromCenter = Vector2.up * 0.5f;

            // Project to the canvas rect edge.
            var canvasRect = (RectTransform)canvas.transform;
            Vector2 canvasSize = canvasRect.rect.size;
            Vector2 half = canvasSize * 0.5f - Vector2.one * edgePadding;

            Vector2 dir = fromCenter.normalized;
            float scale = Mathf.Min(half.x / Mathf.Max(0.0001f, Mathf.Abs(dir.x)),
                                    half.y / Mathf.Max(0.0001f, Mathf.Abs(dir.y)));
            Vector2 pos = dir * scale;

            self.anchoredPosition = pos;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f; // arrow art points up by default
            self.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        private void Show(bool visible)
        {
            if (image != null && image.enabled != visible) image.enabled = visible;
        }
    }
}
