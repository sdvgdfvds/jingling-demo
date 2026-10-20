using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Fixes blurry Game view on Windows HiDPI (pixelsPerPoint &gt; 1, e.g. 150%).
///
/// Root cause: Free Aspect renders at point resolution (window/ppp) then the
/// editor upscales to physical pixels → everything looks soft.
///
/// Fix: switch Game view to a Fixed Resolution matching the physical window
/// pixel size so Screen/camera render 1:1 with the display pixels.
/// </summary>
[InitializeOnLoad]
internal static class ForceSharpGameView
{
    const string CustomSizeTag = "HiDPI Native";
    static bool _pending;
    static int _framesLeft;
    static int _lastAppliedW;
    static int _lastAppliedH;

    static ForceSharpGameView()
    {
        EditorApplication.delayCall += ScheduleApply;
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.update += OnUpdate;
    }

    static void OnPlayMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode ||
            state == PlayModeStateChange.EnteredPlayMode ||
            state == PlayModeStateChange.ExitingEditMode)
        {
            ScheduleApply();
        }
    }

    static void ScheduleApply()
    {
        _pending = true;
        _framesLeft = 8;
    }

    static void OnUpdate()
    {
        if (!_pending) return;
        if (--_framesLeft > 0) return;
        _pending = false;
        Apply(false);
    }

    [MenuItem("NexusAnima/Fix Blurry Game View (HiDPI)")]
    static void MenuFix()
    {
        Apply(true);
        Debug.Log(
            $"[ForceSharpGameView] ppp={EditorGUIUtility.pixelsPerPoint} " +
            $"Screen={Screen.width}x{Screen.height} gv={Handles.GetMainGameViewSize()} " +
            $"AA={QualitySettings.antiAliasing}");
    }

    static void Apply(bool force)
    {
        if (QualitySettings.antiAliasing < 4)
            QualitySettings.antiAliasing = 4;
        if (QualitySettings.anisotropicFiltering != AnisotropicFiltering.ForceEnable)
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;

        float ppp = EditorGUIUtility.pixelsPerPoint;
        if (ppp <= 1.01f)
        {
            SnapZoomInOnly();
            return;
        }

        Vector2 lastPx = GetLastWindowPixelSize();
        if (lastPx.x < 64f || lastPx.y < 64f)
            return;

        int w = Mathf.Max(640, ((int)lastPx.x) & ~1);
        int h = Mathf.Max(360, ((int)(lastPx.y - 31f * ppp)) & ~1);

        if (!force && w == _lastAppliedW && h == _lastAppliedH && IsAlreadyPhysical(lastPx))
        {
            SnapZoomInOnly();
            return;
        }

        int index = EnsureAndSelectPhysicalSize(w, h);
        if (index >= 0)
        {
            _lastAppliedW = w;
            _lastAppliedH = h;
        }

        SnapZoomInOnly();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    static bool IsAlreadyPhysical(Vector2 lastPx)
    {
        // Free Aspect under HiDPI: Screen ≈ lastPx / ppp. Physical: Screen ≈ lastPx content.
        return Screen.width >= lastPx.x * 0.9f;
    }

    static int EnsureAndSelectPhysicalSize(int w, int h)
    {
        Type sizesType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizes");
        Type sizeType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSize");
        Type sizeTypeEnum = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizeType");
        Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (sizesType == null || sizeType == null || sizeTypeEnum == null || gvType == null)
            return -1;

        UnityEngine.Object[] sizeObjs = Resources.FindObjectsOfTypeAll(sizesType);
        if (sizeObjs == null || sizeObjs.Length == 0)
            return -1;

        object sizes = sizeObjs[0];
        object group = sizesType.GetProperty("currentGroup", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(sizes);
        if (group == null)
            return -1;

        Type groupType = group.GetType();
        MethodInfo getTexts = groupType.GetMethod("GetDisplayTexts");
        string[] texts = getTexts != null
            ? (string[])getTexts.Invoke(group, null)
            : Array.Empty<string>();

        string label = $"{CustomSizeTag} {w}x{h}";
        int found = IndexOf(texts, CustomSizeTag);

        if (found < 0)
        {
            object fixedRes = Enum.Parse(sizeTypeEnum, "FixedResolution");
            ConstructorInfo ctor = null;
            foreach (ConstructorInfo c in sizeType.GetConstructors())
            {
                ParameterInfo[] ps = c.GetParameters();
                if (ps.Length == 4)
                {
                    ctor = c;
                    break;
                }
            }

            object newSize = ctor?.Invoke(new object[] { fixedRes, w, h, label });
            if (newSize == null)
                return -1;

            groupType.GetMethod("AddCustomSize")?.Invoke(group, new[] { newSize });
            texts = getTexts != null ? (string[])getTexts.Invoke(group, null) : texts;
            found = IndexOf(texts, CustomSizeTag);
        }
        else
        {
            try
            {
                MethodInfo getter = groupType.GetMethod("GetGameViewSize", new[] { typeof(int) });
                object existing = getter?.Invoke(group, new object[] { found });
                if (existing != null)
                {
                    sizeType.GetProperty("width")?.SetValue(existing, w, null);
                    sizeType.GetProperty("height")?.SetValue(existing, h, null);
                    sizeType.GetProperty("baseText")?.SetValue(existing, label, null);
                }
            }
            catch
            {
                // Selection alone is enough for sharpness
            }
        }

        if (found < 0)
        {
            // Fallback to built-in QHD / Full HD
            found = IndexOf(texts, "2560");
            if (found < 0) found = IndexOf(texts, "1920");
        }

        if (found < 0)
            return -1;

        PropertyInfo selectedProp = gvType.GetProperty("selectedSizeIndex",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(gvType))
        {
            selectedProp?.SetValue(obj, found, null);

            foreach (MethodInfo m in gvType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m.Name != "SizeSelectionCallback") continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 2 && ps[0].ParameterType == typeof(int))
                {
                    m.Invoke(obj, new object[] { found, null });
                    break;
                }
            }

            var so = new SerializedObject(obj);
            SerializedProperty arr = so.FindProperty("m_SelectedSizes");
            if (arr != null && arr.isArray)
            {
                for (int i = 0; i < arr.arraySize; i++)
                    arr.GetArrayElementAtIndex(i).intValue = found;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            ((EditorWindow)obj).Repaint();
        }

        return found;
    }

    static void SnapZoomInOnly()
    {
        Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gvType == null) return;

        FieldInfo zoomAreaField = gvType.GetField("m_ZoomArea", BindingFlags.Instance | BindingFlags.NonPublic);
        if (zoomAreaField == null) return;

        foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(gvType))
        {
            object zoomArea = zoomAreaField.GetValue(obj);
            if (zoomArea == null) continue;

            FieldInfo scaleField = zoomArea.GetType().GetField("m_Scale", BindingFlags.Instance | BindingFlags.NonPublic);
            if (scaleField == null) continue;

            var scale = (Vector2)scaleField.GetValue(zoomArea);
            // Zoom-in (>1) upscales and blurs. Zoom-out (<1) fits a larger buffer — keep it.
            if (scale.x > 1.01f || scale.y > 1.01f)
            {
                scaleField.SetValue(zoomArea, Vector2.one);
                ((EditorWindow)obj).Repaint();
            }

            FieldInfo lowRes = gvType.GetField("m_LowResolutionForAspectRatios",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (lowRes != null && lowRes.FieldType == typeof(bool))
                lowRes.SetValue(obj, false);
        }
    }

    static Vector2 GetLastWindowPixelSize()
    {
        Type gvType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        if (gvType == null) return Vector2.zero;

        foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(gvType))
        {
            SerializedProperty lastPx = new SerializedObject(obj).FindProperty("m_LastWindowPixelSize");
            if (lastPx != null)
                return lastPx.vector2Value;
        }

        return Vector2.zero;
    }

    static int IndexOf(string[] texts, string contains)
    {
        if (texts == null) return -1;
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null && texts[i].IndexOf(contains, StringComparison.Ordinal) >= 0)
                return i;
        }
        return -1;
    }
}
