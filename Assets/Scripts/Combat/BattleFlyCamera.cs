using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NexusAnima
{
    /// <summary>
    /// Play-mode first-person fly through the Game camera (not Scene View).
    /// Enter with the on-screen button — F2 is often eaten by the Unity editor.
    /// </summary>
    public class BattleFlyCamera : MonoBehaviour
    {
        public static bool IsDriving { get; private set; }

        const string HudName = "摆相机飞行";

        [SerializeField] float moveSpeed = 0.35f;
        [SerializeField] float lookSensitivity = 1.8f;
        [SerializeField] float minSpeed = 0.02f;
        [SerializeField] float maxSpeed = 8f;

        Camera _cam;
        float _yaw;
        float _pitch;
        bool _flying;
        GameObject _hud;
        Text _buttonText;
        Image _buttonImage;
        Text _hintText;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BootHook()
        {
            EnsureOnMainCamera();
        }

        public static void EnsureOnMainCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = GameObject.Find("Main Camera");
                if (go != null)
                    cam = go.GetComponent<Camera>();
            }
            if (cam == null)
                return;

            var fly = cam.GetComponent<BattleFlyCamera>();
            if (fly == null)
                fly = cam.gameObject.AddComponent<BattleFlyCamera>();
            if (Application.isPlaying)
                fly.EnsureHud();
            else
                fly.HideHud();
        }

        void Awake()
        {
            _cam = GetComponent<Camera>();
            if (Application.isPlaying)
                EnsureHud();
            else
                HideHud();
        }

        void OnDisable()
        {
            if (_flying)
                StopFlying();
        }

        void OnDestroy()
        {
            if (_hud != null)
            {
                if (Application.isPlaying)
                    Destroy(_hud);
                else
                    DestroyImmediate(_hud);
                _hud = null;
            }
            if (IsDriving && !_flying)
                IsDriving = false;
        }

        void Update()
        {
            if (!Application.isPlaying)
                return;

            if (WasTogglePressed())
                ToggleFlying();

            if (_flying && Input.GetKeyDown(KeyCode.Escape))
                StopFlying();
        }

        static bool WasTogglePressed()
        {
            return Input.GetKeyDown(KeyCode.BackQuote)
                || Input.GetKeyDown(KeyCode.F2)
                || Input.GetKeyDown(KeyCode.F8);
        }

        void LateUpdate()
        {
            if (!_flying)
                return;

            if (_cam == null)
                _cam = GetComponent<Camera>();
            if (_cam == null)
                return;

            _cam.useOcclusionCulling = false;
            if (_cam.nearClipPlane > 0.05f)
                _cam.nearClipPlane = 0.05f;

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                float mul = scroll > 0f ? 1.25f : 0.8f;
                moveSpeed = Mathf.Clamp(moveSpeed * mul, minSpeed, maxSpeed);
                RefreshHud();
            }

            // Editor Game view often cannot lock the cursor. Hold RMB to look, like Scene.
            if (Input.GetMouseButton(1))
            {
                _yaw += Input.GetAxisRaw("Mouse X") * lookSensitivity;
                _pitch -= Input.GetAxisRaw("Mouse Y") * lookSensitivity;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            Vector3 wish = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) wish += transform.forward;
            if (Input.GetKey(KeyCode.S)) wish -= transform.forward;
            if (Input.GetKey(KeyCode.D)) wish += transform.right;
            if (Input.GetKey(KeyCode.A)) wish -= transform.right;
            if (Input.GetKey(KeyCode.E)) wish += Vector3.up;
            if (Input.GetKey(KeyCode.Q)) wish -= Vector3.up;

            if (wish.sqrMagnitude > 0.0001f)
            {
                float boost = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 3f : 1f;
                transform.position += wish.normalized * (moveSpeed * boost * Time.unscaledDeltaTime);
            }

            RememberAsHome();
        }

        public static void StopIfDriving()
        {
            if (!IsDriving)
                return;
            var cam = Camera.main;
            if (cam == null)
                return;
            var fly = cam.GetComponent<BattleFlyCamera>();
            if (fly != null)
                fly.StopFlying();
        }

        public void ToggleFlying()
        {
            if (_flying) StopFlying();
            else StartFlying();
        }

        void StartFlying()
        {
            GridPlayAdjust.StopIfDriving();
            PropPlayAdjust.StopIfDriving();
            SyncAnglesFromTransform();
            var focus = FindFirstObjectByType<BattleCameraFocus>();
            if (focus != null)
                focus.AbortFocus();

            // Do not lock the cursor — Unity's Game tab usually ignores lock, which
            // looks like "fly never started". Hold right mouse to look instead.
            _flying = true;
            IsDriving = true;
            RefreshHud();
        }

        void StopFlying()
        {
            _flying = false;
            IsDriving = false;
            RememberAsHome();
            RefreshHud();
        }

        void SyncAnglesFromTransform()
        {
            Vector3 e = transform.eulerAngles;
            _yaw = e.y;
            _pitch = e.x;
            if (_pitch > 180f)
                _pitch -= 360f;
        }

        void RememberAsHome()
        {
            var focus = FindFirstObjectByType<BattleCameraFocus>();
            if (focus != null)
                focus.CaptureHome();
        }

        void HideHud()
        {
            HudPanelHub.Unregister("fly");
            if (_hud != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(_hud);
                else
                    Object.DestroyImmediate(_hud);
                _hud = null;
            }
            var existing = GameObject.Find(HudName);
            if (existing != null)
            {
                if (Application.isPlaying)
                    Object.Destroy(existing);
                else
                    Object.DestroyImmediate(existing);
            }
            _buttonText = null;
            _buttonImage = null;
            _hintText = null;
        }

        void EnsureHud()
        {
            if (_hud != null)
            {
                if (FindHudPanel() != null)
                {
                    RefreshHud();
                    RegisterHud();
                    return;
                }
                HideHud();
            }

            var existing = GameObject.Find(HudName);
            if (existing != null)
            {
                var foundPanel = FindHudPanelOn(existing.transform);
                if (foundPanel == null)
                {
                    if (Application.isPlaying)
                        Object.Destroy(existing);
                    else
                        Object.DestroyImmediate(existing);
                    existing = null;
                }
            }
            if (existing != null)
            {
                _hud = existing;
                BindHudRefs(existing);
                RefreshHud();
                RegisterHud();
                return;
            }

            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }

            _hud = new GameObject(HudName);
            var canvas = _hud.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5003;
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
            panelRt.anchorMin = new Vector2(1f, 1f);
            panelRt.anchorMax = new Vector2(1f, 1f);
            panelRt.pivot = new Vector2(1f, 1f);
            panelRt.anchoredPosition = new Vector2(-214f, -16f);
            panelRt.sizeDelta = new Vector2(280f, 96f);
            var panelImg = panel.AddComponent<Image>();
            panelImg.color = new Color(0.04f, 0.07f, 0.12f, 0.94f);

            var btnGo = CreateUi(panel.transform, "Toggle");
            var btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin = new Vector2(0f, 1f);
            btnRt.anchorMax = new Vector2(0f, 1f);
            btnRt.pivot = new Vector2(0f, 1f);
            btnRt.anchoredPosition = new Vector2(10f, -10f);
            btnRt.sizeDelta = new Vector2(260f, 40f);
            _buttonImage = btnGo.AddComponent<Image>();
            var btn = btnGo.AddComponent<Button>();
            btn.targetGraphic = _buttonImage;
            btn.onClick.AddListener(ToggleFlying);

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
            _buttonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                ?? Resources.GetBuiltinResource<Font>("Arial.ttf");

            var hintGo = CreateUi(panel.transform, "Hint");
            var hintRt = hintGo.GetComponent<RectTransform>();
            hintRt.anchorMin = new Vector2(0f, 0f);
            hintRt.anchorMax = new Vector2(1f, 0f);
            hintRt.pivot = new Vector2(0.5f, 0f);
            hintRt.anchoredPosition = new Vector2(0f, 8f);
            hintRt.sizeDelta = new Vector2(-20f, 32f);
            _hintText = hintGo.AddComponent<Text>();
            _hintText.alignment = TextAnchor.UpperCenter;
            _hintText.fontSize = 12;
            _hintText.color = new Color(0.82f, 0.85f, 0.9f, 1f);
            _hintText.raycastTarget = false;
            _hintText.font = _buttonText.font;

            RefreshHud();
            RegisterHud();
        }

        void RegisterHud()
        {
            var panel = FindHudPanel();
            if (panel != null)
                HudPanelHub.Register("fly", "相机飞行", panel, true);
        }

        RectTransform FindHudPanel()
        {
            return _hud != null ? FindHudPanelOn(_hud.transform) : null;
        }

        static RectTransform FindHudPanelOn(Transform root)
        {
            if (root == null)
                return null;
            var direct = root.Find("Panel") as RectTransform;
            if (direct != null)
                return direct;
            var win = root.GetComponentInChildren<HudWindow>(true);
            if (win != null)
            {
                var nested = win.transform.Find("Panel") as RectTransform;
                if (nested != null)
                    return nested;
            }
            return null;
        }

        void BindHudRefs(GameObject root)
        {
            var texts = root.GetComponentsInChildren<Text>(true);
            if (texts != null)
            {
                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i] == null) continue;
                    if (texts[i].name == "Label") _buttonText = texts[i];
                    else if (texts[i].name == "Hint") _hintText = texts[i];
                }
            }
            if (_buttonText == null && texts != null && texts.Length > 0)
                _buttonText = texts[0];
            var images = root.GetComponentsInChildren<Image>(true);
            if (images != null)
            {
                for (int i = 0; i < images.Length; i++)
                {
                    if (images[i] != null && images[i].gameObject.name == "Toggle")
                    {
                        _buttonImage = images[i];
                        break;
                    }
                }
            }
        }

        void RefreshHud()
        {
            if (_buttonText != null)
                _buttonText.text = _flying ? "退出飞行" : "进入飞行";
            if (_buttonImage != null)
                _buttonImage.color = _flying
                    ? new Color(0.75f, 0.22f, 0.18f, 1f)
                    : new Color(0.18f, 0.55f, 0.38f, 1f);
            if (_hintText != null)
            {
                _hintText.text = _flying
                    ? "按住右键转头  WASD 平移  QE 升降  滚轮速度 " + moveSpeed.ToString("0.00")
                    : "点按钮或 F8 / ~ 进入；右键转头 + WASD";
            }
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
