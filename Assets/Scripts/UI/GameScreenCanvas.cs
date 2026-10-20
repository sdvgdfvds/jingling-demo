using UnityEngine;
using UnityEngine.UI;

namespace NexusAnima
{
    /// <summary>
    /// Shared Screen Space Overlay canvas. Draws only on the Game view screen.
    /// </summary>
    public static class GameScreenCanvas
    {
        const string RootName = "GameScreenCanvas";

        static Canvas _canvas;
        static RectTransform _root;
        static RectTransform _hudLayer;
        static RectTransform _popupLayer;
        static RectTransform _controlsLayer;

        public static RectTransform Root
        {
            get
            {
                Ensure();
                return _root;
            }
        }

        public static RectTransform HudLayer
        {
            get
            {
                Ensure();
                return _hudLayer;
            }
        }

        public static RectTransform PopupLayer
        {
            get
            {
                Ensure();
                return _popupLayer;
            }
        }

        public static RectTransform ControlsLayer
        {
            get
            {
                Ensure();
                return _controlsLayer;
            }
        }

        public static Canvas Canvas
        {
            get
            {
                Ensure();
                return _canvas;
            }
        }

        public static void Ensure()
        {
            if (_canvas != null && _root != null
                && _hudLayer != null && _popupLayer != null && _controlsLayer != null)
            {
                StretchFull(_root);
                return;
            }

            var existing = GameObject.Find(RootName);
            GameObject go;
            if (existing != null)
            {
                go = existing;
                _root = go.GetComponent<RectTransform>();
                if (_root == null) _root = go.AddComponent<RectTransform>();
                StretchFull(_root);

                _canvas = go.GetComponent<Canvas>();
                if (_canvas == null) _canvas = go.AddComponent<Canvas>();
                var scaler = go.GetComponent<CanvasScaler>();
                if (scaler == null) scaler = go.AddComponent<CanvasScaler>();
                if (go.GetComponent<GraphicRaycaster>() == null) go.AddComponent<GraphicRaycaster>();
                ConfigureCanvas(_canvas, scaler);

                _hudLayer = existing.transform.Find("HudLayer") as RectTransform
                    ?? CreateLayer(go.transform, "HudLayer");
                _popupLayer = existing.transform.Find("PopupLayer") as RectTransform
                    ?? CreateLayer(go.transform, "PopupLayer");
                _controlsLayer = existing.transform.Find("ControlsLayer") as RectTransform
                    ?? CreateLayer(go.transform, "ControlsLayer");
                StretchFull(_hudLayer);
                StretchFull(_popupLayer);
                StretchFull(_controlsLayer);
                return;
            }

            go = new GameObject(RootName, typeof(RectTransform));
            _root = go.GetComponent<RectTransform>();
            StretchFull(_root);

            _canvas = go.AddComponent<Canvas>();
            var newScaler = go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
            ConfigureCanvas(_canvas, newScaler);

            _hudLayer = CreateLayer(go.transform, "HudLayer");
            _popupLayer = CreateLayer(go.transform, "PopupLayer");
            _controlsLayer = CreateLayer(go.transform, "ControlsLayer");
        }

        static void ConfigureCanvas(Canvas canvas, CanvasScaler scaler)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            // Snap UI verts to pixels — reduces soft/blurry roster portraits and text.
            canvas.pixelPerfect = true;
            if (scaler == null) return;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;
        }

        static RectTransform CreateLayer(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return StretchFull(go.GetComponent<RectTransform>());
        }

        public static RectTransform StretchFull(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            return rt;
        }

        public static Vector2 WorldToCanvasLocal(Vector3 worldPos, out bool visible)
        {
            Ensure();
            var cam = Camera.main;
            if (cam == null || _canvas == null || Screen.width <= 0 || Screen.height <= 0)
            {
                visible = false;
                return Vector2.zero;
            }

            Vector3 screen = cam.WorldToScreenPoint(worldPos);
            if (screen.z <= 0f)
            {
                visible = false;
                return Vector2.zero;
            }

            // Screen Space Overlay: convert via canvas scaleFactor (stable even before
            // the first RectTransform layout pass, and avoids raw-pixel false locals).
            float scale = Mathf.Max(0.01f, _canvas.scaleFactor);
            float canvasW = Screen.width / scale;
            float canvasH = Screen.height / scale;
            Vector2 local = new Vector2(
                screen.x / scale - canvasW * 0.5f,
                screen.y / scale - canvasH * 0.5f);

            float pad = 16f;
            visible = local.x >= -canvasW * 0.5f - pad
                && local.x <= canvasW * 0.5f + pad
                && local.y >= -canvasH * 0.5f - pad
                && local.y <= canvasH * 0.5f + pad;
            return local;
        }

        static Sprite _uiWhiteSprite;

        /// <summary>Shared white sprite so UGUI Image / Filled bars actually render.</summary>
        public static Sprite UiWhiteSprite
        {
            get
            {
                if (_uiWhiteSprite != null) return _uiWhiteSprite;
                // Texture2D.whiteTexture is unreliable for UGUI Filled Images — use a real 32x32.
                var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                tex.name = "GameScreenCanvas_WhiteTex";
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                var pixels = new Color32[32 * 32];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32(255, 255, 255, 255);
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                _uiWhiteSprite = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, 32f, 32f),
                    new Vector2(0.5f, 0.5f),
                    32f);
                _uiWhiteSprite.name = "GameScreenCanvas_White";
                return _uiWhiteSprite;
            }
        }

        public static void ClearRuntime()
        {
            ClearChildren(_hudLayer);
            ClearChildren(_popupLayer);
            ClearChildren(_controlsLayer);
        }

        static void ClearChildren(RectTransform layer)
        {
            if (layer == null) return;
            for (int i = layer.childCount - 1; i >= 0; i--)
            {
                // Immediate so Build() in the same frame does not stack with stale bars.
                Object.DestroyImmediate(layer.GetChild(i).gameObject);
            }
        }
    }
}
