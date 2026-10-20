using UnityEngine;
using UnityEngine.UI;

namespace NexusAnima
{
    /// <summary>
    /// Big Game-view panel so concert lights can be ticked on together
    /// without hunting PBRStageEquipment in the Hierarchy.
    /// </summary>
    public class ConcertLightPreviewHud : MonoBehaviour
    {
        const string RootName = "舞台灯预览";
        static readonly Color PanelBg = new Color(0.04f, 0.06f, 0.10f, 0.94f);
        static readonly Color Idle = new Color(0.22f, 0.24f, 0.30f, 0.96f);
        static readonly Color Selected = new Color(0.92f, 0.58f, 0.12f, 1f);

        ConcertLightGuard guard;
        Image allOffImage;
        Image allOnImage;
        Image[] groupImages;
        Image[] indexImages;
        Image[] intensityImages;
        int lastMask = -2;
        int lastIndex = -99;
        float lastIntensity = -1f;

        public static void Ensure(ConcertLightGuard target)
        {
            if (target == null) return;
            var existing = GameObject.Find(RootName);
            ConcertLightPreviewHud hud;
            if (existing == null)
            {
                var go = new GameObject(RootName);
                hud = go.AddComponent<ConcertLightPreviewHud>();
                hud.Build();
            }
            else
            {
                hud = existing.GetComponent<ConcertLightPreviewHud>();
                if (hud == null)
                    hud = existing.AddComponent<ConcertLightPreviewHud>();
            }
            hud.Build();
            hud.guard = target;
            hud.lastMask = -2;
            hud.Refresh();
        }

        public static void Hide()
        {
            var existing = GameObject.Find(RootName);
            if (existing == null) return;
            if (Application.isPlaying)
                Object.Destroy(existing);
            else
                Object.DestroyImmediate(existing);
        }

        void Update()
        {
            if (guard == null)
                guard = FindFirstObjectByType<ConcertLightGuard>();
            Refresh();
        }

        void Build()
        {
            var canvas = gameObject.GetComponent<Canvas>();
            if (canvas == null)
                canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 250;
            canvas.pixelPerfect = true;
            if (gameObject.GetComponent<CanvasScaler>() == null)
            {
                var scaler = gameObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1280f, 720f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
            }
            if (gameObject.GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();

            for (int i = transform.childCount - 1; i >= 0; i--)
                DestroyImmediate(transform.GetChild(i).gameObject);

            var panel = CreatePanel(transform, "Panel", new Vector2(0f, 1f), new Vector2(12f, -12f),
                new Vector2(268f, 470f), PanelBg);

            CreateLabel(panel, "舞台灯  ← 可多选", new Vector2(12f, -10f), 18, FontStyle.Bold,
                new Vector2(244f, 28f));
            CreateLabel(panel, "几组一起开，看同时效果", new Vector2(12f, -34f), 13, FontStyle.Normal,
                new Vector2(244f, 20f), new Color(0.75f, 0.78f, 0.82f, 1f));

            var offBtn = CreateButton(panel, "全关", new Vector2(12f, -58f), new Vector2(118f, 34f),
                ClearAll);
            allOffImage = offBtn.GetComponent<Image>();
            var onBtn = CreateButton(panel, "全开", new Vector2(138f, -58f), new Vector2(118f, 34f),
                EnableAll);
            allOnImage = onBtn.GetComponent<Image>();

            string[] groupNames =
            {
                "04 头灯",
                "03 染色",
                "05 频闪",
                "镜球",
                "其它"
            };
            int[] groupKinds =
            {
                (int)ConcertLightGuard.PreviewGroup.DiscoLight04,
                (int)ConcertLightGuard.PreviewGroup.DiscoLight03,
                (int)ConcertLightGuard.PreviewGroup.DiscoLight05,
                (int)ConcertLightGuard.PreviewGroup.MirrorBall,
                (int)ConcertLightGuard.PreviewGroup.Other
            };
            groupImages = new Image[groupNames.Length];
            for (int i = 0; i < groupNames.Length; i++)
            {
                int captured = groupKinds[i];
                float y = -100f - i * 38f;
                var btn = CreateButton(panel, groupNames[i], new Vector2(12f, y), new Vector2(244f, 34f),
                    () => ToggleGroup(captured));
                groupImages[i] = btn.GetComponent<Image>();
            }

            CreateLabel(panel, "这一盏", new Vector2(12f, -294f), 14, FontStyle.Bold,
                new Vector2(244f, 20f));

            string[] indexNames = { "全部", "0", "1", "2", "3", "4" };
            indexImages = new Image[indexNames.Length];
            for (int i = 0; i < indexNames.Length; i++)
            {
                int captured = i;
                float x = 12f + (i % 6) * 41f;
                var btn = CreateButton(panel, indexNames[i], new Vector2(x, -316f), new Vector2(38f, 32f),
                    () => SetIndex(captured == 0 ? -1 : captured - 1));
                indexImages[i] = btn.GetComponent<Image>();
            }

            CreateLabel(panel, "亮度", new Vector2(12f, -356f), 14, FontStyle.Bold,
                new Vector2(244f, 20f));

            string[] intensityNames = { "暗", "中", "亮" };
            float[] intensityValues = { 0.18f, 0.35f, 0.55f };
            intensityImages = new Image[intensityNames.Length];
            for (int i = 0; i < intensityNames.Length; i++)
            {
                int captured = i;
                float x = 12f + i * 82f;
                var btn = CreateButton(panel, intensityNames[i], new Vector2(x, -380f), new Vector2(76f, 32f),
                    () => SetIntensity(intensityValues[captured]));
                intensityImages[i] = btn.GetComponent<Image>();
            }

            CreateLabel(panel, "洗白就点「全关」或「暗」", new Vector2(12f, -420f), 12, FontStyle.Normal,
                new Vector2(244f, 20f), new Color(0.70f, 0.72f, 0.76f, 1f));
        }

        void ClearAll()
        {
            if (guard == null) return;
            guard.ClearPreview();
        }

        void EnableAll()
        {
            if (guard == null) return;
            guard.SetAllPreview(true);
        }

        void ToggleGroup(int kind)
        {
            if (guard == null) return;
            guard.ToggleGroup(kind);
        }

        void SetIndex(int index)
        {
            if (guard == null) return;
            guard.previewIndex = index;
        }

        void SetIntensity(float value)
        {
            if (guard == null) return;
            guard.previewIntensity = value;
        }

        void Refresh()
        {
            if (guard == null || groupImages == null) return;
            int mask = guard.PreviewMask();
            int index = guard.previewIndex;
            float intensity = guard.previewIntensity;
            if (mask == lastMask && index == lastIndex && Mathf.Abs(intensity - lastIntensity) < 0.001f)
                return;
            lastMask = mask;
            lastIndex = index;
            lastIntensity = intensity;

            if (allOffImage != null)
                allOffImage.color = mask == 0 ? Selected : Idle;
            if (allOnImage != null)
                allOnImage.color = guard.AllPreview() ? Selected : Idle;

            int[] groupKinds =
            {
                (int)ConcertLightGuard.PreviewGroup.DiscoLight04,
                (int)ConcertLightGuard.PreviewGroup.DiscoLight03,
                (int)ConcertLightGuard.PreviewGroup.DiscoLight05,
                (int)ConcertLightGuard.PreviewGroup.MirrorBall,
                (int)ConcertLightGuard.PreviewGroup.Other
            };
            for (int i = 0; i < groupImages.Length; i++)
            {
                if (groupImages[i] != null)
                    groupImages[i].color = guard.GroupEnabled(groupKinds[i]) ? Selected : Idle;
            }
            for (int i = 0; i < indexImages.Length; i++)
            {
                if (indexImages[i] == null) continue;
                int mapped = i == 0 ? -1 : i - 1;
                indexImages[i].color = mapped == index ? Selected : Idle;
            }
            float[] intensityValues = { 0.18f, 0.35f, 0.55f };
            for (int i = 0; i < intensityImages.Length; i++)
            {
                if (intensityImages[i] == null) continue;
                intensityImages[i].color = Mathf.Abs(intensity - intensityValues[i]) < 0.03f ? Selected : Idle;
            }
        }

        static RectTransform CreatePanel(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.sprite = GameScreenCanvas.UiWhiteSprite;
            img.color = color;
            img.raycastTarget = true;
            return rt;
        }

        static Text CreateLabel(Transform parent, string content, Vector2 pos, int size, FontStyle style, Vector2 sizeDelta)
        {
            return CreateLabel(parent, content, pos, size, style, sizeDelta, Color.white);
        }

        static Text CreateLabel(Transform parent, string content, Vector2 pos, int size, FontStyle style, Vector2 sizeDelta, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = sizeDelta;
            var text = go.AddComponent<Text>();
            text.font = RuntimeFont.Resolve();
            text.text = content;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        static Button CreateButton(Transform parent, string label, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.sprite = GameScreenCanvas.UiWhiteSprite;
            img.color = Idle;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);

            var textGo = new GameObject("Label");
            textGo.transform.SetParent(go.transform, false);
            var trt = textGo.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            var text = textGo.AddComponent<Text>();
            text.font = RuntimeFont.Resolve();
            text.text = label;
            text.fontSize = 16;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return btn;
        }
    }
}
