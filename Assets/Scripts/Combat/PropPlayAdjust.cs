using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NexusAnima
{
    /// <summary>
    /// Play / Game 视图里点选、拖移、缩放、隐藏挡视线的舞台物件
    /// （天花板镜地球、吊装头灯/摄像头）。只作用于当前场景的 PBRStageEquipment。
    /// DiscoLight / LaserProjector 点一个就整组一起平移；摆位时暂停摇头动画。
    /// </summary>
    public class PropPlayAdjust : MonoBehaviour
    {
        public static bool IsDriving { get; private set; }

        const string HudName = "摆遮挡";
        const float MinAbsScale = 0.001f;
        const float MaxAbsScale = 8f;
        const float HeadLiftStep = 0.02f;

        Transform _stage;
        Transform _selected;
        bool _adjusting;
        bool _dragging;
        Vector3 _dragOffset;
        GameObject _hud;
        Text _buttonText;
        Image _buttonImage;
        Text _hintText;
        Text _poseText;
        Transform[] _cycle;
        int _cycleIndex;
        Animator[] _frozenAnims;
        bool[] _frozenAnimEnabled;
        float[] _frozenAnimSpeed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IsDriving = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BootHook()
        {
            Ensure();
        }

        public static void Ensure()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.name != "BattleDemo")
                return;

            Transform stage = null;
            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t.gameObject.scene != scene)
                    continue;
                if (t.name == "PBRStageEquipment")
                {
                    stage = t;
                    break;
                }
            }
            if (stage == null)
                return;

            var adj = stage.GetComponent<PropPlayAdjust>();
            if (adj == null)
                adj = stage.gameObject.AddComponent<PropPlayAdjust>();
            adj._stage = stage;
            UnlockCeilingProps(stage);
            AttachHangingRigs(stage);
            UnlockCeilingProps(stage);
            StopMirrorBallSpin(stage);
            if (Application.isPlaying)
                adj.EnsureHud();
        }

        /// <summary>
        /// Disco ball clip yaws the sphere every frame — disable it so Game drag
        /// actually sticks, and so the ball no longer spins in the player's face.
        /// </summary>
        public static void StopMirrorBallSpin(Transform stage)
        {
            if (stage == null)
                return;
            var anims = stage.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < anims.Length; i++)
            {
                var an = anims[i];
                if (an == null)
                    continue;
                string n = an.gameObject.name;
                bool ball = n.StartsWith("MirrorBall") || n == "Ball";
                if (!ball && an.transform.parent != null)
                    ball = an.transform.parent.name.StartsWith("MirrorBall");
                if (!ball)
                    continue;
                an.enabled = false;
                an.speed = 0f;
            }
        }

        public static void StopIfDriving()
        {
            if (!IsDriving)
                return;
            var found = Object.FindObjectsByType<PropPlayAdjust>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] != null)
                    found[i].StopAdjusting();
            }
        }

        public static bool IsCircleTrussName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            return name.IndexOf("Circle5m") >= 0
                || name.IndexOf("Circle15m") >= 0
                || name.IndexOf("Truss01_Circle") >= 0;
        }

        public static bool IsHangingHeadName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            return name.StartsWith("DiscoLight")
                || name.StartsWith("LaserProjector");
        }

        public static bool IsHangingHeadRoot(Transform t, Transform stage)
        {
            if (t == null || stage == null)
                return false;
            if (!IsHangingHeadName(t.name))
                return false;
            var p = t.parent;
            if (p == null)
                return false;
            if (p == stage)
                return true;
            return IsCircleTrussName(p.name);
        }

        public static bool IsCeilingPropName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            return name.StartsWith("MirrorBall")
                || name == "Ball"
                || IsHangingHeadName(name)
                || IsCircleTrussName(name);
        }

        public static bool IsHeadGroupName(string name)
        {
            return IsHangingHeadName(name) || IsCircleTrussName(name);
        }

        public static bool IsLayoutCaptureTarget(Transform t, Transform stage)
        {
            if (t == null)
                return false;
            if (t.name.StartsWith("MirrorBall"))
                return true;
            if (IsCircleTrussName(t.name))
                return true;
            return IsHangingHeadRoot(t, stage);
        }

        public static void UnlockCeilingProps(Transform stage)
        {
            if (stage == null)
                return;
            var all = stage.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null)
                    continue;
                if (!IsLayoutCaptureTarget(t, stage))
                    continue;
                BoardGrid.ClearStaticRecursive(t.gameObject);
            }
        }

        public static void AttachHangingRigs(Transform stage)
        {
            if (stage == null)
                return;

            var all = stage.GetComponentsInChildren<Transform>(true);
            int circleN = 0;
            int headN = 0;
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t == stage)
                    continue;
                if (IsCircleTrussName(t.name))
                    circleN++;
                else if (IsHangingHeadRoot(t, stage))
                    headN++;
            }
            if (circleN == 0 || headN == 0)
                return;

            var circles = new Transform[circleN];
            var heads = new Transform[headN];
            int cw = 0;
            int hw = 0;
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t == stage)
                    continue;
                if (IsCircleTrussName(t.name))
                    circles[cw++] = t;
                else if (IsHangingHeadRoot(t, stage))
                    heads[hw++] = t;
            }

            for (int i = 0; i < hw; i++)
            {
                var head = heads[i];
                if (head == null)
                    continue;
                if (head.parent != null && IsCircleTrussName(head.parent.name))
                    continue;
                var best = BestCircleForHead(head, circles, cw);
                if (best == null)
                    continue;
                BoardGrid.ClearStaticRecursive(head.gameObject);
                BoardGrid.ClearStaticRecursive(best.gameObject);
                head.SetParent(best, true);
            }
        }

        static Transform BestCircleForHead(Transform head, Transform[] circles, int circleN)
        {
            if (head == null || circles == null || circleN <= 0)
                return null;
            Vector3 hp = head.position;
            Transform best = null;
            float bestScore = 1e8f;
            for (int i = 0; i < circleN; i++)
            {
                var c = circles[i];
                if (c == null)
                    continue;
                Vector3 cp = c.position;
                float dx = hp.x - cp.x;
                float dz = hp.z - cp.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                float score = Mathf.Abs(dist - CircleRadiusWorld(c));
                if (score < bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }
            return best;
        }

        static float CircleRadiusWorld(Transform circle)
        {
            if (circle == null)
                return 0.02f;
            var r = circle.GetComponent<Renderer>();
            if (r == null)
                r = circle.GetComponentInChildren<Renderer>(true);
            if (r != null)
            {
                Vector3 e = r.bounds.extents;
                float rad = 0.5f * (e.x + e.z);
                if (rad > 0.001f)
                    return rad;
            }
            if (circle.name.IndexOf("15m") >= 0)
                return 0.047f;
            return 0.019f;
        }

        void Awake()
        {
            if (_stage == null)
                _stage = transform;
        }

        void OnDisable()
        {
            if (_adjusting)
                StopAdjusting();
        }

        void OnDestroy()
        {
            RestoreHeadMotion();
            DestroyHud();
            if (IsDriving && !_adjusting)
                IsDriving = false;
        }

        void Update()
        {
            if (!Application.isPlaying)
                return;

            if (WasTogglePressed())
                ToggleAdjusting();

            if (!_adjusting)
                return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                StopAdjusting();
                return;
            }

            bool overUi = EventSystem.current != null
                && EventSystem.current.IsPointerOverGameObject();

            if (Input.GetKeyDown(KeyCode.LeftBracket) || Input.GetKeyDown(KeyCode.RightBracket)
                || Input.GetKeyDown(KeyCode.Tab))
            {
                int dir = Input.GetKeyDown(KeyCode.LeftBracket) ? -1 : 1;
                Cycle(dir);
            }

            if (_selected != null && Input.GetKeyDown(KeyCode.H))
                ToggleHidden(_selected);

            if (Input.GetKeyDown(KeyCode.PageUp))
                LiftHeadGroup(HeadLiftStep);
            if (Input.GetKeyDown(KeyCode.PageDown))
                LiftHeadGroup(-HeadLiftStep);

            HandlePickAndDrag(overUi);
            if (_selected != null)
            {
                HandleNudge(_selected);
                HandleHeight(_selected);
                HandleScale(overUi);
            }
        }

        static bool WasTogglePressed()
        {
            return Input.GetKeyDown(KeyCode.F4)
                || Input.GetKeyDown(KeyCode.F9);
        }

        public void ToggleAdjusting()
        {
            if (_adjusting) StopAdjusting();
            else StartAdjusting();
        }

        void StartAdjusting()
        {
            BattleFlyCamera.StopIfDriving();
            GridPlayAdjust.StopIfDriving();
            if (_stage == null)
                _stage = transform;
            UnlockCeilingProps(_stage);
            AttachHangingRigs(_stage);
            UnlockCeilingProps(_stage);
            StopMirrorBallSpin(_stage);
            RebuildCycle();
            FreezeHeadMotion();
            _adjusting = true;
            IsDriving = true;
            _dragging = false;
            if (_selected == null)
                SelectFirstVisible();
            EnsureHud();
            HudPanelHub.SetVisible("prop", true, false);
            RefreshHud();
        }

        void StopAdjusting()
        {
            _adjusting = false;
            _dragging = false;
            IsDriving = false;
            RestoreHeadMotion();
            RefreshHud();
        }

        void FreezeHeadMotion()
        {
            if (_frozenAnims != null)
                return;
            if (_stage == null)
            {
                _frozenAnims = new Animator[0];
                _frozenAnimEnabled = new bool[0];
                _frozenAnimSpeed = new float[0];
                return;
            }

            var all = _stage.GetComponentsInChildren<Animator>(true);
            int n = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null)
                    continue;
                var root = MovableRoot(all[i].transform);
                if (root != null && IsHeadGroupName(root.name))
                    n++;
            }

            _frozenAnims = new Animator[n];
            _frozenAnimEnabled = new bool[n];
            _frozenAnimSpeed = new float[n];
            int w = 0;
            for (int i = 0; i < all.Length; i++)
            {
                var an = all[i];
                if (an == null)
                    continue;
                var root = MovableRoot(an.transform);
                if (root == null || !IsHeadGroupName(root.name))
                    continue;
                _frozenAnims[w] = an;
                _frozenAnimEnabled[w] = an.enabled;
                _frozenAnimSpeed[w] = an.speed;
                an.speed = 0f;
                an.enabled = false;
                w++;
            }
        }

        void RestoreHeadMotion()
        {
            if (_frozenAnims == null)
                return;
            for (int i = 0; i < _frozenAnims.Length; i++)
            {
                var an = _frozenAnims[i];
                if (an == null)
                    continue;
                an.enabled = _frozenAnimEnabled[i];
                an.speed = _frozenAnimSpeed[i];
            }
            _frozenAnims = null;
            _frozenAnimEnabled = null;
            _frozenAnimSpeed = null;
            StopMirrorBallSpin(_stage);
        }

        int CountHeadGroup()
        {
            var ring = RingRootOf(_selected);
            if (ring == null || _stage == null)
                return 0;
            int n = 0;
            var all = ring.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && IsHangingHeadRoot(all[i], _stage))
                    n++;
            }
            return n;
        }

        Transform RingRootOf(Transform t)
        {
            if (t == null)
                return null;
            var cur = t;
            int guard = 0;
            while (cur != null && cur != _stage && guard < 16)
            {
                if (IsCircleTrussName(cur.name))
                    return cur;
                cur = cur.parent;
                guard++;
            }
            return t;
        }

        void MoveRingBy(Transform t, Vector3 worldDelta)
        {
            if (t == null)
                return;
            if (worldDelta.sqrMagnitude < 1e-16f)
                return;
            var ring = RingRootOf(t);
            if (ring == null)
                ring = t;
            BoardGrid.ClearStaticRecursive(ring.gameObject);
            ring.position += worldDelta;
        }

        void MoveSelectedBy(Vector3 worldDelta)
        {
            MoveRingBy(_selected, worldDelta);
        }

        public void LiftHeadGroupUp()
        {
            LiftHeadGroup(HeadLiftStep);
        }

        public void LiftHeadGroupDown()
        {
            LiftHeadGroup(-HeadLiftStep);
        }

        void LiftHeadGroup(float dy)
        {
            if (!_adjusting)
                StartAdjusting();
            FreezeHeadMotion();
            if (_selected == null || !IsHeadGroupName(_selected.name))
                SelectFirstHead();
            MoveRingBy(_selected, new Vector3(0f, dy, 0f));
            RefreshHud();
        }

        void SelectFirstHead()
        {
            RebuildCycle();
            for (int i = 0; i < _cycle.Length; i++)
            {
                if (_cycle[i] != null && IsHeadGroupName(_cycle[i].name))
                {
                    Select(_cycle[i]);
                    return;
                }
            }
        }

        void RebuildCycle()
        {
            if (_stage == null)
            {
                _cycle = new Transform[0];
                return;
            }
            int n = 0;
            for (int i = 0; i < _stage.childCount; i++)
            {
                var c = _stage.GetChild(i);
                if (c != null && IsCycleRoot(c))
                    n++;
            }
            _cycle = new Transform[n];
            int w = 0;
            for (int i = 0; i < _stage.childCount; i++)
            {
                var c = _stage.GetChild(i);
                if (c != null && IsCycleRoot(c))
                    _cycle[w++] = c;
            }
        }

        bool IsCycleRoot(Transform t)
        {
            if (t == null || _stage == null || t.parent != _stage)
                return false;
            if (t.name.StartsWith("MirrorBall"))
                return true;
            if (IsCircleTrussName(t.name))
                return true;
            return IsHangingHeadName(t.name);
        }

        void SelectFirstVisible()
        {
            RebuildCycle();
            for (int i = 0; i < _cycle.Length; i++)
            {
                if (_cycle[i] != null && _cycle[i].gameObject.activeInHierarchy
                    && IsHeadGroupName(_cycle[i].name))
                {
                    Select(_cycle[i]);
                    return;
                }
            }
            for (int i = 0; i < _cycle.Length; i++)
            {
                if (_cycle[i] != null && _cycle[i].gameObject.activeInHierarchy)
                {
                    Select(_cycle[i]);
                    return;
                }
            }
            if (_cycle.Length > 0)
                Select(_cycle[0]);
        }

        void Cycle(int dir)
        {
            RebuildCycle();
            if (_cycle == null || _cycle.Length == 0)
                return;
            int n = _cycle.Length;
            int start = _cycleIndex;
            for (int k = 0; k < n; k++)
            {
                start = (start + dir + n) % n;
                if (_cycle[start] != null)
                {
                    _cycleIndex = start;
                    Select(_cycle[start]);
                    return;
                }
            }
        }

        void Select(Transform t)
        {
            _selected = t;
            if (t != null)
                BoardGrid.ClearStaticRecursive(t.gameObject);
            _dragging = false;
            RefreshHud();
        }

        Transform MovableRoot(Transform t)
        {
            if (t == null || _stage == null)
                return null;
            var cur = t;
            int guard = 0;
            while (cur != null && cur != _stage && guard < 16)
            {
                if (cur.name == "Ball" && cur.parent != null && cur.parent.name.StartsWith("MirrorBall"))
                    return cur;
                cur = cur.parent;
                guard++;
            }

            cur = t;
            guard = 0;
            while (cur != null && cur.parent != _stage && guard < 16)
            {
                cur = cur.parent;
                guard++;
            }
            if (cur == null || cur.parent != _stage)
                return null;
            if (!IsCeilingPropName(cur.name))
                return null;
            return cur;
        }

        void HandlePickAndDrag(bool overUi)
        {
            var cam = Camera.main;
            if (cam == null)
                return;

            if (!_dragging && overUi)
                return;

            var ray = cam.ScreenPointToRay(Input.mousePosition);

            if (Input.GetMouseButtonDown(0) && !overUi)
            {
                var picked = PickCeilingProp(ray);
                if (picked != null)
                    Select(picked);
                if (_selected != null)
                {
                    _dragging = true;
                    Vector3 hit;
                    if (TryPlaneHit(cam, _selected.position, out hit))
                        _dragOffset = _selected.position - hit;
                    else
                        _dragOffset = Vector3.zero;
                }
            }

            if (_dragging && _selected != null && Input.GetMouseButton(0))
            {
                Vector3 hit;
                if (TryPlaneHit(cam, _selected.position, out hit))
                {
                    Vector3 target = hit + _dragOffset;
                    MoveSelectedBy(target - _selected.position);
                }
                RefreshHud();
            }

            if (Input.GetMouseButtonUp(0))
                _dragging = false;
        }

        static bool TryPlaneHit(Camera cam, Vector3 pivot, out Vector3 hit)
        {
            var ray = cam.ScreenPointToRay(Input.mousePosition);
            var plane = new Plane(-cam.transform.forward, pivot);
            float dist;
            if (!plane.Raycast(ray, out dist))
            {
                hit = pivot;
                return false;
            }
            hit = ray.GetPoint(dist);
            return true;
        }

        Transform PickCeilingProp(Ray ray)
        {
            if (_stage == null)
                return null;
            Transform best = null;
            float bestDist = 1e8f;
            var rends = _stage.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                var r = rends[i];
                if (r == null || !r.gameObject.activeInHierarchy)
                    continue;
                var root = MovableRoot(r.transform);
                if (root == null)
                    continue;
                Bounds b = r.bounds;
                if (IsCircleTrussName(root.name) || root.name == "Ball" || root.name.StartsWith("MirrorBall"))
                {
                    Vector3 e = b.extents;
                    if (e.y < 0.04f) e.y = 0.04f;
                    if (e.x < 0.03f) e.x = 0.03f;
                    if (e.z < 0.03f) e.z = 0.03f;
                    b.extents = e;
                }
                float d;
                if (!b.IntersectRay(ray, out d))
                    continue;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = root;
                }
            }
            return best;
        }

        void HandleNudge(Transform t)
        {
            float step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                ? 0.02f
                : 0.004f;
            Vector3 delta = Vector3.zero;
            if (Input.GetKey(KeyCode.LeftArrow)) delta.x -= step;
            if (Input.GetKey(KeyCode.RightArrow)) delta.x += step;
            if (Input.GetKey(KeyCode.UpArrow)) delta.z += step;
            if (Input.GetKey(KeyCode.DownArrow)) delta.z -= step;
            if (delta.sqrMagnitude < 1e-12f)
                return;
            MoveRingBy(t, delta);
            RefreshHud();
        }

        void HandleHeight(Transform t)
        {
            float step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                ? 0.008f
                : 0.0015f;
            float dy = 0f;
            if (Input.GetKey(KeyCode.E)) dy += step;
            if (Input.GetKey(KeyCode.Q)) dy -= step;
            if (Mathf.Abs(dy) < 1e-8f)
                return;
            MoveRingBy(t, new Vector3(0f, dy, 0f));
            RefreshHud();
        }

        void HandleScale(bool overUi)
        {
            if (overUi || _selected == null)
                return;
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) < 0.01f)
                return;
            float factor = scroll > 0f ? 1.08f : 1f / 1.08f;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                factor = scroll > 0f ? 1.16f : 1f / 1.16f;
            Vector3 s = _selected.localScale;
            float mag = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z), 1e-5f);
            mag = Mathf.Clamp(mag * factor, MinAbsScale, MaxAbsScale);
            float signX = s.x < 0f ? -1f : 1f;
            float signY = s.y < 0f ? -1f : 1f;
            float signZ = s.z < 0f ? -1f : 1f;
            _selected.localScale = new Vector3(signX * mag, signY * mag, signZ * mag);
            RefreshHud();
        }

        void ToggleHidden(Transform t)
        {
            if (t == null)
                return;
            t.gameObject.SetActive(!t.gameObject.activeSelf);
            RefreshHud();
        }

        public void HideSelected()
        {
            if (_selected != null)
                ToggleHidden(_selected);
        }

        void EnsureHud()
        {
            UnusedHudBuilder();
            RegisterHud();
        }

        void RegisterHud()
        {
            if (_hud == null)
                return;
            var panel = _hud.transform.Find("Panel") as RectTransform;
            if (panel == null)
            {
                var win = _hud.GetComponentInChildren<HudWindow>(true);
                if (win != null)
                    panel = win.transform.Find("Panel") as RectTransform;
            }
            if (panel != null)
                HudPanelHub.Register("prop", "摆遮挡", panel, false);
        }

        void UnusedHudBuilder()
        {
            if (_hud != null)
            {
                if (FindChildNamed(_hud.transform, "LiftUp") != null)
                {
                    RefreshHud();
                    return;
                }
                DestroyHud();
            }

            var existing = GameObject.Find(HudName);
            if (existing != null && FindChildNamed(existing.transform, "LiftUp") == null)
            {
                Object.DestroyImmediate(existing);
                existing = null;
            }

            if (existing != null)
            {
                _hud = existing;
                var texts = existing.GetComponentsInChildren<Text>(true);
                if (texts != null)
                {
                    for (int i = 0; i < texts.Length; i++)
                    {
                        if (texts[i] == null) continue;
                        if (texts[i].name == "Label") _buttonText = texts[i];
                        else if (texts[i].name == "Hint") _hintText = texts[i];
                        else if (texts[i].name == "Pose") _poseText = texts[i];
                    }
                }
                var images = existing.GetComponentsInChildren<Image>(true);
                if (images != null && images.Length > 1)
                    _buttonImage = images[1];
                RefreshHud();
                return;
            }

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            Font font = RuntimeFont.Resolve(18);

            _hud = new GameObject(HudName);
            var canvas = _hud.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5002;
            canvas.overrideSorting = true;
            canvas.pixelPerfect = true;
            var scaler = _hud.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _hud.AddComponent<GraphicRaycaster>();

            var panel = CreateUi(_hud.transform, "Panel");
            var panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(1f, 0f);
            panelRt.anchorMax = new Vector2(1f, 0f);
            panelRt.pivot = new Vector2(1f, 0f);
            panelRt.anchoredPosition = new Vector2(-16f, 148f);
            panelRt.sizeDelta = new Vector2(300f, 196f);
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0.12f, 0.05f, 0.08f, 0.94f);

            var btnGo = CreateUi(panel.transform, "Toggle");
            var btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0f, 1f);
            btnRt.anchorMax = new Vector2(0f, 1f);
            btnRt.pivot = new Vector2(0f, 1f);
            btnRt.anchoredPosition = new Vector2(10f, -10f);
            btnRt.sizeDelta = new Vector2(170f, 36f);
            _buttonImage = btnGo.AddComponent<Image>();
            var btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = _buttonImage;
            btn.onClick.AddListener(ToggleAdjusting);

            var btnLabel = CreateUi(btnGo.transform, "Label");
            Stretch(btnLabel);
            _buttonText = btnLabel.AddComponent<Text>();
            _buttonText.alignment = TextAnchor.MiddleCenter;
            _buttonText.fontSize = 16;
            _buttonText.fontStyle = FontStyle.Bold;
            _buttonText.color = Color.white;
            _buttonText.raycastTarget = false;
            _buttonText.font = font;

            var hideGo = CreateUi(panel.transform, "Hide");
            var hideRt = hideGo.GetComponent<RectTransform>();
            hideRt.anchorMin = new Vector2(0f, 1f);
            hideRt.anchorMax = new Vector2(0f, 1f);
            hideRt.pivot = new Vector2(0f, 1f);
            hideRt.anchoredPosition = new Vector2(188f, -10f);
            hideRt.sizeDelta = new Vector2(102f, 36f);
            var hideImg = hideGo.AddComponent<Image>();
            hideImg.color = new Color(0.45f, 0.18f, 0.16f, 1f);
            var hideBtn = hideGo.AddComponent<Button>();
            hideBtn.targetGraphic = hideImg;
            hideBtn.onClick.AddListener(HideSelected);
            var hideLabel = CreateUi(hideGo.transform, "HideLabel");
            Stretch(hideLabel);
            var hideText = hideLabel.AddComponent<Text>();
            hideText.text = "隐藏当前";
            hideText.alignment = TextAnchor.MiddleCenter;
            hideText.fontSize = 14;
            hideText.color = Color.white;
            hideText.raycastTarget = false;
            hideText.font = font;

            var liftUpGo = CreateUi(panel.transform, "LiftUp");
            var liftUpRt = liftUpGo.GetComponent<RectTransform>();
            liftUpRt.anchorMin = new Vector2(0f, 1f);
            liftUpRt.anchorMax = new Vector2(0f, 1f);
            liftUpRt.pivot = new Vector2(0f, 1f);
            liftUpRt.anchoredPosition = new Vector2(10f, -52f);
            liftUpRt.sizeDelta = new Vector2(170f, 32f);
            var liftUpImg = liftUpGo.AddComponent<Image>();
            liftUpImg.color = new Color(0.22f, 0.48f, 0.32f, 1f);
            var liftUpBtn = liftUpGo.AddComponent<Button>();
            liftUpBtn.targetGraphic = liftUpImg;
            liftUpBtn.onClick.AddListener(LiftHeadGroupUp);
            var liftUpLabel = CreateUi(liftUpGo.transform, "LiftUpLabel");
            Stretch(liftUpLabel);
            var liftUpText = liftUpLabel.AddComponent<Text>();
            liftUpText.text = "这一圈上移";
            liftUpText.alignment = TextAnchor.MiddleCenter;
            liftUpText.fontSize = 14;
            liftUpText.fontStyle = FontStyle.Bold;
            liftUpText.color = Color.white;
            liftUpText.raycastTarget = false;
            liftUpText.font = font;

            var liftDownGo = CreateUi(panel.transform, "LiftDown");
            var liftDownRt = liftDownGo.GetComponent<RectTransform>();
            liftDownRt.anchorMin = new Vector2(0f, 1f);
            liftDownRt.anchorMax = new Vector2(0f, 1f);
            liftDownRt.pivot = new Vector2(0f, 1f);
            liftDownRt.anchoredPosition = new Vector2(188f, -52f);
            liftDownRt.sizeDelta = new Vector2(102f, 32f);
            var liftDownImg = liftDownGo.AddComponent<Image>();
            liftDownImg.color = new Color(0.32f, 0.28f, 0.18f, 1f);
            var liftDownBtn = liftDownGo.AddComponent<Button>();
            liftDownBtn.targetGraphic = liftDownImg;
            liftDownBtn.onClick.AddListener(LiftHeadGroupDown);
            var liftDownLabel = CreateUi(liftDownGo.transform, "LiftDownLabel");
            Stretch(liftDownLabel);
            var liftDownText = liftDownLabel.AddComponent<Text>();
            liftDownText.text = "这一圈下移";
            liftDownText.alignment = TextAnchor.MiddleCenter;
            liftDownText.fontSize = 14;
            liftDownText.color = Color.white;
            liftDownText.raycastTarget = false;
            liftDownText.font = font;

            var hintGo = CreateUi(panel.transform, "Hint");
            var hintRt = hintGo.GetComponent<RectTransform>();
            hintRt.anchorMin = new Vector2(0f, 0f);
            hintRt.anchorMax = new Vector2(1f, 0f);
            hintRt.pivot = new Vector2(0.5f, 0f);
            hintRt.anchoredPosition = new Vector2(0f, 36f);
            hintRt.sizeDelta = new Vector2(-20f, 44f);
            _hintText = hintGo.AddComponent<Text>();
            _hintText.alignment = TextAnchor.MiddleCenter;
            _hintText.fontSize = 12;
            _hintText.color = new Color(0.9f, 0.82f, 0.84f, 1f);
            _hintText.raycastTarget = false;
            _hintText.font = font;

            var poseGo = CreateUi(panel.transform, "Pose");
            var poseRt = poseGo.GetComponent<RectTransform>();
            poseRt.anchorMin = new Vector2(0f, 0f);
            poseRt.anchorMax = new Vector2(1f, 0f);
            poseRt.pivot = new Vector2(0.5f, 0f);
            poseRt.anchoredPosition = new Vector2(0f, 8f);
            poseRt.sizeDelta = new Vector2(-20f, 28f);
            _poseText = poseGo.AddComponent<Text>();
            _poseText.alignment = TextAnchor.MiddleCenter;
            _poseText.fontSize = 11;
            _poseText.color = new Color(0.85f, 0.75f, 0.7f, 1f);
            _poseText.raycastTarget = false;
            _poseText.font = font;

            RefreshHud();
        }

        static Transform FindChildNamed(Transform root, string name)
        {
            if (root == null)
                return null;
            var all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == name)
                    return all[i];
            }
            return null;
        }

        void RefreshHud()
        {
            if (_buttonText != null)
                _buttonText.text = _adjusting ? "完成摆遮挡" : "摆遮挡";
            if (_buttonImage != null)
                _buttonImage.color = _adjusting
                    ? new Color(0.75f, 0.22f, 0.18f, 1f)
                    : new Color(0.62f, 0.28f, 0.22f, 1f);
            if (_hintText != null)
            {
                _hintText.text = _adjusting
                    ? "点镜地球可拖  圆架/头灯=这一圈  球已停转  E抬高"
                    : "点按钮或 F4；点镜地球改位置，圆架 5m/15m 也能点";
            }
            if (_poseText != null)
            {
                if (_selected == null)
                {
                    _poseText.text = "未选中";
                }
                else
                {
                    string vis = _selected.gameObject.activeSelf ? "显示" : "已隐藏";
                    Vector3 p = _selected.localPosition;
                    if (IsCircleTrussName(_selected.name) || IsHangingHeadName(_selected.name))
                    {
                        var ring = RingRootOf(_selected);
                        string ringName = ring != null ? ring.name : _selected.name;
                        _poseText.text = string.Format("{0}  头灯{1}盏一起  y={2:0.000}",
                            NiceName(ringName), CountHeadGroup(), _selected.position.y);
                    }
                    else
                    {
                        _poseText.text = string.Format("{0}  {1}  ({2:0.000},{3:0.000},{4:0.000})",
                            NiceName(_selected.name), vis, p.x, p.y, p.z);
                    }
                }
            }
        }

        static string NiceName(string name)
        {
            if (name == "Ball")
                return "镜地球";
            if (name.StartsWith("MirrorBall"))
                return "镜地球 " + name;
            if (name.StartsWith("DiscoLight"))
                return "吊装头灯/摄像头 " + name;
            if (name.StartsWith("LaserProjector"))
                return "激光头 " + name;
            if (name.IndexOf("Circle15m") >= 0)
                return "圆架 Circle 15m";
            if (IsCircleTrussName(name))
                return "圆架 Circle 5m";
            return name;
        }

        void DestroyHud()
        {
            HudPanelHub.Unregister("prop");
            if (_hud != null)
            {
                if (Application.isPlaying)
                    Destroy(_hud);
                else
                    DestroyImmediate(_hud);
                _hud = null;
            }
            var existing = GameObject.Find(HudName);
            if (existing != null)
            {
                if (Application.isPlaying)
                    Destroy(existing);
                else
                    DestroyImmediate(existing);
            }
            _buttonText = null;
            _buttonImage = null;
            _hintText = null;
            _poseText = null;
        }

        static void Stretch(GameObject go)
        {
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static GameObject CreateUi(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }
    }
}
