using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NexusAnima.Editor
{
    /// <summary>
    /// Links Piloto Studio Super Realistic ARPG FX (and optional free packs)
    /// into Resources/VFX/{Melee|Ranged|Impact}/{Fire|Water|Grass|Light}.prefab
    /// so AttackVfxLibrary can load them by element at runtime.
    /// </summary>
    public static class FreeVfxPackLinker
    {
        const string ResourcesVfx = "Assets/Resources/VFX";
        const string PilotoRoot = "Assets/Piloto Studio/Super Realistic FX Bundle";

        // Prefer bold Hit_Slash streaks (readable on a tiny board) over Stab (too faint)
        // and over C-shape / Circle / Holy (Donut + full crescent = rings).
        static readonly Dictionary<ElementType, string> MeleePreferred = new Dictionary<ElementType, string>
        {
            { ElementType.Fire,  PilotoRoot + "/ARPG Realistic Essentials Fire/Prefabs/Melee/Red_Fire/Hit_Slash_Heavy.prefab" },
            { ElementType.Water, PilotoRoot + "/ARPG Realistic Water FX/Water_Slash.prefab" },
            { ElementType.Grass, PilotoRoot + "/ARPG Realistic Essentials Fire/Prefabs/Melee/Green_Fire/Hit_Slash_Heavy_green.prefab" },
            // Holy_Slash crescent meshes read as rings top-down; use linear Hit streak tinted white.
            { ElementType.Light, PilotoRoot + "/ARPG Realistic Essentials Fire/Prefabs/Melee/Red_Fire/Hit_Slash_Heavy.prefab" },
        };

        static readonly Dictionary<ElementType, string> RangedPreferred = new Dictionary<ElementType, string>
        {
            { ElementType.Fire,  PilotoRoot + "/ARPG Realistic Essentials Fire/Prefabs/Ranged/Red/Projectile_Orb_small.prefab" },
            // WaterSphere is huge authored; scale is reduced further in AttackVfxLibrary.
            { ElementType.Water, PilotoRoot + "/ARPG Realistic Water FX/WaterSphere_Projectile.prefab" },
            { ElementType.Grass, PilotoRoot + "/ARPG Realistic Poison/ARPG_Poison_Liquid_Projectile.prefab" },
            // ShootingStar / Golden packs miss many Clair_* materials in the subset import → magenta.
            // Holy_Aura keeps almost all materials intact.
            { ElementType.Light, PilotoRoot + "/ARPG Realistic Demon VS Angels/Simple/Holy_Aura.prefab" },
        };

        static readonly Dictionary<ElementType, string> ImpactPreferred = new Dictionary<ElementType, string>
        {
            { ElementType.Fire,  PilotoRoot + "/ARPG Realistic Essentials Fire/Prefabs/Ranged/Red/Projectile_Hit_Impact.prefab" },
            { ElementType.Water, PilotoRoot + "/ARPG Realistic Water FX/WaterHit_Small.prefab" },
            { ElementType.Grass, PilotoRoot + "/ARPG Realistic Poison/ARPG_Poison_Hit.prefab" },
            { ElementType.Light, PilotoRoot + "/ARPG Realistic Demon VS Angels/Simple/Holy_Hit.prefab" },
        };

        static readonly string[] MeleeKeywords =
        {
            "slash", "sword", "hit", "claw", "melee", "swipe", "cut"
        };

        static readonly string[] RangedKeywords =
        {
            "projectile", "missile", "bolt", "orb", "bullet", "shot", "magic ball", "fireball", "shootingstar"
        };

        static readonly string[] ImpactKeywords =
        {
            "impact", "hit", "explosion", "burst", "flash"
        };

        // Xinxiang stock picks — CopyAsset only, never sanitize / tint / strip emitters.
        const string XinxiangVfx = ResourcesVfx + "/Xinxiang";
        static readonly (string destName, string source)[] XinxiangStock =
        {
            // Paths live under Super Realistic FX Bundle/ (extracted subset).
            ("Ice",          PilotoRoot + "/Super Realistic FX Bundle/ARPG Realistic Deathice/DeathIce_Ground_Burst.prefab"),
            ("IceBurst",     PilotoRoot + "/Super Realistic FX Bundle/ARPG Realistic Deathice/DeathIce_Hit.prefab"),
            ("Buff",         PilotoRoot + "/Super Realistic FX Bundle/ARPG Realistic Pickups Auras and Buffs/Pickups/6) Legendary yellow.prefab"),
            ("BuffCute",     PilotoRoot + "/Super Realistic FX Bundle/ARPG Realistic Pickups Auras and Buffs/Pickups/6) Legendary yellow.prefab"),
            ("BuffHoly",     PilotoRoot + "/Super Realistic FX Bundle/ARPG Realistic Demon VS Angels/Simple/Holy_Aura.prefab"),
            ("BuffAura",     PilotoRoot + "/ARPG Realistic Demon VS Angels/Simple/Holy_Aura.prefab"),
            ("BuffPower",    PilotoRoot + "/Super Realistic FX Bundle/ARPG Realistic Pickups Auras and Buffs/Character VFX/4) Character Power Buff.prefab"),
            ("Heal",         PilotoRoot + "/Super Realistic FX Bundle/ARPG Realistic Ice and Water pack/Water/Water_Healing_Rain.prefab"),
        };

        [MenuItem("NexusAnima/Link Free VFX Packs")]
        public static void LinkPacks()
        {
            EnsureFolder(ResourcesVfx);
            EnsureFolder(ResourcesVfx + "/Melee");
            EnsureFolder(ResourcesVfx + "/Ranged");
            EnsureFolder(ResourcesVfx + "/Impact");

            var allPrefabs = AssetDatabase.FindAssets("t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.StartsWith("Assets/") && !p.Contains("/Resources/VFX/"))
                .ToList();

            int linked = 0;
            linked += LinkCategory("Melee", MeleePreferred, allPrefabs, MeleeKeywords, preferCartoon: false);
            linked += LinkCategory("Ranged", RangedPreferred, allPrefabs, RangedKeywords, preferCartoon: false);
            linked += LinkCategory("Impact", ImpactPreferred, allPrefabs, ImpactKeywords, preferCartoon: false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog(
                "NexusAnima VFX",
                linked > 0
                    ? $"已链接 {linked} 个元素特效到 Resources/VFX。\n近战 Melee/ · 远程 Ranged/ · 命中 Impact/\n优先使用 Piloto Super Realistic ARPG FX。"
                    : "未找到可用特效预制体。\n请确认已导入 Piloto Studio Super Realistic FX Bundle 子集。",
                "OK");
        }

        /// <summary>
        /// Copy stock Piloto prefabs into Resources/VFX/Xinxiang as-is.
        /// No SanitizeBrokenRenderers / DisableRingEmitters / material edits.
        /// </summary>
        [MenuItem("NexusAnima/Link Xinxiang Stock VFX")]
        public static void LinkXinxiangStockVfx()
        {
            EnsureFolder(ResourcesVfx);
            EnsureFolder(XinxiangVfx);

            int linked = 0;
            var missing = new List<string>();
            foreach (var (destName, source) in XinxiangStock)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(source) == null)
                {
                    missing.Add(source);
                    continue;
                }

                string dest = $"{XinxiangVfx}/{destName}.prefab";
                if (!CopyPrefab(source, dest))
                {
                    missing.Add(source + " -> copy failed");
                    continue;
                }

                // Keep particle authoring intact; only fix broken slots that render magenta on URP.
                StripMissingScripts(dest);
                FixXinxiangBrokenSlots(dest);
                Debug.Log($"[FreeVfxPackLinker] Xinxiang/{destName} <= {source}");
                linked++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string msg = linked > 0
                ? $"已原样链接 {linked} 个心象库存特效到 Resources/VFX/Xinxiang。\nIce / IceBurst / Buff / BuffPower / Heal"
                : "未找到心象库存预制体，请确认已导入 Deathice / Ice and Water / Pickups 包。";
            if (missing.Count > 0)
                msg += "\n\n缺失:\n" + string.Join("\n", missing.Take(8));
            // Log only — DisplayDialog blocks Unity MCP tool calls.
            Debug.Log("[NexusAnima Xinxiang VFX] " + msg);
        }

        static int LinkCategory(
            string category,
            Dictionary<ElementType, string> preferred,
            List<string> allPrefabs,
            string[] keywords,
            bool preferCartoon)
        {
            int count = 0;
            foreach (ElementType element in System.Enum.GetValues(typeof(ElementType)))
            {
                string dest = $"{ResourcesVfx}/{category}/{element}.prefab";
                string source = null;

                if (preferred.TryGetValue(element, out string preferredPath)
                    && AssetDatabase.LoadAssetAtPath<GameObject>(preferredPath) != null)
                {
                    source = preferredPath;
                }
                else
                {
                    var candidates = allPrefabs
                        .Where(p => PathMatchesKeywords(p, keywords))
                        .Where(p => PathSuggestsElement(p, element) || !HasAnyElementToken(p))
                        .OrderByDescending(p => ScorePath(p, element, preferCartoon, keywords))
                        .ToList();

                    if (candidates.Count == 0)
                        continue;
                    source = candidates[0];
                }

                if (!CopyPrefab(source, dest))
                    continue;

                StripMissingScripts(dest);
                SanitizeBrokenRenderers(dest);
                if (category == "Melee")
                    DisableRingEmitters(dest);
                Debug.Log($"[FreeVfxPackLinker] {category}/{element} <= {source}");
                count++;
            }

            return count;
        }

        [MenuItem("NexusAnima/Sanitize VFX Magenta Materials")]
        public static void SanitizeAllLinkedVfx()
        {
            int fixedCount = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { ResourcesVfx }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                fixedCount += SanitizeBrokenRenderers(path);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog(
                "NexusAnima VFX",
                $"已清理洋红材质：禁用/修复了 {fixedCount} 处空材质或错误着色器渲染器。\n请重新进入 Play Mode 查看特效颜色。",
                "OK");
        }

        static bool CopyPrefab(string source, string dest)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(source);
            if (prefab == null) return false;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(dest) != null)
                AssetDatabase.DeleteAsset(dest);

            return AssetDatabase.CopyAsset(source, dest);
        }

        /// <summary>
        /// Resources copies only: replace null / purple / rainbow material slots with a
        /// known-good Piloto soft mat, and disable HDRP DefaultHDParticle leftovers.
        /// Does not rewrite particle emitters or non-broken materials.
        /// </summary>
        static void FixXinxiangBrokenSlots(string assetPath)
        {
            var root = PrefabUtility.LoadPrefabContents(assetPath);
            if (root == null) return;

            try
            {
                Material fallback = FindFallbackFxMaterial()
                    ?? AssetDatabase.LoadAssetAtPath<Material>("Assets/Piloto Studio/Materials/Shared/FuzzAdd.mat");

                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    // Piloto nests real FX under parents named DefaultHDParticle*.
                    // Only blank the parent renderer when it has no child particle systems;
                    // never deactivate the GameObject (that would hide Buff/Heal entirely).
                    if (renderer.name.IndexOf("DefaultHDParticle", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        int childPs = 0;
                        foreach (Transform c in renderer.transform)
                        {
                            if (c.GetComponent<ParticleSystem>() != null)
                                childPs++;
                        }
                        if (childPs == 0)
                        {
                            renderer.enabled = false;
                            var psHd = renderer.GetComponent<ParticleSystem>();
                            if (psHd != null)
                            {
                                var emission = psHd.emission;
                                emission.enabled = false;
                            }
                            continue;
                        }
                        // Parent container with children: keep hierarchy, fix mats below.
                    }

                    var mats = renderer.sharedMaterials;
                    if (mats == null || mats.Length == 0)
                    {
                        renderer.enabled = false;
                        continue;
                    }

                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        bool bad = m == null;
                        if (!bad && m != null)
                        {
                            string n = m.name ?? "";
                            bad = n.IndexOf("Purple", System.StringComparison.OrdinalIgnoreCase) >= 0
                                || n.IndexOf("Rainbow", System.StringComparison.OrdinalIgnoreCase) >= 0
                                || n.IndexOf("Violet", System.StringComparison.OrdinalIgnoreCase) >= 0;
                        }

                        if (!bad || fallback == null)
                            continue;
                        mats[i] = fallback;
                        changed = true;
                    }

                    if (changed)
                        renderer.sharedMaterials = mats;
                }

                PrefabUtility.SaveAsPrefabAsset(root, assetPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static void StripMissingScripts(string assetPath)
        {
            var root = PrefabUtility.LoadPrefabContents(assetPath);
            if (root == null) return;
            try
            {
                int removed = 0;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                if (removed > 0)
                    Debug.Log($"[FreeVfxPackLinker] Removed {removed} missing scripts from {assetPath}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Null / InternalErrorShader materials render as magenta (pink-purple).
        /// Disable those renderers and stop emission so only valid UberFXSG layers remain.
        /// </summary>
        static int SanitizeBrokenRenderers(string assetPath)
        {
            var root = PrefabUtility.LoadPrefabContents(assetPath);
            if (root == null) return 0;

            int fixedCount = 0;
            try
            {
                Material fallback = FindFallbackFxMaterial();
                var errShader = Shader.Find("Hidden/InternalErrorShader");

                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = renderer.sharedMaterials;
                    if (mats == null || mats.Length == 0)
                    {
                        fixedCount += DisableBrokenParticle(renderer);
                        continue;
                    }

                    bool changed = false;
                    bool anyValid = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        bool broken = m == null
                            || m.shader == null
                            || (errShader != null && m.shader == errShader)
                            || (m.shader.name != null && m.shader.name.Contains("InternalError"));

                        if (broken)
                        {
                            if (fallback != null)
                            {
                                mats[i] = fallback;
                                changed = true;
                            }
                        }
                        else
                        {
                            anyValid = true;
                        }
                    }

                    if (changed && anyValid)
                    {
                        renderer.sharedMaterials = mats;
                        fixedCount++;
                    }
                    else if (!anyValid)
                    {
                        // Still no usable material — kill magenta emitters entirely.
                        if (fallback != null && mats.Length > 0)
                        {
                            for (int i = 0; i < mats.Length; i++)
                                mats[i] = fallback;
                            renderer.sharedMaterials = mats;
                        }

                        fixedCount += DisableBrokenParticle(renderer);
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                Debug.Log($"[FreeVfxPackLinker] Sanitized {fixedCount} broken renderers in {assetPath}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return fixedCount;
        }

        static int DisableBrokenParticle(Renderer renderer)
        {
            int n = 0;
            if (renderer.enabled)
            {
                renderer.enabled = false;
                n++;
            }

            var ps = renderer.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                var emission = ps.emission;
                if (emission.enabled)
                {
                    emission.enabled = false;
                    n++;
                }
            }

            return Mathf.Max(1, n);
        }

        /// <summary>
        /// C-shape / Circle / Holy crescent meshes + Donut sparks read as rings on a tiny board.
        /// Keep linear stab / streak emitters only.
        /// </summary>
        static int DisableRingEmitters(string assetPath)
        {
            var root = PrefabUtility.LoadPrefabContents(assetPath);
            if (root == null) return 0;

            int n = 0;
            try
            {
                foreach (var ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var shape = ps.shape;
                    string name = ps.name.ToLowerInvariant();
                    bool isRing = shape.enabled && (
                        shape.shapeType == ParticleSystemShapeType.Donut
                        || (shape.shapeType == ParticleSystemShapeType.Circle && shape.arc >= 300f));
                    // Only strip true ring/C-circle authored layers. Keep slash blade meshes
                    // (Water_Slash_A / Hit streaks) so melee still reads clearly.
                    bool meshArc = name.Contains("circle_shape")
                        || name.Contains("c_shape")
                        || name.Contains("slash_holy_fill")
                        || name.Contains("slash_fire_fill");

                    if (!isRing && !meshArc)
                        continue;

                    var emission = ps.emission;
                    emission.enabled = false;
                    var renderer = ps.GetComponent<ParticleSystemRenderer>();
                    if (renderer != null)
                        renderer.enabled = false;
                    n++;
                }

                PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                if (n > 0)
                    Debug.Log($"[FreeVfxPackLinker] Disabled {n} ring/arc emitters in {assetPath}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            return n;
        }

        static Material FindFallbackFxMaterial()
        {
            string[] preferred =
            {
                "Assets/Piloto Studio/Super Realistic FX Bundle/ARPG Realistic Essentials Fire/Materials/FuzzAdd_Soft.mat",
                "Assets/Piloto Studio/Super Realistic FX Bundle/ARPG Realistic Essentials Fire/Materials/FuzzAdd.mat",
            };

            foreach (var path in preferred)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null && mat.shader != null && mat.shader.isSupported)
                    return mat;
            }

            foreach (var guid in AssetDatabase.FindAssets("FuzzAdd_Soft t:Material", new[] { "Assets/Piloto Studio" }))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (mat != null && mat.shader != null && mat.shader.isSupported)
                    return mat;
            }

            return null;
        }

        static bool PathMatchesKeywords(string path, string[] keywords)
        {
            string lower = path.ToLowerInvariant();
            for (int i = 0; i < keywords.Length; i++)
            {
                if (lower.Contains(keywords[i]))
                    return true;
            }
            return false;
        }

        static bool PathSuggestsElement(string path, ElementType element)
        {
            string lower = path.ToLowerInvariant();
            switch (element)
            {
                case ElementType.Fire:
                    return lower.Contains("fire") || lower.Contains("flame") || lower.Contains("ember")
                        || lower.Contains("/red/") || lower.Contains("red_fire");
                case ElementType.Water:
                    return lower.Contains("water") || lower.Contains("ice") || lower.Contains("frost")
                        || lower.Contains("aqua");
                case ElementType.Grass:
                    return lower.Contains("grass") || lower.Contains("nature") || lower.Contains("leaf")
                        || lower.Contains("poison") || lower.Contains("green_fire") || lower.Contains("/green/");
                case ElementType.Light:
                    return lower.Contains("light") || lower.Contains("holy") || lower.Contains("golden")
                        || lower.Contains("electric") || lower.Contains("spark") || lower.Contains("paladin")
                        || lower.Contains("shootingstar") || lower.Contains("ambar");
                default:
                    return false;
            }
        }

        static bool HasAnyElementToken(string path)
        {
            string lower = path.ToLowerInvariant();
            return lower.Contains("fire") || lower.Contains("water") || lower.Contains("ice")
                || lower.Contains("grass") || lower.Contains("nature") || lower.Contains("light")
                || lower.Contains("holy") || lower.Contains("flame") || lower.Contains("leaf")
                || lower.Contains("poison") || lower.Contains("golden");
        }

        static int ScorePath(string path, ElementType element, bool preferCartoon, string[] keywords)
        {
            string lower = path.ToLowerInvariant();
            int score = 0;
            if (PathSuggestsElement(path, element)) score += 50;
            if (lower.Contains("piloto") || lower.Contains("super realistic"))
                score += 40;
            if (preferCartoon && (lower.Contains("cartoon") || lower.Contains("cfx") || lower.Contains("jmo")))
                score += 20;
            if (!preferCartoon && (lower.Contains("magic") || lower.Contains("spell") || lower.Contains("projectile")))
                score += 20;
            for (int i = 0; i < keywords.Length; i++)
            {
                if (lower.Contains(keywords[i]))
                    score += 5;
            }
            if (lower.Contains("simple") || lower.Contains("small") || lower.Contains("tiny") || lower.Contains("light"))
                score += 8;
            if (lower.Contains("heavy") || lower.Contains("complex") || lower.Contains("big_"))
                score -= 10;
            return score;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parts = path.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
