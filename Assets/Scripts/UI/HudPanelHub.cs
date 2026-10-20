using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NexusAnima
{
    /// <summary>
    /// Game-view panel dock: left-edge arrow opens a checklist. Each registered
    /// window can be shown, hidden, dragged, scaled, and closed. The dock's
    /// 保存 button snapshots HUD layout now and Game-world tweaks for write-back
    /// after exiting Play.
    /// </summary>
    public class HudPanelHub : MonoBehaviour
    {
        public const string PrefIntensityDisco03 = "NexusAnima.Hud.disco03.Intensity";
        public const string PrefSwingDisco03 = "NexusAnima.Hud.disco03.Swing";

        /// <summary>Editor PlayLayoutSync subscribes to snapshot camera/board/stage.</summary>
        public static event Action GameChangesSaveRequested;

        const string HubName = "面板坞";
        const string PrefPrefix = "NexusAnima.Hud.";
        const string PrefDock = "NexusAnima.Hud.DockOpen";
        const string PrefLayoutV = "NexusAnima.Hud.LayoutV";
        const int LayoutVersion = 6;
        const float TitleH = 28f;
        static readonly string[] KnownIds =
        {
            "pool", "actions", "bond", "fly", "grid", "prop", "disco03"
        };

        class Entry
        {
            public string Id;
            public string Title;
            public RectTransform Chrome;
            public bool DefaultOn;
            public Image ToggleImage;
            public Text ToggleLabel;
        }

        static HudPanelHub _instance;

        readonly List<Entry> _entries = new List<Entry>();
        RectTransform _hostLayer;
        RectTransform _arrowRt;
        RectTransform _dockRt;
        Text _arrowText;
        Image _arrowImage;
        Text _saveLabel;
        Coroutine _saveFlash;
        bool _dockOpen;
        bool _shellReady;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BootHook()
        {
            if (Application.isPlaying)
                Ensure();
        }

        public static HudPanelHub Ensure()
        {
            if (!Application.isPlaying)
                return _instance;

            if (_instance != null)
            {
                if (!_instance._shellReady)
                    _instance.BuildShell();
                return _instance;
            }

            var existing = GameObject.Find(HubName);
            HudPanelHub hub;
            if (existing != null)
            {
                hub = existing.GetComponent<HudPanelHub>();
                if (hub == null)
                    hub = existing.AddComponent<HudPanelHub>();
            }
            else
            {
                var go = new GameObject(HubName);
                hub = go.AddComponent<HudPanelHub>();
            }
            _instance = hub;
            if (!hub._shellReady)
                hub.BuildShell();
            return hub;
        }

        public RectTransform HostLayer
        {
            get
            {
                Ensure();
                return _hostLayer;
            }
        }

        public static RectTransform Register(string id, string title, RectTransform content, bool defaultOn)
        {
            if (content == null || string.IsNullOrEmpty(id))
                return null;
            var hub = Ensure();
            if (hub == null)
                return null;
            return hub.RegisterInternal(id, title, content, defaultOn);
        }

        public static void Unregister(string id)
        {
            if (_instance == null)
                return;
            _instance.UnregisterInternal(id);
        }

        public static void SetVisible(string id, bool on)
        {
            SetVisible(id, on, true);
        }

        public static void SetVisible(string id, bool on, bool save)
        {
            var hub = Ensure();
            if (hub != null)
                hub.SetVisibleInternal(id, on, save);
        }

        RectTransform RegisterInternal(string id, string title, RectTransform content, bool defaultOn)
        {
            var already = content.GetComponentInParent<HudWindow>(true);
            if (already != null && already.Id == id)
            {
                var existingWrapped = FindEntry(id);
                if (existingWrapped == null)
                {
                    existingWrapped = new Entry
                    {
                        Id = id,
                        Title = title,
                        Chrome = already.GetComponent<RectTransform>(),
                        DefaultOn = defaultOn
                    };
                    _entries.Add(existingWrapped);
                }
                existingWrapped.Title = title;
                existingWrapped.DefaultOn = defaultOn;
                existingWrapped.Chrome = already.GetComponent<RectTransform>();
                RebindChrome(existingWrapped.Chrome, id);
                ApplySavedLayout(existingWrapped);
                RebuildDock();
                ApplySavedVisible(existingWrapped);
                StartApplySavedNextFrame(existingWrapped);
                return existingWrapped.Chrome;
            }

            DestroyOrphanChrome(id, content);

            var existingEntry = FindEntry(id);
            if (existingEntry != null)
                UnregisterInternal(id);

            var chrome = Wrap(content, id, title);
            if (chrome == null)
                return null;

            var entry = new Entry
            {
                Id = id,
                Title = title,
                Chrome = chrome,
                DefaultOn = defaultOn
            };
            _entries.Add(entry);
            ApplySavedLayout(entry);
            ApplySavedVisible(entry);
            RebuildDock();
            StartApplySavedNextFrame(entry);
            return chrome;
        }

        void DestroyOrphanChrome(string id, RectTransform keepContent)
        {
            var wins = Object.FindObjectsByType<HudWindow>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < wins.Length; i++)
            {
                var win = wins[i];
                if (win == null || win.Id != id)
                    continue;
                if (keepContent != null && keepContent.IsChildOf(win.transform))
                    continue;
                Object.DestroyImmediate(win.gameObject);
            }
        }

        void UnregisterInternal(string id)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Id != id)
                    continue;
                _entries.RemoveAt(i);
                break;
            }
            RebuildDock();
        }

        void SetVisibleInternal(string id, bool on, bool save)
        {
            var entry = FindEntry(id);
            if (entry == null || entry.Chrome == null)
                return;
            entry.Chrome.gameObject.SetActive(on);
            if (on)
                entry.Chrome.SetAsLastSibling();
            if (save)
            {
                PlayerPrefs.SetInt(PrefOn(id), on ? 1 : 0);
                PlayerPrefs.Save();
            }
            RefreshDockRow(entry);
        }

        Entry FindEntry(string id)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i].Id == id)
                    return _entries[i];
            }
            return null;
        }

        void ApplySavedVisible(Entry entry)
        {
            int def = entry.DefaultOn ? 1 : 0;
            bool on = PlayerPrefs.GetInt(PrefOn(entry.Id), def) == 1;
            SetVisibleInternal(entry.Id, on, false);
        }

        public static void ReapplySaved(string id)
        {
            if (_instance == null || string.IsNullOrEmpty(id))
                return;
            var entry = _instance.FindEntry(id);
            if (entry != null)
                _instance.ApplySavedLayout(entry);
        }

        void ApplySavedLayout(Entry entry)
        {
            if (entry == null || entry.Chrome == null)
                return;
            var rt = entry.Chrome;
            if (PlayerPrefs.HasKey(PrefAMinX(entry.Id)))
            {
                rt.anchorMin = new Vector2(
                    PlayerPrefs.GetFloat(PrefAMinX(entry.Id), rt.anchorMin.x),
                    PlayerPrefs.GetFloat(PrefAMinY(entry.Id), rt.anchorMin.y));
                rt.anchorMax = new Vector2(
                    PlayerPrefs.GetFloat(PrefAMaxX(entry.Id), rt.anchorMax.x),
                    PlayerPrefs.GetFloat(PrefAMaxY(entry.Id), rt.anchorMax.y));
                rt.pivot = new Vector2(
                    PlayerPrefs.GetFloat(PrefPivX(entry.Id), rt.pivot.x),
                    PlayerPrefs.GetFloat(PrefPivY(entry.Id), rt.pivot.y));
            }
            if (PlayerPrefs.HasKey(PrefX(entry.Id)))
            {
                rt.anchoredPosition = new Vector2(
                    PlayerPrefs.GetFloat(PrefX(entry.Id), rt.anchoredPosition.x),
                    PlayerPrefs.GetFloat(PrefY(entry.Id), rt.anchoredPosition.y));
            }
            if (PlayerPrefs.HasKey(PrefW(entry.Id)))
            {
                rt.sizeDelta = new Vector2(
                    PlayerPrefs.GetFloat(PrefW(entry.Id), rt.sizeDelta.x),
                    PlayerPrefs.GetFloat(PrefH(entry.Id), rt.sizeDelta.y));
            }
            ApplySavedScale(entry);
            // Do not ClampToParent here: on the first Play frames DemoUIRoot has a
            // degenerate world rect, and clamp piles every window at the margin.
        }

        void ApplySavedScale(Entry entry)
        {
            if (entry == null || entry.Chrome == null)
                return;
            if (!PlayerPrefs.HasKey(PrefS(entry.Id)))
                return;
            float s = PlayerPrefs.GetFloat(PrefS(entry.Id), 1f);
            SetWindowScale(entry.Chrome, entry.Id, s);
        }

        void StartApplySavedNextFrame(Entry entry)
        {
            if (entry == null || !isActiveAndEnabled)
                return;
            StartCoroutine(ApplySavedNextFrame(entry.Id));
        }

        IEnumerator ApplySavedNextFrame(string id)
        {
            yield return new WaitForEndOfFrame();
            ReapplySaved(id);
            yield return null;
            ReapplySaved(id);
            yield return new WaitForEndOfFrame();
            ReapplySaved(id);
        }

        static void RebindChrome(RectTransform chrome, string id)
        {
            if (chrome == null)
                return;
            var drag = chrome.GetComponent<HudWindowDrag>();
            if (drag == null)
                drag = chrome.gameObject.AddComponent<HudWindowDrag>();
            drag.Bind(chrome, id);
            EnsureResizeGrip(chrome, id);
        }

        public static void SaveAllGameChanges()
        {
            var hub = Ensure();
            if (hub != null)
                hub.SaveAllGameChangesInternal();
        }

        void SaveAllGameChangesInternal()
        {
            for (int i = 0; i < _entries.Count; i++)
                WriteWindowLayout(_entries[i]);

            var scene = SceneManager.GetActiveScene();
            var boards = FindObjectsByType<BoardGrid>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < boards.Length; i++)
            {
                if (boards[i] != null && boards[i].gameObject.scene == scene)
                    boards[i].RememberAuthoredDynamicGridPose();
            }

            PlayerPrefs.Save();
            GameChangesSaveRequested?.Invoke();
            FlashSaved();
            Debug.Log("[HudPanelHub] 已保存 Game 改动（面板位置/大小/显隐）。舞台、网格、相机在退出 Play 后写回当前场景。");
        }

        void WriteWindowLayout(Entry entry)
        {
            if (entry == null || entry.Chrome == null)
                return;
            var rt = entry.Chrome;
            string id = entry.Id;
            PlayerPrefs.SetFloat(PrefX(id), rt.anchoredPosition.x);
            PlayerPrefs.SetFloat(PrefY(id), rt.anchoredPosition.y);
            PlayerPrefs.SetFloat(PrefW(id), rt.sizeDelta.x);
            PlayerPrefs.SetFloat(PrefH(id), rt.sizeDelta.y);
            PlayerPrefs.SetFloat(PrefS(id), rt.localScale.x);
            PlayerPrefs.SetFloat(PrefAMinX(id), rt.anchorMin.x);
            PlayerPrefs.SetFloat(PrefAMinY(id), rt.anchorMin.y);
            PlayerPrefs.SetFloat(PrefAMaxX(id), rt.anchorMax.x);
            PlayerPrefs.SetFloat(PrefAMaxY(id), rt.anchorMax.y);
            PlayerPrefs.SetFloat(PrefPivX(id), rt.pivot.x);
            PlayerPrefs.SetFloat(PrefPivY(id), rt.pivot.y);
            PlayerPrefs.SetInt(PrefOn(id), rt.gameObject.activeSelf ? 1 : 0);
        }

        void FlashSaved()
        {
            if (_saveFlash != null)
                StopCoroutine(_saveFlash);
            _saveFlash = StartCoroutine(FlashSavedRoutine());
        }

        IEnumerator FlashSavedRoutine()
        {
            if (_saveLabel != null)
                _saveLabel.text = "已保存 ✓";
            yield return new WaitForSecondsRealtime(1.6f);
            if (_saveLabel != null)
                _saveLabel.text = "保存";
            _saveFlash = null;
        }

        public static void SavePose(string id, Vector2 pos)
        {
            PlayerPrefs.SetFloat(PrefX(id), pos.x);
            PlayerPrefs.SetFloat(PrefY(id), pos.y);
            PlayerPrefs.Save();
        }

        public static void SaveFullLayout(string id, RectTransform rt)
        {
            if (string.IsNullOrEmpty(id) || rt == null)
                return;
            PlayerPrefs.SetFloat(PrefX(id), rt.anchoredPosition.x);
            PlayerPrefs.SetFloat(PrefY(id), rt.anchoredPosition.y);
            PlayerPrefs.SetFloat(PrefW(id), rt.sizeDelta.x);
            PlayerPrefs.SetFloat(PrefH(id), rt.sizeDelta.y);
            PlayerPrefs.SetFloat(PrefS(id), rt.localScale.x);
            PlayerPrefs.SetFloat(PrefAMinX(id), rt.anchorMin.x);
            PlayerPrefs.SetFloat(PrefAMinY(id), rt.anchorMin.y);
            PlayerPrefs.SetFloat(PrefAMaxX(id), rt.anchorMax.x);
            PlayerPrefs.SetFloat(PrefAMaxY(id), rt.anchorMax.y);
            PlayerPrefs.SetFloat(PrefPivX(id), rt.pivot.x);
            PlayerPrefs.SetFloat(PrefPivY(id), rt.pivot.y);
            PlayerPrefs.Save();
        }

        public static void SaveSize(string id, Vector2 size)
        {
            PlayerPrefs.SetFloat(PrefW(id), size.x);
            PlayerPrefs.SetFloat(PrefH(id), size.y);
            PlayerPrefs.Save();
        }

        public static void SaveScale(string id, float scale)
        {
            PlayerPrefs.SetFloat(PrefS(id), scale);
            PlayerPrefs.Save();
        }

        public static void ClampWindow(RectTransform window)
        {
            ClampToParent(window);
        }

        static string PrefOn(string id) { return PrefPrefix + id + ".On"; }
        static string PrefX(string id) { return PrefPrefix + id + ".X"; }
        static string PrefY(string id) { return PrefPrefix + id + ".Y"; }
        static string PrefW(string id) { return PrefPrefix + id + ".W"; }
        static string PrefH(string id) { return PrefPrefix + id + ".H"; }
        static string PrefS(string id) { return PrefPrefix + id + ".S"; }
        static string PrefAMinX(string id) { return PrefPrefix + id + ".AMinX"; }
        static string PrefAMinY(string id) { return PrefPrefix + id + ".AMinY"; }
        static string PrefAMaxX(string id) { return PrefPrefix + id + ".AMaxX"; }
        static string PrefAMaxY(string id) { return PrefPrefix + id + ".AMaxY"; }
        static string PrefPivX(string id) { return PrefPrefix + id + ".PivX"; }
        static string PrefPivY(string id) { return PrefPrefix + id + ".PivY"; }

        void BuildShell()
        {
            EnsureEventSystem();
            WipeBrokenChildren();
            EnsureCanvas();
            EnsureHostLayer();
            EnsureArrow();
            EnsureDock();
            MigrateLayoutPrefs();
            _shellReady = _hostLayer != null && _arrowRt != null && _dockRt != null;
            Canvas.ForceUpdateCanvases();
            RefreshArrow();
            RebuildDock();
            RaiseChrome();
        }

        static void MigrateLayoutPrefs()
        {
            int v = PlayerPrefs.GetInt(PrefLayoutV, 0);
            if (v >= LayoutVersion)
                return;
            if (v < 3)
            {
                for (int i = 0; i < KnownIds.Length; i++)
                {
                    PlayerPrefs.DeleteKey(PrefX(KnownIds[i]));
                    PlayerPrefs.DeleteKey(PrefY(KnownIds[i]));
                }
            }
            if (v < 4)
            {
                // Combat actions went from a tall column to a horizontal bar.
                // Drop saved size/pose so the old 186x430 chrome does not snap back.
                // Keep PrefS so the user's scale still applies.
                const string id = "actions";
                PlayerPrefs.DeleteKey(PrefX(id));
                PlayerPrefs.DeleteKey(PrefY(id));
                PlayerPrefs.DeleteKey(PrefW(id));
                PlayerPrefs.DeleteKey(PrefH(id));
                PlayerPrefs.DeleteKey(PrefAMinX(id));
                PlayerPrefs.DeleteKey(PrefAMinY(id));
                PlayerPrefs.DeleteKey(PrefAMaxX(id));
                PlayerPrefs.DeleteKey(PrefAMaxY(id));
                PlayerPrefs.DeleteKey(PrefPivX(id));
                PlayerPrefs.DeleteKey(PrefPivY(id));
            }
            if (v < 5)
            {
                // Cheer meter added under the action row — drop saved size only.
                const string id = "actions";
                PlayerPrefs.DeleteKey(PrefW(id));
                PlayerPrefs.DeleteKey(PrefH(id));
            }
            if (v < 6)
            {
                // Combat actions went from a horizontal bar back to a vertical column.
                // Drop saved size/pose so the old wide chrome does not snap back.
                const string id = "actions";
                PlayerPrefs.DeleteKey(PrefX(id));
                PlayerPrefs.DeleteKey(PrefY(id));
                PlayerPrefs.DeleteKey(PrefW(id));
                PlayerPrefs.DeleteKey(PrefH(id));
                PlayerPrefs.DeleteKey(PrefAMinX(id));
                PlayerPrefs.DeleteKey(PrefAMinY(id));
                PlayerPrefs.DeleteKey(PrefAMaxX(id));
                PlayerPrefs.DeleteKey(PrefAMaxY(id));
                PlayerPrefs.DeleteKey(PrefPivX(id));
                PlayerPrefs.DeleteKey(PrefPivY(id));
            }
            PlayerPrefs.SetInt(PrefLayoutV, LayoutVersion);
            PlayerPrefs.Save();
        }

        void WipeBrokenChildren()
        {
            if (_hostLayer != null && _arrowRt != null && _dockRt != null)
                return;

            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child == null)
                    continue;
                Object.DestroyImmediate(child.gameObject);
            }
            _hostLayer = null;
            _arrowRt = null;
            _dockRt = null;
            _arrowText = null;
            _arrowImage = null;
        }

        static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
                return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        void EnsureCanvas()
        {
            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
                canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 6200;
            canvas.overrideSorting = true;
            canvas.pixelPerfect = true;
            var scaler = gameObject.GetComponent<CanvasScaler>();
            if (scaler == null)
                scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            if (gameObject.GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();
        }

        void EnsureHostLayer()
        {
            if (_hostLayer != null)
                return;
            var existing = transform.Find("Windows") as RectTransform;
            if (existing != null)
            {
                _hostLayer = existing;
                Stretch(_hostLayer);
                return;
            }

            var hostGo = new GameObject("Windows", typeof(RectTransform));
            hostGo.transform.SetParent(transform, false);
            _hostLayer = hostGo.GetComponent<RectTransform>();
            Stretch(_hostLayer);
        }

        void EnsureArrow()
        {
            if (_arrowRt != null && _arrowText != null && _arrowImage != null)
                return;

            var existing = transform.Find("Arrow") as RectTransform;
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            _arrowRt = MakeRt(transform, "Arrow");
            _arrowRt.anchorMin = new Vector2(0f, 0.5f);
            _arrowRt.anchorMax = new Vector2(0f, 0.5f);
            _arrowRt.pivot = new Vector2(0f, 0.5f);
            _arrowRt.anchoredPosition = new Vector2(0f, 0f);
            _arrowRt.sizeDelta = new Vector2(28f, 72f);
            _arrowImage = _arrowRt.GetComponent<Image>();
            _arrowImage.color = new Color(0.10f, 0.12f, 0.18f, 0.94f);
            _arrowImage.raycastTarget = true;
            var arrowBtn = _arrowRt.gameObject.AddComponent<Button>();
            arrowBtn.targetGraphic = _arrowImage;
            arrowBtn.onClick.AddListener(ToggleDock);
            _arrowText = MakeText(_arrowRt, "Label", "›", 22, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            Stretch(_arrowText.rectTransform);
        }

        void EnsureDock()
        {
            if (_dockRt != null)
                return;

            var existing = transform.Find("Dock") as RectTransform;
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            _dockRt = MakeRt(transform, "Dock");
            _dockRt.anchorMin = new Vector2(0f, 0.5f);
            _dockRt.anchorMax = new Vector2(0f, 0.5f);
            _dockRt.pivot = new Vector2(0f, 0.5f);
            _dockRt.anchoredPosition = new Vector2(28f, 0f);
            _dockRt.sizeDelta = new Vector2(220f, 280f);
            var dockImg = _dockRt.GetComponent<Image>();
            dockImg.color = new Color(0.06f, 0.07f, 0.12f, 0.96f);
            dockImg.raycastTarget = true;

            _dockOpen = PlayerPrefs.GetInt(PrefDock, 0) == 1;
            _dockRt.gameObject.SetActive(_dockOpen);
        }

        void ToggleDock()
        {
            _dockOpen = !_dockOpen;
            if (_dockRt != null)
            {
                _dockRt.gameObject.SetActive(_dockOpen);
                if (_dockOpen)
                    RebuildDock();
            }
            PlayerPrefs.SetInt(PrefDock, _dockOpen ? 1 : 0);
            PlayerPrefs.Save();
            RefreshArrow();
            RaiseChrome();
        }

        void RefreshArrow()
        {
            if (_arrowText != null)
                _arrowText.text = _dockOpen ? "‹" : "›";
            if (_arrowImage != null)
                _arrowImage.color = _dockOpen
                    ? new Color(0.18f, 0.42f, 0.36f, 0.96f)
                    : new Color(0.10f, 0.12f, 0.18f, 0.94f);
        }

        void RaiseChrome()
        {
            if (_dockRt != null)
                _dockRt.SetAsLastSibling();
            if (_arrowRt != null)
                _arrowRt.SetAsLastSibling();
        }

        void RebuildDock()
        {
            if (_dockRt == null)
                return;

            for (int i = _dockRt.childCount - 1; i >= 0; i--)
            {
                var child = _dockRt.GetChild(i);
                if (child == null)
                    continue;
                Object.DestroyImmediate(child.gameObject);
            }

            var title = MakeText(_dockRt, "Title", "显示哪些面板", 16, FontStyle.Bold,
                TextAnchor.MiddleLeft, new Color(0.92f, 0.94f, 0.96f, 1f));
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0.5f, 1f);
            titleRt.anchoredPosition = new Vector2(0f, -8f);
            titleRt.sizeDelta = new Vector2(-16f, 24f);

            float y = -36f;
            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                var rowRt = MakeRt(_dockRt, "Row_" + entry.Id);
                rowRt.anchorMin = new Vector2(0f, 1f);
                rowRt.anchorMax = new Vector2(1f, 1f);
                rowRt.pivot = new Vector2(0.5f, 1f);
                rowRt.anchoredPosition = new Vector2(0f, y);
                rowRt.sizeDelta = new Vector2(-16f, 32f);
                var img = rowRt.GetComponent<Image>();
                img.raycastTarget = true;
                var btn = rowRt.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                string captured = entry.Id;
                btn.onClick.AddListener(() => ToggleEntry(captured));
                var label = MakeText(rowRt, "Label", entry.Title, 15, FontStyle.Normal,
                    TextAnchor.MiddleLeft, Color.white);
                Stretch(label.rectTransform);
                label.rectTransform.offsetMin = new Vector2(8f, 0f);
                entry.ToggleImage = img;
                entry.ToggleLabel = label;
                RefreshDockRow(entry);
                y -= 36f;
            }

            y -= 8f;
            var saveRt = MakeRt(_dockRt, "Row_save");
            saveRt.anchorMin = new Vector2(0f, 1f);
            saveRt.anchorMax = new Vector2(1f, 1f);
            saveRt.pivot = new Vector2(0.5f, 1f);
            saveRt.anchoredPosition = new Vector2(0f, y);
            saveRt.sizeDelta = new Vector2(-16f, 36f);
            var saveImg = saveRt.GetComponent<Image>();
            saveImg.color = new Color(0.28f, 0.46f, 0.22f, 0.98f);
            saveImg.raycastTarget = true;
            var saveBtn = saveRt.gameObject.AddComponent<Button>();
            saveBtn.targetGraphic = saveImg;
            saveBtn.onClick.AddListener(SaveAllGameChangesInternal);
            _saveLabel = MakeText(saveRt, "Label", "保存", 15, FontStyle.Bold,
                TextAnchor.MiddleCenter, Color.white);
            Stretch(_saveLabel.rectTransform);

            float h = 56f + _entries.Count * 36f + 44f;
            if (h < 160f) h = 160f;
            _dockRt.sizeDelta = new Vector2(220f, h);
            RaiseChrome();
        }

        void ToggleEntry(string id)
        {
            var entry = FindEntry(id);
            if (entry == null || entry.Chrome == null)
                return;
            bool on = !entry.Chrome.gameObject.activeSelf;
            SetVisibleInternal(id, on, true);
        }

        void RefreshDockRow(Entry entry)
        {
            if (entry == null)
                return;
            bool on = entry.Chrome != null && entry.Chrome.gameObject.activeSelf;
            if (entry.ToggleImage != null)
                entry.ToggleImage.color = on
                    ? new Color(0.18f, 0.48f, 0.36f, 1f)
                    : new Color(0.22f, 0.24f, 0.30f, 0.96f);
            if (entry.ToggleLabel != null)
                entry.ToggleLabel.text = (on ? "✓  " : "    ") + entry.Title;
        }

        RectTransform Wrap(RectTransform content, string id, string title)
        {
            if (content == null)
                return null;

            var existingWin = content.GetComponentInParent<HudWindow>(true);
            if (existingWin != null && existingWin.Id == id)
                return existingWin.GetComponent<RectTransform>();

            var parent = content.parent as RectTransform;
            if (parent == null)
                parent = _hostLayer;
            if (parent == null)
                return null;

            int sibling = content.GetSiblingIndex();

            var chromeGo = new GameObject("HudWindow_" + id, typeof(RectTransform), typeof(Image));
            chromeGo.transform.SetParent(parent, false);
            chromeGo.transform.SetSiblingIndex(sibling);
            var chrome = chromeGo.GetComponent<RectTransform>();
            chrome.anchorMin = content.anchorMin;
            chrome.anchorMax = content.anchorMax;
            chrome.pivot = content.pivot;
            chrome.anchoredPosition = content.anchoredPosition;
            chrome.sizeDelta = content.sizeDelta;
            chrome.offsetMin = content.offsetMin;
            chrome.offsetMax = content.offsetMax;
            ExpandForTitle(chrome, TitleH);

            var chromeImg = chromeGo.GetComponent<Image>();
            chromeImg.color = new Color(0.07f, 0.08f, 0.13f, 0.97f);
            chromeImg.raycastTarget = true;

            var win = chromeGo.AddComponent<HudWindow>();
            win.Id = id;
            win.GrowUp = DetectGrowUp(chrome);
            // Drag lives on the whole chrome so empty body / labels / title all move
            // the window. Sliders and the corner resize grip keep their own IDragHandler
            // and win the EventSystem search first.
            var drag = chromeGo.AddComponent<HudWindowDrag>();
            drag.Bind(chrome, id);

            var bar = MakeRt(chrome, "TitleBar");
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(0f, TitleH);
            var barImg = bar.GetComponent<Image>();
            barImg.color = new Color(0.12f, 0.16f, 0.24f, 1f);
            barImg.raycastTarget = true;

            var titleText = MakeText(bar, "Title", title, 14, FontStyle.Bold,
                TextAnchor.MiddleLeft, Color.white);
            var titleRt = titleText.rectTransform;
            titleRt.anchorMin = Vector2.zero;
            titleRt.anchorMax = Vector2.one;
            titleRt.offsetMin = new Vector2(10f, 0f);
            titleRt.offsetMax = new Vector2(-32f, 0f);

            var closeRt = MakeRt(bar, "Close");
            closeRt.anchorMin = new Vector2(1f, 0.5f);
            closeRt.anchorMax = new Vector2(1f, 0.5f);
            closeRt.pivot = new Vector2(1f, 0.5f);
            closeRt.anchoredPosition = new Vector2(-4f, 0f);
            closeRt.sizeDelta = new Vector2(24f, 22f);
            var closeImg = closeRt.GetComponent<Image>();
            closeImg.color = new Color(0.62f, 0.22f, 0.20f, 1f);
            closeImg.raycastTarget = true;
            var closeBtn = closeRt.gameObject.AddComponent<Button>();
            closeBtn.targetGraphic = closeImg;
            string captured = id;
            closeBtn.onClick.AddListener(() => SetVisible(captured, false));
            var closeLabel = MakeText(closeRt, "X", "×", 16, FontStyle.Bold,
                TextAnchor.MiddleCenter, Color.white);
            Stretch(closeLabel.rectTransform);

            content.SetParent(chrome, false);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 0.5f);
            content.offsetMin = new Vector2(0f, 0f);
            content.offsetMax = new Vector2(0f, -TitleH);
            content.localScale = Vector3.one;
            content.localRotation = Quaternion.identity;
            var contentImg = content.GetComponent<Image>();
            if (contentImg != null)
                contentImg.raycastTarget = true;

            EnsureResizeGrip(chrome, id);
            if (!PlayerPrefs.HasKey(PrefX(id)))
                ClampToParent(chrome);
            return chrome;
        }

        static void EnsureResizeGrip(RectTransform chrome, string id)
        {
            if (chrome == null)
                return;

            var legacy = chrome.GetComponentsInChildren<BondPanelResizer>(true);
            for (int i = 0; i < legacy.Length; i++)
            {
                if (legacy[i] != null)
                    Object.DestroyImmediate(legacy[i].gameObject);
            }

            var win = chrome.GetComponent<HudWindow>();
            bool growUp = win != null ? win.GrowUp : DetectGrowUp(chrome);
            var existing = chrome.Find("ResizeGrip") as RectTransform;
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            var gripGo = new GameObject("ResizeGrip", typeof(RectTransform), typeof(Image));
            gripGo.transform.SetParent(chrome, false);
            var grip = gripGo.GetComponent<RectTransform>();
            if (growUp)
            {
                grip.anchorMin = new Vector2(1f, 1f);
                grip.anchorMax = new Vector2(1f, 1f);
                grip.pivot = new Vector2(1f, 1f);
                grip.anchoredPosition = new Vector2(-2f, -2f);
            }
            else
            {
                grip.anchorMin = new Vector2(1f, 0f);
                grip.anchorMax = new Vector2(1f, 0f);
                grip.pivot = new Vector2(1f, 0f);
                grip.anchoredPosition = new Vector2(-2f, 2f);
            }
            grip.sizeDelta = new Vector2(22f, 22f);
            var img = gripGo.GetComponent<Image>();
            img.color = new Color(0.42f, 0.72f, 0.78f, 0.95f);
            img.raycastTarget = true;
            var label = MakeText(grip, "Mark", growUp ? "◥" : "◢", 13, FontStyle.Bold,
                TextAnchor.MiddleCenter, Color.white);
            Stretch(label.rectTransform);
            var resize = gripGo.AddComponent<HudWindowResize>();
            resize.Bind(chrome, id, growUp);
            grip.SetAsLastSibling();
        }

        internal static bool DetectGrowUp(RectTransform rt)
        {
            if (rt == null)
                return false;
            bool stretchX = Mathf.Abs(rt.anchorMax.x - rt.anchorMin.x) > 0.001f;
            return stretchX && rt.anchorMax.y < 0.45f;
        }

        internal static void FreezeForResize(RectTransform rt)
        {
            if (rt == null)
                return;
            var parent = rt.parent as RectTransform;
            if (parent == null)
                return;

            var win = rt.GetComponent<HudWindow>();
            bool growUp = win != null ? win.GrowUp : DetectGrowUp(rt);
            Vector3[] wc = new Vector3[4];
            rt.GetWorldCorners(wc);
            Vector2 bl = parent.InverseTransformPoint(wc[0]);
            Vector2 tl = parent.InverseTransformPoint(wc[1]);
            Vector2 br = parent.InverseTransformPoint(wc[3]);
            float visW = br.x - bl.x;
            float visH = tl.y - bl.y;
            float sx = Mathf.Abs(rt.localScale.x);
            float sy = Mathf.Abs(rt.localScale.y);
            if (sx < 0.01f) sx = 1f;
            if (sy < 0.01f) sy = 1f;
            float w = visW / sx;
            float h = visH / sy;
            if (w < 8f) w = 8f;
            if (h < 8f) h = 8f;

            Vector2 pivot = growUp ? new Vector2(0f, 0f) : new Vector2(0f, 1f);
            rt.anchorMin = pivot;
            rt.anchorMax = pivot;
            rt.pivot = pivot;
            rt.sizeDelta = new Vector2(w, h);
            Rect prect = parent.rect;
            if (growUp)
                rt.anchoredPosition = new Vector2(bl.x - prect.xMin, bl.y - prect.yMin);
            else
                rt.anchoredPosition = new Vector2(bl.x - prect.xMin, tl.y - prect.yMax);
        }

        internal static void SetWindowScale(RectTransform rt, string id, float s)
        {
            if (rt == null)
                return;
            float minS = 0.5f;
            float maxS = 2.4f;
            if (id == "pool")
            {
                minS = 0.55f;
                maxS = 1.5f;
            }
            s = Mathf.Clamp(s, minS, maxS);
            rt.localScale = new Vector3(s, s, 1f);
        }

        static void ExpandForTitle(RectTransform chrome, float titleH)
        {
            if (chrome == null)
                return;
            bool stretchY = Mathf.Abs(chrome.anchorMax.y - chrome.anchorMin.y) > 0.001f;
            bool growUp = chrome.anchorMax.y < 0.45f;
            if (stretchY)
            {
                if (growUp)
                {
                    Vector2 max = chrome.offsetMax;
                    max.y += titleH;
                    chrome.offsetMax = max;
                }
                else
                {
                    Vector2 min = chrome.offsetMin;
                    min.y -= titleH;
                    chrome.offsetMin = min;
                }
                return;
            }

            chrome.sizeDelta = new Vector2(chrome.sizeDelta.x, chrome.sizeDelta.y + titleH);
            if (growUp)
                chrome.anchoredPosition += new Vector2(0f, titleH * chrome.pivot.y);
            else
                chrome.anchoredPosition -= new Vector2(0f, titleH * (1f - chrome.pivot.y));
        }

        static void ClampToParent(RectTransform window)
        {
            if (window == null)
                return;
            var parent = window.parent as RectTransform;
            if (parent == null)
                return;
            // Overlay canvas has not laid out yet — world corners collapse to a
            // point and every window gets shoved to the same margin offset.
            if (parent.rect.width < 16f || parent.rect.height < 16f)
                return;

            Vector3[] wc = new Vector3[4];
            window.GetWorldCorners(wc);
            Vector3[] pc = new Vector3[4];
            parent.GetWorldCorners(pc);

            float pMinX = pc[0].x;
            float pMaxX = pc[0].x;
            float pMinY = pc[0].y;
            float pMaxY = pc[0].y;
            float wMinX = wc[0].x;
            float wMaxX = wc[0].x;
            float wMinY = wc[0].y;
            float wMaxY = wc[0].y;
            for (int i = 1; i < 4; i++)
            {
                if (pc[i].x < pMinX) pMinX = pc[i].x;
                if (pc[i].x > pMaxX) pMaxX = pc[i].x;
                if (pc[i].y < pMinY) pMinY = pc[i].y;
                if (pc[i].y > pMaxY) pMaxY = pc[i].y;
                if (wc[i].x < wMinX) wMinX = wc[i].x;
                if (wc[i].x > wMaxX) wMaxX = wc[i].x;
                if (wc[i].y < wMinY) wMinY = wc[i].y;
                if (wc[i].y > wMaxY) wMaxY = wc[i].y;
            }

            float margin = 28f;
            var canvas = window.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.scaleFactor > 0.01f)
                margin *= canvas.scaleFactor;

            float dx = 0f;
            float dy = 0f;
            if (wMaxX < pMinX + margin)
                dx = (pMinX + margin) - wMaxX;
            else if (wMinX > pMaxX - margin)
                dx = (pMaxX - margin) - wMinX;
            if (wMaxY < pMinY + margin)
                dy = (pMinY + margin) - wMaxY;
            else if (wMinY > pMaxY - margin)
                dy = (pMaxY - margin) - wMinY;

            if (dx != 0f || dy != 0f)
                window.position += new Vector3(dx, dy, 0f);
        }

        static RectTransform MakeRt(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = new Color(1f, 1f, 1f, 0f);
            img.raycastTarget = false;
            return go.GetComponent<RectTransform>();
        }

        static Text MakeText(Transform parent, string name, string value, int size,
            FontStyle style, TextAnchor align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<Text>();
            var font = SafeFont(size);
            if (font != null)
                text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = align;
            text.color = color;
            text.text = value ?? "";
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        static Font SafeFont(int size)
        {
            Font font = null;
            try
            {
                font = RuntimeFont.Resolve(size);
            }
            catch
            {
                font = null;
            }
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font == null)
                    font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            return font;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }

    public class HudWindow : MonoBehaviour
    {
        public string Id;
        public bool GrowUp;
        int _applyFrames;

        void OnEnable()
        {
            _applyFrames = 4;
        }

        void LateUpdate()
        {
            if (_applyFrames <= 0 || string.IsNullOrEmpty(Id))
                return;
            _applyFrames--;
            HudPanelHub.ReapplySaved(Id);
        }

        public void CancelPendingApply()
        {
            _applyFrames = 0;
        }
    }

    public class HudWindowDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        RectTransform _window;
        string _id;

        public void Bind(RectTransform window, string id)
        {
            _window = window;
            _id = id;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_window == null)
                return;
            _window.SetAsLastSibling();
            var win = _window.GetComponent<HudWindow>();
            if (win != null)
                win.CancelPendingApply();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_window == null || eventData == null)
                return;
            float scale = 1f;
            var canvas = _window.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.scaleFactor > 0.01f)
                scale = canvas.scaleFactor;
            _window.anchoredPosition += eventData.delta / scale;
            HudPanelHub.ClampWindow(_window);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_window == null)
                return;
            HudPanelHub.ClampWindow(_window);
            HudPanelHub.SaveFullLayout(_id, _window);
        }
    }

    public class HudWindowResize : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        RectTransform _window;
        string _id;
        bool _growUp;

        public void Bind(RectTransform window, string id, bool growUp)
        {
            _window = window;
            _id = id;
            _growUp = growUp;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (_window == null)
                return;
            _window.SetAsLastSibling();
            var win = _window.GetComponent<HudWindow>();
            if (win != null)
                win.CancelPendingApply();
            HudPanelHub.FreezeForResize(_window);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_window == null || eventData == null)
                return;
            float canvasScale = 1f;
            var canvas = _window.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.scaleFactor > 0.01f)
                canvasScale = canvas.scaleFactor;
            float dx = eventData.delta.x / canvasScale;
            float dy = eventData.delta.y / canvasScale;
            float baseSize = Mathf.Max(_window.rect.width, _window.rect.height, 80f);
            float s = _window.localScale.x;
            if (_growUp)
                s += (dx + dy) / baseSize;
            else
                s += (dx - dy) / baseSize;
            HudPanelHub.SetWindowScale(_window, _id, s);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_window == null)
                return;
            HudPanelHub.ClampWindow(_window);
            HudPanelHub.SaveFullLayout(_id, _window);
        }
    }
}
