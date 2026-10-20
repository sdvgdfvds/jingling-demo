using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NexusAnima
{
    /// <summary>
    /// Play / Game 视图里移动、缩放阵型网格。Game 没有 Scene gizmo，
    /// 左键拖 / 滚轮缩放必须走这套 HUD。
    /// </summary>
    public class GridPlayAdjust : MonoBehaviour
    {
        public static bool IsDriving { get; private set; }

        const string HudName = "摆网格";
        const float MinAbsScale = 0.004f;
        const float MaxAbsScale = 4f;

        BoardGrid _board;
        bool _adjusting;
        bool _dragging;
        Vector3 _dragOffset;
        GameObject _hud;
        Text _buttonText;
        Image _buttonImage;
        Text _hintText;
        Text _poseText;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            IsDriving = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BootHook()
        {
            var board = Object.FindFirstObjectByType<BoardGrid>();
            Ensure(board);
        }

        public static void Ensure(BoardGrid board)
        {
            if (board == null)
                return;
            var adj = board.GetComponent<GridPlayAdjust>();
            if (adj == null)
                adj = board.gameObject.AddComponent<GridPlayAdjust>();
            adj._board = board;
            board.UnlockDynamicGridStatic();
            if (Application.isPlaying)
                adj.EnsureHud();
        }

        public static void StopIfDriving()
        {
            if (!IsDriving)
                return;
            var boards = Object.FindObjectsByType<GridPlayAdjust>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < boards.Length; i++)
            {
                if (boards[i] != null)
                    boards[i].StopAdjusting();
            }
        }

        void Awake()
        {
            if (_board == null)
                _board = GetComponent<BoardGrid>();
        }

        void OnDisable()
        {
            if (_adjusting)
                StopAdjusting();
        }

        void OnDestroy()
        {
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

            var dg = Grid;
            if (dg == null)
                return;

            _board.UnlockDynamicGridStatic();
            _board.SetVisualVisible(true);

            bool overUi = EventSystem.current != null
                && EventSystem.current.IsPointerOverGameObject();

            HandleNudge(dg);
            HandleHeight(dg);
            HandleScale(overUi);
            HandleDrag(dg, overUi);
        }

        void LateUpdate()
        {
            if (_adjusting && _board != null)
                _board.SetVisualVisible(true);
        }

        static bool WasTogglePressed()
        {
            return Input.GetKeyDown(KeyCode.F3)
                || Input.GetKeyDown(KeyCode.F7);
        }

        public void ToggleAdjusting()
        {
            if (_adjusting) StopAdjusting();
            else StartAdjusting();
        }

        void StartAdjusting()
        {
            BattleFlyCamera.StopIfDriving();
            PropPlayAdjust.StopIfDriving();
            if (_board == null)
                _board = GetComponent<BoardGrid>();
            if (_board == null)
                return;

            _board.UnlockDynamicGridStatic();
            _board.SetVisualVisible(true);
            _adjusting = true;
            IsDriving = true;
            _dragging = false;
            HudPanelHub.SetVisible("grid", true, false);
            RefreshHud();
        }

        void StopAdjusting()
        {
            _adjusting = false;
            _dragging = false;
            IsDriving = false;
            if (_board != null)
            {
                _board.UnlockDynamicGridStatic();
                CommitLogic();
                _board.RememberAuthoredDynamicGridPose();
            }
            RefreshHud();
        }

        Transform Grid
        {
            get
            {
                if (_board == null)
                    return null;
                return _board.transform.Find("DynamicGrid");
            }
        }

        void HandleDrag(Transform dg, bool overUi)
        {
            var cam = Camera.main;
            if (cam == null)
                return;

            if (!_dragging && overUi)
                return;

            var ray = cam.ScreenPointToRay(Input.mousePosition);
            float y = dg.position.y;
            var plane = new Plane(Vector3.up, new Vector3(0f, y, 0f));
            float dist;
            if (!plane.Raycast(ray, out dist))
                return;
            Vector3 hit = ray.GetPoint(dist);

            if (Input.GetMouseButtonDown(0) && !overUi)
            {
                _dragging = true;
                _dragOffset = dg.position - hit;
            }

            if (_dragging && Input.GetMouseButton(0))
            {
                Vector3 next = hit + _dragOffset;
                next.y = y;
                dg.position = next;
                CommitLogic();
            }

            if (Input.GetMouseButtonUp(0))
                _dragging = false;
        }

        void HandleNudge(Transform dg)
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
            dg.position += delta;
            CommitLogic();
        }

        void HandleHeight(Transform dg)
        {
            float step = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                ? 0.008f
                : 0.0015f;
            float dy = 0f;
            if (Input.GetKey(KeyCode.E)) dy += step;
            if (Input.GetKey(KeyCode.Q)) dy -= step;
            if (Mathf.Abs(dy) < 1e-8f)
                return;
            Vector3 p = dg.position;
            p.y += dy;
            dg.position = p;
            CommitLogic();
        }

        void HandleScale(bool overUi)
        {
            if (overUi)
                return;
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) < 0.01f)
                return;
            float factor = scroll > 0f ? 1.06f : 1f / 1.06f;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                factor = scroll > 0f ? 1.12f : 1f / 1.12f;
            ScaleBy(factor);
        }

        void ScaleBy(float factor)
        {
            var dg = Grid;
            if (dg == null)
                return;
            Vector3 s = dg.localScale;
            float sx = Mathf.Max(1e-5f, Mathf.Abs(s.x));
            float sy = Mathf.Max(1e-5f, Mathf.Abs(s.y));
            float sz = Mathf.Max(1e-5f, Mathf.Abs(s.z));
            float signX = s.x < 0f ? -1f : 1f;
            float signY = s.y < 0f ? -1f : 1f;
            float signZ = s.z < 0f ? -1f : 1f;
            sx = Mathf.Clamp(sx * factor, MinAbsScale, MaxAbsScale);
            sz = Mathf.Clamp(sz * factor, MinAbsScale, MaxAbsScale);
            dg.localScale = new Vector3(signX * sx, signY * sy, signZ * sz);
            CommitLogic();
        }

        void CommitLogic()
        {
            if (_board == null)
                return;
            _board.UnlockDynamicGridStatic();
            _board.SyncFromDynamicGrid(false);
            _board.SetVisualVisible(true);
            _board.SnapOccupantsToGrid();
            RefreshHud();
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
            var panel = FindPanelOn(_hud.transform);
            if (panel != null)
                HudPanelHub.Register("grid", "摆网格", panel, false);
        }

        static RectTransform FindPanelOn(Transform root)
        {
            if (root == null)
                return null;
            var direct = root.Find("Panel") as RectTransform;
            if (direct != null)
                return direct;
            var win = root.GetComponentInChildren<HudWindow>(true);
            if (win != null)
                return win.transform.Find("Panel") as RectTransform;
            return null;
        }

        void UnusedHudBuilder()
        {
            if (_hud != null && FindPanelOn(_hud.transform) != null)
            {
                RefreshHud();
                return;
            }
            if (_hud != null)
            {
                Object.DestroyImmediate(_hud);
                _hud = null;
            }

            var existing = GameObject.Find(HudName);
            if (existing != null && FindPanelOn(existing.transform) == null)
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
            canvas.sortingOrder = 5001;
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
            panelRt.anchorMin = new Vector2(0f, 1f);
            panelRt.anchorMax = new Vector2(0f, 1f);
            panelRt.pivot = new Vector2(0f, 1f);
            panelRt.anchoredPosition = new Vector2(16f, -16f);
            panelRt.sizeDelta = new Vector2(300f, 118f);
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0.05f, 0.06f, 0.14f, 0.94f);

            var btnGo = CreateUi(panel.transform, "Toggle");
            var btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0f, 1f);
            btnRt.anchorMax = new Vector2(0f, 1f);
            btnRt.pivot = new Vector2(0f, 1f);
            btnRt.anchoredPosition = new Vector2(10f, -10f);
            btnRt.sizeDelta = new Vector2(280f, 40f);
            _buttonImage = btnGo.AddComponent<Image>();
            var btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = _buttonImage;
            btn.onClick.AddListener(ToggleAdjusting);

            var btnLabel = CreateUi(btnGo.transform, "Label");
            var btnLabelRt = btnLabel.GetComponent<RectTransform>();
            btnLabelRt.anchorMin = Vector2.zero;
            btnLabelRt.anchorMax = Vector2.one;
            btnLabelRt.offsetMin = Vector2.zero;
            btnLabelRt.offsetMax = Vector2.zero;
            _buttonText = btnLabel.AddComponent<Text>();
            _buttonText.alignment = TextAnchor.MiddleCenter;
            _buttonText.fontSize = 18;
            _buttonText.fontStyle = FontStyle.Bold;
            _buttonText.color = Color.white;
            _buttonText.raycastTarget = false;
            _buttonText.font = font;

            var hintGo = CreateUi(panel.transform, "Hint");
            var hintRt = hintGo.GetComponent<RectTransform>();
            hintRt.anchorMin = new Vector2(0f, 0f);
            hintRt.anchorMax = new Vector2(1f, 0f);
            hintRt.pivot = new Vector2(0.5f, 0f);
            hintRt.anchoredPosition = new Vector2(0f, 28f);
            hintRt.sizeDelta = new Vector2(-20f, 28f);
            _hintText = hintGo.AddComponent<Text>();
            _hintText.alignment = TextAnchor.MiddleCenter;
            _hintText.fontSize = 12;
            _hintText.color = new Color(0.82f, 0.85f, 0.9f, 1f);
            _hintText.raycastTarget = false;
            _hintText.font = font;

            var poseGo = CreateUi(panel.transform, "Pose");
            var poseRt = poseGo.GetComponent<RectTransform>();
            poseRt.anchorMin = new Vector2(0f, 0f);
            poseRt.anchorMax = new Vector2(1f, 0f);
            poseRt.pivot = new Vector2(0.5f, 0f);
            poseRt.anchoredPosition = new Vector2(0f, 6f);
            poseRt.sizeDelta = new Vector2(-20f, 22f);
            _poseText = poseGo.AddComponent<Text>();
            _poseText.alignment = TextAnchor.MiddleCenter;
            _poseText.fontSize = 11;
            _poseText.color = new Color(0.7f, 0.78f, 0.88f, 1f);
            _poseText.raycastTarget = false;
            _poseText.font = font;

            RefreshHud();
        }

        void RefreshHud()
        {
            if (_buttonText != null)
                _buttonText.text = _adjusting ? "完成摆网格" : "摆网格";
            if (_buttonImage != null)
                _buttonImage.color = _adjusting
                    ? new Color(0.75f, 0.22f, 0.18f, 1f)
                    : new Color(0.2f, 0.48f, 0.78f, 1f);
            if (_hintText != null)
            {
                _hintText.text = _adjusting
                    ? "左键拖移  滚轮缩放  方向键微调  QE升降"
                    : "点按钮或 F3，在 Game 里改网格位置/大小";
            }
            if (_poseText != null)
            {
                var dg = Grid;
                if (dg != null)
                {
                    Vector3 lp = dg.localPosition;
                    Vector3 ls = dg.localScale;
                    _poseText.text = string.Format(
                        "pos {0:0.000},{1:0.000},{2:0.000}  scale {3:0.000},{4:0.000},{5:0.000}",
                        lp.x, lp.y, lp.z, ls.x, ls.y, ls.z);
                }
                else
                {
                    _poseText.text = "未找到 DynamicGrid";
                }
            }
        }

        void DestroyHud()
        {
            HudPanelHub.Unregister("grid");
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

        static GameObject CreateUi(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }
    }
}
