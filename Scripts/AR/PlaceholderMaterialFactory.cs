using UnityEngine;

namespace Locackthon.AR
{
    // Tints a renderer with a flat colour, robust to URP shader stripping on Android.
    //
    // Why this exists:
    //   GameObject.CreatePrimitive(...) at runtime assigns the URP/Lit default material, but
    //   that shader can be stripped from the Android build if no asset directly references it.
    //   When stripped, the renderer falls back to the magenta error material and our colour
    //   change is invisible.
    //
    //   This factory tries three sources in order, each more reliable than the last:
    //     1. A Material asset in Assets/Resources/PlaceholderSpiritMaterial.mat — being in
    //        Resources guarantees Unity bundles it AND its shader.
    //     2. The renderer's existing material (works only if URP/Lit is in
    //        GraphicsSettings → Always Included Shaders, which the editor script ensures).
    //     3. Shader.Find as a last-ditch fallback for environments where neither path applies.
    public static class PlaceholderMaterialFactory
    {
        private const string ResourceName = "PlaceholderSpiritMaterial";

        private static Material cachedTemplate;

        public static void Apply(GameObject go, Color color)
        {
            var renderer = go != null ? go.GetComponent<Renderer>() : null;
            if (renderer == null) return;

            // 1. Resources material — most reliable.
            if (cachedTemplate == null)
            {
                cachedTemplate = Resources.Load<Material>(ResourceName);
                if (cachedTemplate == null)
                    Debug.LogWarning($"[PlaceholderMaterial] Resources/{ResourceName}.mat not found — falling back to existing material.");
            }
            if (cachedTemplate != null)
            {
                renderer.sharedMaterial = TintedClone(cachedTemplate, color);
                return;
            }

            // 2. Clone the existing default material that CreatePrimitive assigned.
            var existing = renderer.sharedMaterial;
            if (existing != null && existing.shader != null)
            {
                renderer.sharedMaterial = TintedClone(existing, color);
                return;
            }

            // 3. Last-ditch fallback.
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogWarning("[PlaceholderMaterial] No shader available; primitive will render with the default tint.");
                return;
            }
            renderer.sharedMaterial = TintedClone(new Material(shader), color);
        }

        // Both URP/Unlit/Lit (use _BaseColor) and the legacy built-in (use _Color) are covered.
        private static Material TintedClone(Material source, Color color)
        {
            var mat = new Material(source);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))     mat.color = color;
            return mat;
        }
    }
}
