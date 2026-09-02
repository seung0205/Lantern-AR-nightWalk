using UnityEngine;

namespace Locackthon.Data
{
    // ScriptableObject holding one spirit's design data. One asset per spirit lives in
    // Assets/Spirits/Spirit_*.asset; the master list is SpiritRegistry.
    //
    // Read by:
    //   ProximityWatcher    — latitude / longitude / captureRadiusMeters for proximity gating
    //   SpiritBinder        — sprite, accentColor for the Lantern flame
    //   ARCaptureController — accentColor for the AR placeholder sphere
    //   CollectionBookUI    — displayName, description, sprite, accentColor for the book entry
    //
    // To add a new spirit: right-click in the Project window → Create → Locackthon → Spirit,
    // fill in fields, then add the asset to SpiritRegistry's list AND ProximityWatcher's
    // watched list (or just to the registry — DEV mode swaps the watched list at runtime).
    [CreateAssetMenu(menuName = "Locackthon/Spirit", fileName = "Spirit_New")]
    public class Spirit : ScriptableObject
    {
        [Header("Identity")]
        public string displayName;
        [TextArea(2, 5)] public string description;
        [Tooltip("Standalone spirit art — used by the AR billboard and as a fallback overlay on the result-panel Img_CapturedSpirit when no composite is available.")]
        public Sprite sprite;
        [Tooltip("Composite \"spirit inside lantern\" art for the result panel. When set, it REPLACES Img_LanternFrame's default art and the separate spirit overlay is hidden. Leave empty to use the generic lantern frame + Img_CapturedSpirit overlay as before.")]
        public Sprite capturedSprite;
        [Tooltip("Grey 'shadow' silhouette art shown in the 도감 for this spirit while UNCAUGHT (grid cell + featured image when 0 caught). Displayed at full color/no tint. Leave empty to fall back to a dark silhouette-colored square.")]
        public Sprite silhouetteSprite;
        [Tooltip("Placeholder fill color used by Collection Book slots (and other UI) when no sprite is assigned, or as a tint companion to the sprite.")]
        public Color accentColor = new Color(1f, 0.7f, 0.3f, 1f);

        [Header("Collection Book — detail view")]
        [Tooltip("Stat-bars image shown in the 도감 detail view. Assign your own art; left empty shows a grey placeholder.")]
        public Sprite statImage;
        [Tooltip("발견 위치 (where found) — free text shown in the detail view, e.g. \"수원시 팔달구 …\".")]
        public string foundLocation;
        [Tooltip("발견 일시 (when found) — free text shown in the detail view, e.g. \"26.05.19 20:32\".")]
        public string foundDate;

        [Header("Location")]
        [Tooltip("Latitude in decimal degrees (e.g., 37.2839 near Hwaseong Haenggung, Suwon).")]
        public double latitude;
        [Tooltip("Longitude in decimal degrees (e.g., 127.0153 near Hwaseong Haenggung, Suwon).")]
        public double longitude;
        [Tooltip("Distance in meters within which this spirit's area activates and it can be captured.")]
        [Min(1f)] public float captureRadiusMeters = 20f;
    }
}
