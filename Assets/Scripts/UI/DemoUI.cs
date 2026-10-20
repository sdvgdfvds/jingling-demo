using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NexusAnima
{
    public class DemoUI : MonoBehaviour
    {
        BattleManager _battle;
        XinxiangPrankCallDirector _prankCall;

        RectTransform _rosterRoot;
        Text _bondText;
        Text _bondBonusText;
        RectTransform _bondListRoot;
        Text _eventText;
        Text _phaseText;
        Text _energyText;
        Text _resultText;
        Button _startBtn;
        Button _restartBtn;
        Button _clearBtn;
        Image _cheerFill;
        Text _cheerLabel;
        readonly Dictionary<string, Button> _rosterButtons = new Dictionary<string, Button>();
        readonly XinxiangSlotUi[] _xinxiangSlots = new XinxiangSlotUi[3];
        readonly List<BondRowUi> _bondRows = new List<BondRowUi>();
        Coroutine _emojiRoutine;

        // 三个心象表情包池；仅在点对应技能时弹出（不再自动轮播）
        readonly List<Sprite>[] _idleEmojiPools = { new List<Sprite>(), new List<Sprite>(), new List<Sprite>() };
        readonly bool[] _idleEmojiBusy = new bool[3];
        readonly Coroutine[] _slotEmojiRoutines = new Coroutine[3];
        bool _idleEmojiPaused;

        SpiritDefinition _selected;

        class XinxiangSlotUi
        {
            public XinxiangSkillId Id;
            public Button Button;
            public Image Frame;
            public Image Icon;
            public Image CdMask;
            public Text CdText;
            public Text NameText;
            public Image EmojiBubble;
            public Image EmojiImage;
            public Vector2 EmojiRestPos;
            public bool EmojiRestCaptured;
        }

        class BondRowUi
        {
            public string Key;
            public BondDefinition PairDef;
            public ElementType? ElementTier;
            public Image Bg;
            public Image Accent;
            public Text Label;
            public Text Detail;
            public Button Button;
        }

        // Anime glass palette
        static readonly Color GlassPanel = new Color(0.98f, 0.88f, 0.94f, 0.55f);
        static readonly Color GlassBorder = new Color(1f, 0.72f, 0.86f, 0.90f);
        static readonly Color TitleColor = new Color(0.42f, 0.16f, 0.36f, 1f);
        // Locked (not eligible) / Available (can toggle) / Selected (enabled).
        static readonly Color RowLockedBg = new Color(1f, 0.95f, 0.98f, 0.58f);
        static readonly Color RowLockedText = new Color(0.38f, 0.22f, 0.34f, 0.96f);
        static readonly Color RowLockedDetail = new Color(0.48f, 0.32f, 0.44f, 0.92f);
        static readonly Color RowAvailableBg = new Color(1f, 0.86f, 0.92f, 0.72f);
        static readonly Color RowAvailableText = new Color(0.32f, 0.12f, 0.28f, 1f);
        static readonly Color RowAvailableDetail = new Color(0.42f, 0.18f, 0.36f, 0.95f);
        static readonly Color RowActiveBg = new Color(0.78f, 0.32f, 0.52f, 0.78f);
        static readonly Color RowActiveText = new Color(0.18f, 0.05f, 0.14f, 1f);
        static readonly Color RowActiveDetail = new Color(0.28f, 0.08f, 0.20f, 0.95f);
        static readonly Color AccentLocked = new Color(0.78f, 0.48f, 0.62f, 0.45f);
        static readonly Color AccentAvailable = new Color(1f, 0.72f, 0.40f, 0.90f);
        static readonly Color AccentActive = new Color(1f, 0.92f, 0.55f, 0.95f);

        // Backward-compatible aliases used by older helpers.
        static readonly Color RowInactiveBg = RowLockedBg;
        static readonly Color RowInactiveText = RowLockedText;
        static readonly Color RowInactiveDetail = RowLockedDetail;
        static readonly Color AccentInactive = AccentLocked;

        public void Build(BattleManager battle)
        {
            _battle = battle;
            EnsureEventSystem();
            GameScreenCanvas.Ensure();
            GameScreenCanvas.ClearRuntime();

            // Full-screen overlay root for Game view only.
            var uiRootGo = new GameObject("DemoUIRoot", typeof(RectTransform));
            uiRootGo.transform.SetParent(GameScreenCanvas.ControlsLayer, false);
            var uiRoot = GameScreenCanvas.StretchFull(uiRootGo.GetComponent<RectTransform>());

            // Compact full-width spirit pool — horizontal scroll, does not cover board center.
            // Lift the pool slightly so two-line card labels are not clipped at the screen edge.
            var rosterPanel = CreateBottomBar(uiRoot, "SpiritPoolBar", 16f, 122f, 12f,
                new Color(0.02f, 0.04f, 0.07f, 0.55f));
            _rosterRoot = CreateHorizontalScrollContent(rosterPanel, "SpiritPool");
            HudPanelHub.Register("pool", "精灵池", rosterPanel, true);

            // Vertical action column, top-right. HudPanelHub still wraps it so
            // the user can drag / corner-scale the whole window.
            const float actPad = 8f;
            const float actGap = 8f;
            const float actColW = 148f;
            const float actH = 64f;
            const float cheerBarH = 36f;
            const int actRows = 6; // start + 3 skills + restart + clear
            float actPanelW = actPad + actColW + actPad;
            float actPanelH = actPad
                              + actRows * actH + (actRows - 1) * actGap
                              + actGap + cheerBarH
                              + actPad;
            var actions = CreateAnchoredPanel(
                uiRoot,
                "Actions",
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(-12f, -12f),
                new Vector2(actPanelW, actPanelH),
                new Color(0.04f, 0.06f, 0.09f, 0.88f));

            var actSize = new Vector2(actColW, actH);
            float x = actPad;
            float y = -actPad;
            _startBtn = CreateTopButton(actions, "开始战斗", new Vector2(x, y), actSize,
                () => { GameAudio.I?.PlayUiClick(); _battle.StartBattle(); });
            y -= actH + actGap;
            BuildXinxiangSlots(actions, new Vector2(x, y), actSize, actGap);
            y -= 3f * (actH + actGap);
            _restartBtn = CreateTopButton(actions, "重新布阵", new Vector2(x, y), actSize,
                () => { GameAudio.I?.PlayUiClick(); _battle.RestartToFormation(); });
            y -= actH + actGap;
            _clearBtn = CreateTopButton(actions, "清空棋盘", new Vector2(x, y), actSize,
                () => { GameAudio.I?.PlayUiClick(); _battle.ClearBoardSpirits(); });
            // Distinct color so the clear action is easy to spot.
            var clearImg = _clearBtn.targetGraphic as Image;
            if (clearImg != null)
                clearImg.color = new Color(0.72f, 0.25f, 0.22f, 0.95f);
            y -= actH + actGap;
            BuildCheerMeter(actions, new Vector2(actPad, y),
                new Vector2(actColW, cheerBarH));
            HudPanelHub.Register("actions", "战斗操作", actions, true);

            BuildCuteBondPanel(uiRoot);

            GameAudio.Ensure();
            if (GameAudio.I != null)
                GameAudio.I.CheerMeterChanged += RefreshCheerMeter;

            if (_battle.Xinxiang != null)
                _battle.Xinxiang.StateChanged += RefreshAll;

            BindPrankCall();
            BuildRosterButtons();
            LoadIdleEmojiPools();
            RefreshAll();
        }

        void BindPrankCall()
        {
            if (_prankCall != null)
            {
                _prankCall.CallStarted -= OnPrankCallStarted;
                _prankCall.CallEnded -= OnPrankCallEnded;
            }

            _prankCall = FindAnyObjectByType<XinxiangPrankCallDirector>();
            if (_prankCall == null) return;
            _prankCall.CallStarted += OnPrankCallStarted;
            _prankCall.CallEnded += OnPrankCallEnded;
        }

        void OnPrankCallStarted()
        {
            _idleEmojiPaused = true;
            // Clear any leftover stickers; Ice emoji pops only after hang-up.
            StopAllSlotEmojis();
            RefreshAll();
        }

        void OnPrankCallEnded(bool hungUp)
        {
            if (hungUp)
                ShowIceEmojiAfterHangup();
            else
                _idleEmojiPaused = false;
            RefreshAll();
        }

        void ShowIceEmojiAfterHangup()
        {
            _idleEmojiPaused = true;
            if (_emojiRoutine != null)
                StopCoroutine(_emojiRoutine);
            float hold = _prankCall != null ? _prankCall.EmojiSeconds : 2.6f;
            _emojiRoutine = StartCoroutine(PlayIceHangupEmoji(hold));
        }

        IEnumerator PlayIceHangupEmoji(float holdSeconds)
        {
            StopAllSlotEmojis();

            var iceSprite = LoadXinxiangEmoji("IceReply");
            XinxiangSlotUi iceSlot = null;
            for (int i = 0; i < _xinxiangSlots.Length; i++)
            {
                var slot = _xinxiangSlots[i];
                if (slot != null && slot.Id == XinxiangSkillId.IceDamage)
                {
                    iceSlot = slot;
                    break;
                }
            }

            if (iceSlot != null && iceSprite != null)
                yield return AnimateSmsPopIn(iceSlot, iceSprite, fromLeft: true);

            float hold = Mathf.Max(0.6f, holdSeconds);
            yield return new WaitForSecondsRealtime(hold);

            yield return AnimateSmsFadeOut(iceSlot);

            for (int i = 0; i < _xinxiangSlots.Length; i++)
                HideSlotEmoji(_xinxiangSlots[i]);
            _emojiRoutine = null;
            _idleEmojiPaused = false;
        }

        void LoadIdleEmojiPools()
        {
            for (int i = 0; i < _idleEmojiPools.Length; i++)
                _idleEmojiPools[i].Clear();

            // 桌面拆好的贴纸 + 旧有专用贴纸
            AddPoolSprites(_idleEmojiPools[SlotIndex(XinxiangSkillId.IceDamage)],
                "Ice01", "Ice02", "Ice03", "IceReply");
            AddPoolSprites(_idleEmojiPools[SlotIndex(XinxiangSkillId.Buff)],
                "Buff01", "Buff02", "Buff03", "BuffCall");
            AddPoolSprites(_idleEmojiPools[SlotIndex(XinxiangSkillId.Heal)],
                "Heal01", "Heal02", "Heal03");
        }

        static int SlotIndex(XinxiangSkillId id)
        {
            switch (id)
            {
                case XinxiangSkillId.IceDamage: return 0;
                case XinxiangSkillId.Buff: return 1;
                default: return 2; // Heal
            }
        }

        void AddPoolSprites(List<Sprite> pool, params string[] names)
        {
            if (pool == null || names == null) return;
            for (int i = 0; i < names.Length; i++)
            {
                var sp = LoadXinxiangEmoji(names[i]);
                if (sp != null)
                    pool.Add(sp);
            }
        }

        void StopAllSlotEmojis()
        {
            for (int i = 0; i < _xinxiangSlots.Length; i++)
            {
                if (_slotEmojiRoutines[i] != null)
                {
                    StopCoroutine(_slotEmojiRoutines[i]);
                    _slotEmojiRoutines[i] = null;
                }
                _idleEmojiBusy[i] = false;
                HideSlotEmoji(_xinxiangSlots[i]);
            }
        }

        /// <summary>
        /// Pop a random sticker for the clicked Xinxiang skill slot.
        /// </summary>
        void PopEmojiOnSkillClick(XinxiangSkillId id)
        {
            if (_idleEmojiPaused) return;
            if (_emojiRoutine != null) return; // hang-up special emoji owns the stage
            var xinxiang = _battle != null ? _battle.Xinxiang : null;
            if (xinxiang != null && xinxiang.IsPrankCalling) return;

            int slotIndex = SlotIndex(id);
            if (slotIndex < 0 || slotIndex >= _xinxiangSlots.Length) return;
            var slot = _xinxiangSlots[slotIndex];
            if (slot == null) return;
            var pool = _idleEmojiPools[slotIndex];
            if (pool == null || pool.Count == 0) return;

            if (_slotEmojiRoutines[slotIndex] != null)
            {
                StopCoroutine(_slotEmojiRoutines[slotIndex]);
                _slotEmojiRoutines[slotIndex] = null;
            }
            _idleEmojiBusy[slotIndex] = false;
            HideSlotEmoji(slot);

            _slotEmojiRoutines[slotIndex] = StartCoroutine(PlayClickEmoji(slotIndex));
        }

        IEnumerator PlayClickEmoji(int slotIndex)
        {
            var slot = _xinxiangSlots[slotIndex];
            if (slot == null) yield break;
            var pool = _idleEmojiPools[SlotIndex(slot.Id)];
            if (pool == null || pool.Count == 0) yield break;

            Sprite sticker = pool[Random.Range(0, pool.Count)];
            if (sticker == null) yield break;

            _idleEmojiBusy[slotIndex] = true;
            yield return AnimateSmsPopIn(slot, sticker, fromLeft: true);

            float hold = Random.Range(0.9f, 1.9f);
            float t = 0f;
            while (t < hold)
            {
                if (_idleEmojiPaused)
                    break;
                t += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!_idleEmojiPaused && slot.EmojiBubble != null && slot.EmojiBubble.enabled)
                yield return AnimateSmsFadeOut(slot);
            else
                HideSlotEmoji(slot);

            _idleEmojiBusy[slotIndex] = false;
            _slotEmojiRoutines[slotIndex] = null;
        }

        static IEnumerator AnimateSmsPopIn(XinxiangSlotUi slot, Sprite sticker, bool fromLeft)
        {
            if (slot == null || slot.EmojiBubble == null || slot.EmojiImage == null)
                yield break;

            var bubble = slot.EmojiBubble;
            var rt = bubble.rectTransform;
            var cg = bubble.GetComponent<CanvasGroup>();
            if (cg == null) cg = bubble.gameObject.AddComponent<CanvasGroup>();

            Vector2 rest = rt.anchoredPosition;
            // Remember rest once so repeated plays don't drift.
            if (!slot.EmojiRestCaptured)
            {
                slot.EmojiRestPos = rest;
                slot.EmojiRestCaptured = true;
            }
            else
            {
                rest = slot.EmojiRestPos;
            }

            bubble.enabled = true;
            // Chat bubble plate: Ice = pure white; Buff = soft warm bubble.
            if (slot.Id == XinxiangSkillId.IceDamage)
                bubble.color = new Color(1f, 1f, 1f, 1f);
            else if (slot.Id == XinxiangSkillId.Buff)
                bubble.color = new Color(1f, 0.94f, 0.88f, 0.98f);
            else
                bubble.color = new Color(1f, 1f, 1f, 0.98f);
            slot.EmojiImage.enabled = true;
            slot.EmojiImage.sprite = sticker;
            slot.EmojiImage.color = Color.white;
            slot.EmojiImage.preserveAspect = true;

            float slide = fromLeft ? -88f : 88f;
            Vector2 start = rest + new Vector2(slide, 0f);
            rt.anchoredPosition = start;
            rt.localScale = new Vector3(0.22f, 0.22f, 1f);
            cg.alpha = 0f;

            const float dur = 0.38f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / dur);
                // Ease-out back (SMS bounce).
                float overshoot = 1.55f;
                float inv = u - 1f;
                float bounce = 1f + overshoot * inv * inv * inv + overshoot * inv * inv;
                float ease = 1f - Mathf.Pow(1f - u, 3f);

                rt.anchoredPosition = Vector2.LerpUnclamped(start, rest, ease);
                float s = Mathf.LerpUnclamped(0.22f, 1f, bounce);
                rt.localScale = new Vector3(s, s, 1f);
                cg.alpha = ease;
                yield return null;
            }

            rt.anchoredPosition = rest;
            rt.localScale = Vector3.one;
            cg.alpha = 1f;
        }

        static IEnumerator AnimateSmsFadeOut(XinxiangSlotUi slot)
        {
            if (slot == null || slot.EmojiBubble == null) yield break;
            if (!slot.EmojiBubble.enabled) yield break;

            var rt = slot.EmojiBubble.rectTransform;
            var cg = slot.EmojiBubble.GetComponent<CanvasGroup>();
            if (cg == null) cg = slot.EmojiBubble.gameObject.AddComponent<CanvasGroup>();

            Vector2 rest = slot.EmojiRestCaptured ? slot.EmojiRestPos : rt.anchoredPosition;
            const float dur = 0.22f;
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / dur);
                float ease = u * u;
                rt.localScale = Vector3.Lerp(Vector3.one, new Vector3(0.7f, 0.7f, 1f), ease);
                rt.anchoredPosition = Vector2.Lerp(rest, rest + new Vector2(0f, 10f), ease);
                cg.alpha = 1f - ease;
                yield return null;
            }

            cg.alpha = 0f;
            HideSlotEmoji(slot);
            rt.anchoredPosition = rest;
            rt.localScale = Vector3.one;
        }

        static Sprite LoadXinxiangEmoji(string fileNameWithoutExt)
        {
            if (string.IsNullOrEmpty(fileNameWithoutExt)) return null;
            var path = "Xinxiang/Emojis/" + fileNameWithoutExt;
            var sprite = Resources.Load<Sprite>(path);
            if (sprite != null) return sprite;

            var tex = Resources.Load<Texture2D>(path);
            if (tex == null) return null;
            tex.filterMode = FilterMode.Bilinear;
            return Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect);
        }

static void SetSlotEmoji(XinxiangSlotUi slot, Sprite sticker)
        {
            if (slot == null) return;
            if (slot.EmojiBubble != null)
            {
                slot.EmojiBubble.enabled = true;
                if (slot.Id == XinxiangSkillId.IceDamage)
                    slot.EmojiBubble.color = new Color(1f, 1f, 1f, 1f);
                else if (slot.Id == XinxiangSkillId.Buff)
                    slot.EmojiBubble.color = new Color(1f, 0.94f, 0.88f, 0.98f);
                else
                    slot.EmojiBubble.color = new Color(1f, 1f, 1f, 0.98f);
                var cg = slot.EmojiBubble.GetComponent<CanvasGroup>();
                if (cg == null) cg = slot.EmojiBubble.gameObject.AddComponent<CanvasGroup>();
                cg.alpha = 1f;
            }
            if (slot.EmojiImage != null)
            {
                slot.EmojiImage.enabled = sticker != null;
                slot.EmojiImage.sprite = sticker;
                slot.EmojiImage.color = Color.white;
                slot.EmojiImage.preserveAspect = true;
            }
        }

static void HideSlotEmoji(XinxiangSlotUi slot)
        {
            if (slot == null) return;
            if (slot.EmojiBubble != null)
            {
                slot.EmojiBubble.enabled = false;
                var cg = slot.EmojiBubble.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = 1f;
                if (slot.EmojiRestCaptured)
                    slot.EmojiBubble.rectTransform.anchoredPosition = slot.EmojiRestPos;
                slot.EmojiBubble.rectTransform.localScale = Vector3.one;
            }
            if (slot.EmojiImage != null)
            {
                slot.EmojiImage.enabled = false;
                slot.EmojiImage.sprite = null;
            }
        }

        void BuildCuteBondPanel(RectTransform uiRoot)
        {
            var shellRt = CreateAnchoredPanel(
                uiRoot,
                "BondShell",
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(10f, -10f),
                new Vector2(276f, 392f),
                new Color(0f, 0f, 0f, 0f));
            shellRt.GetComponent<Image>().raycastTarget = false;

            var glowRt = CreateAnchoredPanel(
                shellRt,
                "BondGlow",
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero,
                new Color(1f, 0.55f, 0.78f, 0.18f));
            StretchFullLocal(glowRt, 0f);
            glowRt.GetComponent<Image>().raycastTarget = false;

            var panelRt = CreateAnchoredPanel(
                shellRt,
                "BondPanel",
                Vector2.zero,
                Vector2.one,
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero,
                GlassPanel);
            StretchFullLocal(panelRt, 4f);
            var panelImg = panelRt.GetComponent<Image>();
            var glassSprite = CuteUi.LoadAnimeGlass("button_rectangle_depth_flat")
                               ?? CuteUi.LoadKenneySprite("Yellow_Default_button_rectangle_depth_flat")
                               ?? GameScreenCanvas.UiWhiteSprite;
            panelImg.sprite = glassSprite;
            panelImg.type = Image.Type.Sliced;
            panelImg.pixelsPerUnitMultiplier = 1.05f;
            panelImg.color = GlassPanel;
            panelImg.raycastTarget = false;

            var outline = panelRt.gameObject.AddComponent<Outline>();
            outline.effectColor = GlassBorder;
            outline.effectDistance = new Vector2(2.2f, -2.2f);
            var shadow = panelRt.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.55f, 0.20f, 0.40f, 0.28f);
            shadow.effectDistance = new Vector2(0f, -4f);

            var accentBar = CreateAnchoredPanel(
                panelRt,
                "AccentBar",
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, -6f),
                new Vector2(-16f, 4f),
                new Color(1f, 0.70f, 0.84f, 0.85f));
            StretchWidth(accentBar, 10f, -34f);
            accentBar.GetComponent<Image>().raycastTarget = false;

            var title = CreateText(panelRt, "✦ 精灵羁绊", new Vector2(16f, -16f), 20, FontStyle.Bold,
                new Vector2(200f, 30f));
            title.color = TitleColor;
            title.alignment = TextAnchor.MiddleLeft;
            var titleRt = title.rectTransform;
            titleRt.anchorMin = new Vector2(0f, 1f);
            titleRt.anchorMax = new Vector2(1f, 1f);
            titleRt.pivot = new Vector2(0f, 1f);
            titleRt.anchoredPosition = new Vector2(16f, -16f);
            titleRt.sizeDelta = new Vector2(-48f, 30f);

            var hint = CreateText(panelRt, "浅色未激活 · 深色已共鸣", new Vector2(16f, -44f), 11, FontStyle.Normal,
                new Vector2(220f, 18f));
            hint.color = new Color(0.55f, 0.40f, 0.50f, 0.85f);
            var hintRt = hint.rectTransform;
            hintRt.anchorMin = new Vector2(0f, 1f);
            hintRt.anchorMax = new Vector2(1f, 1f);
            hintRt.pivot = new Vector2(0f, 1f);
            hintRt.anchoredPosition = new Vector2(16f, -44f);
            hintRt.sizeDelta = new Vector2(-48f, 18f);

            var listFrame = CreateAnchoredPanel(
                panelRt,
                "BondListFrame",
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0.5f, 0.5f),
                Vector2.zero,
                Vector2.zero,
                new Color(1f, 1f, 1f, 0.14f));
            listFrame.offsetMin = new Vector2(12f, 52f);
            listFrame.offsetMax = new Vector2(-12f, -68f);
            listFrame.GetComponent<Image>().raycastTarget = true;
            var listOutline = listFrame.gameObject.AddComponent<Outline>();
            listOutline.effectColor = new Color(1f, 0.80f, 0.90f, 0.55f);
            listOutline.effectDistance = new Vector2(1.2f, -1.2f);

            var scrollGo = new GameObject("BondScroll", typeof(RectTransform));
            scrollGo.transform.SetParent(listFrame, false);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            StretchFullLocal(scrollRt, 6f);
            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 22f;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRt = viewportGo.GetComponent<RectTransform>();
            StretchFullLocal(viewportRt, 0f);
            viewportGo.AddComponent<RectMask2D>();
            var vpImg = viewportGo.AddComponent<Image>();
            vpImg.color = new Color(1f, 1f, 1f, 0.02f);
            vpImg.raycastTarget = true;

            var contentGo = new GameObject("BondList", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            _bondListRoot = contentGo.GetComponent<RectTransform>();
            _bondListRoot.anchorMin = new Vector2(0f, 1f);
            _bondListRoot.anchorMax = new Vector2(1f, 1f);
            _bondListRoot.pivot = new Vector2(0.5f, 1f);
            _bondListRoot.anchoredPosition = Vector2.zero;
            _bondListRoot.sizeDelta = new Vector2(0f, 0f);

            var layout = contentGo.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(4, 4, 4, 4);
            layout.spacing = 5f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.viewport = viewportRt;
            scroll.content = _bondListRoot;

            BuildBondRows();

            _bondBonusText = CreateText(panelRt, "当前加成：无", new Vector2(16f, 8f), 12, FontStyle.Bold,
                new Vector2(236f, 42f));
            _bondBonusText.color = new Color(0.40f, 0.22f, 0.34f, 0.95f);
            _bondBonusText.alignment = TextAnchor.UpperLeft;
            var bonusRt = _bondBonusText.rectTransform;
            bonusRt.anchorMin = new Vector2(0f, 0f);
            bonusRt.anchorMax = new Vector2(1f, 0f);
            bonusRt.pivot = new Vector2(0.5f, 0f);
            bonusRt.anchoredPosition = new Vector2(0f, 8f);
            bonusRt.sizeDelta = new Vector2(-32f, 42f);

            _bondText = _bondBonusText;

            HudPanelHub.Register("bond", "精灵羁绊", shellRt, true);
        }

        void BuildBondRows()
        {
            _bondRows.Clear();
            if (_bondListRoot == null) return;

            for (int i = _bondListRoot.childCount - 1; i >= 0; i--)
                Object.Destroy(_bondListRoot.GetChild(i).gameObject);

            // Element-count bonds (always listed)
            AddElementTierRow(ElementType.Fire, "火共鸣", "火×2 / 火×4");
            AddElementTierRow(ElementType.Water, "水共鸣", "水×2 / 水×4");
            AddElementTierRow(ElementType.Grass, "草共鸣", "草×2 / 草×4");
            AddElementTierRow(ElementType.Light, "光共鸣", "光×2 / 光×4");

            // Complementary pairs (always listed)
            BondCatalog.EnsureLoaded();
            foreach (var def in BondCatalog.All)
            {
                if (def == null || def.MatchKind != BondMatchKind.ElementPair) continue;
                string a = DemoDataFactory.ElementLabel(def.ElementA);
                string b = DemoDataFactory.ElementLabel(def.ElementB);
                string detail = $"{a}+{b}  ·  {FormatBondBonus(def)}";
                var row = CreateBondRow(def.Id, def.DisplayName, detail);
                row.PairDef = def;
                _bondRows.Add(row);
            }
        }

        void AddElementTierRow(ElementType element, string title, string detail)
        {
            var row = CreateBondRow("tier_" + element, title, detail);
            row.ElementTier = element;
            _bondRows.Add(row);
        }

        BondRowUi CreateBondRow(string key, string title, string detail)
        {
            var go = new GameObject("BondRow_" + key, typeof(RectTransform));
            go.transform.SetParent(_bondListRoot, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(0f, 40f);
            var le = go.AddComponent<LayoutElement>();
            le.minHeight = 40f;
            le.preferredHeight = 40f;

            var bg = go.AddComponent<Image>();
            bg.sprite = CuteUi.LoadAnimeGlass("button_rectangle_gloss")
                        ?? GameScreenCanvas.UiWhiteSprite;
            bg.type = Image.Type.Sliced;
            bg.color = RowLockedBg;
            bg.raycastTarget = true;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.transition = Selectable.Transition.None;
            string capturedKey = key;
            btn.onClick.AddListener(() => OnBondRowClicked(capturedKey));

            var accentGo = new GameObject("Accent", typeof(RectTransform));
            accentGo.transform.SetParent(go.transform, false);
            var accentRt = accentGo.GetComponent<RectTransform>();
            accentRt.anchorMin = new Vector2(0f, 0.15f);
            accentRt.anchorMax = new Vector2(0f, 0.85f);
            accentRt.pivot = new Vector2(0f, 0.5f);
            accentRt.anchoredPosition = new Vector2(6f, 0f);
            accentRt.sizeDelta = new Vector2(4f, 0f);
            var accent = accentGo.AddComponent<Image>();
            accent.sprite = GameScreenCanvas.UiWhiteSprite;
            accent.color = AccentLocked;
            accent.raycastTarget = false;

            var label = CreateText(go.transform, title, new Vector2(16f, -4f), 13, FontStyle.Bold,
                new Vector2(200f, 18f));
            label.color = RowLockedText;
            label.alignment = TextAnchor.MiddleLeft;
            label.raycastTarget = false;

            var detailText = CreateText(go.transform, detail, new Vector2(16f, -22f), 11, FontStyle.Normal,
                new Vector2(210f, 16f));
            detailText.color = RowLockedDetail;
            detailText.alignment = TextAnchor.MiddleLeft;
            detailText.raycastTarget = false;

            return new BondRowUi
            {
                Key = key,
                Bg = bg,
                Accent = accent,
                Label = label,
                Detail = detailText,
                Button = btn
            };
        }

        void OnBondRowClicked(string key)
        {
            if (_battle == null) return;
            if (_battle.Phase != GamePhase.Formation)
            {
                _battle.LastEvent = "战斗中不能改羁绊";
                RefreshAll();
                return;
            }

            GameAudio.I?.PlayUiClick();
            var bonds = _battle.PlayerBonds;
            bool turningOn = !bonds.IsSelected(key);
            bool ok = _battle.TryTogglePlayerBond(key);
            if (!ok)
            {
                if (!bonds.IsEligible(key))
                    _battle.LastEvent = "条件未满足，无法启用";
                else if (bonds.SelectedCount >= BondState.MaxSelected && turningOn)
                    _battle.LastEvent = $"最多同时启用 {BondState.MaxSelected} 条羁绊";
                else
                    _battle.LastEvent = "无法切换该羁绊";
            }
            else
            {
                _battle.LastEvent = turningOn
                    ? $"已启用羁绊（{bonds.SelectedCount}/{BondState.MaxSelected}）"
                    : $"已关闭羁绊（{bonds.SelectedCount}/{BondState.MaxSelected}）";
            }
            RefreshAll();
        }

        void RefreshBondRows()
        {
            if (_battle == null || _bondRows.Count == 0) return;
            var bonds = _battle.PlayerBonds;
            bool canEdit = _battle.Phase == GamePhase.Formation;

            foreach (var row in _bondRows)
            {
                bool eligible = false;
                bool selected = bonds.IsSelected(row.Key);
                string title = row.Key;
                string detail = "";

                if (row.ElementTier.HasValue)
                {
                    var el = row.ElementTier.Value;
                    bonds.Counts.TryGetValue(el, out int count);
                    bonds.EligibleLevels.TryGetValue(el, out int level);
                    eligible = level > 0;
                    string elName = DemoDataFactory.ElementLabel(el);
                    title = eligible ? $"{elName}共鸣 {level}级" : $"{elName}共鸣";
                    if (selected)
                        detail = $"已启用 · {count}只 · {DescribeElementTierBonus(el, level)}";
                    else if (eligible)
                        detail = $"可勾选 · {count}只 · {DescribeElementTierBonus(el, level)} · 点击启用";
                    else
                        detail = $"未满足 · 需要 {elName}×2（进阶×4）· 当前 {count}";
                }
                else if (row.PairDef != null)
                {
                    eligible = row.PairDef.Matches(bonds.Counts);
                    string a = DemoDataFactory.ElementLabel(row.PairDef.ElementA);
                    string b = DemoDataFactory.ElementLabel(row.PairDef.ElementB);
                    title = row.PairDef.DisplayName;
                    string bonus = FormatBondBonus(row.PairDef);
                    if (selected)
                        detail = $"已启用 · {a}+{b} · {bonus}";
                    else if (eligible)
                        detail = $"可勾选 · {a}+{b} · {bonus} · 点击启用";
                    else
                        detail = $"未满足 · 需要 {a}+{b} · {bonus}";
                }

                ApplyBondRowVisual(row, eligible, selected, title, detail);
                if (row.Button != null)
                    row.Button.interactable = canEdit && (eligible || selected);
            }

            if (_bondBonusText != null)
            {
                int selectedCount = bonds.SelectedCount;
                if (selectedCount <= 0)
                {
                    _bondBonusText.text =
                        $"已选 0/{BondState.MaxSelected}\n" +
                        "满足条件后点击列表勾选（敌方不加）";
                    _bondBonusText.color = RowLockedText;
                }
                else
                {
                    _bondBonusText.text =
                        $"已选 {selectedCount}/{BondState.MaxSelected} · 仅相关元素\n" +
                        FormatActiveTotals(bonds);
                    _bondBonusText.color = RowActiveDetail;
                }
            }
        }

        static void ApplyBondRowVisual(
            BondRowUi row, bool eligible, bool selected, string title, string detail)
        {
            Color bg, accent, labelCol, detailCol;
            string mark;
            if (selected)
            {
                bg = RowActiveBg;
                accent = AccentActive;
                labelCol = RowActiveText;
                detailCol = RowActiveDetail;
                mark = "● ";
            }
            else if (eligible)
            {
                bg = RowAvailableBg;
                accent = AccentAvailable;
                labelCol = RowAvailableText;
                detailCol = RowAvailableDetail;
                mark = "◎ ";
            }
            else
            {
                bg = RowLockedBg;
                accent = AccentLocked;
                labelCol = RowLockedText;
                detailCol = RowLockedDetail;
                mark = "○ ";
            }

            if (row.Bg != null) row.Bg.color = bg;
            if (row.Accent != null) row.Accent.color = accent;
            if (row.Label != null)
            {
                row.Label.text = mark + title;
                row.Label.color = labelCol;
                row.Label.fontStyle = selected ? FontStyle.Bold : FontStyle.Normal;
            }
            if (row.Detail != null)
            {
                row.Detail.text = detail;
                row.Detail.color = detailCol;
            }
        }

        static string FormatBondBonus(BondDefinition def)
        {
            if (def == null) return "";
            var parts = new List<string>();
            if (def.AttackBonus > 0f) parts.Add($"攻+{def.AttackBonus * 100f:0}%");
            if (def.HpBonus > 0f) parts.Add($"血+{def.HpBonus * 100f:0}%");
            if (def.DefenseBonus > 0f) parts.Add($"防+{def.DefenseBonus * 100f:0}%");
            if (def.EnergyBonus > 0f) parts.Add($"能+{def.EnergyBonus * 100f:0}%");
            return parts.Count > 0 ? string.Join(" ", parts) : "特殊效果";
        }

        static string DescribeElementTierBonus(ElementType el, int level)
        {
            switch (el)
            {
                case ElementType.Fire: return level >= 2 ? "攻+20% 灼烧" : "攻+10%";
                case ElementType.Water: return level >= 2 ? "血+20%" : "血+10%";
                case ElementType.Grass: return level >= 2 ? "防+20% 减伤" : "防+10%";
                case ElementType.Light: return level >= 2 ? "回能+30%" : "回能+15%";
                default: return "";
            }
        }

        static string FormatActiveTotals(BondState bonds)
        {
            var parts = new List<string>();
            if (bonds.AttackBonus > 0f) parts.Add($"攻 +{bonds.AttackBonus * 100f:0}%");
            if (bonds.HpBonus > 0f) parts.Add($"血 +{bonds.HpBonus * 100f:0}%");
            if (bonds.DefenseBonus > 0f) parts.Add($"防 +{bonds.DefenseBonus * 100f:0}%");
            if (bonds.EnergyBonus > 0f) parts.Add($"能 +{bonds.EnergyBonus * 100f:0}%");
            return parts.Count > 0 ? string.Join("  ", parts) : "—";
        }

        static void StretchFullLocal(RectTransform rt, float pad)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
        }

        static void StretchWidth(RectTransform rt, float left, float height)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(left, -height - 6f);
            rt.offsetMax = new Vector2(-left, -6f);
        }

        void BuildXinxiangSlots(RectTransform actions, Vector2 origin, Vector2 slotSize, float gap)
        {
            var skills = _battle.Xinxiang != null
                ? _battle.Xinxiang.Skills
                : XinxiangSkillDefinition.CreateDefaults();

            for (int i = 0; i < 3; i++)
            {
                var def = skills[i];
                var pos = new Vector2(origin.x, origin.y - i * (slotSize.y + gap));
                _xinxiangSlots[i] = CreateXinxiangSlot(actions, def, pos, slotSize);
            }
        }

        XinxiangSlotUi CreateXinxiangSlot(Transform parent, XinxiangSkillDefinition def, Vector2 pos, Vector2 size)
        {
            var slot = new XinxiangSlotUi { Id = def.Id };

            var go = new GameObject("Xinxiang_" + def.Id, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var frame = go.AddComponent<Image>();
            frame.sprite = GameScreenCanvas.UiWhiteSprite;
            frame.color = Color.Lerp(def.FrameColor, Color.black, 0.45f);
            slot.Frame = frame;

            var btn = go.AddComponent<Button>();
            btn.targetGraphic = frame;
            var captured = def.Id;
            btn.onClick.AddListener(() => OnXinxiangSlotClicked(captured));
            slot.Button = btn;

            float iconSize = Mathf.Min(52f, size.y - 12f);
            var iconGo = new GameObject("Icon", typeof(RectTransform));
            iconGo.transform.SetParent(go.transform, false);
            var iconRt = iconGo.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0f, 0.5f);
            iconRt.anchorMax = new Vector2(0f, 0.5f);
            iconRt.pivot = new Vector2(0f, 0.5f);
            iconRt.anchoredPosition = new Vector2(6f, 0f);
            iconRt.sizeDelta = new Vector2(iconSize, iconSize);
            var icon = iconGo.AddComponent<Image>();
            var iconSprite = def.TryLoadIcon();
            if (iconSprite != null)
            {
                icon.sprite = iconSprite;
                icon.color = Color.white;
                icon.preserveAspect = true;
            }
            else
            {
                icon.sprite = GameScreenCanvas.UiWhiteSprite;
                icon.color = def.FrameColor;
            }
            icon.raycastTarget = false;
            slot.Icon = icon;

            var nameGo = new GameObject("Name", typeof(RectTransform));
            nameGo.transform.SetParent(go.transform, false);
            var nameRt = nameGo.GetComponent<RectTransform>();
            nameRt.anchorMin = new Vector2(0f, 0f);
            nameRt.anchorMax = new Vector2(1f, 1f);
            nameRt.offsetMin = new Vector2(iconSize + 12f, 8f);
            nameRt.offsetMax = new Vector2(-6f, -8f);
            var name = nameGo.AddComponent<Text>();
            name.font = ResolveUiFont();
            name.text = def.ShortLabel;
            name.fontSize = 16;
            name.alignment = TextAnchor.MiddleLeft;
            name.color = Color.white;
            name.raycastTarget = false;
            slot.NameText = name;

            var cdGo = new GameObject("CdMask", typeof(RectTransform));
            cdGo.transform.SetParent(go.transform, false);
            var cdRt = cdGo.GetComponent<RectTransform>();
            cdRt.anchorMin = Vector2.zero;
            cdRt.anchorMax = Vector2.one;
            cdRt.offsetMin = Vector2.zero;
            cdRt.offsetMax = Vector2.zero;
            var cdMask = cdGo.AddComponent<Image>();
            cdMask.sprite = GameScreenCanvas.UiWhiteSprite;
            cdMask.color = new Color(0f, 0f, 0f, 0.55f);
            cdMask.raycastTarget = false;
            slot.CdMask = cdMask;

            var cdTextGo = new GameObject("CdText", typeof(RectTransform));
            cdTextGo.transform.SetParent(go.transform, false);
            var cdTextRt = cdTextGo.GetComponent<RectTransform>();
            cdTextRt.anchorMin = Vector2.zero;
            cdTextRt.anchorMax = Vector2.one;
            cdTextRt.offsetMin = Vector2.zero;
            cdTextRt.offsetMax = Vector2.zero;
            var cdText = cdTextGo.AddComponent<Text>();
            cdText.font = ResolveUiFont();
            cdText.alignment = TextAnchor.MiddleCenter;
            cdText.fontSize = 22;
            cdText.color = Color.white;
            cdText.raycastTarget = false;
            slot.CdText = cdText;

            // Chat-bubble sticker sits to the left of the vertical skill and
            // slides in from further left so it does not cover neighbor slots.
            var emojiGo = new GameObject("EmojiBubble", typeof(RectTransform));
            emojiGo.transform.SetParent(go.transform, false);
            var emojiRt = emojiGo.GetComponent<RectTransform>();
            emojiRt.anchorMin = new Vector2(0f, 0.5f);
            emojiRt.anchorMax = new Vector2(0f, 0.5f);
            emojiRt.pivot = new Vector2(1f, 0.5f);
            float staggerY = 0f;
            if (def.Id == XinxiangSkillId.IceDamage)
                staggerY = 10f;
            else if (def.Id == XinxiangSkillId.Heal)
                staggerY = -10f;
            emojiRt.anchoredPosition = new Vector2(-8f, staggerY);
            emojiRt.sizeDelta = new Vector2(140f, 140f);
            var emojiBg = emojiGo.AddComponent<Image>();
            emojiBg.sprite = GameScreenCanvas.UiWhiteSprite;
            emojiBg.color = new Color(1f, 1f, 1f, 0.98f);
            emojiBg.raycastTarget = false;
            emojiBg.enabled = false;
            slot.EmojiBubble = emojiBg;
            slot.EmojiRestPos = emojiRt.anchoredPosition;
            slot.EmojiRestCaptured = true;

            var emojiImgGo = new GameObject("EmojiImage", typeof(RectTransform));
            emojiImgGo.transform.SetParent(emojiGo.transform, false);
            var emojiImgRt = emojiImgGo.GetComponent<RectTransform>();
            emojiImgRt.anchorMin = Vector2.zero;
            emojiImgRt.anchorMax = Vector2.one;
            emojiImgRt.offsetMin = new Vector2(10f, 10f);
            emojiImgRt.offsetMax = new Vector2(-10f, -10f);
            var emojiImg = emojiImgGo.AddComponent<Image>();
            emojiImg.sprite = GameScreenCanvas.UiWhiteSprite;
            emojiImg.color = Color.white;
            emojiImg.preserveAspect = true;
            emojiImg.raycastTarget = false;
            emojiImg.enabled = false;
            slot.EmojiImage = emojiImg;

            return slot;
        }

        void OnXinxiangSlotClicked(XinxiangSkillId id)
        {
            if (_battle == null || _battle.Phase != GamePhase.Battle) return;
            var xinxiang = _battle.Xinxiang;
            if (xinxiang == null) return;

            GameAudio.I?.PlayUiClick();
            if (xinxiang.IsSlotSelected(id))
            {
                xinxiang.CancelSelect();
                RefreshAll();
                return;
            }

            if (xinxiang.BeginSelect(id))
            {
                GameAudio.Ensure()?.PlayXinxiangVoice(id);
                PopEmojiOnSkillClick(id);
            }
            RefreshAll();
        }

        static RectTransform CreateBottomBar(
            Transform parent,
            string name,
            float bottom,
            float height,
            float rightInset,
            Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(12f, bottom);
            rt.offsetMax = new Vector2(-rightInset, bottom + height);
            var img = go.AddComponent<Image>();
            img.sprite = GameScreenCanvas.UiWhiteSprite;
            img.type = Image.Type.Simple;
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        static RectTransform CreateAnchoredPanel(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPos,
            Vector2 size,
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
            var img = go.AddComponent<Image>();
            img.sprite = GameScreenCanvas.UiWhiteSprite;
            img.type = Image.Type.Simple;
            img.color = color;
            img.raycastTarget = true;
            return rt;
        }

        static RectTransform CreateStretchChild(Transform parent, string name, float padding)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padding, padding);
            rt.offsetMax = new Vector2(-padding, -padding);
            return rt;
        }

        /// <summary>
        /// Bottom bar with a horizontal ScrollRect so every spirit card stays selectable
        /// without stretching a tall opaque strip over the board.
        /// </summary>
        static RectTransform CreateHorizontalScrollContent(Transform bar, string contentName)
        {
            var scrollGo = new GameObject("SpiritPoolScroll", typeof(RectTransform));
            scrollGo.transform.SetParent(bar, false);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = Vector2.zero;
            scrollRt.anchorMax = Vector2.one;
            scrollRt.offsetMin = new Vector2(6f, 4f);
            scrollRt.offsetMax = new Vector2(-6f, -4f);

            var scroll = scrollGo.AddComponent<ScrollRect>();
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRt = viewportGo.GetComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = Vector2.zero;
            viewportGo.AddComponent<RectMask2D>();
            var viewportImg = viewportGo.AddComponent<Image>();
            viewportImg.color = new Color(1f, 1f, 1f, 0.01f);
            viewportImg.raycastTarget = true;

            var contentGo = new GameObject(contentName, typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 0f);
            contentRt.anchorMax = new Vector2(0f, 1f);
            contentRt.pivot = new Vector2(0f, 0.5f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, 0f);

            var layout = contentGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(2, 2, 0, 0);

            var fitter = contentGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll.viewport = viewportRt;
            scroll.content = contentRt;
            return contentRt;
        }

        static void AddHorizontalLayout(RectTransform root)
        {
            var layout = root.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.padding = new RectOffset(0, 0, 0, 0);
        }

        void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
        }

        void BuildRosterButtons()
        {
            if (_rosterRoot == null) return;

            _rosterButtons.Clear();
            for (int i = _rosterRoot.childCount - 1; i >= 0; i--)
                Destroy(_rosterRoot.GetChild(i).gameObject);

            const float cardWidth = 72f;
            const float cardHeight = 110f;
            const float gap = 6f;

            var pool = _battle.SpiritPool ?? _battle.Roster;
            float x = 0f;
            foreach (var def in pool)
            {
                if (def == null) continue;
                var captured = def;
                var btn = CreateRosterCard(
                    _rosterRoot,
                    def,
                    new Vector2(x, 0f),
                    new Vector2(cardWidth, cardHeight),
                    () => SelectSpirit(captured));

                ColorBlock colors = btn.colors;
                colors.normalColor = Color.Lerp(def.Color, Color.black, 0.32f);
                colors.highlightedColor = Color.Lerp(def.Color, Color.white, 0.16f);
                colors.pressedColor = Color.Lerp(def.Color, Color.black, 0.48f);
                btn.colors = colors;
                _rosterButtons[def.Id] = btn;
                x += cardWidth + gap;
            }
        }

        string FormatRosterLabel(SpiritDefinition def)
        {
            bool onPlayer = _battle.PlayerPlacement.ContainsKey(def.Id);
            bool onEnemy = _battle.EnemyPlacement.ContainsKey(def.Id);
            string mark;
            if (onPlayer && onEnemy) mark = "✓双方 ";
            else if (onPlayer) mark = "✓我方 ";
            else if (onEnemy) mark = "✓敌方 ";
            else mark = "";
            string element = DemoDataFactory.ElementLabel(def.Element);
            string combatType = DemoDataFactory.IsRangedCombatant(def) ? "远程" : "近战";
            // Keep the second line short (e.g. 火·远程) so it never clips in the narrow card.
            return $"{mark}{def.DisplayName}\n{element}·{combatType}";
        }

string GetSpiritPortraitName(SpiritDefinition def)
        {
            if (def == null) return string.Empty;

            switch (def.Id)
            {
                case "p_light_dps": return "Art_KS_1502_00";
                case "p_water_tank": return "Art_KS_1506_00";
                case "p_dark_dps": return "Art_KS_1602_00";
                case "p_fire_dps": return "Art_KS_1605_00";
                case "p_fire_tank": return "Art_KS_1605_01";
                case "p_grass_dps": return "Art_KS_2001_00";
                case "p_light_sup": return "Art_KS_2002_00";
                case "p_beauty_dps": return "Art_KS_2302_00";
                case "p_grass_tank": return "Art_KS_2304_00";
                case "p_water_sup": return "Art_KS_2705_00";
                case "p_competition_dps": return "Art_KS_3304_00";

                case "e_grass_tank1": return "Art_KS_1605_01";
                case "e_grass_tank2": return "Art_KS_1605_00";
                case "e_water_dps1": return "Art_KS_3304_00";
                case "e_water_dps2": return "Art_KS_6701_00";
                case "e_fire_sup": return "Art_KS_2302_00";
                default: return string.Empty;
            }
        }

        Button CreateRosterCard(
            Transform parent,
            SpiritDefinition def,
            Vector2 anchoredPos,
            Vector2 size,
            UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("SpiritCard_" + def.Id);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = size.x;
            layoutElement.preferredHeight = size.y;
            layoutElement.minWidth = size.x;
            layoutElement.minHeight = size.y;

            var background = go.AddComponent<Image>();
            background.sprite = GameScreenCanvas.UiWhiteSprite;
            background.type = Image.Type.Simple;
            background.color = Color.Lerp(def.Color, Color.black, 0.32f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(onClick);

            var portraitGo = new GameObject("Portrait");
            portraitGo.transform.SetParent(go.transform, false);
            var portraitRt = portraitGo.AddComponent<RectTransform>();
            portraitRt.anchorMin = new Vector2(0.06f, 0.30f);
            portraitRt.anchorMax = new Vector2(0.94f, 0.95f);
            portraitRt.offsetMin = Vector2.zero;
            portraitRt.offsetMax = Vector2.zero;

            var portrait = portraitGo.AddComponent<Image>();
            string portraitName = GetSpiritPortraitName(def);
            portrait.sprite = string.IsNullOrEmpty(portraitName)
                ? null
                : Resources.Load<Sprite>("UI/SpiritPortraits/" + portraitName);
            portrait.preserveAspect = true;
            portrait.raycastTarget = false;
            portrait.color = Color.white;
            // Prefer crisp sampling for small roster thumbnails.
            if (portrait.sprite != null && portrait.sprite.texture != null)
                portrait.sprite.texture.filterMode = FilterMode.Bilinear;

            var labelGo = new GameObject("Label");
            labelGo.transform.SetParent(go.transform, false);
            var labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0, 0);
            labelRt.anchorMax = new Vector2(1, 0.30f);
            labelRt.offsetMin = new Vector2(2, 2);
            labelRt.offsetMax = new Vector2(-2, 0);

            var label = labelGo.AddComponent<Text>();
            label.font = ResolveUiFont();
            label.text = FormatRosterLabel(def);
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.color = Color.white;
            label.fontSize = 10;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 8;
            label.resizeTextMaxSize = 10;
            label.raycastTarget = false;
            var outline = labelGo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(1f, -1f);

            return button;
        }

        void SelectSpirit(SpiritDefinition def)
        {
            if (_battle.Phase != GamePhase.Formation) return;

            var formation = _battle.GetComponent<FormationInput>();
            bool sameSelection = _selected != null && _selected.Id == def.Id;

            if (sameSelection)
            {
                _selected = null;
                if (formation != null)
                    formation.SetSelected(null);
                RefreshAll();
                return;
            }

            _selected = def;
            if (formation != null)
                formation.SetSelected(def);
            RefreshAll();
        }


        public void RefreshAll()
        {
            if (_battle == null) return;

            var pool = _battle.SpiritPool ?? _battle.Roster;
            foreach (var def in pool)
            {
                if (def == null) continue;
                if (!_rosterButtons.TryGetValue(def.Id, out var btn)) continue;

                var label = btn.GetComponentInChildren<Text>();
                if (label != null) label.text = FormatRosterLabel(def);
                btn.interactable = _battle.Phase == GamePhase.Formation;
                bool selected = _selected != null && _selected.Id == def.Id;
                btn.transform.localScale = selected ? new Vector3(1.08f, 1.08f, 1f) : Vector3.one;
            }

            RefreshBondRows();

            var xinxiang = _battle.Xinxiang;
            bool selecting = xinxiang != null && xinxiang.IsSelecting;
            if (_energyText != null)
            {
                bool prank = xinxiang != null && xinxiang.IsPrankCalling;
                bool combo = xinxiang != null && xinxiang.IsComboSelecting;
                var selectingDef = selecting && xinxiang.SelectingSkill.HasValue
                    ? xinxiang.GetDefinition(xinxiang.SelectingSkill.Value)
                    : null;
                bool area = combo || (selectingDef != null && selectingDef.IsArea);
                bool canComboHint = selecting && !combo && !prank && selectingDef != null
                    && (selectingDef.Id == XinxiangSkillId.Heal || selectingDef.Id == XinxiangSkillId.Buff);
                bool canPrankHint = selecting && !combo && !prank && selectingDef != null
                    && selectingDef.Id == XinxiangSkillId.Buff
                    && xinxiang.IsReady(XinxiangSkillId.IceDamage);
                bool revenge = xinxiang != null && xinxiang.IsPrankRevengeSelecting;
                if (prank)
                    _energyText.text = "心象：琪亚娜来电中…连点红色按钮挂断！";
                else if (revenge)
                    _energyText.text = "心象：别再打来了！点选敌方单位释放强化冰伤";
                else if (combo)
                    _energyText.text = "心象：【星辉庇护】选我方 → 跟上节奏点按/空格";
                else if (canPrankHint)
                    _energyText.text = "心象：再点【冰伤】琪亚娜来电（彩蛋），或点目标单放增益";
                else if (canComboHint)
                    _energyText.text = "心象：再点另一个（治疗/增益）合阵，或直接点目标单放";
                else if (selecting)
                    _energyText.text = area ? "心象：【选择区域中心单位】" : "心象：【选择目标单位】";
                else
                    _energyText.text = "心象：治疗+增益=合阵；增益+冰伤=搞怪来电";
            }

            if (_phaseText != null)
            {
                _phaseText.text = $"阶段：{PhaseLabel(_battle.Phase)}"
                    + $"\n我方：{_battle.PlayerPlacement.Count}/6"
                    + $"  敌方：{_battle.EnemyPlacement.Count}/6";
            }

            if (_eventText != null)
                _eventText.text = _battle.LastEvent;

            if (_resultText != null)
            {
                _resultText.text = _battle.ResultText;
                _resultText.color = _battle.ResultText.Contains("胜")
                    ? new Color(0.4f, 1f, 0.5f)
                    : new Color(1f, 0.45f, 0.4f);
            }

            if (_startBtn != null)
                _startBtn.interactable = _battle.Phase == GamePhase.Formation
                    && _battle.PlayerPlacement.Count > 0;
            if (_restartBtn != null)
                _restartBtn.interactable = _battle.Phase == GamePhase.Result
                    || _battle.Phase == GamePhase.Formation;
            if (_clearBtn != null)
                _clearBtn.interactable = _battle.Phase == GamePhase.Formation
                    && (_battle.PlayerPlacement.Count > 0 || _battle.EnemyPlacement.Count > 0);

            RefreshXinxiangSlots();
            RefreshCheerMeter();

            if (_selected != null && _eventText != null)
            {
                _eventText.text = _battle.LastEvent
                    + $"  |  已选 {_selected.DisplayName}（蓝区=我方 / 红区=敌方）";
            }
        }

        void RefreshXinxiangSlots()
        {
            var xinxiang = _battle != null ? _battle.Xinxiang : null;
            for (int i = 0; i < _xinxiangSlots.Length; i++)
            {
                var slot = _xinxiangSlots[i];
                if (slot == null || slot.Button == null) continue;

                var def = xinxiang != null ? xinxiang.GetDefinition(slot.Id) : null;
                float cd = xinxiang != null ? xinxiang.GetCooldownRemaining(slot.Id) : 0f;
                bool battle = _battle != null && _battle.Phase == GamePhase.Battle;
                bool casting = xinxiang != null && xinxiang.IsCasting;
                bool prank = xinxiang != null && xinxiang.IsPrankCalling;
                bool ready = battle && xinxiang != null && xinxiang.IsReady(slot.Id);
                bool selected = xinxiang != null && xinxiang.IsSlotSelected(slot.Id);
                bool combo = xinxiang != null && xinxiang.IsComboSelecting;
                bool prankPair = prank && (slot.Id == XinxiangSkillId.Buff || slot.Id == XinxiangSkillId.IceDamage);

                slot.Button.interactable = (ready || selected) && !prank;
                if (slot.CdMask != null)
                    slot.CdMask.enabled = battle && (cd > 0.05f || casting || prank) && !selected && !prankPair;
                if (slot.CdText != null)
                {
                    if (!battle)
                        slot.CdText.text = "";
                    else if (prank && prankPair)
                        slot.CdText.text = "来电";
                    else if ((casting || prank) && !selected)
                        slot.CdText.text = "";
                    else if (cd > 0.05f)
                        slot.CdText.text = Mathf.CeilToInt(cd).ToString();
                    else
                        slot.CdText.text = selected ? (combo ? "合阵" : "选目标") : "";
                }

                if (slot.Frame != null && def != null)
                {
                    Color baseCol = Color.Lerp(def.FrameColor, Color.black, 0.45f);
                    slot.Frame.color = selected
                        ? Color.Lerp(def.FrameColor, Color.white, 0.35f)
                        : baseCol;
                }

                slot.Button.transform.localScale = selected
                    ? new Vector3(1.06f, 1.06f, 1f)
                    : Vector3.one;
            }
        }

        void RefreshCheerMeter()
        {
            var audio = GameAudio.I;
            int n = audio != null ? audio.CheerCount : 0;
            int need = audio != null ? audio.CheerNeededCount : GameAudio.CheerNeeded;
            bool ready = audio != null && audio.IsCheerReady;
            if (need < 1) need = 1;

            if (_cheerFill != null)
            {
                _cheerFill.fillAmount = Mathf.Clamp01((float)n / need);
                _cheerFill.color = ready
                    ? new Color(1.0f, 0.28f, 0.95f, 1f)
                    : new Color(0.18f, 0.92f, 1f, 0.95f);
            }

            if (_cheerLabel != null)
                _cheerLabel.text = ready ? "狂欢中 下一发增强" : $"欢呼 {n}/{need}";
        }

        void BuildCheerMeter(RectTransform actions, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("CheerMeter", typeof(RectTransform));
            go.transform.SetParent(actions, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            labelGo.transform.SetParent(go.transform, false);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0f, 1f);
            labelRt.anchorMax = new Vector2(1f, 1f);
            labelRt.pivot = new Vector2(0.5f, 1f);
            labelRt.anchoredPosition = Vector2.zero;
            labelRt.sizeDelta = new Vector2(0f, 18f);
            var label = labelGo.AddComponent<Text>();
            label.font = ResolveUiFont();
            label.fontSize = 13;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = new Color(0.75f, 0.95f, 1f, 0.95f);
            label.raycastTarget = false;
            label.text = "欢呼 0/2";
            _cheerLabel = label;

            var trackGo = new GameObject("Track", typeof(RectTransform));
            trackGo.transform.SetParent(go.transform, false);
            var trackRt = trackGo.GetComponent<RectTransform>();
            trackRt.anchorMin = Vector2.zero;
            trackRt.anchorMax = Vector2.one;
            trackRt.offsetMin = new Vector2(0f, 2f);
            trackRt.offsetMax = new Vector2(0f, -20f);
            var track = trackGo.AddComponent<Image>();
            track.sprite = GameScreenCanvas.UiWhiteSprite;
            track.color = new Color(0.08f, 0.10f, 0.14f, 0.92f);
            track.raycastTarget = false;

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(trackGo.transform, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            var fill = fillGo.AddComponent<Image>();
            fill.sprite = GameScreenCanvas.UiWhiteSprite;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            fill.fillAmount = 0f;
            fill.color = new Color(0.18f, 0.92f, 1f, 0.95f);
            fill.raycastTarget = false;
            _cheerFill = fill;
        }

        void OnDestroy()
        {
            if (GameAudio.I != null)
                GameAudio.I.CheerMeterChanged -= RefreshCheerMeter;
            if (_battle != null && _battle.Xinxiang != null)
                _battle.Xinxiang.StateChanged -= RefreshAll;
            if (_prankCall != null)
            {
                _prankCall.CallStarted -= OnPrankCallStarted;
                _prankCall.CallEnded -= OnPrankCallEnded;
            }
            if (_emojiRoutine != null)
                StopCoroutine(_emojiRoutine);
            for (int i = 0; i < _slotEmojiRoutines.Length; i++)
            {
                if (_slotEmojiRoutines[i] != null)
                {
                    StopCoroutine(_slotEmojiRoutines[i]);
                    _slotEmojiRoutines[i] = null;
                }
            }
        }

        static string PhaseLabel(GamePhase p)
        {
            switch (p)
            {
                case GamePhase.Formation: return "布阵";
                case GamePhase.Battle: return "战斗中";
                case GamePhase.Result: return "结算";
                default: return p.ToString();
            }
        }

        static RectTransform CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin, Vector2 offsetMax, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var img = go.AddComponent<Image>();
            img.color = color;
            return rt;
        }

        static Text CreateText(Transform parent, string content, Vector2 anchoredPos, int size, FontStyle style,
            Vector2? sizeDelta = null)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta ?? new Vector2(240, 28);
            var text = go.AddComponent<Text>();
            text.font = ResolveUiFont();
            text.text = content;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        static Font ResolveUiFont()
        {
            return RuntimeFont.Resolve();
        }

        static Button CreateButton(Transform parent, string label, Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            return CreateButtonInternal(parent, label, anchoredPos, size, onClick, new Vector2(0, 0), new Vector2(0, 0), new Vector2(0, 0));
        }

        static Button CreateTopButton(Transform parent, string label, Vector2 anchoredPos, Vector2 size, UnityEngine.Events.UnityAction onClick)
        {
            return CreateButtonInternal(parent, label, anchoredPos, size, onClick, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1));
        }

        static Button CreateButtonInternal(
            Transform parent,
            string label,
            Vector2 anchoredPos,
            Vector2 size,
            UnityEngine.Events.UnityAction onClick,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot)
        {
            var go = new GameObject("Btn_" + label);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.sprite = GameScreenCanvas.UiWhiteSprite;
            img.type = Image.Type.Simple;
            img.color = new Color(0.2f, 0.45f, 0.75f, 0.95f);
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
            text.font = ResolveUiFont();
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.fontSize = 15;
            return btn;
        }
    }
}
