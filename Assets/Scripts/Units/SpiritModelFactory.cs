using UnityEngine;

namespace NexusAnima
{
    /// <summary>
    /// Shared visual helpers for spirit mesh load / material / bounds.
    /// Keeps SpiritUnit free of low-level rendering utility code.
    /// </summary>
    public static class SpiritModelFactory
    {
        public static bool IsHelperRenderer(Renderer renderer)
        {
            if (renderer == null) return true;
            string name = renderer.gameObject.name;
            return name.StartsWith("CasterMesh") || name.Contains("CasterMesh");
        }

        public static void HideHelperRenderers(GameObject root)
        {
            if (root == null) return;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (IsHelperRenderer(renderers[i]))
                    renderers[i].enabled = false;
            }
        }

        public static Bounds GetRenderBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = default;
            bool started = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null || !renderer.enabled || IsHelperRenderer(renderer))
                    continue;
                if (!started)
                {
                    bounds = renderer.bounds;
                    started = true;
                }
                else
                    bounds.Encapsulate(renderer.bounds);
            }
            if (started)
                return bounds;
            return new Bounds(root != null ? root.transform.position : Vector3.zero, Vector3.one * 0.01f);
        }

        public static void ApplySpiritTexture(Renderer[] renderers, string modelName)
        {
            if (renderers == null || string.IsNullOrEmpty(modelName)) return;

            string textureName = modelName.Replace("Art_", "Tex_") + "_Body_Color";
            var texture = Resources.Load<Texture2D>("Spirits/" + textureName);
            if (texture == null) return;

            var shader = FindUrpLitShader();
            foreach (var renderer in renderers)
            {
                if (renderer == null || IsHelperRenderer(renderer)) continue;
                var source = renderer.sharedMaterial;
                var material = new Material(shader);
                // Keep tint white so Body_Color textures are not multiplied by roster Color.
                SetMaterialColor(material, Color.white);
                float gloss = 0.25f;
                if (source != null && source.HasProperty("_Glossiness"))
                    gloss = source.GetFloat("_Glossiness");
                else if (source != null && source.HasProperty("_Smoothness"))
                    gloss = source.GetFloat("_Smoothness");
                if (material.HasProperty("_Smoothness"))
                    material.SetFloat("_Smoothness", gloss);
                if (material.HasProperty("_Glossiness"))
                    material.SetFloat("_Glossiness", gloss);
                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", texture);
                material.mainTexture = texture;
                // FBX body often has Body_S + Body_D submeshes. Only assigning slot 0
                // leaves the second shell untextured (pale ghost) during attack motion.
                int slotCount = renderer.sharedMaterials != null
                    ? Mathf.Max(1, renderer.sharedMaterials.Length)
                    : 1;
                var slots = new Material[slotCount];
                for (int i = 0; i < slotCount; i++)
                    slots[i] = material;
                renderer.materials = slots;
            }
        }

        public static Shader FindUrpLitShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                ?? Shader.Find("Standard");
        }

        public static void SetMaterialColor(Material material, Color color)
        {
            if (material == null) return;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            material.color = color;
        }

        public static void SetRendererColor(Renderer renderer, Color color)
        {
            if (renderer == null) return;
            SetMaterialColor(renderer.material, color);
        }
    }
}
