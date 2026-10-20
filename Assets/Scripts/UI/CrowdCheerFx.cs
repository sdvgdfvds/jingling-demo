using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NexusAnima
{
    /// <summary>
    /// BattleDemo concert juice: neon rim strobe around the Game view while
    /// the crowd cheer clip is playing. Center of the board stays clear.
    /// Overlay never steals clicks (no GraphicRaycaster, raycastTarget off).
    /// </summary>
    public class CrowdCheerFx : MonoBehaviour
    {
        const string RootName = "CrowdCheerFx";
        const float CornerLen = 86f;
        const float CornerThick = 6f;
        const float TickLen = 16f;
        const float TickThick = 3f;
        const int TicksPerEdge = 7;

        static readonly Color[] Palette =
        {
            new Color(0.12f, 1.35f, 1.55f, 1f),
            new Color(1.55f, 0.10f, 1.15f, 1f),
            new Color(0.35f, 1.45f, 0.12f, 1f),
            new Color(1.50f, 0.92f, 0.10f, 1f),
            new Color(0.62f, 0.18f, 1.55f, 1f)
        };

        static CrowdCheerFx _i;
        static Sprite _softRim;
        static Sprite _hardRim;
        static Sprite _white;
        static Material _additive;

        CanvasGroup _group;
        Image _soft;
        Image _hard;
        Image _scanH;
        Image _scanV;
        readonly Image[] _chrome = new Image[40];
        int _chromeCount;
        float _started;
        float _until;
        int _beat;
        bool _looping;
        bool _subtle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _i = null;
            _softRim = null;
            _hardRim = null;
            _white = null;
            _additive = null;
        }

        public static void Play(float duration)
        {
            if (!Application.isPlaying)
                return;
            if (SceneManager.GetActiveScene().name != "BattleDemo")
                return;

            var fx = Ensure();
            if (fx == null)
                return;
            fx.Begin(duration, looping: false, subtle: false);
        }

        /// <summary>
        /// Hold the neon rim until <see cref="Stop"/> — used while the
        /// cheer-boosted Xinxiang skill is on screen.
        /// </summary>
        public static void StartLoop()
        {
            if (!Application.isPlaying)
                return;
            if (SceneManager.GetActiveScene().name != "BattleDemo")
                return;

            var fx = Ensure();
            if (fx == null)
                return;
            fx.Begin(8f, looping: true, subtle: false);
        }

        /// <summary>
        /// Faint breathing rim after the 2nd skill — no jump, no cheer.
        /// Upgraded to <see cref="StartLoop"/> on the boosted 3rd cast.
        /// </summary>
        public static void StartSubtle()
        {
            if (!Application.isPlaying)
                return;
            if (SceneManager.GetActiveScene().name != "BattleDemo")
                return;

            var fx = Ensure();
            if (fx == null)
                return;
            fx.Begin(8f, looping: true, subtle: true);
        }

        public static bool IsSubtleActive =>
            _i != null && _i._subtle && _i.gameObject.activeInHierarchy;

        public static bool IsFullActive =>
            _i != null && !_i._subtle && _i.gameObject.activeInHierarchy;

        public static void Stop()
        {
            if (_i == null)
                return;
            _i.HideNow();
        }

        static CrowdCheerFx Ensure()
        {
            if (_i != null)
                return _i;

            GameScreenCanvas.Ensure();
            var parent = GameScreenCanvas.Root;
            if (parent == null)
                return null;

            var existing = parent.Find(RootName);
            if (existing != null)
            {
                _i = existing.GetComponent<CrowdCheerFx>();
                if (_i == null)
                    _i = existing.gameObject.AddComponent<CrowdCheerFx>();
                return _i;
            }

            var go = new GameObject(RootName, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = GameScreenCanvas.StretchFull(go.GetComponent<RectTransform>());
            rt.SetAsLastSibling();

            var canvas = go.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 7000;
            canvas.pixelPerfect = false;

            var group = go.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            group.ignoreParentGroups = true;

            _i = go.AddComponent<CrowdCheerFx>();
            _i._group = group;
            _i.Build();
            go.SetActive(false);
            _i.enabled = false;
            return _i;
        }

        void Build()
        {
            EnsureAssets();
            var root = transform;

            _soft = MakeFull(root, "SoftRim", _softRim, new Color(1f, 1f, 1f, 0.85f));
            _hard = MakeFull(root, "HardRim", _hardRim, new Color(1f, 1f, 1f, 1f));

            BuildCorners(root);
            BuildTicks(root);

            _scanH = MakeBar(root, "ScanH", true);
            _scanV = MakeBar(root, "ScanV", false);
        }

        void BuildCorners(Transform root)
        {
            AddCorner(root, "TL", new Vector2(0f, 1f), new Vector2(0f, 1f), 1f, -1f);
            AddCorner(root, "TR", new Vector2(1f, 1f), new Vector2(1f, 1f), -1f, -1f);
            AddCorner(root, "BL", new Vector2(0f, 0f), new Vector2(0f, 0f), 1f, 1f);
            AddCorner(root, "BR", new Vector2(1f, 0f), new Vector2(1f, 0f), -1f, 1f);
        }

        void AddCorner(Transform parent, string tag, Vector2 anchor, Vector2 pivot, float sx, float sy)
        {
            var go = new GameObject("Corner_" + tag, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(CornerLen, CornerLen);

            RegisterChrome(MakeStroke(rt, "H",
                new Vector2(sx > 0f ? 0f : 1f, sy > 0f ? 0f : 1f),
                new Vector2(sx > 0f ? 0f : 1f, sy > 0f ? 0f : 1f),
                new Vector2(CornerLen, CornerThick)));
            RegisterChrome(MakeStroke(rt, "V",
                new Vector2(sx > 0f ? 0f : 1f, sy > 0f ? 0f : 1f),
                new Vector2(sx > 0f ? 0f : 1f, sy > 0f ? 0f : 1f),
                new Vector2(CornerThick, CornerLen)));
        }

        void BuildTicks(Transform parent)
        {
            for (int i = 0; i < TicksPerEdge; i++)
            {
                float t = (i + 1f) / (TicksPerEdge + 1f);
                RegisterChrome(MakeTick(parent, "TickT" + i, new Vector2(t, 1f), new Vector2(0.5f, 1f), TickThick, TickLen));
                RegisterChrome(MakeTick(parent, "TickB" + i, new Vector2(t, 0f), new Vector2(0.5f, 0f), TickThick, TickLen));
                RegisterChrome(MakeTick(parent, "TickL" + i, new Vector2(0f, t), new Vector2(0f, 0.5f), TickLen, TickThick));
                RegisterChrome(MakeTick(parent, "TickR" + i, new Vector2(1f, t), new Vector2(1f, 0.5f), TickLen, TickThick));
            }
        }

        void RegisterChrome(Image img)
        {
            if (img == null || _chromeCount >= _chrome.Length)
                return;
            _chrome[_chromeCount++] = img;
        }

        Image MakeFull(Transform parent, string name, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            GameScreenCanvas.StretchFull(go.GetComponent<RectTransform>());
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            img.maskable = false;
            if (_additive != null)
                img.material = _additive;
            return img;
        }

        Image MakeStroke(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.sprite = _white;
            img.color = Color.white;
            img.raycastTarget = false;
            img.maskable = false;
            if (_additive != null)
                img.material = _additive;
            return img;
        }

        Image MakeTick(Transform parent, string name, Vector2 anchor, Vector2 pivot, float w, float h)
        {
            return MakeStroke(parent, name, anchor, pivot, new Vector2(w, h));
        }

        Image MakeBar(Transform parent, string name, bool horizontal)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            if (horizontal)
            {
                rt.anchorMin = new Vector2(0.5f, 1f);
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(140f, 10f);
            }
            else
            {
                rt.anchorMin = new Vector2(0f, 0.5f);
                rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(10f, 140f);
            }
            rt.anchoredPosition = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.sprite = _white;
            img.color = Color.white;
            img.raycastTarget = false;
            img.maskable = false;
            if (_additive != null)
                img.material = _additive;
            return img;
        }

        void Begin(float duration, bool looping, bool subtle)
        {
            duration = Mathf.Max(0.65f, duration);
            _looping = looping;
            _subtle = subtle;
            _started = Time.unscaledTime;
            _until = looping ? float.PositiveInfinity : _started + duration;
            _beat = -1;
            if (_group == null)
                _group = GetComponent<CanvasGroup>();
            if (_group != null)
            {
                _group.alpha = 1f;
                _group.blocksRaycasts = false;
                _group.interactable = false;
            }
            gameObject.SetActive(true);
            enabled = true;
            transform.SetAsLastSibling();
            ApplyLayerVisibility();
            ApplyBeat(0, 1f);
        }

        void ApplyLayerVisibility()
        {
            bool full = !_subtle;
            if (_scanH != null)
                _scanH.enabled = full;
            if (_scanV != null)
                _scanV.enabled = full;
            for (int i = 0; i < _chromeCount; i++)
            {
                if (_chrome[i] == null) continue;
                _chrome[i].enabled = full;
                _chrome[i].rectTransform.localScale = Vector3.one;
            }
        }

        void HideNow()
        {
            _looping = false;
            _subtle = false;
            _until = 0f;
            if (_group != null)
                _group.alpha = 0f;
            enabled = false;
            if (gameObject != null)
                gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            transform.SetAsLastSibling();

            float now = Time.unscaledTime;
            if (!_looping && now >= _until)
            {
                HideNow();
                return;
            }

            float t = now - _started;
            float fadeIn = Mathf.Clamp01(t / 0.07f);
            float envelope;
            if (_looping)
            {
                envelope = fadeIn;
            }
            else
            {
                float left = _until - now;
                float fadeOut = left < 0.42f ? left / 0.42f : 1f;
                envelope = fadeIn * fadeOut;
            }

            if (_subtle)
            {
                int slowBeat = Mathf.FloorToInt(t * 1.15f);
                if (slowBeat != _beat)
                {
                    _beat = slowBeat;
                    ApplyBeat(slowBeat, envelope);
                }
                float breathe = 0.22f + 0.14f * (0.5f + 0.5f * Mathf.Sin(t * 2.35f));
                if (_group != null)
                    _group.alpha = envelope * breathe;
                return;
            }

            int beat = Mathf.FloorToInt(t * 8.8f);
            if (beat != _beat)
            {
                _beat = beat;
                ApplyBeat(beat, envelope);
            }

            float pulse = 0.58f + 0.42f * Mathf.Abs(Mathf.Sin(t * 23.5f));
            if (_group != null)
                _group.alpha = envelope * pulse;

            float jump = 1f + 0.42f * Mathf.Abs(Mathf.Sin(t * 19.5f));
            for (int i = 0; i < _chromeCount; i++)
            {
                if (_chrome[i] == null) continue;
                _chrome[i].rectTransform.localScale = new Vector3(jump, jump, 1f);
            }

            float w = ((RectTransform)transform).rect.width;
            float h = ((RectTransform)transform).rect.height;
            if (w < 8f) w = 1280f;
            if (h < 8f) h = 720f;

            float hx = (Mathf.PingPong(t * 1.35f, 1f) - 0.5f) * (w - 160f);
            float vy = (Mathf.PingPong(t * 1.1f + 0.25f, 1f) - 0.5f) * (h - 160f);
            if (_scanH != null)
                _scanH.rectTransform.anchoredPosition = new Vector2(hx, 0f);
            if (_scanV != null)
                _scanV.rectTransform.anchoredPosition = new Vector2(0f, vy);
        }

        void ApplyBeat(int beat, float envelope)
        {
            Color a = Palette[beat % Palette.Length];
            Color b = Palette[(beat + 2) % Palette.Length];
            Color c = Palette[(beat + 4) % Palette.Length];
            if (_subtle)
            {
                if (_soft != null)
                    _soft.color = MulAlpha(a, 0.32f * envelope);
                if (_hard != null)
                    _hard.color = MulAlpha(b, 0.16f * envelope);
                return;
            }
            if (_soft != null)
                _soft.color = MulAlpha(a, 0.72f * envelope);
            if (_hard != null)
                _hard.color = MulAlpha(b, 0.95f * envelope);
            if (_scanH != null)
                _scanH.color = MulAlpha(c, 1f);
            if (_scanV != null)
                _scanV.color = MulAlpha(a, 1f);

            for (int i = 0; i < _chromeCount; i++)
            {
                if (_chrome[i] == null)
                    continue;
                Color col = ((i + beat) & 1) == 0 ? b : c;
                _chrome[i].color = MulAlpha(col, 1f);
            }
        }

        static Color MulAlpha(Color color, float alpha)
        {
            color.a *= alpha;
            return color;
        }

        static void EnsureAssets()
        {
            if (_white == null)
                _white = GameScreenCanvas.UiWhiteSprite;
            if (_softRim == null)
                _softRim = MakeRimSprite(0.70f, 1f, 1.2f);
            if (_hardRim == null)
                _hardRim = MakeRimSprite(0.905f, 1f, 0.55f);
            if (_additive == null)
            {
                var shader = Shader.Find("NexusAnima/UiAdditive");
                if (shader != null)
                {
                    _additive = new Material(shader);
                    _additive.name = "CrowdCheerFx_Additive";
                    _additive.hideFlags = HideFlags.HideAndDontSave;
                }
            }
        }

        static Sprite MakeRimSprite(float inner, float outer, float power)
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            tex.name = "CrowdCheerFx_Rim";
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[n * n];
            float inv = 1f / n;
            for (int y = 0; y < n; y++)
            {
                float ny = Mathf.Abs((y + 0.5f) * inv * 2f - 1f);
                int row = y * n;
                for (int x = 0; x < n; x++)
                {
                    float nx = Mathf.Abs((x + 0.5f) * inv * 2f - 1f);
                    float m = nx > ny ? nx : ny;
                    float a = Mathf.Clamp01((m - inner) / Mathf.Max(0.001f, outer - inner));
                    a = Mathf.Pow(a, power);
                    pixels[row + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = "CrowdCheerFx_RimSprite";
            return sprite;
        }
    }
}
