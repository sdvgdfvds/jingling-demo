using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

namespace NexusAnima
{
    /// <summary>
    /// Lightweight runtime audio hub. Clips live under Resources/Audio/{Bgm,Sfx}.
    /// Battle BGM can also come from a VideoClip (audio-only VideoPlayer) — used for
    /// desktop grok「战斗音乐」mp4.
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        public static GameAudio I { get; private set; }

        const string BgmRoot = "Audio/Bgm/";
        const string SfxRoot = "Audio/Sfx/";

        [SerializeField] float bgmVolume = 0.26f;
        [SerializeField] float sfxVolume = 0.92f;
        [SerializeField] float bgmFadeSeconds = 0.85f;
        /// <summary>Target volume for skill/character voice AudioSources.</summary>
        [SerializeField] float voiceVolume = 1f;

        AudioSource _bgm;
        AudioSource _sfx;
        AudioSource _voice;
        AudioSource _crowd;
        VideoPlayer _bgmVideo;
        VideoPlayer _voiceVideo;
        readonly Dictionary<string, AudioClip> _cache = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, VideoClip> _videoCache = new Dictionary<string, VideoClip>();
        readonly Dictionary<XinxiangSkillId, AudioClip[]> _xinxiangVoicePacks =
            new Dictionary<XinxiangSkillId, AudioClip[]>();
        string _currentBgmKey;
        Coroutine _fadeRoutine;
        Coroutine _cheerDuckRoutine;
        bool _usingVideoBgm;
        bool _bgmPaused;
        bool _xinxiangVoicesLoaded;
        int _cheerCount;
        bool _carnivalActive;

        public const int CheerNeeded = 2;
        public int CheerCount => _cheerCount;
        public int CheerNeededCount => CheerNeeded;
        /// <summary>True after two casts: neon is looping and the next skill is boosted.</summary>
        public bool IsCheerReady => _carnivalActive;
        public bool IsCarnivalActive => _carnivalActive;
        public event Action CheerMeterChanged;

        float EffectiveBgmVolume => bgmVolume;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            I = null;
        }

        public static GameAudio Ensure()
        {
            if (I != null) return I;
            var existing = FindAnyObjectByType<GameAudio>();
            if (existing != null)
            {
                I = existing;
                return I;
            }

            var go = new GameObject("GameAudio");
            if (Application.isPlaying)
                DontDestroyOnLoad(go);
            I = go.AddComponent<GameAudio>();
            I.BuildSources();
            return I;
        }

        void Awake()
        {
            if (I != null && I != this)
            {
                Destroy(gameObject);
                return;
            }
            I = this;
            if (Application.isPlaying)
                DontDestroyOnLoad(gameObject);
            BuildSources();
        }

        void BuildSources()
        {
            if (_bgm == null)
            {
                _bgm = gameObject.AddComponent<AudioSource>();
                _bgm.playOnAwake = false;
                _bgm.loop = true;
                _bgm.spatialBlend = 0f;
                _bgm.priority = 64;
            }

            if (_sfx == null)
            {
                _sfx = gameObject.AddComponent<AudioSource>();
                _sfx.playOnAwake = false;
                _sfx.loop = false;
                _sfx.spatialBlend = 0f;
                _sfx.priority = 128;
            }

            if (_voice == null)
            {
                _voice = gameObject.AddComponent<AudioSource>();
                _voice.playOnAwake = false;
                _voice.loop = false;
                _voice.spatialBlend = 0f;
                ConfigureVoiceSource(_voice);
            }

            // Dedicated bus so cheer is not mixed through PlaySfx's Clamp01.
            // AudioSource.volume is clamped to 1; extra loudness lives in the clip + duck.
            if (_crowd == null)
            {
                _crowd = gameObject.AddComponent<AudioSource>();
                _crowd.playOnAwake = false;
                _crowd.loop = false;
                _crowd.spatialBlend = 0f;
                _crowd.priority = 24;
                _crowd.bypassListenerEffects = true;
            }
            _crowd.volume = 1f;

            if (_bgmVideo == null)
            {
                _bgmVideo = gameObject.AddComponent<VideoPlayer>();
                _bgmVideo.playOnAwake = false;
                _bgmVideo.isLooping = true;
                _bgmVideo.skipOnDrop = true;
                _bgmVideo.renderMode = VideoRenderMode.APIOnly; // audio only, no picture
                _bgmVideo.audioOutputMode = VideoAudioOutputMode.AudioSource;
                _bgmVideo.EnableAudioTrack(0, true);
                _bgmVideo.SetTargetAudioSource(0, _bgm);
            }

            if (_voiceVideo == null)
            {
                _voiceVideo = gameObject.AddComponent<VideoPlayer>();
                _voiceVideo.playOnAwake = false;
                _voiceVideo.isLooping = false;
                _voiceVideo.skipOnDrop = true;
                _voiceVideo.renderMode = VideoRenderMode.APIOnly; // VO only, no picture
                _voiceVideo.audioOutputMode = VideoAudioOutputMode.AudioSource;
                _voiceVideo.EnableAudioTrack(0, true);
                _voiceVideo.SetTargetAudioSource(0, _voice);
            }

            // Scene-serialized GameAudio may still carry older louder BGM defaults.
            if (bgmVolume > 0.32f)
                bgmVolume = 0.26f;
            if (voiceVolume < 0.85f)
                voiceVolume = 1f;

            _bgm.volume = EffectiveBgmVolume;
            _sfx.volume = sfxVolume;
        }

        /// <summary>
        /// Wire a skill/character VO source: full volume, high priority over BGM.
        /// </summary>
        public void ConfigureVoiceSource(AudioSource source)
        {
            if (source == null) return;
            source.volume = Mathf.Clamp01(voiceVolume);
            source.priority = 0; // highest — BGM is 64
            source.spatialBlend = 0f;
            source.bypassListenerEffects = true;
        }

        AudioClip Load(string resourcesPath)
        {
            if (string.IsNullOrEmpty(resourcesPath)) return null;
            if (_cache.TryGetValue(resourcesPath, out var cached) && cached != null)
                return cached;

            var clip = Resources.Load<AudioClip>(resourcesPath);
            if (clip != null)
                _cache[resourcesPath] = clip;
            return clip;
        }

        VideoClip LoadVideo(string resourcesPath)
        {
            if (string.IsNullOrEmpty(resourcesPath)) return null;
            if (_videoCache.TryGetValue(resourcesPath, out var cached) && cached != null)
                return cached;

            var clip = Resources.Load<VideoClip>(resourcesPath);
            if (clip != null)
                _videoCache[resourcesPath] = clip;
            return clip;
        }

        void StopVideoBgm()
        {
            if (_bgmVideo == null) return;
            if (_bgmVideo.isPlaying)
                _bgmVideo.Stop();
            _bgmVideo.clip = null;
            _usingVideoBgm = false;
        }

        void StopClipBgm()
        {
            if (_bgm == null) return;
            // When video drives the AudioSource, Stop() also silences video audio.
            if (_bgm.isPlaying && !_usingVideoBgm)
                _bgm.Stop();
            if (!_usingVideoBgm)
                _bgm.clip = null;
        }

        public void PlayBgm(string key, bool fade = true)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_currentBgmKey == key && IsBgmPlaying())
                return;

            _bgmPaused = false;
            BuildSources();

            // AudioClip first (Battle.wav = desktop grok 战斗音乐 extracted).
            var clip = Load(BgmRoot + key);
            if (clip != null)
            {
                PlayClipBgm(key, clip, fade);
                return;
            }

            // Fallback: video audio-only (mp4 still in Resources if wav missing).
            VideoClip video = null;
            if (key == "Battle")
            {
                video = LoadVideo(BgmRoot + "BattleGrok")
                        ?? LoadVideo(BgmRoot + "BattleFromGrok")
                        ?? LoadVideo(BgmRoot + "Battle");
            }
            else
            {
                video = LoadVideo(BgmRoot + key);
            }

            if (video != null)
            {
                PlayVideoBgm(key, video, fade);
                return;
            }

            Debug.LogWarning("[GameAudio] missing BGM: " + key);
        }

        bool IsBgmPlaying()
        {
            if (_usingVideoBgm)
                return _bgmVideo != null && _bgmVideo.isPlaying;
            return _bgm != null && _bgm.isPlaying;
        }

        void PlayClipBgm(string key, AudioClip clip, bool fade)
        {
            if (_fadeRoutine != null)
                StopCoroutine(_fadeRoutine);

            StopVideoBgm();
            _currentBgmKey = key;
            _usingVideoBgm = false;

            if (!fade || !(_bgm != null && _bgm.isPlaying))
            {
                _bgm.clip = clip;
                _bgm.loop = true;
                _bgm.volume = EffectiveBgmVolume;
                _bgm.Play();
                return;
            }

            _fadeRoutine = StartCoroutine(FadeToClipBgm(clip));
        }

        void PlayVideoBgm(string key, VideoClip video, bool fade)
        {
            if (_fadeRoutine != null)
                StopCoroutine(_fadeRoutine);

            // Detach clip-driven playback; video will feed _bgm.
            if (_bgm != null)
            {
                _bgm.Stop();
                _bgm.clip = null;
                _bgm.loop = true;
            }

            _currentBgmKey = key;
            _usingVideoBgm = true;

            _bgmVideo.Stop();
            _bgmVideo.clip = video;
            _bgmVideo.isLooping = true;
            _bgmVideo.audioOutputMode = VideoAudioOutputMode.AudioSource;
            _bgmVideo.EnableAudioTrack(0, true);
            _bgmVideo.SetTargetAudioSource(0, _bgm);
            _bgm.volume = fade ? 0f : EffectiveBgmVolume;
            _bgmVideo.Play();

            if (fade)
                _fadeRoutine = StartCoroutine(FadeInBgmVolume());
            else
                _bgm.volume = EffectiveBgmVolume;
        }

        IEnumerator FadeToClipBgm(AudioClip next)
        {
            float startVol = _bgm.volume;
            float half = Mathf.Max(0.05f, bgmFadeSeconds * 0.5f);
            float t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                _bgm.volume = Mathf.Lerp(startVol, 0f, t / half);
                yield return null;
            }

            StopVideoBgm();
            _bgm.Stop();
            _bgm.clip = next;
            _bgm.loop = true;
            _bgm.Play();
            _usingVideoBgm = false;

            t = 0f;
            float target = EffectiveBgmVolume;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                _bgm.volume = Mathf.Lerp(0f, target, t / half);
                yield return null;
            }

            _bgm.volume = EffectiveBgmVolume;
            _fadeRoutine = null;
        }

        IEnumerator FadeInBgmVolume()
        {
            float half = Mathf.Max(0.05f, bgmFadeSeconds);
            float t = 0f;
            while (t < half)
            {
                t += Time.unscaledDeltaTime;
                if (_bgm != null)
                    _bgm.volume = Mathf.Lerp(0f, EffectiveBgmVolume, t / half);
                yield return null;
            }
            if (_bgm != null)
                _bgm.volume = EffectiveBgmVolume;
            _fadeRoutine = null;
        }

        public void StopBgm(bool fade = true)
        {
            _currentBgmKey = null;
            _bgmPaused = false;
            if (_fadeRoutine != null)
                StopCoroutine(_fadeRoutine);

            if (!fade)
            {
                StopVideoBgm();
                if (_bgm != null) _bgm.Stop();
                return;
            }

            _fadeRoutine = StartCoroutine(FadeOutBgm());
        }

        /// <summary>
        /// Pause battle/formation BGM in place (keeps timeline). Used during QTE click window.
        /// </summary>
        public void PauseBgm()
        {
            BuildSources();
            if (_bgmPaused) return;
            _bgmPaused = true;

            if (_fadeRoutine != null)
            {
                StopCoroutine(_fadeRoutine);
                _fadeRoutine = null;
                if (_bgm != null)
                    _bgm.volume = EffectiveBgmVolume;
            }

            if (_usingVideoBgm && _bgmVideo != null && _bgmVideo.isPlaying)
                _bgmVideo.Pause();

            if (_bgm != null && _bgm.isPlaying)
                _bgm.Pause();
        }

        /// <summary>
        /// Resume BGM from the paused position after a successful QTE click.
        /// </summary>
        public void ResumeBgm()
        {
            BuildSources();
            if (!_bgmPaused) return;
            _bgmPaused = false;

            if (_usingVideoBgm && _bgmVideo != null && _bgmVideo.clip != null)
            {
                if (!_bgmVideo.isPlaying)
                    _bgmVideo.Play();
            }

            if (_bgm != null)
            {
                _bgm.volume = EffectiveBgmVolume;
                // UnPause restores clip BGM; video-driven source may already be feeding again.
                if (!_bgm.isPlaying)
                    _bgm.UnPause();
                if (!_bgm.isPlaying && _bgm.clip != null && !_usingVideoBgm)
                    _bgm.Play();
            }
        }

        public bool IsBgmPaused => _bgmPaused;

        /// <summary>
        /// Current BGM playback position in seconds (clip or video-driven).
        /// Used by the formation rhythm demo to place notes on beats.
        /// </summary>
        public float GetBgmSongTime()
        {
            BuildSources();
            if (_usingVideoBgm && _bgmVideo != null && _bgmVideo.clip != null)
                return (float)_bgmVideo.time;
            if (_bgm != null && _bgm.clip != null)
                return _bgm.time;
            return 0f;
        }

        /// <summary>True when a BGM track is loaded and either playing or paused mid-track.</summary>
        public bool HasActiveBgm()
        {
            if (_usingVideoBgm)
                return _bgmVideo != null && _bgmVideo.clip != null;
            return _bgm != null && _bgm.clip != null;
        }

        IEnumerator FadeOutBgm()
        {
            float startVol = _bgm != null ? _bgm.volume : 0f;
            float dur = Mathf.Max(0.05f, bgmFadeSeconds);
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                if (_bgm != null)
                    _bgm.volume = Mathf.Lerp(startVol, 0f, t / dur);
                yield return null;
            }
            StopVideoBgm();
            if (_bgm != null)
            {
                _bgm.Stop();
                _bgm.volume = EffectiveBgmVolume;
            }
            _fadeRoutine = null;
        }

        public void PlaySfx(string key, float volumeScale = 1f)
        {
            if (string.IsNullOrEmpty(key)) return;
            var clip = Load(SfxRoot + key);
            if (clip == null)
            {
                Debug.LogWarning("[GameAudio] missing SFX: " + key);
                return;
            }
            BuildSources();
            _sfx.PlayOneShot(clip, Mathf.Clamp01(sfxVolume * volumeScale));
        }

        // --- Semantic helpers used by gameplay ---

        public void PlayUiClick() => PlaySfx("UiClick", 0.7f);
        public void PlayUiSelect() => PlaySfx("UiSelect", 0.85f);
        public void PlayHit() => PlaySfx("Hit", 0.9f);
        public void PlayHeal() => PlaySfx("Heal");
        public void PlayBuff() => PlaySfx("Buff");
        public void PlayMagicCast() => PlaySfx("MagicCast");
        public void PlayWhoosh() => PlaySfx("Whoosh", 0.65f);

        /// <summary>
        /// Concert crowd (BattleDemo only). Cast 1 fills 1/2. Cast 2 fills 2/2
        /// but is NOT boosted. When the 2nd finishes, carnival idle starts:
        /// a faint rim glow, no cheer SFX, no jumping neon. Cast 3 (carnival
        /// already on) plays cheer + full neon and returns true so the skill
        /// is boosted. After the boosted 3rd finishes, ConsumeCheer() clears
        /// FX and resets the bar to 0/2.
        /// </summary>
        public bool NotifySkillCastForCheer()
        {
            if (SceneManager.GetActiveScene().name != "BattleDemo")
                return false;
            if (_carnivalActive)
            {
                PlayCrowdCheerNow();
                return true;
            }

            _cheerCount = Mathf.Min(CheerNeeded, _cheerCount + 1);
            CheerMeterChanged?.Invoke();
            return false;
        }

        /// <summary>
        /// Call after a Xinxiang cast ends. The 2nd finish enters carnival;
        /// a boosted 3rd finish consumes it.
        /// </summary>
        public void NotifyCheerCastFinished(bool wasBoosted)
        {
            if (SceneManager.GetActiveScene().name != "BattleDemo")
                return;
            if (wasBoosted)
            {
                ConsumeCheer();
                return;
            }
            if (!_carnivalActive && _cheerCount >= CheerNeeded)
                EnterCarnival();
        }

        void EnterCarnival()
        {
            if (_carnivalActive)
                return;
            _carnivalActive = true;
            CrowdCheerFx.StartSubtle();
            CheerMeterChanged?.Invoke();
        }

        public void ConsumeCheer()
        {
            if (!_carnivalActive && _cheerCount == 0)
                return;
            _carnivalActive = false;
            _cheerCount = 0;
            CrowdCheerFx.Stop();
            CheerMeterChanged?.Invoke();
        }

        public void ResetCheerMeter()
        {
            _carnivalActive = false;
            _cheerCount = 0;
            CrowdCheerFx.Stop();
            CheerMeterChanged?.Invoke();
        }

        void PlayCrowdCheerNow()
        {
            BuildSources();
            var clip = Load(SfxRoot + "CrowdCheer");
            if (clip == null)
            {
                Debug.LogWarning("[GameAudio] missing SFX: CrowdCheer");
                CrowdCheerFx.StartLoop();
                return;
            }

            _crowd.PlayOneShot(clip, 1f);
            float dur = clip.length > 0.15f ? clip.length : 2.85f;
            CrowdCheerFx.StartLoop();
            if (_cheerDuckRoutine != null)
                StopCoroutine(_cheerDuckRoutine);
            _cheerDuckRoutine = StartCoroutine(DuckBgmForCheer(dur));
        }

        IEnumerator DuckBgmForCheer(float duration)
        {
            if (_bgm == null || _fadeRoutine != null)
            {
                _cheerDuckRoutine = null;
                yield break;
            }

            float orig = EffectiveBgmVolume;
            float ducked = orig * 0.38f;
            float t = 0f;
            while (t < 0.08f)
            {
                t += Time.unscaledDeltaTime;
                _bgm.volume = Mathf.Lerp(orig, ducked, t / 0.08f);
                yield return null;
            }
            _bgm.volume = ducked;

            float hold = Mathf.Max(0.2f, duration - 0.5f);
            yield return new WaitForSecondsRealtime(hold);

            t = 0f;
            while (t < 0.38f)
            {
                t += Time.unscaledDeltaTime;
                if (_fadeRoutine != null)
                {
                    _cheerDuckRoutine = null;
                    yield break;
                }
                _bgm.volume = Mathf.Lerp(ducked, orig, t / 0.38f);
                yield return null;
            }
            if (_fadeRoutine == null)
                _bgm.volume = orig;
            _cheerDuckRoutine = null;
        }

        public void PlayBattleStart() => PlaySfx("BattleStart");
        public void PlayVictory() => PlaySfx("Victory");
        public void PlayDefeat() => PlaySfx("Defeat");

        public void PlayQteOpen() => PlaySfx("QteOpen", 0.9f);

        public void PlayQteResult(XinxiangQteGrade grade)
        {
            switch (grade)
            {
                case XinxiangQteGrade.Perfect:
                    PlaySfx("QtePerfect");
                    break;
                case XinxiangQteGrade.Miss:
                    PlaySfx("QteMiss", 0.85f);
                    break;
                default:
                    PlaySfx("QteGood", 0.9f);
                    break;
            }
        }

        // Buff↔Ice gag phone call (procedural SFX under Resources/Audio/Sfx).
        public void PlayPhoneMash() => PlaySfx("PhoneMash", 0.55f);
        public void PlayPhoneHangup() => PlaySfx("PhoneHangup", 0.9f);
        public void PlayPhoneConnect() => PlaySfx("PhoneConnect", 0.95f);

        public void PlayFormationBgm() => PlayBgm("Formation");
        public void PlayBattleBgm() => PlayBgm("Battle");

        /// <summary>
        /// ParallelStage ambient BGM. Put the stage track at Resources/Audio/Bgm/Stage.wav
        /// (original Blender ref: Gimme×Gimme). Falls back to Formation if missing.
        /// </summary>
        public void PlayStageBgm()
        {
            if (Load(BgmRoot + "Stage") != null)
            {
                PlayBgm("Stage");
                return;
            }
            PlayFormationBgm();
        }

        /// <summary>
        /// Play a random line from the Xinxiang skill voice pack
        /// (Resources/Audio/Voice/Xinxiang/{Ice|Buff|Heal}/01..03 as trimmed AudioClip).
        /// Triggered when the player taps a Xinxiang skill slot.
        /// </summary>
        public void PlayXinxiangVoice(XinxiangSkillId id)
        {
            BuildSources();
            EnsureXinxiangVoicesLoaded();

            if (!_xinxiangVoicePacks.TryGetValue(id, out var pack) || pack == null || pack.Length == 0)
            {
                Debug.LogWarning("[GameAudio] missing Xinxiang voice pack: " + id);
                return;
            }

            var clip = pack[UnityEngine.Random.Range(0, pack.Length)];
            if (clip == null)
            {
                Debug.LogWarning("[GameAudio] null Xinxiang voice clip in pack: " + id);
                return;
            }

            StopXinxiangVoice();
            ConfigureVoiceSource(_voice);
            _voice.PlayOneShot(clip, 1f);
        }

        public void StopXinxiangVoice()
        {
            if (_voiceVideo != null)
            {
                if (_voiceVideo.isPlaying)
                    _voiceVideo.Stop();
                _voiceVideo.clip = null;
            }

            if (_voice != null)
                _voice.Stop();
        }

        void EnsureXinxiangVoicesLoaded()
        {
            if (_xinxiangVoicesLoaded) return;
            _xinxiangVoicesLoaded = true;

            LoadXinxiangVoicePack(XinxiangSkillId.IceDamage, "Ice");
            LoadXinxiangVoicePack(XinxiangSkillId.Buff, "Buff");
            LoadXinxiangVoicePack(XinxiangSkillId.Heal, "Heal");
        }

        void LoadXinxiangVoicePack(XinxiangSkillId id, string folder)
        {
            var list = new List<AudioClip>(3);
            for (int i = 1; i <= 3; i++)
            {
                string path = "Audio/Voice/Xinxiang/" + folder + "/" + i.ToString("00");
                var clip = Load(path);
                if (clip != null)
                    list.Add(clip);
            }

            if (list.Count == 0)
            {
                var all = Resources.LoadAll<AudioClip>("Audio/Voice/Xinxiang/" + folder);
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] != null)
                            list.Add(all[i]);
                    }
                }
            }

            _xinxiangVoicePacks[id] = list.ToArray();
            if (list.Count == 0)
                Debug.LogWarning("[GameAudio] Xinxiang voice folder empty: " + folder);
        }
    }
}
