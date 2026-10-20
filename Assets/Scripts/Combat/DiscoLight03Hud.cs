using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NexusAnima
{
    /// <summary>
    /// Game-view controls for DiscoLight03: start/stop the swing clip and a
    /// brightness slider. Lives behind the left-edge panel dock.
    /// </summary>
    public class DiscoLight03Hud : MonoBehaviour
    {
        const string PanelName = "DiscoLight03Panel";

        Slider _slider;
        Text _valueText;
        Text _swingLabel;
        Image _startImg;
        Image _stopImg;
        bool _swingOn = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void BootHook()
        {
            if (!Application.isPlaying)
                return;
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.name != "BattleDemo")
                return;
            Ensure();
        }

        public static void Ensure()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.name != "BattleDemo")
                return;

            var hub = HudPanelHub.Ensure();
            if (hub == null)
                return;

            var existing = hub.GetComponentInChildren<DiscoLight03Hud>(true);
            if (existing != null)
            {
                existing.ApplyPrefs();
                return;
            }

            var host = hub.HostLayer;
            if (host == null)
                return;

            var go = new GameObject("DiscoLight03Hud");
            go.transform.SetParent(host, false);
            var hud = go.AddComponent<DiscoLight03Hud>();
            hud.Build(host);
        }

        void Build(RectTransform host)
        {
            Font font = RuntimeFont.Resolve(16);

            var panelGo = new GameObject(PanelName, typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(host, false);
            var panelRt = panelGo.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(280f, 168f);
            panelRt.anchoredPosition = new Vector2(-500f, 24f);
            var panelImg = panelGo.GetComponent<Image>();
            panelImg.color = new Color(0.05f, 0.08f, 0.12f, 0.96f);
            panelImg.raycastTarget = true;

            _startImg = MakeButton(panelRt, "StartSwing", "开始摇摆",
                new Vector2(12f, -12f), new Vector2(124f, 40f),
                new Color(0.18f, 0.52f, 0.34f, 1f), StartSwing, font);
            _stopImg = MakeButton(panelRt, "StopSwing", "停止摇摆",
                new Vector2(144f, -12f), new Vector2(124f, 40f),
                new Color(0.62f, 0.24f, 0.20f, 1f), StopSwing, font);

            var hintRt = MakeRt(panelRt, "Hint");
            hintRt.anchorMin = new Vector2(0f, 1f);
            hintRt.anchorMax = new Vector2(1f, 1f);
            hintRt.pivot = new Vector2(0.5f, 1f);
            hintRt.anchoredPosition = new Vector2(0f, -58f);
            hintRt.sizeDelta = new Vector2(-24f, 22f);
            _swingLabel = hintRt.gameObject.AddComponent<Text>();
            _swingLabel.font = font;
            _swingLabel.fontSize = 13;
            _swingLabel.alignment = TextAnchor.MiddleLeft;
            _swingLabel.color = new Color(0.82f, 0.86f, 0.90f, 1f);
            _swingLabel.raycastTarget = false;

            var brightRt = MakeRt(panelRt, "BrightLabel");
            brightRt.anchorMin = new Vector2(0f, 1f);
            brightRt.anchorMax = new Vector2(1f, 1f);
            brightRt.pivot = new Vector2(0.5f, 1f);
            brightRt.anchoredPosition = new Vector2(0f, -84f);
            brightRt.sizeDelta = new Vector2(-24f, 20f);
            var bright = brightRt.gameObject.AddComponent<Text>();
            bright.font = font;
            bright.fontSize = 14;
            bright.fontStyle = FontStyle.Bold;
            bright.alignment = TextAnchor.MiddleLeft;
            bright.color = Color.white;
            bright.text = "亮度";
            bright.raycastTarget = false;

            _valueText = MakeRt(panelRt, "Value").gameObject.AddComponent<Text>();
            var valueRt = _valueText.rectTransform;
            valueRt.anchorMin = new Vector2(1f, 1f);
            valueRt.anchorMax = new Vector2(1f, 1f);
            valueRt.pivot = new Vector2(1f, 1f);
            valueRt.anchoredPosition = new Vector2(-12f, -84f);
            valueRt.sizeDelta = new Vector2(80f, 20f);
            _valueText.font = font;
            _valueText.fontSize = 13;
            _valueText.alignment = TextAnchor.MiddleRight;
            _valueText.color = new Color(0.85f, 0.88f, 0.92f, 1f);
            _valueText.raycastTarget = false;

            _slider = BuildSlider(panelRt, new Vector2(12f, -112f), new Vector2(256f, 28f));
            _slider.minValue = 0.05f;
            _slider.maxValue = 0.35f;
            _slider.onValueChanged.AddListener(OnSlider);

            HudPanelHub.Register("disco03", "染色灯 Disco3", panelRt, true);
            ApplyPrefs();
        }

        void StartSwing()
        {
            SetSwing(true);
        }

        void StopSwing()
        {
            SetSwing(false);
        }

        void SetSwing(bool on)
        {
            _swingOn = on;
            var anims = FindDisco03Animators();
            for (int i = 0; i < anims.Length; i++)
            {
                var anim = anims[i];
                if (anim == null)
                    continue;
                anim.enabled = on;
                anim.speed = on ? 1f : 0f;
            }
            PlayerPrefs.SetInt(HudPanelHub.PrefSwingDisco03, on ? 1 : 0);
            PlayerPrefs.Save();
            RefreshSwingUi();
        }

        void OnSlider(float value)
        {
            var guard = FindGuard();
            if (guard != null)
                guard.previewDisco03Intensity = value;
            PlayerPrefs.SetFloat(HudPanelHub.PrefIntensityDisco03, value);
            PlayerPrefs.Save();
            if (_valueText != null)
                _valueText.text = value.ToString("0.00");
        }

        void ApplyPrefs()
        {
            float intensity = PlayerPrefs.GetFloat(HudPanelHub.PrefIntensityDisco03, 0.07f);
            intensity = Mathf.Clamp(intensity, 0.05f, 0.35f);
            var guard = FindGuard();
            if (guard != null)
                guard.previewDisco03Intensity = intensity;
            if (_slider != null)
            {
                _slider.SetValueWithoutNotify(intensity);
            }
            if (_valueText != null)
                _valueText.text = intensity.ToString("0.00");

            bool swing = PlayerPrefs.GetInt(HudPanelHub.PrefSwingDisco03, 1) == 1;
            SetSwing(swing);
        }

        void RefreshSwingUi()
        {
            if (_swingLabel != null)
                _swingLabel.text = _swingOn ? "正在摇摆" : "已停止";
            if (_startImg != null)
                _startImg.color = _swingOn
                    ? new Color(0.22f, 0.62f, 0.40f, 1f)
                    : new Color(0.18f, 0.52f, 0.34f, 1f);
            if (_stopImg != null)
                _stopImg.color = !_swingOn
                    ? new Color(0.78f, 0.28f, 0.22f, 1f)
                    : new Color(0.62f, 0.24f, 0.20f, 1f);
        }

        static ConcertLightGuard FindGuard()
        {
            var scene = SceneManager.GetActiveScene();
            var guards = Object.FindObjectsByType<ConcertLightGuard>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < guards.Length; i++)
            {
                if (guards[i] != null && guards[i].gameObject.scene == scene)
                    return guards[i];
            }
            return null;
        }

        static Animator[] FindDisco03Animators()
        {
            var scene = SceneManager.GetActiveScene();
            var all = Object.FindObjectsByType<Animator>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            int n = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (IsDisco03Animator(all[i], scene))
                    n++;
            }
            var list = new Animator[n];
            int w = 0;
            for (int i = 0; i < all.Length; i++)
            {
                if (!IsDisco03Animator(all[i], scene))
                    continue;
                list[w] = all[i];
                w++;
            }
            return list;
        }

        static bool IsDisco03Animator(Animator anim, Scene scene)
        {
            if (anim == null || anim.gameObject.scene != scene)
                return false;
            Transform t = anim.transform;
            int guard = 0;
            while (t != null && guard < 12)
            {
                if (t.name.StartsWith("DiscoLight03"))
                    return true;
                t = t.parent;
                guard++;
            }
            return false;
        }

        static Image MakeButton(RectTransform parent, string name, string label,
            Vector2 pos, Vector2 size, Color color, UnityEngine.Events.UnityAction click, Font font)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = true;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(click);
            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            var text = labelGo.AddComponent<Text>();
            text.font = font;
            text.fontSize = 16;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = label;
            text.raycastTarget = false;
            return img;
        }

        static Slider BuildSlider(RectTransform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Slider", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var bg = MakeRt(rt, "Background");
            bg.anchorMin = Vector2.zero;
            bg.anchorMax = Vector2.one;
            bg.offsetMin = Vector2.zero;
            bg.offsetMax = Vector2.zero;
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.color = new Color(0.16f, 0.18f, 0.24f, 1f);
            bgImg.raycastTarget = true;

            var fillArea = MakeRt(rt, "Fill Area");
            fillArea.anchorMin = Vector2.zero;
            fillArea.anchorMax = Vector2.one;
            fillArea.offsetMin = new Vector2(6f, 6f);
            fillArea.offsetMax = new Vector2(-6f, -6f);

            var fill = MakeRt(fillArea, "Fill");
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0.5f, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            var fillImg = fill.gameObject.AddComponent<Image>();
            fillImg.color = new Color(0.28f, 0.72f, 0.52f, 1f);
            fillImg.raycastTarget = false;

            var handleArea = MakeRt(rt, "Handle Slide Area");
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.offsetMin = new Vector2(8f, 0f);
            handleArea.offsetMax = new Vector2(-8f, 0f);

            var handle = MakeRt(handleArea, "Handle");
            handle.sizeDelta = new Vector2(18f, 18f);
            var handleImg = handle.gameObject.AddComponent<Image>();
            handleImg.color = new Color(0.95f, 0.96f, 0.98f, 1f);
            handleImg.raycastTarget = true;

            var slider = go.AddComponent<Slider>();
            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.wholeNumbers = false;
            slider.transition = Selectable.Transition.None;
            return slider;
        }

        static RectTransform MakeRt(Transform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            return child.GetComponent<RectTransform>();
        }
    }
}
