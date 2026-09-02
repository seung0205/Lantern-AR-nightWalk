using UnityEngine;

namespace Locackthon.Gameplay
{
    // Lantern Mode decoration: drifts the amber-circle spirit in a Lissajous pattern around its
    // spawn position. Capture happens exclusively in AR Capture Scene — taps on this sprite are
    // intentionally ignored here, so no tap handler / collider is read.
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpiritWanderer : MonoBehaviour
    {
        [Header("Drift Motion")]
        [Tooltip("Half-width and half-height of the drift area in world units, centered on the spawn position.")]
        [SerializeField] private Vector2 amplitude = new Vector2(2f, 1f);
        [Tooltip("Drift cycles per second on each axis. Different X/Y values create a Lissajous-like figure.")]
        [SerializeField] private Vector2 frequency = new Vector2(0.25f, 0.4f);

        private Vector3 home;
        private float phase;

        private void Awake()
        {
            home = transform.position;
            phase = Random.Range(0f, Mathf.PI * 2f);
        }

        private void Update()
        {
            float t = Time.time + phase;
            transform.position = home + new Vector3(
                Mathf.Sin(t * frequency.x * Mathf.PI * 2f) * amplitude.x,
                Mathf.Cos(t * frequency.y * Mathf.PI * 2f) * amplitude.y,
                0f);
        }
    }
}
