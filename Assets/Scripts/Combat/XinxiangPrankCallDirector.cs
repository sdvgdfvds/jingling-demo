using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace NexusAnima
{
    /// <summary>
    /// Gag "Qiana incoming call": fullscreen sajiao video + side phone + red hang-up button.
    /// Mash the red button (5 clicks / Space) to hang up; Ice emoji pops after hang-up.
    /// </summary>
    public class XinxiangPrankCallDirector : MonoBehaviour
    {
        const int OverlaySort = 1210;
        const string OverlayName = "PrankCallOverlayCanvas";
        const string VideoResourcePath = "Xinxiang/Videos/QianaCall";
        const string OverlayVersion = "QianaCall_v7";
        const int HangClicksRequired = 5;

        [SerializeField] float ringTimeoutSeconds = 10f;
        [SerializeField] float resultHoldSeconds = 1.2f;
        [SerializeField] float emojiSeconds = 2.6f;

        public bool IsPlaying { get; private set; }

        public event Action CallStarted;
        /// <summary>Arg is true when the player mashed the red button to hang up.</summary>
        public event Action<bool> CallEnded;

        Coroutine _routine;
        RectTransform _overlayRoot;
        RawImage _videoImage;
        VideoPlayer _videoPlayer;
        GameObject _videoHost;
        RenderTexture _rt;
        Image _phoneImage;
        Vector2 _phoneRestPos;
        Button _hangButton;
        Image _hangImage;
        RectTransform _hangRt;
        Text _hangProgressText;
        Text _titleText;
        Text _hintText;
        Text _resultText;
        AudioSource _ringSource;
        AudioSource _videoAudio;
        List<CanvasGroup> _hudBlock;
        Action<bool> _onFinished;
        bool _cancelRequested;
        bool _hungUp;
        int _hangClicks;

        public void EnsureBuilt()
        {
            EnsureOverlay();
            EnsureVideo();
            EnsureRingSource();
            // Always force-hide: bootstrap builds this before battle starts.
            HideUi();
            ForceHideAllOverlays();
        }

        static void ForceHideAllOverlays()
        {
            // Catch stale DontDestroyOnLoad leftovers that Destroy() deferred.
            var all = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t.name != OverlayName) continue;
                if (t.hideFlags != HideFlags.None) continue;
                if (!t.gameObject.scene.IsValid()) continue;
                if (t.gameObject.activeSelf)
                    t.gameObject.SetActive(false);
            }
        }

        /// <param name="onFinished">Arg is true when the player mashed hang-up.</param>
        public bool Play(Action<bool> onFinished = null)
        {
            if (IsPlaying) return false;
            EnsureBuilt();
            _onFinished = onFinished;
            _cancelRequested = false;
            _hungUp = false;
            _hangClicks = 0;
            if (_routine != null)
                StopCoroutine(_routine);
            _routine = StartCoroutine(PlayRoutine());
            return true;
        }

        public void Cancel()
        {
            if (!IsPlaying) return;
            _cancelRequested = true;
        }

        IEnumerator PlayRoutine()
        {
            IsPlaying = true;
            CallStarted?.Invoke();

            _hudBlock = PushHudBlock();
            ShowUi();
            StartRing();
            StartVideoLoop();
            GameAudio.I?.PlaySfx("PhoneRing", 0.55f);

            if (_titleText != null)
                _titleText.text = "琪亚娜打来电话";
            if (_resultText != null)
            {
                _resultText.text = "";
                _resultText.enabled = false;
            }

            SetHangInteractable(true);
            RefreshHangProgressUi();
            float elapsed = 0f;
            float animT = 0f;

            while (elapsed < ringTimeoutSeconds && !_cancelRequested && !_hungUp)
            {
                elapsed += Time.unscaledDeltaTime;
                animT += Time.unscaledDeltaTime;
                AnimateRingingUi(animT);
                yield return null;
            }

            StopRing();
            ResetHangVisual();

            bool hungUp = _hungUp && !_cancelRequested;

            if (_resultText != null)
                _resultText.enabled = true;

            GameAudio.I?.PlayPhoneHangup();
            if (_hintText != null)
                _hintText.text = "";
            if (_hangButton != null)
                _hangButton.gameObject.SetActive(false);

            if (_resultText != null)
            {
                if (hungUp)
                {
                    _resultText.color = new Color(0.55f, 0.85f, 1f, 1f);
                    _resultText.text = "别再打来了";
                }
                else
                {
                    _resultText.color = new Color(1f, 0.55f, 0.55f, 1f);
                    _resultText.text = _cancelRequested ? "已挂断" : "未接来电…";
                }
            }

            float hold = 0f;
            float holdTarget = hungUp ? Mathf.Max(resultHoldSeconds, 1.5f) : resultHoldSeconds;
            while (hold < holdTarget && !_cancelRequested)
            {
                hold += Time.unscaledDeltaTime;
                yield return null;
            }

            StopVideo();
            HideUi();
            PopHudBlock(_hudBlock);
            _hudBlock = null;

            IsPlaying = false;
            _routine = null;
            CallEnded?.Invoke(hungUp);

            var cb = _onFinished;
            _onFinished = null;
            cb?.Invoke(hungUp);
        }

        void AnimateRingingUi(float t)
        {
            // Phone bob — vertical only.
            if (_phoneImage != null)
            {
                float bob = Mathf.Sin(t * 14f) * 10f;
                _phoneImage.rectTransform.anchoredPosition = _phoneRestPos + new Vector2(0f, bob);
                _phoneImage.rectTransform.localRotation = Quaternion.identity;
            }

            // Red hang button pulse — urgency grows with mash progress.
            if (_hangRt != null)
            {
                float urgency = _hangClicks / (float)HangClicksRequired;
                float pulse = 1f + Mathf.Sin(t * (6.5f + urgency * 4f)) * (0.08f + urgency * 0.06f);
                _hangRt.localScale = new Vector3(pulse, pulse, 1f);
            }

            if (_hangImage != null)
            {
                float glow = 0.85f + Mathf.Sin(t * 6.5f) * 0.15f;
                float mash = _hangClicks / (float)HangClicksRequired;
                _hangImage.color = Color.Lerp(
                    new Color(glow, glow * 0.55f, glow * 0.55f, 1f),
                    new Color(1f, 0.35f, 0.35f, 1f),
                    mash);
            }
        }

        void ResetHangVisual()
        {
            if (_phoneImage != null)
            {
                _phoneImage.rectTransform.anchoredPosition = _phoneRestPos;
                _phoneImage.rectTransform.localRotation = Quaternion.identity;
            }
            if (_hangRt != null)
                _hangRt.localScale = Vector3.one;
            if (_hangImage != null)
                _hangImage.color = Color.white;
        }

        void OnHangClicked()
        {
            if (!IsPlaying || _hungUp || _cancelRequested) return;

            _hangClicks++;
            GameAudio.I?.PlayPhoneMash();
            RefreshHangProgressUi();
            if (_hangRt != null)
                StartCoroutine(HangClickFeedback());

            if (_hangClicks >= HangClicksRequired)
            {
                _hungUp = true;
                SetHangInteractable(false);
            }
        }

        IEnumerator HangClickFeedback()
        {
            if (_hangRt == null) yield break;
            float t = 0f;
            while (t < 0.14f)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / 0.14f);
                float s = u < 0.45f
                    ? Mathf.Lerp(1f, 0.72f, u / 0.45f)
                    : Mathf.Lerp(0.72f, 1.1f, (u - 0.45f) / 0.55f);
                _hangRt.localScale = new Vector3(s, s, 1f);
                if (_hangImage != null)
                    _hangImage.color = Color.Lerp(Color.white, new Color(1f, 0.45f, 0.45f, 1f), u);
                yield return null;
            }
        }

        void RefreshHangProgressUi()
        {
            int left = Mathf.Max(0, HangClicksRequired - _hangClicks);
            if (_hintText != null)
            {
                _hintText.text = _hangClicks <= 0
                    ? "连点红色按钮挂断！"
                    : $"再点 {left} 次挂断…";
            }
            if (_hangProgressText != null)
            {
                _hangProgressText.text = $"{_hangClicks}/{HangClicksRequired}";
                _hangProgressText.enabled = true;
            }
        }

        void SetHangInteractable(bool on)
        {
            if (_hangButton != null)
            {
                _hangButton.interactable = on;
                _hangButton.gameObject.SetActive(true);
            }
        }

        void EnsureRingSource()
        {
            if (_ringSource != null) return;
            _ringSource = gameObject.GetComponent<AudioSource>();
            if (_ringSource == null)
                _ringSource = gameObject.AddComponent<AudioSource>();
            _ringSource.playOnAwake = false;
            _ringSource.loop = true;
            _ringSource.spatialBlend = 0f;
            _ringSource.priority = 32;
            _ringSource.volume = 0.55f;
        }

        void StartRing()
        {
            EnsureRingSource();
            var clip = Resources.Load<AudioClip>("Audio/Sfx/PhoneRing");
            if (clip == null || _ringSource == null) return;
            _ringSource.clip = clip;
            _ringSource.volume = 0.55f;
            if (!_ringSource.isPlaying)
                _ringSource.Play();
        }

        void StopRing()
        {
            if (_ringSource != null && _ringSource.isPlaying)
                _ringSource.Stop();
            if (_ringSource != null)
                _ringSource.clip = null;
        }

        void EnsureVideo()
        {
            // Re-bind host if destroyed (Unity fake-null) or reuse leftover child.
            if (_videoHost == null)
            {
                var existing = transform.Find("PrankCallVideoHost");
                if (existing != null)
                    _videoHost = existing.gameObject;
                else
                {
                    _videoHost = new GameObject("PrankCallVideoHost");
                    _videoHost.transform.SetParent(transform, false);
                }
            }

            // Always re-resolve from the live host. Stale VideoPlayer refs throw
            // MissingComponentException on property access; never use ?? on UnityEngine.Object
            // because ?? ignores Unity's fake-null and can keep a dead component.
            _videoPlayer = _videoHost.GetComponent<VideoPlayer>();
            if (_videoPlayer == null)
                _videoPlayer = _videoHost.AddComponent<VideoPlayer>();

            EnsureRenderTexture(false);
            EnsureVideoAudioSource();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.isLooping = true;
            _videoPlayer.waitForFirstFrame = true;
            _videoPlayer.skipOnDrop = true;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            _videoPlayer.aspectRatio = VideoAspectRatio.FitVertically;
            _videoPlayer.targetTexture = _rt;
            // Play embedded sajiao voice (was None → silent).
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            try
            {
                if (_videoPlayer.controlledAudioTrackCount < 1)
                    _videoPlayer.controlledAudioTrackCount = 1;
                _videoPlayer.EnableAudioTrack(0, true);
                _videoPlayer.SetTargetAudioSource(0, _videoAudio);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[PrankCall] Video audio setup skipped: " + e.Message);
            }
        }

        void EnsureVideoAudioSource()
        {
            if (_videoHost == null) return;
            if (_videoAudio == null)
                _videoAudio = _videoHost.GetComponent<AudioSource>();
            if (_videoAudio == null)
                _videoAudio = _videoHost.AddComponent<AudioSource>();
            _videoAudio.playOnAwake = false;
            _videoAudio.spatialBlend = 0f;
            _videoAudio.loop = false;
            _videoAudio.priority = 48;
            _videoAudio.volume = 1f;
            GameAudio.Ensure()?.ConfigureVoiceSource(_videoAudio);
        }

        void EnsureRenderTexture(bool force)
        {
            int w = Mathf.Max(640, Screen.width > 16 ? Screen.width : 1920);
            int h = Mathf.Max(360, Screen.height > 16 ? Screen.height : 1080);
            w &= ~1;
            h &= ~1;

            if (!force && _rt != null && _rt.IsCreated() && _rt.width == w && _rt.height == h)
                return;

            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
            }

            _rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "PrankCallVideoRT",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            _rt.Create();

            if (_videoPlayer != null)
                _videoPlayer.targetTexture = _rt;
            if (_videoImage != null)
                _videoImage.texture = _rt;
        }

        void StartVideoLoop()
        {
            EnsureVideo();
            EnsureRenderTexture(false);
            if (_videoImage != null)
            {
                _videoImage.texture = _rt;
                _videoImage.enabled = true;
                _videoImage.color = Color.white;
            }

            var clip = Resources.Load<VideoClip>(VideoResourcePath);
            if (clip != null && _videoPlayer != null)
            {
                _videoPlayer.source = VideoSource.VideoClip;
                _videoPlayer.clip = clip;
                _videoPlayer.url = string.Empty;
                _videoPlayer.isLooping = true;
                EnsureVideoAudioSource();
                _videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
                try
                {
                    // Re-bind after clip assign — some platforms only expose tracks then.
                    int tracks = Mathf.Max(1, (int)clip.audioTrackCount);
                    if (_videoPlayer.controlledAudioTrackCount < tracks)
                        _videoPlayer.controlledAudioTrackCount = (ushort)tracks;
                    _videoPlayer.EnableAudioTrack(0, true);
                    _videoPlayer.SetTargetAudioSource(0, _videoAudio);
                    if (_videoAudio != null)
                        _videoAudio.volume = 1f;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[PrankCall] Video audio bind skipped: " + e.Message);
                }
                _videoPlayer.Play();
            }
            else
            {
                Debug.LogWarning("[PrankCall] QianaCall video missing at Resources/" + VideoResourcePath);
            }
        }

        void StopVideo()
        {
            // Re-resolve so we never touch a MissingComponent stub.
            if (_videoHost != null)
                _videoPlayer = _videoHost.GetComponent<VideoPlayer>();
            else
                _videoPlayer = null;

            if (_videoPlayer != null)
            {
                if (_videoPlayer.isPlaying)
                    _videoPlayer.Stop();
                _videoPlayer.clip = null;
            }
            if (_videoAudio != null && _videoAudio.isPlaying)
                _videoAudio.Stop();
            if (_videoImage != null)
            {
                _videoImage.enabled = false;
                _videoImage.color = new Color(0f, 0f, 0f, 0f);
            }
        }

        void EnsureOverlay()
        {
            // Tear down any stale overlay immediately (Destroy is deferred and left UI on screen).
            DestroyStaleOverlays();

            if (_overlayRoot != null)
            {
                var marker = _overlayRoot.Find("Version_" + OverlayVersion);
                if (marker != null)
                {
                    _overlayRoot.gameObject.SetActive(false);
                    return;
                }
                DestroyImmediateSafe(_overlayRoot.gameObject);
                _overlayRoot = null;
            }

            var go = new GameObject(OverlayName, typeof(RectTransform));
            // Hide BEFORE parenting any children so bootstrap never flashes phone/title.
            go.SetActive(false);
            if (Application.isPlaying)
                DontDestroyOnLoad(go);

            // Version marker so stale overlays rebuild.
            var verGo = new GameObject("Version_" + OverlayVersion, typeof(RectTransform));
            verGo.transform.SetParent(go.transform, false);

            var canvas = go.GetComponent<Canvas>();
            if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = OverlaySort;
            canvas.overrideSorting = true;

            if (go.GetComponent<CanvasScaler>() == null)
            {
                var scaler = go.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280f, 720f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
            }

            if (go.GetComponent<GraphicRaycaster>() == null)
                go.AddComponent<GraphicRaycaster>();

            _overlayRoot = go.GetComponent<RectTransform>();
            GameScreenCanvas.StretchFull(_overlayRoot);

            // Fullscreen video (behind phone).
            var videoGo = new GameObject("FullscreenVideo", typeof(RectTransform));
            videoGo.transform.SetParent(_overlayRoot, false);
            GameScreenCanvas.StretchFull(videoGo.GetComponent<RectTransform>());
            _videoImage = videoGo.AddComponent<RawImage>();
            _videoImage.texture = Texture2D.blackTexture;
            _videoImage.color = Color.white;
            _videoImage.raycastTarget = false;

            // Soft left vignette — phone lives on the LEFT so right-side 心象 slots stay clear.
            var dimGo = new GameObject("SideDim", typeof(RectTransform));
            dimGo.transform.SetParent(_overlayRoot, false);
            var dimRt = dimGo.GetComponent<RectTransform>();
            dimRt.anchorMin = new Vector2(0f, 0f);
            dimRt.anchorMax = new Vector2(0.38f, 1f);
            dimRt.offsetMin = Vector2.zero;
            dimRt.offsetMax = Vector2.zero;
            var dim = dimGo.AddComponent<Image>();
            dim.sprite = GameScreenCanvas.UiWhiteSprite;
            dim.color = new Color(0.02f, 0.04f, 0.08f, 0.28f);
            dim.raycastTarget = false;

            // Cute title — top center, pink, larger. Empty until Play().
            _titleText = CreateText(_overlayRoot, "Title",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -36f), new Vector2(720f, 64f), 42,
                new Color(1f, 0.55f, 0.78f, 1f));
            _titleText.alignment = TextAnchor.MiddleCenter;
            _titleText.fontStyle = FontStyle.Bold;
            _titleText.text = "";
            // Soft outline via shadow for "cute" read.
            var outline = _titleText.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.85f);
            outline.effectDistance = new Vector2(2f, -2f);
            var shadow = _titleText.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.85f, 0.25f, 0.55f, 0.45f);
            shadow.effectDistance = new Vector2(0f, -3f);

            // Phone on the LEFT-LOWER side: avoids right 心象栏 and top-left 羁绊面板.
            var phoneRoot = new GameObject("PhoneRoot", typeof(RectTransform));
            phoneRoot.transform.SetParent(_overlayRoot, false);
            var phoneRootRt = phoneRoot.GetComponent<RectTransform>();
            phoneRootRt.anchorMin = new Vector2(0f, 0f);
            phoneRootRt.anchorMax = new Vector2(0f, 0f);
            phoneRootRt.pivot = new Vector2(0f, 0f);
            phoneRootRt.sizeDelta = new Vector2(220f, 440f);
            // Sit above the bottom spirit pool (~122px), clear of right Actions panel.
            phoneRootRt.anchoredPosition = new Vector2(18f, 136f);

            var phoneGo = new GameObject("Phone", typeof(RectTransform));
            phoneGo.transform.SetParent(phoneRoot.transform, false);
            var phoneRt = phoneGo.GetComponent<RectTransform>();
            phoneRt.anchorMin = new Vector2(0.5f, 0.5f);
            phoneRt.anchorMax = new Vector2(0.5f, 0.5f);
            phoneRt.pivot = new Vector2(0.5f, 0.5f);
            phoneRt.sizeDelta = new Vector2(200f, 400f);
            phoneRt.anchoredPosition = Vector2.zero;
            _phoneRestPos = phoneRt.anchoredPosition;
            _phoneImage = phoneGo.AddComponent<Image>();
            var phoneSprite = CuteUi.LoadPocketSprite("SmartphoneCute")
                              ?? CuteUi.LoadPocketSprite("Smartphone");
            _phoneImage.sprite = phoneSprite != null ? phoneSprite : GameScreenCanvas.UiWhiteSprite;
            _phoneImage.color = Color.white;
            _phoneImage.preserveAspect = true;
            _phoneImage.raycastTarget = false;

            // Red hang-up button — sits on the phone art's right (red) dial button.
            var btnGo = new GameObject("HangButton", typeof(RectTransform));
            btnGo.transform.SetParent(phoneGo.transform, false);
            _hangRt = btnGo.GetComponent<RectTransform>();
            _hangRt.anchorMin = new Vector2(0.5f, 0f);
            _hangRt.anchorMax = new Vector2(0.5f, 0f);
            _hangRt.pivot = new Vector2(0.5f, 0.5f);
            // SmartphoneCute art: RED hang-up is on the LEFT, green answer on the RIGHT.
            // Pixel map (1028×2177 → 200×400 preserveAspect): red ≈ (-36, 109).
            _hangRt.anchoredPosition = new Vector2(-36f, 109f);
            _hangRt.sizeDelta = new Vector2(58f, 58f);
            _hangImage = btnGo.AddComponent<Image>();
            var red = CuteUi.LoadSprite("UI/CuteUi/PrankCall/RedHangButton", border: 8f)
                      ?? CuteUi.LoadKenneySprite("Red_Default_button_round_depth_gloss")
                      ?? CuteUi.LoadKenneySprite("Red_Default_button_rectangle_gloss")
                      ?? CuteUi.LoadSprite("UI/CuteUi/PrankCall/GreenAnswerButton", border: 8f);
            _hangImage.sprite = red != null ? red : GameScreenCanvas.UiWhiteSprite;
            _hangImage.color = red != null ? Color.white : new Color(0.95f, 0.25f, 0.3f, 1f);
            _hangImage.preserveAspect = true;
            _hangImage.raycastTarget = true;
            _hangButton = btnGo.AddComponent<Button>();
            _hangButton.targetGraphic = _hangImage;
            var colors = _hangButton.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 0.75f, 0.75f, 1f);
            colors.pressedColor = new Color(0.85f, 0.35f, 0.35f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.7f, 0.55f, 0.55f, 0.6f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.06f;
            _hangButton.colors = colors;
            _hangButton.onClick.AddListener(OnHangClicked);

            _hangProgressText = CreateText(btnGo.transform, "HangProgress",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, 0f), new Vector2(56f, 28f), 16,
                Color.white);
            _hangProgressText.alignment = TextAnchor.MiddleCenter;
            _hangProgressText.fontStyle = FontStyle.Bold;
            _hangProgressText.text = $"0/{HangClicksRequired}";
            var progOutline = _hangProgressText.gameObject.AddComponent<Outline>();
            progOutline.effectColor = new Color(0.35f, 0f, 0f, 0.85f);
            progOutline.effectDistance = new Vector2(1.2f, -1.2f);

            _hintText = CreateText(phoneRoot.transform, "Hint",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, -6f), new Vector2(210f, 28f), 15,
                new Color(1f, 0.92f, 0.65f, 1f));
            _hintText.alignment = TextAnchor.MiddleCenter;
            _hintText.text = "";

            // 挂断结果字幕：画面上方，字号加大
            _resultText = CreateText(_overlayRoot, "Result",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -72f), new Vector2(720f, 64f), 40,
                new Color(1f, 0.75f, 0.9f, 1f));
            _resultText.alignment = TextAnchor.MiddleCenter;
            _resultText.fontStyle = FontStyle.Bold;
            _resultText.text = "";
            _resultText.enabled = false;
            var resultOutline = _resultText.gameObject.AddComponent<Outline>();
            resultOutline.effectColor = new Color(0.05f, 0.12f, 0.28f, 0.9f);
            resultOutline.effectDistance = new Vector2(2f, -2f);

            // Stay hidden until Play().
            go.SetActive(false);
        }

        void DestroyStaleOverlays()
        {
            // Immediate destroy so deferred Destroy cannot leave a visible phone/title.
            var found = new List<GameObject>();
            var all = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t.name != OverlayName) continue;
                if (t.hideFlags != HideFlags.None) continue;
                if (!t.gameObject.scene.IsValid()) continue;
                found.Add(t.gameObject);
            }

            for (int i = 0; i < found.Count; i++)
            {
                var go = found[i];
                if (go == null) continue;
                bool isCurrent = _overlayRoot != null && ReferenceEquals(_overlayRoot.gameObject, go);
                if (isCurrent)
                {
                    var marker = go.transform.Find("Version_" + OverlayVersion);
                    if (marker != null)
                    {
                        go.SetActive(false);
                        continue;
                    }
                }
                DestroyImmediateSafe(go);
                if (isCurrent)
                    _overlayRoot = null;
            }
        }

        static void DestroyImmediateSafe(GameObject go)
        {
            if (go == null) return;
            UnityEngine.Object.DestroyImmediate(go);
        }

        static Text CreateText(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPos,
            Vector2 size,
            int fontSize,
            Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var text = go.AddComponent<Text>();
            text.font = RuntimeFont.Resolve(Mathf.Max(36, fontSize));
            text.fontSize = fontSize;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        void ShowUi()
        {
            if (_overlayRoot != null)
                _overlayRoot.gameObject.SetActive(true);
        }

        void HideUi()
        {
            StopRing();
            StopVideo();
            if (_titleText != null) _titleText.text = "";
            if (_hintText != null) _hintText.text = "";
            if (_resultText != null)
            {
                _resultText.text = "";
                _resultText.enabled = false;
            }
            if (_overlayRoot != null)
                _overlayRoot.gameObject.SetActive(false);
            ForceHideAllOverlays();
        }

        void Update()
        {
            if (!IsPlaying || _hungUp || _cancelRequested) return;
            // Space / Enter also count as mash clicks on the red hang button.
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                OnHangClicked();
            }
        }

        static List<CanvasGroup> PushHudBlock()
        {
            var touched = new List<CanvasGroup>();
            var demo = GameObject.Find("DemoUIRoot");
            if (demo != null)
            {
                var cg = demo.GetComponent<CanvasGroup>();
                if (cg == null) cg = demo.AddComponent<CanvasGroup>();
                cg.alpha = 0.55f;
                cg.blocksRaycasts = false;
                cg.interactable = false;
                touched.Add(cg);
            }

            if (GameScreenCanvas.ControlsLayer != null)
            {
                var cg = GameScreenCanvas.ControlsLayer.GetComponent<CanvasGroup>();
                if (cg == null) cg = GameScreenCanvas.ControlsLayer.gameObject.AddComponent<CanvasGroup>();
                cg.blocksRaycasts = false;
                cg.interactable = false;
                if (!touched.Contains(cg)) touched.Add(cg);
            }

            return touched;
        }

        static void PopHudBlock(List<CanvasGroup> touched)
        {
            if (touched == null) return;
            for (int i = 0; i < touched.Count; i++)
            {
                var cg = touched[i];
                if (cg == null) continue;
                cg.alpha = 1f;
                cg.blocksRaycasts = true;
                cg.interactable = true;
            }
        }

        public float EmojiSeconds => emojiSeconds;

        void OnDisable()
        {
            StopRing();
            StopVideo();
        }

        void OnDestroy()
        {
            StopRing();
            StopVideo();
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
                _rt = null;
            }
            if (_overlayRoot != null)
            {
                Destroy(_overlayRoot.gameObject);
                _overlayRoot = null;
            }
            if (_videoHost != null)
            {
                Destroy(_videoHost);
                _videoHost = null;
            }
            _videoPlayer = null;
        }
    }
}
