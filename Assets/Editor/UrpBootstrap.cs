using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NexusAnima.Editor
{
    /// <summary>
    /// Creates/assigns URP pipeline via reflection (so this file compiles even before
    /// URP assemblies finish importing), then reimports Piloto Shader Graphs.
    /// </summary>
    public static class UrpBootstrap
    {
        const string SettingsFolder = "Assets/Settings";
        const string RendererPath = SettingsFolder + "/URP_Renderer.asset";
        const string PipelinePath = SettingsFolder + "/URP_Pipeline.asset";

        [MenuItem("NexusAnima/Setup URP For Piloto VFX")]
        public static void Setup()
        {
            var urpRuntime = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Unity.RenderPipelines.Universal.Runtime");
            if (urpRuntime == null)
            {
                EditorUtility.DisplayDialog(
                    "URP Setup",
                    "还没有加载到 URP 程序集。\n请等 Package Manager 导入完成，或重启 Unity 后再运行本菜单。\n\n已安装包：com.unity.render-pipelines.universal",
                    "OK");
                Debug.LogError("[UrpBootstrap] Unity.RenderPipelines.Universal.Runtime not loaded yet.");
                return;
            }

            EnsureFolder(SettingsFolder);

            Type rendererType = urpRuntime.GetType("UnityEngine.Rendering.Universal.UniversalRendererData");
            Type pipelineType = urpRuntime.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset");
            if (rendererType == null || pipelineType == null)
            {
                EditorUtility.DisplayDialog("URP Setup", "找不到 URP 类型，请确认包版本兼容 Unity 6。", "OK");
                return;
            }

            var renderer = AssetDatabase.LoadAssetAtPath(RendererPath, rendererType) as ScriptableObject;
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance(rendererType) as ScriptableObject;
                AssetDatabase.CreateAsset(renderer, RendererPath);
            }

            var pipeline = AssetDatabase.LoadAssetAtPath(PipelinePath, pipelineType) as RenderPipelineAsset;
            if (pipeline == null)
            {
                // UniversalRenderPipelineAsset.Create(UniversalRendererData)
                var create = pipelineType.GetMethod(
                    "Create",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { rendererType },
                    null);
                if (create != null)
                {
                    pipeline = create.Invoke(null, new object[] { renderer }) as RenderPipelineAsset;
                }
                else
                {
                    pipeline = ScriptableObject.CreateInstance(pipelineType) as RenderPipelineAsset;
                    var so = new SerializedObject(pipeline);
                    var list = so.FindProperty("m_RendererDataList");
                    if (list != null)
                    {
                        list.arraySize = Mathf.Max(1, list.arraySize);
                        list.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();

            AssetDatabase.ImportAsset(
                "Assets/Piloto Studio/Shaders_Reforged",
                ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);

            var graphs = AssetDatabase.FindAssets("t:ShaderGraph", new[] { "Assets/Piloto Studio" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .ToArray();
            foreach (var path in graphs)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            AssetDatabase.ImportAsset(
                "Assets/Piloto Studio/Materials",
                ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();

            int good = 0, bad = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources/VFX" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null) continue;
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var m in r.sharedMaterials)
                    {
                        if (m == null || m.shader == null
                            || m.shader.name.Contains("InternalError")
                            || !m.shader.isSupported)
                            bad++;
                        else
                            good++;
                    }
                }
            }

            string msg =
                $"URP 已启用：{pipeline.name}\n" +
                $"ShaderGraphs：{graphs.Length}\n" +
                $"Resources/VFX 材质：正常 {good} / 异常 {bad}\n\n" +
                "若角色模型发粉，再运行：NexusAnima/Upgrade Materials To URP";
            EditorUtility.DisplayDialog("URP Setup", msg, "OK");
            Debug.Log("[UrpBootstrap] " + msg.Replace("\n", " | "));
        }

        [MenuItem("NexusAnima/Upgrade Materials To URP")]
        public static void UpgradeMaterials()
        {
            int upgraded = 0;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var particles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");

            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Assets/Piloto Studio/") || path.StartsWith("Assets/Resources/VFX/"))
                    continue;

                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null || mat.shader == null) continue;
                string name = mat.shader.name;

                Shader target = null;
                if (name == "Standard" || name == "Standard (Specular setup)")
                    target = lit;
                else if (name.Contains("Particles/") || name.StartsWith("Legacy Shaders/Particles"))
                    target = particles;
                else if (name == "Unlit/Color" || name == "Unlit/Texture")
                    target = unlit;

                if (target == null) continue;
                mat.shader = target;
                EditorUtility.SetDirty(mat);
                upgraded++;
            }

            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("URP Materials", $"已升级 {upgraded} 个材质到 URP。", "OK");
        }

        [MenuItem("NexusAnima/Diagnose URP Status")]
        public static void Diagnose()
        {
            var asms = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetName().Name)
                .Where(n => n.IndexOf("RenderPipeline", StringComparison.OrdinalIgnoreCase) >= 0
                            || n.IndexOf("ShaderGraph", StringComparison.OrdinalIgnoreCase) >= 0
                            || n.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(n => n)
                .ToArray();

            var rp = GraphicsSettings.currentRenderPipeline;
            string info =
                $"currentRP={(rp != null ? rp.name : "Built-in")}\n" +
                $"URP Runtime loaded={asms.Any(a => a == "Unity.RenderPipelines.Universal.Runtime")}\n" +
                $"Assemblies:\n- {string.Join("\n- ", asms)}";
            Debug.Log("[UrpBootstrap] " + info.Replace("\n", " | "));
            EditorUtility.DisplayDialog("URP Status", info, "OK");
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
