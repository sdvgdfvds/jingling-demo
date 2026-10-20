using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace NexusAnima
{
    /// <summary>
    /// Plays Xinxiang cast presentation: camera focus, chroma-keyed video (first segment only),
    /// full audio, timed gameplay resolve + VFX.
    /// </summary>
    public class XinxiangCastDirector : MonoBehaviour
    {
        BattleCameraFocus _cameraFocus;
        RectTransform _overlayRoot;
        Canvas _overlayCanvas;
        CanvasGroup _canvasGroup;
        RawImage _videoImage;
        RawImage _videoImageB;
        RawImage _backdrop;
        VideoPlayer _videoPlayer;
        VideoPlayer _videoPlayerB;
        GameObject _videoHost;
        GameObject _videoHostB;
        AudioSource _audioSource;
        AudioSource _audioSourceB;
        RenderTexture _rt;
        RenderTexture _rtB;
        Material _chromaMat;
        Material _plainUiMat;
        Coroutine _routine;
        bool _hitApplied;
        bool _useChroma;
        bool _splitLayout;

        // Combo QTE (星辉庇护): shrinking-ring timing over split video.
        RectTransform _qteRoot;
        Image _qteOuter;
        Image _qteMoving;
        Image _qtePerfectZone;
        Image _qteCenter;
        Image _qteFlash;
        Image _qteBadge;
        Image _qtePromptChip;
        Text _qteResult;
        XinxiangQteGrade _splitQteGrade = XinxiangQteGrade.Good;
        const float QteMovingStartScale = 1.32f;
        const float QteMovingPerfectScale = 0.69f; // 110/160 authored radii

        public bool IsPlaying { get; private set; }

        /// <summary>
        /// Concert BattleDemo: close-up after the skill resolves.
        /// Ancient China keeps the old during-cast FocusOn / Release.
        /// </summary>
        static bool ConcertPostCastCloseup => !ArenaEnvironment.IsLegacyArchiveScene();

        void CaptureHomeAndMaybeStartFocus(SpiritUnit target)
        {
            _cameraFocus?.CaptureHome();
            if (!ConcertPostCastCloseup && target != null)
                _cameraFocus?.FocusOn(target.transform, 0.28f);
        }

        /// <summary>
        /// Ult video/overlay is done — combat should resume even if the camera is still closing in.
        /// </summary>
        static void ReleaseCombatHold()
        {
            var battle = Object.FindAnyObjectByType<BattleManager>();
            battle?.SetCombatFrozen(false);
        }

        IEnumerator FinishCastCamera(SpiritUnit target, Vector3 lookFallback, float ancientRelease)
        {
            ReleaseCombatHold();
            if (ConcertPostCastCloseup)
            {
                Transform t = null;
                try
                {
                    if (target != null)
                        t = target.transform;
                }
                catch
                {
                    t = null;
                }
                if (_cameraFocus != null)
                    yield return StartCoroutine(
                        _cameraFocus.CloseupThenHome(t, lookFallback, 0.32f, 0.65f, 0.4f));
                yield break;
            }

            _cameraFocus?.Release(ancientRelease);
        }

        public void EnsureBuilt(BattleCameraFocus cameraFocus)
        {
            _cameraFocus = cameraFocus;

            // Rebuild if missing, or if a broken root-canvas / tiny-video build is still around.
            bool brokenLayout = false;
            if (_videoImage != null)
            {
                var r = _videoImage.rectTransform.rect;
                brokenLayout = r.width < 32f || r.height < 32f;
            }
            bool needsRebuild = _overlayRoot == null
                || _videoPlayer == null
                || _videoImage == null
                || _rt == null
                || !_rt.IsCreated()
                || _videoHost == null
                || brokenLayout
                || (_overlayRoot != null && _overlayRoot.parent == null); // orphan root canvas from prior build

            if (!needsRebuild)
            {
                if (!_overlayRoot.gameObject.activeSelf)
                    _overlayRoot.gameObject.SetActive(true);
                if (!_videoHost.activeSelf)
                    _videoHost.SetActive(true);
                EnsureRenderTexture(forceRecreate: false);
                if (_videoPlayer != null)
                {
                    _videoPlayer.aspectRatio = VideoAspectRatio.Stretch;
                    _videoPlayer.targetTexture = _rt;
                }
                if (_videoImage != null)
                    _videoImage.texture = _rt;
                ForceVideoFullscreen();
                _overlayRoot.SetAsLastSibling();
                EnsureSplitResources();
                return;
            }

            TearDownOverlay();

            GameScreenCanvas.Ensure();

            // Nested under GameScreenCanvas (proven path). Own Canvas for sorting above HUD.
            var go = new GameObject("XinxiangVideoOverlay", typeof(RectTransform));
            go.transform.SetParent(GameScreenCanvas.Root, false);
            _overlayRoot = GameScreenCanvas.StretchFull(go.GetComponent<RectTransform>());
            _overlayRoot.SetAsLastSibling();

            _overlayCanvas = go.AddComponent<Canvas>();
            _overlayCanvas.overrideSorting = true;
            _overlayCanvas.sortingOrder = 800;
            if (go.GetComponent<GraphicRaycaster>() == null)
                go.AddComponent<GraphicRaycaster>();

            _canvasGroup = go.AddComponent<CanvasGroup>();
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable = false;
            _canvasGroup.alpha = 0f;

            var bdGo = new GameObject("Backdrop", typeof(RectTransform));
            bdGo.transform.SetParent(_overlayRoot, false);
            GameScreenCanvas.StretchFull(bdGo.GetComponent<RectTransform>());
            _backdrop = bdGo.AddComponent<RawImage>();
            _backdrop.texture = Texture2D.whiteTexture;
            // Near-opaque cinematic dim so the cast reads as fullscreen.
            _backdrop.color = new Color(0f, 0f, 0f, 0.88f);
            _backdrop.raycastTarget = false;

            var imgGo = new GameObject("Video", typeof(RectTransform));
            imgGo.transform.SetParent(_overlayRoot, false);
            GameScreenCanvas.StretchFull(imgGo.GetComponent<RectTransform>());
            _videoImage = imgGo.AddComponent<RawImage>();
            _videoImage.color = Color.white;
            _videoImage.raycastTarget = false;
            _videoImage.uvRect = new Rect(0f, 0f, 1f, 1f);

            EnsureRenderTexture(forceRecreate: true);
            _videoImage.texture = _rt;

            // Plain UI material first — chroma is applied only after the first frame arrives,
            // otherwise a black empty RT is fully keyed to transparent (invisible "animation").
            _plainUiMat = new Material(Shader.Find("UI/Default"));
            _videoImage.material = _plainUiMat;
            _useChroma = false;

            var shader = Shader.Find("NexusAnima/XinxiangChromaKey");
            if (shader != null && shader.isSupported)
            {
                _chromaMat = new Material(shader);
                _chromaMat.SetColor("_KeyColor", Color.black);
                // Conservative key: only near-pure black backdrop, keep dark clothing.
                _chromaMat.SetFloat("_Threshold", 0.035f);
                _chromaMat.SetFloat("_Smoothness", 0.08f);
                _chromaMat.SetColor("_Color", Color.white);
            }
            else
            {
                Debug.LogWarning("[Xinxiang] ChromaKey shader missing — video will play without keying.");
            }

            // VideoPlayer must live on an always-active non-UI host (more reliable than Canvas GO).
            _videoHost = new GameObject("XinxiangVideoHost");
            _videoHost.transform.SetParent(transform, false);

            _audioSource = _videoHost.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f;
            _audioSource.loop = false;
            GameAudio.Ensure()?.ConfigureVoiceSource(_audioSource);

            _videoPlayer = _videoHost.AddComponent<VideoPlayer>();
            _videoPlayer.playOnAwake = false;
            _videoPlayer.isLooping = false;
            _videoPlayer.waitForFirstFrame = true;
            _videoPlayer.skipOnDrop = true;
            _videoPlayer.renderMode = VideoRenderMode.RenderTexture;
            // Stretch into the RT so letterboxed source frames don't leave black bars in-texture.
            _videoPlayer.aspectRatio = VideoAspectRatio.Stretch;
            _videoPlayer.targetTexture = _rt;
            _videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
            try
            {
                _videoPlayer.controlledAudioTrackCount = 1;
                _videoPlayer.EnableAudioTrack(0, true);
                _videoPlayer.SetTargetAudioSource(0, _audioSource);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Xinxiang] Audio track setup skipped: " + e.Message);
            }

            SetOverlayVisible(false);
            EnsureSplitResources();
            Debug.Log("[Xinxiang] Overlay + VideoHost built.");
        }

        void EnsureSplitResources()
        {
            if (_overlayRoot == null)
                return;

            if (_plainUiMat == null)
                _plainUiMat = new Material(Shader.Find("UI/Default"));

            if (_videoImageB == null)
            {
                var imgGo = new GameObject("VideoB", typeof(RectTransform));
                imgGo.transform.SetParent(_overlayRoot, false);
                _videoImageB = imgGo.AddComponent<RawImage>();
                _videoImageB.color = Color.white;
                _videoImageB.raycastTarget = false;
                _videoImageB.enabled = false;
                _videoImageB.uvRect = new Rect(0f, 0f, 1f, 1f);
                _videoImageB.material = _plainUiMat;
            }

            EnsureRenderTextureB(forceRecreate: false);
            if (_videoImageB != null)
                _videoImageB.texture = _rtB;

            if (_videoHostB == null)
            {
                _videoHostB = new GameObject("XinxiangVideoHostB");
                _videoHostB.transform.SetParent(transform, false);

                _audioSourceB = _videoHostB.AddComponent<AudioSource>();
                _audioSourceB.playOnAwake = false;
                _audioSourceB.mute = true;
                _audioSourceB.volume = 0f;

                _videoPlayerB = _videoHostB.AddComponent<VideoPlayer>();
                _videoPlayerB.playOnAwake = false;
                _videoPlayerB.isLooping = false;
                _videoPlayerB.waitForFirstFrame = true;
                _videoPlayerB.skipOnDrop = true;
                _videoPlayerB.renderMode = VideoRenderMode.RenderTexture;
                _videoPlayerB.aspectRatio = VideoAspectRatio.Stretch;
                _videoPlayerB.targetTexture = _rtB;
                _videoPlayerB.audioOutputMode = VideoAudioOutputMode.None;
            }
            else if (_videoPlayerB != null)
            {
                _videoPlayerB.targetTexture = _rtB;
            }
        }

        void TearDownOverlay()
        {
            if (_overlayRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(_overlayRoot.gameObject);
                _overlayRoot = null;
            }
            // Also kill any stray overlay left by older builds (nested under GameScreenCanvas).
            var stray = GameObject.Find("XinxiangVideoOverlay");
            if (stray != null)
                UnityEngine.Object.DestroyImmediate(stray);

            _overlayCanvas = null;
            _canvasGroup = null;
            _videoImage = null;
            _videoImageB = null;
            _backdrop = null;
            _qteRoot = null;
            _qteOuter = null;
            _qteMoving = null;
            _qtePerfectZone = null;
            _qteCenter = null;
            _qteFlash = null;
            _qteBadge = null;
            _qtePromptChip = null;
            _qteResult = null;

            if (_videoHost != null)
            {
                UnityEngine.Object.DestroyImmediate(_videoHost);
                _videoHost = null;
            }
            _videoPlayer = null;
            _audioSource = null;

            if (_videoHostB != null)
            {
                UnityEngine.Object.DestroyImmediate(_videoHostB);
                _videoHostB = null;
            }
            _videoPlayerB = null;
            _audioSourceB = null;

            if (_rt != null)
            {
                _rt.Release();
                UnityEngine.Object.DestroyImmediate(_rt);
                _rt = null;
            }
            if (_rtB != null)
            {
                _rtB.Release();
                UnityEngine.Object.DestroyImmediate(_rtB);
                _rtB = null;
            }
        }

        public void Play(XinxiangSkillDefinition def, SpiritUnit target, Action<XinxiangSkillDefinition, SpiritUnit, bool> onFinished)
        {
            if (def == null || target == null)
            {
                onFinished?.Invoke(def, target, false);
                return;
            }

            if (_routine != null)
                StopCoroutine(_routine);
            _splitLayout = false;
            _routine = StartCoroutine(PlayRoutine(def, target, onFinished));
        }

        public void PlaySplit(
            XinxiangSkillDefinition leftDef,
            XinxiangSkillDefinition rightDef,
            SpiritUnit target,
            Action<XinxiangSkillDefinition, SpiritUnit, bool> onFinished)
        {
            if (leftDef == null || rightDef == null || target == null)
            {
                onFinished?.Invoke(leftDef, target, false);
                return;
            }

            if (_routine != null)
                StopCoroutine(_routine);
            _splitLayout = true;
            _routine = StartCoroutine(PlaySplitRoutine(leftDef, rightDef, target, onFinished));
        }

        IEnumerator PlaySplitRoutine(
            XinxiangSkillDefinition leftDef,
            XinxiangSkillDefinition rightDef,
            SpiritUnit target,
            Action<XinxiangSkillDefinition, SpiritUnit, bool> onFinished)
        {
            IsPlaying = true;
            _hitApplied = false;
            _splitLayout = true;
            XinxiangVfxLibrary.CleanupStale();
            EnsureBuilt(_cameraFocus);
            EnsureSplitResources();
            EnsureRenderTexture(forceRecreate: false);
            EnsureRenderTextureB(forceRecreate: false);

            if (_videoImage != null)
                _videoImage.texture = _rt;
            if (_videoImageB != null)
                _videoImageB.texture = _rtB;
            if (_videoPlayer != null)
                _videoPlayer.targetTexture = _rt;
            if (_videoPlayerB != null)
                _videoPlayerB.targetTexture = _rtB;

            // Keep cast overlay + dual skill art OFF during rhythm — player stays on the battle scene.
            if (_videoImage != null) _videoImage.enabled = false;
            if (_videoImageB != null) _videoImageB.enabled = false;
            if (_backdrop != null) _backdrop.enabled = false;
            SetOverlayVisible(false);
            HideQteUi();

            Vector3 hitPos = XinxiangVfxLibrary.AnchorOn(target);
            Vector2Int originCell = target.Cell;
            BoardGrid board = target.Board;

            // Capture home but do NOT zoom yet — rhythm focuses on the live arena.
            CaptureHomeAndMaybeStartFocus(target);

            // ---------------------------------------------------------------
            // Phase 1: Rhythm on battle scene — notes locked to Battle BGM beats.
            // Phase 2: only after Perfect/Good → play dual skill videos (prepped in parallel).
            // ---------------------------------------------------------------
            if (GameAudio.I != null && GameAudio.I.IsBgmPaused)
                GameAudio.I.ResumeBgm();

            bool leftOk = false;
            bool rightOk = false;
            bool leftDone = false;
            bool rightDone = false;
            StartCoroutine(PrepareVideo(_videoPlayer, leftDef, (ok, _) =>
            {
                leftOk = ok;
                leftDone = true;
            }));
            StartCoroutine(PrepareVideo(_videoPlayerB, rightDef, (ok, _) =>
            {
                rightOk = ok;
                rightDone = true;
            }));

            _splitQteGrade = XinxiangQteGrade.Miss;
            bool rhythmDone = false;
            yield return RhythmFormationDemo.Run(GameScreenCanvas.PopupLayer, grade =>
            {
                _splitQteGrade = grade;
                rhythmDone = true;
            });
            if (!rhythmDone)
                _splitQteGrade = XinxiangQteGrade.Miss;

            bool qteSuccess = _splitQteGrade != XinxiangQteGrade.Miss;

            if (!qteSuccess)
            {
                // Miss: weak combo, stay on scene, no dual-pane art — still show VFX.
                XinxiangSkillController.ApplyHealBuffCombo(
                    leftDef, rightDef, target, board, originCell,
                    spawnVfx: false, qteGrade: _splitQteGrade);
                XinxiangVfxLibrary.SpawnOn(leftDef, target, hitPos, cleanupFirst: true);
                XinxiangVfxLibrary.SpawnOn(rightDef, target, hitPos, cleanupFirst: false);
                _hitApplied = true;
                yield return new WaitForSecondsRealtime(0.45f);
                HideQteUi();
                SetOverlayVisible(false);
                yield return StartCoroutine(FinishCastCamera(target, hitPos, 0.2f));
                IsPlaying = false;
                onFinished?.Invoke(leftDef, target, true);
                CleanupPlayback();
                _routine = null;
                yield break;
            }

            // --- Success: now play dual skill animation ---
            // Concert: keep home framing until the skill finishes, then close-up.
            yield return new WaitForSecondsRealtime(0.08f);
            HideQteUi();
            if (!ConcertPostCastCloseup)
                _cameraFocus?.FocusOn(target.transform, 0.28f);

            if (_overlayRoot != null && !_overlayRoot.gameObject.activeSelf)
                _overlayRoot.gameObject.SetActive(true);
            if (_videoHost != null && !_videoHost.activeSelf)
                _videoHost.SetActive(true);
            if (_videoHostB != null && !_videoHostB.activeSelf)
                _videoHostB.SetActive(true);

            if (_videoImage != null)
                _videoImage.texture = _rt;
            if (_videoImageB != null)
                _videoImageB.texture = _rtB;
            if (_videoPlayer != null)
                _videoPlayer.targetTexture = _rt;
            if (_videoPlayerB != null)
                _videoPlayerB.targetTexture = _rtB;

            float prepWait = 8f;
            while ((!leftDone || !rightDone) && prepWait > 0f)
            {
                prepWait -= Time.unscaledDeltaTime;
                yield return null;
            }

            bool hasVideo = leftOk || rightOk;
            if (!hasVideo)
                Debug.LogWarning("[Xinxiang] Split video prepare failed — VFX only after rhythm success.");

            if (!hasVideo)
            {
                XinxiangSkillController.ApplyHealBuffCombo(
                    leftDef, rightDef, target, board, originCell,
                    spawnVfx: false, qteGrade: _splitQteGrade);
                XinxiangVfxLibrary.SpawnOn(leftDef, target, hitPos, cleanupFirst: true);
                XinxiangVfxLibrary.SpawnOn(rightDef, target, hitPos, cleanupFirst: false);
                _hitApplied = true;
                yield return new WaitForSecondsRealtime(0.55f);
                HideQteUi();
                if (_backdrop != null) _backdrop.enabled = false;
                SetOverlayVisible(false);
                yield return StartCoroutine(FinishCastCamera(target, hitPos, 0.35f));
                IsPlaying = false;
                onFinished?.Invoke(leftDef, target, true);
                CleanupPlayback();
                _routine = null;
                yield break;
            }

            ApplyPlainTo(_videoImage);
            ApplyPlainTo(_videoImageB);
            if (_videoImage != null)
            {
                _videoImage.enabled = true;
                _videoImage.texture = _rt;
            }
            if (_videoImageB != null)
            {
                _videoImageB.enabled = true;
                _videoImageB.texture = _rtB;
            }
            if (_backdrop != null)
            {
                _backdrop.enabled = true;
                _backdrop.color = new Color(0f, 0f, 0f, 0.55f);
            }

            LayoutSplitPanes();
            Canvas.ForceUpdateCanvases();
            SetOverlayVisible(true);
            LayoutSplitPanes();
            Canvas.ForceUpdateCanvases();

            if (_audioSource != null)
                GameAudio.Ensure()?.ConfigureVoiceSource(_audioSource);

            if (leftOk && _videoPlayer != null)
                _videoPlayer.Play();
            if (rightOk && _videoPlayerB != null)
                _videoPlayerB.Play();

            float arm = 2.5f;
            while (arm > 0f)
            {
                bool leftFrame = _videoPlayer != null && (_videoPlayer.frame >= 0 || _videoPlayer.isPlaying);
                bool rightFrame = _videoPlayerB != null && (_videoPlayerB.frame >= 0 || _videoPlayerB.isPlaying);
                if ((leftOk && leftFrame) || (rightOk && rightFrame))
                    break;
                arm -= Time.unscaledDeltaTime;
                yield return null;
            }

            yield return null;
            yield return null;
            LayoutSplitPanes();
            Canvas.ForceUpdateCanvases();
            AutoCropPane(_videoImage, _rt);
            AutoCropPane(_videoImageB, _rtB);
            ApplyChromaTo(_videoImage);
            ApplyChromaTo(_videoImageB);

            float visualEnd = Mathf.Max(
                Mathf.Max(0.35f, leftDef.VisualEndTime),
                Mathf.Max(0.35f, rightDef.VisualEndTime));
            float hitAt = Mathf.Max(
                visualEnd * Mathf.Clamp01(leftDef.HitNormalizedTime),
                visualEnd * Mathf.Clamp01(rightDef.HitNormalizedTime));
            hitAt = Mathf.Clamp(hitAt, 0.1f, visualEnd);

            float audioEnd = Mathf.Max(EstimateClipLength(leftDef), EstimateClipLength(rightDef));
            if (_videoPlayer != null && leftOk && _videoPlayer.length > 0.1)
                audioEnd = Mathf.Max(audioEnd, (float)_videoPlayer.length);
            if (_videoPlayerB != null && rightOk && _videoPlayerB.length > 0.1)
                audioEnd = Mathf.Max(audioEnd, (float)_videoPlayerB.length);

            float t = 0f;
            bool visualHidden = false;
            bool vfxSpawned = false;
            bool cameraReleased = false;
            bool castFinished = false;
            float cameraHoldAfterVisual = 0.12f;
            float castUnlockAt = visualEnd + cameraHoldAfterVisual;

            while (t < audioEnd)
            {
                t += Time.unscaledDeltaTime;

                if (target != null)
                {
                    try { hitPos = XinxiangVfxLibrary.AnchorOn(target); }
                    catch { }
                }

                if (!_hitApplied && t >= hitAt)
                {
                    XinxiangSkillController.ApplyHealBuffCombo(
                        leftDef, rightDef, target, board, originCell,
                        spawnVfx: false, qteGrade: _splitQteGrade);
                    _hitApplied = true;
                }

                if (!visualHidden && t >= visualEnd)
                {
                    visualHidden = true;
                    if (_videoImage != null)
                        _videoImage.enabled = false;
                    if (_videoImageB != null)
                        _videoImageB.enabled = false;
                    if (_backdrop != null)
                        _backdrop.enabled = false;
                    HideQteUi();
                    SetOverlayVisible(false);
                    ReleaseCombatHold();

                    if (!vfxSpawned)
                    {
                        XinxiangVfxLibrary.SpawnOn(leftDef, target, hitPos, cleanupFirst: true);
                        XinxiangVfxLibrary.SpawnOn(rightDef, target, hitPos, cleanupFirst: false);
                        vfxSpawned = true;
                    }
                }

                if (!cameraReleased && visualHidden
                    && (ConcertPostCastCloseup || t >= castUnlockAt))
                {
                    cameraReleased = true;
                    yield return StartCoroutine(FinishCastCamera(target, hitPos, 0.28f));
                }

                if (!castFinished && t >= castUnlockAt && cameraReleased)
                {
                    castFinished = true;
                    if (!_hitApplied)
                    {
                        XinxiangSkillController.ApplyHealBuffCombo(
                            leftDef, rightDef, target, board, originCell,
                            spawnVfx: false, qteGrade: _splitQteGrade);
                        _hitApplied = true;
                    }
                    if (!vfxSpawned)
                    {
                        XinxiangVfxLibrary.SpawnOn(leftDef, target, hitPos, cleanupFirst: true);
                        XinxiangVfxLibrary.SpawnOn(rightDef, target, hitPos, cleanupFirst: false);
                        vfxSpawned = true;
                    }
                    IsPlaying = false;
                    onFinished?.Invoke(leftDef, target, _hitApplied);
                }

                yield return null;
            }

            if (!castFinished)
            {
                if (!_hitApplied)
                {
                    XinxiangSkillController.ApplyHealBuffCombo(
                        leftDef, rightDef, target, board, originCell,
                        spawnVfx: false, qteGrade: _splitQteGrade);
                    _hitApplied = true;
                }
                if (!vfxSpawned)
                {
                    XinxiangVfxLibrary.SpawnOn(leftDef, target, hitPos, cleanupFirst: true);
                    XinxiangVfxLibrary.SpawnOn(rightDef, target, hitPos, cleanupFirst: false);
                }
                if (!cameraReleased)
                    yield return StartCoroutine(FinishCastCamera(target, hitPos, 0.25f));
                IsPlaying = false;
                onFinished?.Invoke(leftDef, target, _hitApplied);
            }
            else if (!cameraReleased)
            {
                yield return StartCoroutine(FinishCastCamera(target, hitPos, 0.25f));
            }

            // Safety: never leave BGM paused if cast ends early.
            if (GameAudio.I != null && GameAudio.I.IsBgmPaused)
                GameAudio.I.ResumeBgm();

            HideQteUi();
            CleanupPlayback();
            _routine = null;
        }

        IEnumerator PlayRoutine(
            XinxiangSkillDefinition def,
            SpiritUnit target,
            Action<XinxiangSkillDefinition, SpiritUnit, bool> onFinished)
        {
            IsPlaying = true;
            _hitApplied = false;
            _splitLayout = false;
            XinxiangVfxLibrary.CleanupStale();
            EnsureBuilt(_cameraFocus);
            EnsureRenderTexture(forceRecreate: false);
            if (_videoPlayer != null)
            {
                _videoPlayer.aspectRatio = VideoAspectRatio.Stretch;
                _videoPlayer.targetTexture = _rt;
            }
            if (_videoImage != null)
                _videoImage.texture = _rt;

            if (_overlayRoot != null && !_overlayRoot.gameObject.activeSelf)
                _overlayRoot.gameObject.SetActive(true);
            if (_videoHost != null && !_videoHost.activeSelf)
                _videoHost.SetActive(true);

            // Cache hit point / cell up front — IceDamage can kill the unit before visualEnd,
            // and AoE still needs the origin cell for remaining units.
            Vector3 hitPos = XinxiangVfxLibrary.AnchorOn(target);
            Vector2Int originCell = target.Cell;
            BoardGrid board = target.Board;

            CaptureHomeAndMaybeStartFocus(target);

            bool prepared = false;
            string failReason = null;
            yield return StartCoroutine(PrepareVideo(def, (ok, reason) =>
            {
                prepared = ok;
                failReason = reason;
            }));

            if (!prepared)
            {
                Debug.LogWarning($"[Xinxiang] Video prepare failed ({failReason}). Applying effect without animation.");
                XinxiangSkillController.ApplyEffect(def, target, board, originCell, spawnVfx: false);
                XinxiangVfxLibrary.SpawnOn(def, target, hitPos);
                _hitApplied = true;
                yield return StartCoroutine(FinishCastCamera(target, hitPos, 0.35f));
                IsPlaying = false;
                onFinished?.Invoke(def, target, true);
                _routine = null;
                yield break;
            }

            // Reset to plain material until a real frame lands (avoids keyed-black invisible RT).
            ApplyPlainMaterial();
            if (_videoImage != null)
            {
                _videoImage.enabled = true;
                _videoImage.texture = _rt;
            }
            if (_backdrop != null)
                _backdrop.enabled = true;

            SetOverlayVisible(true);
            if (_audioSource != null)
                GameAudio.Ensure()?.ConfigureVoiceSource(_audioSource);
            _videoPlayer.Play();

            // Wait for first decoded frame, then enable chroma.
            float arm = 2.5f;
            bool gotFrame = false;
            while (arm > 0f && _videoPlayer != null)
            {
                if (_videoPlayer.texture != null || _videoPlayer.frame >= 0)
                {
                    // frame starts at -1; >=0 means at least one frame presented
                    if (_videoPlayer.frame >= 0 || _videoPlayer.isPlaying)
                    {
                        gotFrame = true;
                        break;
                    }
                }
                arm -= Time.unscaledDeltaTime;
                yield return null;
            }

            if (gotFrame)
            {
                // Give a couple frames for RT to fill, then crop black margins + key.
                yield return null;
                yield return null;
                ForceVideoFullscreen();
                AutoCropContentUv();
                ApplyChromaMaterial();
                Debug.Log($"[Xinxiang] Playing '{def.Id}' frame={_videoPlayer.frame} chroma={_useChroma} uv={_videoImage.uvRect} rect={_videoImage.rectTransform.rect.size}");
            }
            else
            {
                Debug.LogWarning("[Xinxiang] No video frame yet — showing unkeyed RT.");
                ForceVideoFullscreen();
                ApplyPlainMaterial();
            }

            float visualEnd = Mathf.Max(0.35f, def.VisualEndTime);
            float hitAt = Mathf.Clamp(visualEnd * Mathf.Clamp01(def.HitNormalizedTime), 0.1f, visualEnd);
            float audioEnd = EstimateClipLength(def);
            float t = 0f;
            bool visualHidden = false;
            bool vfxSpawned = false;
            bool cameraReleased = false;
            bool castFinished = false;
            // Video segment + full audio stay original length.
            // Only the camera linger after visualEnd is shortened; combat unlocks then,
            // while VideoPlayer keeps running so the VO/SFX is not cut.
            float cameraHoldAfterVisual = def.Id == XinxiangSkillId.Heal ? 0.1f : 0.18f;
            float castUnlockAt = visualEnd + cameraHoldAfterVisual;

            while (t < audioEnd)
            {
                t += Time.unscaledDeltaTime;

                // Refresh hit position while the target is still alive / present.
                if (target != null)
                {
                    try
                    {
                        hitPos = XinxiangVfxLibrary.AnchorOn(target);
                    }
                    catch
                    {
                        // Target may have been destroyed after lethal IceDamage.
                    }
                }

                // Gameplay resolves mid-clip; VFX waits until overlay clears.
                // AoE uses the cached cell so splash still lands if the focus unit already died.
                if (!_hitApplied && t >= hitAt)
                {
                    XinxiangSkillController.ApplyEffect(def, target, board, originCell, spawnVfx: false);
                    _hitApplied = true;
                    if (target != null)
                    {
                        try { hitPos = XinxiangVfxLibrary.AnchorOn(target); }
                        catch { }
                    }
                }

                if (!visualHidden && t >= visualEnd)
                {
                    visualHidden = true;
                    if (_videoImage != null)
                        _videoImage.enabled = false;
                    if (_backdrop != null)
                        _backdrop.enabled = false;
                    SetOverlayVisible(false);
                    ReleaseCombatHold();

                    if (!vfxSpawned)
                    {
                        XinxiangVfxLibrary.SpawnOn(def, target, hitPos);
                        vfxSpawned = true;
                    }
                }

                // Concert: close-up after overlay/VFX. Ancient China: restore after visual beat.
                if (!cameraReleased && visualHidden
                    && (ConcertPostCastCloseup || t >= castUnlockAt))
                {
                    cameraReleased = true;
                    yield return StartCoroutine(FinishCastCamera(target, hitPos, 0.28f));
                }

                // Unlock combat / cooldown after visual; do NOT CleanupPlayback yet
                // or the remaining audio track would be chopped off.
                if (!castFinished && t >= castUnlockAt && cameraReleased)
                {
                    castFinished = true;
                    if (!_hitApplied)
                    {
                        XinxiangSkillController.ApplyEffect(def, target, board, originCell, spawnVfx: false);
                        _hitApplied = true;
                    }
                    if (!vfxSpawned)
                    {
                        XinxiangVfxLibrary.SpawnOn(def, target, hitPos);
                        vfxSpawned = true;
                    }
                    IsPlaying = false;
                    onFinished?.Invoke(def, target, _hitApplied);
                }

                yield return null;
            }

            if (!castFinished)
            {
                if (!_hitApplied)
                {
                    XinxiangSkillController.ApplyEffect(def, target, board, originCell, spawnVfx: false);
                    _hitApplied = true;
                }
                if (!vfxSpawned)
                {
                    XinxiangVfxLibrary.SpawnOn(def, target, hitPos);
                    vfxSpawned = true;
                }
                if (!cameraReleased)
                    yield return StartCoroutine(FinishCastCamera(target, hitPos, 0.25f));
                IsPlaying = false;
                onFinished?.Invoke(def, target, _hitApplied);
            }
            else if (!cameraReleased)
            {
                yield return StartCoroutine(FinishCastCamera(target, hitPos, 0.25f));
            }

            // Stop player only after the full audio has been allowed to finish.
            CleanupPlayback();
            _routine = null;
        }

        IEnumerator PrepareVideo(XinxiangSkillDefinition def, Action<bool, string> done)
        {
            yield return PrepareVideo(_videoPlayer, def, done);
        }

        IEnumerator PrepareVideo(VideoPlayer player, XinxiangSkillDefinition def, Action<bool, string> done)
        {
            if (player == null)
            {
                done(false, "no VideoPlayer");
                yield break;
            }

            if (player.gameObject != null && !player.gameObject.activeSelf)
                player.gameObject.SetActive(true);
            if (_overlayRoot != null && !_overlayRoot.gameObject.activeSelf)
                _overlayRoot.gameObject.SetActive(true);

            player.Stop();
            if (_backdrop != null && !_splitLayout)
                _backdrop.enabled = true;

            var clip = def != null ? def.TryLoadVideoClip() : null;
            if (clip != null)
            {
                player.source = VideoSource.VideoClip;
                player.clip = clip;
                player.url = string.Empty;
                Debug.Log($"[Xinxiang] Prepare clip '{clip.name}' length={clip.length:F2}s on {player.name}");
            }
            else
            {
                string url = def != null ? def.ResolveVideoUrl() : null;
                if (string.IsNullOrEmpty(url))
                {
                    done(false, "missing clip/url");
                    yield break;
                }
                player.source = VideoSource.Url;
                player.url = url;
                player.clip = null;
                Debug.Log($"[Xinxiang] Prepare url '{url}' on {player.name}");
            }

            bool ready = false;
            bool error = false;
            string errMsg = null;
            void OnReady(VideoPlayer _) => ready = true;
            void OnError(VideoPlayer _, string msg)
            {
                error = true;
                errMsg = msg;
            }

            player.prepareCompleted += OnReady;
            player.errorReceived += OnError;
            player.Prepare();

            float timeout = 10f;
            while (!ready && !error && timeout > 0f)
            {
                timeout -= Time.unscaledDeltaTime;
                yield return null;
            }

            player.prepareCompleted -= OnReady;
            player.errorReceived -= OnError;

            if (error)
                done(false, errMsg ?? "VideoPlayer error");
            else if (!ready)
                done(false, "prepare timeout");
            else
                done(true, null);
        }

        float EstimateClipLength(XinxiangSkillDefinition def)
        {
            if (_videoPlayer != null && _videoPlayer.length > 0.1)
                return (float)_videoPlayer.length;

            switch (def.Id)
            {
                case XinxiangSkillId.Buff: return 3f;
                case XinxiangSkillId.Heal: return 4f;
                default: return 4f;
            }
        }

        static Sprite LoadQteSprite(string name)
        {
            return Resources.Load<Sprite>("UI/Qte/" + name);
        }

        Image CreateQteImage(string goName, Transform parent, Vector2 anchor, Vector2 size, Sprite sprite)
        {
            var go = new GameObject(goName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.sprite = sprite != null ? sprite : GameScreenCanvas.UiWhiteSprite;
            img.preserveAspect = true;
            img.raycastTarget = false;
            img.color = Color.white;
            return img;
        }

        void EnsureQteUi()
        {
            if (_overlayRoot == null)
                return;

            // Rebuild if old bar-style QTE or missing ring art.
            if (_qteRoot != null && (_qteMoving == null || _qteOuter == null))
            {
                Object.Destroy(_qteRoot.gameObject);
                _qteRoot = null;
                _qteOuter = null;
                _qteMoving = null;
                _qtePerfectZone = null;
                _qteCenter = null;
                _qteFlash = null;
                _qteBadge = null;
                _qtePromptChip = null;
                _qteResult = null;
            }

            if (_qteRoot != null)
                return;

            var rootGo = new GameObject("ComboQte", typeof(RectTransform));
            rootGo.transform.SetParent(_overlayRoot, false);
            _qteRoot = GameScreenCanvas.StretchFull(rootGo.GetComponent<RectTransform>());
            _qteRoot.SetAsLastSibling();

            // Lower-center so split character faces stay readable.
            var hubGo = new GameObject("QteHub", typeof(RectTransform));
            hubGo.transform.SetParent(_qteRoot, false);
            var hubRt = hubGo.GetComponent<RectTransform>();
            hubRt.anchorMin = new Vector2(0.5f, 0.22f);
            hubRt.anchorMax = new Vector2(0.5f, 0.22f);
            hubRt.pivot = new Vector2(0.5f, 0.5f);
            hubRt.sizeDelta = new Vector2(320f, 320f);

            Vector2 mid = new Vector2(0.5f, 0.5f);
            Vector2 ringSize = new Vector2(300f, 300f);

            _qteOuter = CreateQteImage("Outer", hubGo.transform, mid, ringSize, LoadQteSprite("QteOuter"));
            _qtePerfectZone = CreateQteImage("PerfectZone", hubGo.transform, mid, ringSize, LoadQteSprite("QtePerfectZone"));
            _qteMoving = CreateQteImage("Moving", hubGo.transform, mid, ringSize, LoadQteSprite("QteMoving"));
            _qteCenter = CreateQteImage("Center", hubGo.transform, mid, ringSize, LoadQteSprite("QteCenter"));
            _qteFlash = CreateQteImage("Flash", hubGo.transform, mid, ringSize, LoadQteSprite("QteFlashPerfect"));
            _qteFlash.enabled = false;

            _qteBadge = CreateQteImage(
                "Badge",
                hubGo.transform,
                mid,
                new Vector2(360f, 90f),
                LoadQteSprite("QteBadgePerfect"));
            _qteBadge.rectTransform.anchoredPosition = new Vector2(0f, 8f);
            _qteBadge.enabled = false;

            _qtePromptChip = CreateQteImage(
                "PromptChip",
                hubGo.transform,
                mid,
                new Vector2(280f, 64f),
                LoadQteSprite("QtePromptChip"));
            _qtePromptChip.rectTransform.anchoredPosition = new Vector2(0f, -150f);

            // Fallback text if badge sprites missing.
            var resultGo = new GameObject("QteResultFallback", typeof(RectTransform));
            resultGo.transform.SetParent(hubGo.transform, false);
            var resultRt = resultGo.GetComponent<RectTransform>();
            resultRt.anchorMin = mid;
            resultRt.anchorMax = mid;
            resultRt.pivot = mid;
            resultRt.sizeDelta = new Vector2(400f, 80f);
            _qteResult = resultGo.AddComponent<Text>();
            _qteResult.font = RuntimeFont.Resolve(56);
            _qteResult.fontSize = 48;
            _qteResult.fontStyle = FontStyle.Bold;
            _qteResult.alignment = TextAnchor.MiddleCenter;
            _qteResult.color = Color.white;
            _qteResult.text = "";
            _qteResult.raycastTarget = false;
            _qteResult.enabled = false;
            var resultOutline = resultGo.AddComponent<Outline>();
            resultOutline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            resultOutline.effectDistance = new Vector2(2f, -2f);

            HideQteUi();
        }

        void ShowQtePrompt(bool show)
        {
            EnsureQteUi();
            if (_qteRoot != null)
                _qteRoot.gameObject.SetActive(true);

            SetQteRingVisible(show);
            if (_qtePromptChip != null)
                _qtePromptChip.enabled = show;
            if (_qteBadge != null)
                _qteBadge.enabled = false;
            if (_qteFlash != null)
                _qteFlash.enabled = false;
            if (_qteResult != null)
            {
                _qteResult.enabled = false;
                _qteResult.text = "";
            }
            if (_qteMoving != null)
                _qteMoving.rectTransform.localScale = Vector3.one * QteMovingStartScale;

            if (show)
                GameAudio.I?.PlayQteOpen();
        }

        void SetQteRingVisible(bool show)
        {
            if (_qteOuter != null) _qteOuter.enabled = show;
            if (_qtePerfectZone != null) _qtePerfectZone.enabled = show;
            if (_qteMoving != null) _qteMoving.enabled = show;
            if (_qteCenter != null) _qteCenter.enabled = show;
        }

        void UpdateQteMeter(float t, float openAt, float hitAt)
        {
            if (_qteMoving == null) return;
            float span = Mathf.Max(0.05f, hitAt - openAt);
            float amount = Mathf.Clamp01((t - openAt) / span);
            float scale = Mathf.Lerp(QteMovingStartScale, QteMovingPerfectScale, amount);
            // Soft pulse near perfect.
            if (amount > 0.78f)
                scale *= 1f + 0.03f * Mathf.Sin(t * 18f);
            _qteMoving.rectTransform.localScale = new Vector3(scale, scale, 1f);

            if (_qtePerfectZone != null)
            {
                float a = amount > 0.78f ? 1f : 0.75f;
                _qtePerfectZone.color = new Color(1f, 1f, 1f, a);
            }
            if (_qteCenter != null)
            {
                float pulse = 0.92f + 0.08f * Mathf.Sin(t * 9f);
                _qteCenter.rectTransform.localScale = Vector3.one * pulse;
            }
        }

        void ShowQteResult(XinxiangQteGrade grade)
        {
            EnsureQteUi();
            if (_qteRoot != null)
                _qteRoot.gameObject.SetActive(true);

            SetQteRingVisible(false);
            if (_qtePromptChip != null)
                _qtePromptChip.enabled = false;

            GameAudio.I?.PlayQteResult(grade);

            string badgeName = "QteBadgeGood";
            string flashName = "QteFlashGood";
            Color fallbackCol = new Color(0.55f, 1f, 0.65f, 1f);
            string fallbackText = "成功";
            switch (grade)
            {
                case XinxiangQteGrade.Perfect:
                    badgeName = "QteBadgePerfect";
                    flashName = "QteFlashPerfect";
                    fallbackCol = new Color(1f, 0.92f, 0.35f, 1f);
                    fallbackText = "完美";
                    break;
                case XinxiangQteGrade.Miss:
                    badgeName = "QteBadgeMiss";
                    flashName = "QteFlashMiss";
                    fallbackCol = new Color(1f, 0.45f, 0.4f, 1f);
                    fallbackText = "失败";
                    break;
            }

            var badgeSpr = LoadQteSprite(badgeName);
            if (_qteBadge != null)
            {
                if (badgeSpr != null)
                {
                    _qteBadge.sprite = badgeSpr;
                    _qteBadge.color = Color.white;
                    _qteBadge.enabled = true;
                    if (_qteResult != null) _qteResult.enabled = false;
                }
                else
                {
                    _qteBadge.enabled = false;
                    if (_qteResult != null)
                    {
                        _qteResult.enabled = true;
                        _qteResult.text = fallbackText;
                        _qteResult.color = fallbackCol;
                    }
                }
            }

            var flashSpr = LoadQteSprite(flashName);
            if (_qteFlash != null)
            {
                if (flashSpr != null)
                    _qteFlash.sprite = flashSpr;
                _qteFlash.color = Color.white;
                _qteFlash.enabled = true;
                _qteFlash.rectTransform.localScale = Vector3.one * 0.85f;
            }
        }

        void HideQtePromptOnly()
        {
            SetQteRingVisible(false);
            if (_qtePromptChip != null) _qtePromptChip.enabled = false;
            // Keep badge / flash until HideQteUi.
        }

        void HideQteUi()
        {
            if (_qteRoot != null)
                _qteRoot.gameObject.SetActive(false);
            SetQteRingVisible(false);
            if (_qtePromptChip != null) _qtePromptChip.enabled = false;
            if (_qteBadge != null) _qteBadge.enabled = false;
            if (_qteFlash != null) _qteFlash.enabled = false;
            if (_qteResult != null)
            {
                _qteResult.enabled = false;
                _qteResult.text = "";
            }
        }

        static bool WasQtePressed()
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.KeypadEnter))
                return true;
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                return true;
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                {
                    if (Input.GetTouch(i).phase == TouchPhase.Began)
                        return true;
                }
            }
            return false;
        }

        static XinxiangQteGrade GradeFromTiming(float t, float hitAt)
        {
            float err = Mathf.Abs(t - hitAt);
            if (err <= 0.08f)
                return XinxiangQteGrade.Perfect;
            if (err <= 0.25f)
                return XinxiangQteGrade.Good;
            return XinxiangQteGrade.Miss;
        }

        void CleanupPlayback()
        {
            // Cast may be interrupted mid-QTE — never leave battle BGM paused.
            if (GameAudio.I != null && GameAudio.I.IsBgmPaused)
                GameAudio.I.ResumeBgm();

            if (_videoPlayer != null)
            {
                if (_videoPlayer.isPlaying)
                    _videoPlayer.Stop();
                _videoPlayer.clip = null;
            }
            if (_videoPlayerB != null)
            {
                if (_videoPlayerB.isPlaying)
                    _videoPlayerB.Stop();
                _videoPlayerB.clip = null;
            }
            if (_audioSource != null && _audioSource.isPlaying)
                _audioSource.Stop();

            _splitLayout = false;
            HideQteUi();
            SetOverlayVisible(false);
            ApplyPlainMaterial();
            if (_videoImage != null)
            {
                _videoImage.enabled = true;
                _videoImage.uvRect = new Rect(0f, 0f, 1f, 1f);
                _videoImage.rectTransform.localScale = Vector3.one;
            }
            if (_videoImageB != null)
            {
                _videoImageB.enabled = false;
                _videoImageB.uvRect = new Rect(0f, 0f, 1f, 1f);
                _videoImageB.rectTransform.localScale = Vector3.one;
            }
            if (_backdrop != null)
            {
                _backdrop.enabled = true;
                _backdrop.color = new Color(0f, 0f, 0f, 0.88f);
            }
            ForceVideoFullscreen();
        }

        void ApplyPlainMaterial()
        {
            _useChroma = false;
            ApplyPlainTo(_videoImage);
            if (_videoImage != null)
                _videoImage.texture = _rt;
        }

        void ApplyPlainTo(RawImage img)
        {
            if (img == null) return;
            if (_plainUiMat == null)
                _plainUiMat = new Material(Shader.Find("UI/Default"));
            img.material = _plainUiMat;
        }

        void ApplyChromaMaterial()
        {
            ApplyChromaTo(_videoImage);
            if (_videoImage != null)
                _videoImage.texture = _rt;
        }

        void ApplyChromaTo(RawImage img)
        {
            if (img == null) return;
            if (_chromaMat != null)
            {
                img.material = _chromaMat;
                _useChroma = true;
            }
            else
            {
                ApplyPlainTo(img);
            }
        }

        void ForceVideoFullscreen()
        {
            // Parent GameScreenCanvas must also be full-bleed (HiDPI / Fixed Resolution changes).
            GameScreenCanvas.Ensure();

            if (_overlayRoot != null)
            {
                GameScreenCanvas.StretchFull(_overlayRoot);
                _overlayRoot.SetAsLastSibling();
            }
            if (_backdrop != null)
                GameScreenCanvas.StretchFull(_backdrop.rectTransform);
            if (_splitLayout)
            {
                LayoutSplitPanes();
                return;
            }

            if (_videoImage != null)
            {
                var rt = _videoImage.rectTransform;
                GameScreenCanvas.StretchFull(rt);
                // Keep scale at 1 — UV cover-crop enlarges content without collapsing the rect.
                rt.localScale = Vector3.one;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
            if (_videoImageB != null)
                _videoImageB.enabled = false;
        }

        void LayoutSplitPanes()
        {
            _splitLayout = true;
            LayoutHalfPane(_videoImage, 0f, 0.5f);
            LayoutHalfPane(_videoImageB, 0.5f, 1f);
        }

        static void LayoutHalfPane(RawImage img, float x0, float x1)
        {
            if (img == null) return;
            var rt = img.rectTransform;
            rt.anchorMin = new Vector2(x0, 0f);
            rt.anchorMax = new Vector2(x1, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            img.enabled = true;
        }

        void EnsureRenderTextureB(bool forceRecreate)
        {
            int w = Mathf.Max(640, Screen.width > 16 ? Screen.width : 1920);
            int h = Mathf.Max(360, Screen.height > 16 ? Screen.height : 1080);
            w &= ~1;
            h &= ~1;

            bool sizeOk = _rtB != null && _rtB.IsCreated() && _rtB.width == w && _rtB.height == h;
            if (!forceRecreate && sizeOk)
                return;

            if (_rtB != null)
            {
                _rtB.Release();
                Object.DestroyImmediate(_rtB);
                _rtB = null;
            }

            _rtB = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "XinxiangVideoRT_B",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            _rtB.Create();

            if (_videoPlayerB != null)
                _videoPlayerB.targetTexture = _rtB;
            if (_videoImageB != null)
                _videoImageB.texture = _rtB;
        }

        /// <summary>
        /// Match RT to the current Game view / screen so the overlay maps 1:1 to the display.
        /// </summary>
        void EnsureRenderTexture(bool forceRecreate)
        {
            int w = Mathf.Max(640, Screen.width > 16 ? Screen.width : 1920);
            int h = Mathf.Max(360, Screen.height > 16 ? Screen.height : 1080);
            // Even dimensions play nicer with most video decoders.
            w &= ~1;
            h &= ~1;

            bool sizeOk = _rt != null && _rt.IsCreated() && _rt.width == w && _rt.height == h;
            if (!forceRecreate && sizeOk)
                return;

            if (_rt != null)
            {
                _rt.Release();
                Object.DestroyImmediate(_rt);
                _rt = null;
            }

            _rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                name = "XinxiangVideoRT",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            _rt.Create();

            if (_videoPlayer != null)
                _videoPlayer.targetTexture = _rt;
            if (_videoImage != null)
                _videoImage.texture = _rt;

            Debug.Log($"[Xinxiang] RT sized to {w}x{h} (Screen={Screen.width}x{Screen.height})");
        }

        void AutoCropPane(RawImage image, RenderTexture sourceRt)
        {
            if (image == null || sourceRt == null || !sourceRt.IsCreated())
                return;

            var prevImg = _videoImage;
            var prevRt = _rt;
            _videoImage = image;
            _rt = sourceRt;
            AutoCropContentUv();
            _videoImage = prevImg;
            _rt = prevRt;
        }

        /// <summary>
        /// Crop UV to non-black content, then width-first cover so left/right fill the
        /// Game view (may crop top/bottom). Never expands UV past content width —
        /// expanding was re-introducing pillarbox bars.
        /// </summary>
        void AutoCropContentUv()
        {
            if (_videoImage == null || _rt == null || !_rt.IsCreated())
                return;

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = _rt;
            int w = _rt.width;
            int h = _rt.height;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            try
            {
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply(false);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Xinxiang] AutoCrop ReadPixels failed: " + e.Message);
                RenderTexture.active = prev;
                Object.Destroy(tex);
                return;
            }
            RenderTexture.active = prev;

            var pixels = tex.GetPixels32();
            Object.Destroy(tex);

            int minX = w, minY = h, maxX = -1, maxY = -1;
            const byte lumThresh = 32;
            const int step = 2;
            for (int y = 0; y < h; y += step)
            {
                int row = y * w;
                for (int x = 0; x < w; x += step)
                {
                    var c = pixels[row + x];
                    int lum = ((int)c.r + c.g + c.b) / 3;
                    if (lum <= lumThresh) continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < minX || maxY < minY)
            {
                _videoImage.uvRect = new Rect(0f, 0f, 1f, 1f);
                if (_videoImage != null)
                    _videoImage.rectTransform.localScale = Vector3.one;
                return;
            }

            // Tight on sides; tiny vertical pad.
            float padX = (maxX - minX + 1) * 0.01f;
            float padY = (maxY - minY + 1) * 0.02f;
            float contentX0 = Mathf.Clamp01((minX - padX) / w);
            float contentY0 = Mathf.Clamp01((minY - padY) / h);
            float contentX1 = Mathf.Clamp01((maxX + 1 + padX) / w);
            float contentY1 = Mathf.Clamp01((maxY + 1 + padY) / h);
            float contentW = Mathf.Max(0.08f, contentX1 - contentX0);
            float contentH = Mathf.Max(0.08f, contentY1 - contentY0);
            float cx = contentX0 + contentW * 0.5f;
            float cy = contentY0 + contentH * 0.5f;

            float paneW = _videoImage.rectTransform.rect.width;
            float paneH = _videoImage.rectTransform.rect.height;
            float screenAspect = paneH > 1f
                ? paneW / paneH
                : (Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f);
            float rtAspect = (float)w / Mathf.Max(1, h);
            float targetUvAspect = screenAspect / Mathf.Max(0.01f, rtAspect);

            // Width-first: UV width = content width (zoomed), height derived for screen aspect.
            // Do NOT expand width past content — that recreates left/right empty bars.
            const float sideZoom = 0.88f;
            float uvW = Mathf.Clamp(contentW * sideZoom, 0.10f, 1f);
            float uvH = uvW / Mathf.Max(0.01f, targetUvAspect);
            if (uvH > 1f)
                uvH = 1f;

            float x0 = cx - uvW * 0.5f;
            float y0 = cy - uvH * 0.5f;
            if (x0 < 0f) x0 = 0f;
            if (y0 < 0f) y0 = 0f;
            if (x0 + uvW > 1f) x0 = 1f - uvW;
            if (y0 + uvH > 1f) y0 = 1f - uvH;
            x0 = Mathf.Clamp01(x0);
            y0 = Mathf.Clamp01(y0);
            uvW = Mathf.Clamp(uvW, 0.10f, 1f - x0);
            uvH = Mathf.Clamp(uvH, 0.10f, 1f - y0);

            _videoImage.uvRect = new Rect(x0, y0, uvW, uvH);

            // Extra X stretch if source content is still too portrait vs the Game view.
            var imgRt = _videoImage.rectTransform;
            if (!_splitLayout)
                GameScreenCanvas.StretchFull(imgRt);
            float contentScreenAspect = (contentW * w) / Mathf.Max(1f, contentH * (float)h);
            float xScale = 1f;
            if (!_splitLayout && contentScreenAspect < screenAspect * 0.98f)
                xScale = Mathf.Clamp(screenAspect / Mathf.Max(0.01f, contentScreenAspect), 1f, 1.45f);
            imgRt.localScale = new Vector3(xScale, 1f, 1f);

            Debug.Log(
                $"[Xinxiang] AutoCrop width-first uv=({x0:F3},{y0:F3},{uvW:F3},{uvH:F3}) " +
                $"content=({contentW:F3}x{contentH:F3}) xScale={xScale:F2} screenAspect={screenAspect:F3}");
        }

        void SetOverlayVisible(bool visible)
        {
            if (_overlayRoot == null) return;

            if (!_overlayRoot.gameObject.activeSelf)
                _overlayRoot.gameObject.SetActive(true);

            ForceVideoFullscreen();

            if (_overlayCanvas != null)
            {
                _overlayCanvas.overrideSorting = true;
                _overlayCanvas.sortingOrder = 800;
            }

            if (_canvasGroup == null)
                _canvasGroup = _overlayRoot.GetComponent<CanvasGroup>();
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = visible ? 1f : 0f;
                _canvasGroup.blocksRaycasts = visible;
            }
        }

        void OnDestroy()
        {
            if (_rt != null)
            {
                _rt.Release();
                Destroy(_rt);
            }
            if (_rtB != null)
            {
                _rtB.Release();
                Destroy(_rtB);
            }
            if (_chromaMat != null)
                Destroy(_chromaMat);
            if (_plainUiMat != null)
                Destroy(_plainUiMat);
            if (_videoHost != null)
                Destroy(_videoHost);
            if (_videoHostB != null)
                Destroy(_videoHostB);
        }
    }
}
