using System.IO;
using UnityEditor;
using UnityEngine;

namespace NexusAnima.Editor
{
    public static class SpiritDataBaker
    {
        const string PlayerDir = "Assets/Resources/Data/Spirits/Player";
        const string EnemyDir = "Assets/Resources/Data/Spirits/Enemy";
        const string MasterDir = "Assets/Resources/Data/Masters";

        [MenuItem("NexusAnima/Bake Spirit Data Assets")]
        public static void BakeFromFallback()
        {
            EnsureFolder(PlayerDir);
            EnsureFolder(EnemyDir);
            EnsureFolder(MasterDir);

            int count = 0;
            foreach (var def in DemoDataFactory.CreatePlayerRosterFallback())
            {
                SaveSpirit(def, PlayerDir);
                count++;
            }

            foreach (var def in DemoDataFactory.CreateEnemyRosterFallback())
            {
                SaveSpirit(def, EnemyDir);
                count++;
            }

            var master = ScriptableObject.CreateInstance<FateMasterDefinition>();
            master.Id = "master_fire";
            master.DisplayName = "炎律因缘师";
            var masterPath = $"{MasterDir}/FateMaster_Fire.asset";
            AssetDatabase.CreateAsset(master, masterPath);
            count++;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SpiritDataBaker] Baked {count} assets.");
        }

        [MenuItem("NexusAnima/Apply Presentation Defaults To Spirit Assets")]
        public static void ApplyPresentationToExistingAssets()
        {
            int count = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:SpiritDefinition"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<SpiritDefinition>(path);
                if (def == null) continue;
                SpiritPresentationCatalog.ApplyDefaults(def);
                EditorUtility.SetDirty(def);
                count++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[SpiritDataBaker] Applied presentation defaults to {count} SpiritDefinition assets.");
        }

        static void SaveSpirit(SpiritDefinition runtime, string folder)
        {
            SpiritPresentationCatalog.ApplyDefaults(runtime);
            var path = $"{folder}/{runtime.Id}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<SpiritDefinition>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(runtime, existing);
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(runtime);
                return;
            }

            runtime.name = runtime.Id;
            AssetDatabase.CreateAsset(runtime, path);
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
