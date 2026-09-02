using UnityEngine;
using Locackthon.Core;
using Locackthon.Data;

namespace Locackthon.Gameplay
{
    // Drives the visual on a spirit GameObject in the Lantern scene from a Spirit
    // ScriptableObject. Sets the SpriteRenderer's sprite (if the asset has one) and tints
    // it with the spirit's accent colour.
    //
    // At runtime, prefers GameStateManager.LastSelectedSpirit over the serialized field — that
    // way the DEV-picked spirit's colour shows up immediately on Lantern reload, with no
    // flicker through the scene-default value. In edit mode (OnValidate), the serialized
    // field is used because GameStateManager isn't around.
    //
    // SetSpirit(Spirit) is the runtime entry point — DevModeSpiritSelector calls this.
    [ExecuteAlways]
    [RequireComponent(typeof(SpriteRenderer))]
    public class SpiritBinder : MonoBehaviour
    {
        [Tooltip("ScriptableObject describing this spirit's identity, sprite, GPS coordinate, and capture radius.")]
        [SerializeField] private Spirit spirit;

        public Spirit Spirit => spirit;

        public void SetSpirit(Spirit s)
        {
            spirit = s;
            Apply();
        }

        private void OnEnable()  => Apply();
        private void OnValidate() => Apply();

        private void Apply()
        {
            // At play time, prefer the persisted DEV selection on GameStateManager so the flame
            // tint matches the user's last pick across the Lantern → AR → Lantern round-trip
            // (no flicker to the scene-default colour). In edit mode, GameStateManager.Instance
            // is null, so OnValidate uses the serialized field.
            Spirit effective = spirit;
            if (Application.isPlaying
                && GameStateManager.Instance != null
                && GameStateManager.Instance.LastSelectedSpirit != null)
            {
                effective = GameStateManager.Instance.LastSelectedSpirit;
            }

            if (effective == null) return;
            var sr = GetComponent<SpriteRenderer>();
            if (sr == null) return;
            if (effective.sprite != null) sr.sprite = effective.sprite;
            // Tint the placeholder by the spirit's accent color so DEV-mode selection is visible
            // on the Lantern screen even before art is assigned.
            sr.color = effective.accentColor;
        }
    }
}
