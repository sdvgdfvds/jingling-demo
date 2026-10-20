using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// BattleDemo only: keep edit-mode Scene matching Play layout.
/// On exiting Play, capture camera / board / stage / sun and write them
/// back after Unity discards Play-mode changes.
/// </summary>
[InitializeOnLoad]
internal static class PlayLayoutSync
{
    const string ConcertScene = "BattleDemo";
    const string EnabledKey = "NexusAnima.PlayLayoutSync.Enabled";
    const string PendingKey = "NexusAnima.PlayLayoutSync.Pending";
    const string MenuPath = "NexusAnima/退出 Play 时写回布局";
    const float Epsilon = 0.00005f;

    [System.Serializable]
    class Pose
    {
        public string path;
        public float lx, ly, lz;
        public float ex, ey, ez;
        public float sx, sy, sz;
        public bool hasCam;
        public float fov, near;
        public bool hasLight;
        public float intensity, shadowStrength, shadowBias, shadowNormalBias, shadowNear;
        public int shadows;
        public bool hasBoard;
        public float cellSize;
        public bool hasActive;
        public bool active;
        public bool unlockStatic;
        public bool positionOnly;
    }

    [System.Serializable]
    class Snapshot
    {
        public string scene;
        public Pose[] poses;
        public Pose truss;
    }

    static PlayLayoutSync()
    {
        EditorApplication.playModeStateChanged += OnPlayMode;
        BindHubSave();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    static void BindHubSave()
    {
        NexusAnima.HudPanelHub.GameChangesSaveRequested -= CaptureNow;
        NexusAnima.HudPanelHub.GameChangesSaveRequested += CaptureNow;
    }

    /// <summary>Called from the Game 面板坞 保存 button while playing.</summary>
    internal static void CaptureNow()
    {
        Capture(force: true);
    }

    [MenuItem(MenuPath)]
    static void ToggleEnabled()
    {
        bool next = !IsEnabled();
        EditorPrefs.SetBool(EnabledKey, next);
        Debug.Log(next
            ? "[PlayLayoutSync] 已开启：退出 Play 时把布局写回 BattleDemo。"
            : "[PlayLayoutSync] 已关闭。");
    }

    [MenuItem(MenuPath, true)]
    static bool ToggleEnabledValidate()
    {
        Menu.SetChecked(MenuPath, IsEnabled());
        return true;
    }

    static bool IsEnabled()
    {
        return EditorPrefs.GetBool(EnabledKey, true);
    }

    static string JsonPath()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "PlayLayoutSync.json"));
    }

    static void OnPlayMode(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingPlayMode)
            Capture(force: false);
        else if (state == PlayModeStateChange.EnteredEditMode)
            ApplyPending();
    }

    static bool IsConcertSceneAlone(Scene scene)
    {
        if (!scene.IsValid() || scene.name != ConcertScene)
            return false;
        if (SceneManager.sceneCount != 1)
            return false;
        return true;
    }

    static Transform FindScenePath(Scene scene, string path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        string[] parts = path.Split('/');
        var roots = scene.GetRootGameObjects();
        Transform t = null;
        for (int i = 0; i < roots.Length; i++)
        {
            if (roots[i].name != parts[0])
                continue;
            t = roots[i].transform;
            break;
        }
        if (t == null)
            return null;
        for (int i = 1; i < parts.Length; i++)
        {
            t = t.Find(parts[i]);
            if (t == null)
                return null;
        }
        return t;
    }

    static Pose ReadPose(Transform t, string path)
    {
        var pose = new Pose();
        pose.path = path;
        Vector3 lp = t.localPosition;
        Vector3 le = t.localEulerAngles;
        Vector3 ls = t.localScale;
        pose.lx = lp.x;
        pose.ly = lp.y;
        pose.lz = lp.z;
        pose.ex = le.x;
        pose.ey = le.y;
        pose.ez = le.z;
        pose.sx = ls.x;
        pose.sy = ls.y;
        pose.sz = ls.z;

        var cam = t.GetComponent<Camera>();
        if (cam != null)
        {
            pose.hasCam = true;
            pose.fov = cam.fieldOfView;
            pose.near = cam.nearClipPlane;
        }

        var light = t.GetComponent<Light>();
        if (light != null && light.type == LightType.Directional)
        {
            pose.hasLight = true;
            pose.intensity = light.intensity;
            pose.shadowStrength = light.shadowStrength;
            pose.shadowBias = light.shadowBias;
            pose.shadowNormalBias = light.shadowNormalBias;
            pose.shadowNear = light.shadowNearPlane;
            pose.shadows = (int)light.shadows;
        }

        var board = t.GetComponent<NexusAnima.BoardGrid>();
        if (board != null)
        {
            pose.hasBoard = true;
            pose.cellSize = board.CellSize;
        }

        pose.hasActive = true;
        pose.active = t.gameObject.activeSelf;
        pose.unlockStatic = NexusAnima.PropPlayAdjust.IsCeilingPropName(t.name);
        return pose;
    }

    static bool NearlyEqual(float a, float b)
    {
        return Mathf.Abs(a - b) <= Epsilon;
    }

    static bool PoseMatches(Transform t, Pose pose)
    {
        Vector3 lp = t.localPosition;
        Vector3 le = t.localEulerAngles;
        Vector3 ls = t.localScale;
        if (!NearlyEqual(lp.x, pose.lx) || !NearlyEqual(lp.y, pose.ly) || !NearlyEqual(lp.z, pose.lz))
            return false;
        if (pose.positionOnly)
            return true;
        if (!NearlyEqual(ls.x, pose.sx) || !NearlyEqual(ls.y, pose.sy) || !NearlyEqual(ls.z, pose.sz))
            return false;
        if (Quaternion.Angle(t.localRotation, Quaternion.Euler(pose.ex, pose.ey, pose.ez)) > 0.05f)
            return false;

        if (pose.hasCam)
        {
            var cam = t.GetComponent<Camera>();
            if (cam == null)
                return false;
            if (!NearlyEqual(cam.fieldOfView, pose.fov) || !NearlyEqual(cam.nearClipPlane, pose.near))
                return false;
        }
        if (pose.hasLight)
        {
            var light = t.GetComponent<Light>();
            if (light == null)
                return false;
            if (!NearlyEqual(light.intensity, pose.intensity) ||
                !NearlyEqual(light.shadowStrength, pose.shadowStrength))
                return false;
        }
        if (pose.hasBoard)
        {
            var board = t.GetComponent<NexusAnima.BoardGrid>();
            if (board == null)
                return false;
            if (!NearlyEqual(board.CellSize, pose.cellSize))
                return false;
        }
        if (pose.hasActive && t.gameObject.activeSelf != pose.active)
            return false;
        return true;
    }

    static bool WritePose(Transform t, Pose pose)
    {
        if (t == null || pose == null)
            return false;
        if (PoseMatches(t, pose))
            return false;

        Undo.RecordObject(t, "Play layout sync");
        t.localPosition = new Vector3(pose.lx, pose.ly, pose.lz);
        if (!pose.positionOnly)
        {
            t.localRotation = Quaternion.Euler(pose.ex, pose.ey, pose.ez);
            t.localScale = new Vector3(pose.sx, pose.sy, pose.sz);
        }
        EditorUtility.SetDirty(t);

        if (pose.hasCam)
        {
            var cam = t.GetComponent<Camera>();
            if (cam != null)
            {
                Undo.RecordObject(cam, "Play layout sync camera");
                cam.fieldOfView = pose.fov;
                cam.nearClipPlane = pose.near;
                cam.useOcclusionCulling = false;
                EditorUtility.SetDirty(cam);
            }
        }
        if (pose.hasLight)
        {
            var light = t.GetComponent<Light>();
            if (light != null)
            {
                Undo.RecordObject(light, "Play layout sync light");
                light.intensity = pose.intensity;
                light.shadowStrength = pose.shadowStrength;
                light.shadowBias = pose.shadowBias;
                light.shadowNormalBias = pose.shadowNormalBias;
                light.shadowNearPlane = pose.shadowNear;
                light.shadows = (LightShadows)pose.shadows;
                EditorUtility.SetDirty(light);
            }
        }
        if (pose.hasBoard)
        {
            var board = t.GetComponent<NexusAnima.BoardGrid>();
            if (board != null)
            {
                Undo.RecordObject(board, "Play layout sync board");
                board.CellSize = pose.cellSize;
                EditorUtility.SetDirty(board);
            }
        }
        if (pose.hasActive && t.gameObject.activeSelf != pose.active)
        {
            Undo.RecordObject(t.gameObject, "Play layout sync active");
            t.gameObject.SetActive(pose.active);
            EditorUtility.SetDirty(t.gameObject);
        }
        if (pose.unlockStatic)
            ClearStaticEditor(t.gameObject);
        return true;
    }

    static void ClearStaticEditor(GameObject go)
    {
        if (go == null)
            return;
        GameObjectUtility.SetStaticEditorFlags(go, 0);
        go.isStatic = false;
        var tr = go.transform;
        for (int i = 0; i < tr.childCount; i++)
            ClearStaticEditor(tr.GetChild(i).gameObject);
    }

    static Transform FindNamedUnderStage(Scene scene, string leaf)
    {
        var stage = FindScenePath(scene, "GameRoot/PBRStageEquipment");
        if (stage == null || string.IsNullOrEmpty(leaf))
            return null;
        var all = stage.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].name == leaf)
                return all[i];
        }
        return null;
    }

    static Transform FindTruss(Scene scene)
    {
        return FindNamedUnderStage(scene, "Truss01_Circle5m");
    }

    static Transform FindPoseTarget(Scene scene, string path)
    {
        var t = FindScenePath(scene, path);
        if (t != null)
            return t;
        if (string.IsNullOrEmpty(path))
            return null;
        string[] parts = path.Split('/');
        string leaf = parts[parts.Length - 1];
        return FindNamedUnderStage(scene, leaf);
    }

    static int CountCapture(Transform t, Transform stage)
    {
        if (t == null)
            return 0;
        int n = 0;
        if (NexusAnima.PropPlayAdjust.IsLayoutCaptureTarget(t, stage))
            n++;
        if (t.name.StartsWith("MirrorBall") && t.Find("Ball") != null)
            n++;
        for (int i = 0; i < t.childCount; i++)
            n += CountCapture(t.GetChild(i), stage);
        return n;
    }

    static void CollectCapture(Transform t, string path, Transform stage, Pose[] poses, ref int count)
    {
        if (t == null)
            return;
        if (NexusAnima.PropPlayAdjust.IsLayoutCaptureTarget(t, stage))
            poses[count++] = ReadPose(t, path);
        if (t.name.StartsWith("MirrorBall"))
        {
            var ball = t.Find("Ball");
            if (ball != null)
            {
                var bp = ReadPose(ball, path + "/Ball");
                bp.positionOnly = true;
                poses[count++] = bp;
            }
        }
        for (int i = 0; i < t.childCount; i++)
        {
            var c = t.GetChild(i);
            if (c == null)
                continue;
            CollectCapture(c, path + "/" + c.name, stage, poses, ref count);
        }
    }

    static void Capture(bool force = false)
    {
        EditorPrefs.SetBool(PendingKey, false);
        if (!force && !IsEnabled())
            return;

        var scene = SceneManager.GetActiveScene();
        if (!IsConcertSceneAlone(scene))
            return;

        string[] paths =
        {
            "Main Camera",
            "Directional Light",
            "GameRoot/Board",
            "GameRoot/Board/DynamicGrid",
            "GameRoot/PBRStageEquipment"
        };

        var stage = FindScenePath(scene, "GameRoot/PBRStageEquipment");
        int extra = stage != null ? CountCapture(stage, stage) : 0;

        var poses = new Pose[paths.Length + extra];
        int count = 0;
        for (int i = 0; i < paths.Length; i++)
        {
            var t = FindScenePath(scene, paths[i]);
            if (t == null)
                continue;
            poses[count++] = ReadPose(t, paths[i]);
        }
        if (stage != null)
            CollectCapture(stage, "GameRoot/PBRStageEquipment", stage, poses, ref count);
        if (count == 0)
            return;

        var trimmed = new Pose[count];
        for (int i = 0; i < count; i++)
            trimmed[i] = poses[i];

        var snap = new Snapshot();
        snap.scene = scene.name;
        snap.poses = trimmed;
        var truss = FindTruss(scene);
        if (truss != null)
            snap.truss = ReadPose(truss, "Truss01_Circle5m");

        try
        {
            string dir = Path.GetDirectoryName(JsonPath());
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(JsonPath(), JsonUtility.ToJson(snap, true));
            EditorPrefs.SetBool(PendingKey, true);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[PlayLayoutSync] 无法写入快照: " + ex.Message);
        }
    }

    static void ApplyPending()
    {
        if (!EditorPrefs.GetBool(PendingKey, false))
            return;
        EditorPrefs.SetBool(PendingKey, false);
        if (!IsEnabled())
            return;

        var scene = SceneManager.GetActiveScene();
        if (!IsConcertSceneAlone(scene))
            return;

        string path = JsonPath();
        if (!File.Exists(path))
            return;

        Snapshot snap;
        try
        {
            snap = JsonUtility.FromJson<Snapshot>(File.ReadAllText(path));
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("[PlayLayoutSync] 无法读取快照: " + ex.Message);
            return;
        }
        if (snap == null || snap.scene != ConcertScene || snap.poses == null)
            return;

        bool dirty = false;
        for (int i = 0; i < snap.poses.Length; i++)
        {
            var pose = snap.poses[i];
            if (pose == null || string.IsNullOrEmpty(pose.path))
                continue;
            var t = FindPoseTarget(scene, pose.path);
            if (WritePose(t, pose))
                dirty = true;
        }
        if (snap.truss != null)
        {
            var truss = FindTruss(scene);
            if (WritePose(truss, snap.truss))
                dirty = true;
        }

        if (!dirty)
            return;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[PlayLayoutSync] 已把 Play 布局写回 " + scene.path);
    }
}
