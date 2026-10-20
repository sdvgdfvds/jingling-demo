using UnityEngine;
using UnityEngine.UI;

namespace NexusAnima
{
    public partial class SpiritUnit
    {
        void RefreshLabel()
        {
            // Overhead name/species label removed — only the HP bar remains.
            if (_label != null)
            {
                _label.text = string.Empty;
                _label.enabled = false;
            }
        }

        void UpdateOverheadUi()
        {
            if (_hudRoot == null) return;

            Vector3 anchor = GetOverheadWorldAnchor();
            Vector2 canvasPos = GameScreenCanvas.WorldToCanvasLocal(anchor, out bool visible);
            _hudRoot.gameObject.SetActive(visible && gameObject.activeInHierarchy);
            if (visible)
                _hudRoot.anchoredPosition = canvasPos;
        }

        Vector3 GetOverheadWorldAnchor()
        {
            float cellSize = _board != null ? _board.CellSize : 0.24f;
            bool flying = IsFlyingModel();
            // Ground units: keep HUD compact. Flying units need a taller ceiling so the
            // bar sits above the elevated mesh instead of clipping through/under it.
            float maxHudHeight = flying ? cellSize * 3.6f : cellSize * 1.15f;
            float minHudHeight = flying ? cellSize * 0.9f : cellSize * 0.45f;
            float height = _hudAnchorHeight > 0f
                ? Mathf.Min(_hudAnchorHeight, maxHudHeight)
                : cellSize * 0.85f;

            Transform modelRoot = transform.Find("Body");
            if (modelRoot != null)
            {
                Renderer[] renderers = modelRoot.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                        bounds.Encapsulate(renderers[i].bounds);
                    float fromFeet = bounds.max.y - transform.position.y;
                    float padding = flying ? cellSize * 0.22f : cellSize * 0.12f;
                    height = Mathf.Clamp(fromFeet + padding, minHudHeight, maxHudHeight);
                    return new Vector3(transform.position.x, transform.position.y + height, transform.position.z);
                }
            }

            return transform.position + Vector3.up * height;
        }

        // HP-only HUD — compact so it does not dominate the unit silhouette.
        static Vector2 HudSize => new Vector2(64f, 9f);

        void UpdateHpBar()
        {
            if (_hpBarFill == null || MaxHp <= 0f) return;
            _displayedHpRatio = Mathf.Clamp01(CurrentHp / MaxHp);
            if (_hpValueLabel != null)
                _hpValueLabel.text = $"{Mathf.CeilToInt(CurrentHp)}/{Mathf.CeilToInt(MaxHp)}";
        }

        void AnimateHpBar(float dt)
        {
            if (_hpBarFill == null) return;

            _lagHpRatio = Mathf.MoveTowards(_lagHpRatio, _displayedHpRatio, dt * 0.35f);
            _hpBarFill.fillAmount = Mathf.Clamp01(_displayedHpRatio);
            if (_hpBarLag != null)
                _hpBarLag.fillAmount = Mathf.Clamp01(_lagHpRatio);

            Color healthy = Team == Team.Player ? new Color(0.18f, 0.78f, 0.38f) : new Color(0.9f, 0.22f, 0.18f);
            Color critical = new Color(0.95f, 0.62f, 0.1f);
            float criticalBlend = Mathf.Clamp01((0.4f - _displayedHpRatio) / 0.4f);
            Color color = Color.Lerp(healthy, critical, criticalBlend);
            if (_displayedHpRatio <= 0.25f)
                color *= 0.65f + Mathf.Sin(Time.time * 12f) * 0.2f;
            _hpBarFill.color = color;
        }

        static void SpawnFloatText(Vector3 pos, string text, Color color)
        {
            // Combat float numbers (damage / heal / stun / skill labels) cancelled.
        }

        void DestroyWorldHudLeftovers()
        {
            var oldLabel = transform.Find("Label");
            if (oldLabel != null)
                Destroy(oldLabel.gameObject);

            var oldHp = transform.Find("HpBg");
            if (oldHp != null)
                Destroy(oldHp.gameObject);
        }

        void EnsureScreenHud()
        {
            if (_hudRoot != null)
            {
                // Scene units reuse HUD across restarts — refresh sprites/parent.
                if (_hudRoot.parent != GameScreenCanvas.HudLayer)
                    _hudRoot.SetParent(GameScreenCanvas.HudLayer, false);
                EnsureHudSprites();
                return;
            }

            GameScreenCanvas.Ensure();
            var go = new GameObject("UnitHud_" + gameObject.name, typeof(RectTransform));
            go.transform.SetParent(GameScreenCanvas.HudLayer, false);
            _hudRoot = go.GetComponent<RectTransform>();
            _hudRoot.anchorMin = new Vector2(0.5f, 0.5f);
            _hudRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _hudRoot.pivot = new Vector2(0.5f, 0f);
            _hudRoot.sizeDelta = HudSize;

            // Keep a disabled label node for backwards-compatible Find() paths.
            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(_hudRoot, false);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.zero;
            labelRt.sizeDelta = Vector2.zero;
            _label = labelGo.AddComponent<Text>();
            _label.enabled = false;
            _label.raycastTarget = false;
            labelGo.SetActive(false);

            var barGo = new GameObject("HpBg", typeof(RectTransform));
            barGo.transform.SetParent(_hudRoot, false);
            var barRt = barGo.GetComponent<RectTransform>();
            barRt.anchorMin = Vector2.zero;
            barRt.anchorMax = Vector2.one;
            barRt.offsetMin = Vector2.zero;
            barRt.offsetMax = Vector2.zero;
            var barBg = barGo.AddComponent<Image>();
            barBg.sprite = GameScreenCanvas.UiWhiteSprite;
            barBg.type = Image.Type.Simple;
            barBg.color = new Color(0.05f, 0.05f, 0.05f, 0.92f);
            barBg.raycastTarget = false;

            _hpBarLag = CreateFillImage(barGo.transform, "HpLag", new Color(0.75f, 0.55f, 0.12f, 0.9f));
            _hpBarFill = CreateFillImage(barGo.transform, "HpFill", new Color(0.18f, 0.78f, 0.38f, 1f));

            var hpTextGo = new GameObject("HpValue", typeof(RectTransform));
            hpTextGo.transform.SetParent(barGo.transform, false);
            var hpTextRt = hpTextGo.GetComponent<RectTransform>();
            hpTextRt.anchorMin = Vector2.zero;
            hpTextRt.anchorMax = Vector2.one;
            hpTextRt.offsetMin = Vector2.zero;
            hpTextRt.offsetMax = Vector2.zero;
            _hpValueLabel = hpTextGo.AddComponent<Text>();
            _hpValueLabel.font = RuntimeFont.Resolve();
            _hpValueLabel.fontSize = 8;
            _hpValueLabel.alignment = TextAnchor.MiddleCenter;
            _hpValueLabel.color = Color.white;
            _hpValueLabel.raycastTarget = false;
            ApplyReadableOutline(_hpValueLabel);

            EnsureHudSprites();
        }

        void EnsureHudSprites()
        {
            if (_hudRoot == null) return;
            // Existing HUDs may still hold a broken 1x1 whiteTexture sprite — always refresh.
            var images = _hudRoot.GetComponentsInChildren<Image>(true);
            var white = GameScreenCanvas.UiWhiteSprite;
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] == null) continue;
                images[i].sprite = white;
                if (images[i].type == Image.Type.Filled)
                {
                    images[i].fillMethod = Image.FillMethod.Horizontal;
                    images[i].fillOrigin = (int)Image.OriginHorizontal.Left;
                }
            }
            _hudRoot.sizeDelta = HudSize;

            if (_hpBarFill == null)
            {
                var fill = _hudRoot.Find("HpBg/HpFill");
                if (fill != null) _hpBarFill = fill.GetComponent<Image>();
            }
            if (_hpBarLag == null)
            {
                var lag = _hudRoot.Find("HpBg/HpLag");
                if (lag != null) _hpBarLag = lag.GetComponent<Image>();
            }
            if (_hpValueLabel == null)
            {
                var value = _hudRoot.Find("HpBg/HpValue");
                if (value != null) _hpValueLabel = value.GetComponent<Text>();
            }
            if (_label == null)
            {
                var label = _hudRoot.Find("Label");
                if (label != null) _label = label.GetComponent<Text>();
            }

            var labelGo = _hudRoot.Find("Label");
            if (labelGo != null)
                labelGo.gameObject.SetActive(false);
            if (_label != null)
            {
                _label.text = string.Empty;
                _label.enabled = false;
            }

            if (_hpValueLabel != null)
            {
                _hpValueLabel.fontSize = 8;
                _hpValueLabel.color = Color.white;
                ApplyReadableOutline(_hpValueLabel);
            }

            // Reused HUDs still have the old tall layout — force HP bar to fill the root.
            var bar = _hudRoot.Find("HpBg") as RectTransform;
            if (bar != null)
            {
                bar.anchorMin = Vector2.zero;
                bar.anchorMax = Vector2.one;
                bar.offsetMin = Vector2.zero;
                bar.offsetMax = Vector2.zero;
            }
        }

        static void ApplyReadableOutline(Text text)
        {
            if (text == null) return;

            var outline = text.GetComponent<Outline>();
            if (outline == null)
                outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
            outline.effectDistance = new Vector2(0.8f, -0.8f);
            outline.useGraphicAlpha = true;

            var shadow = text.GetComponent<Shadow>();
            if (shadow == null || shadow is Outline)
            {
                // Outline already subclasses Shadow; add a second Shadow only if none exists besides Outline.
                var shadows = text.GetComponents<Shadow>();
                bool hasPlainShadow = false;
                for (int i = 0; i < shadows.Length; i++)
                {
                    if (shadows[i] != null && !(shadows[i] is Outline))
                    {
                        hasPlainShadow = true;
                        shadow = shadows[i];
                        break;
                    }
                }
                if (!hasPlainShadow)
                    shadow = text.gameObject.AddComponent<Shadow>();
            }
            if (shadow != null && !(shadow is Outline))
            {
                shadow.effectColor = new Color(0f, 0f, 0f, 0.8f);
                shadow.effectDistance = new Vector2(0.8f, -0.8f);
                shadow.useGraphicAlpha = true;
            }
        }

        static Image CreateFillImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(1f, 1f);
            rt.offsetMax = new Vector2(-1f, -1f);
            var image = go.AddComponent<Image>();
            image.sprite = GameScreenCanvas.UiWhiteSprite;
            image.color = color;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = (int)Image.OriginHorizontal.Left;
            image.fillAmount = 1f;
            image.raycastTarget = false;
            return image;
        }
    }
}
