using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NexusAnima
{
    /// <summary>
    /// Formation rhythm QTE (osu!/音游-style):
    /// notes come from librosa-extracted beat/accent charts (variable intervals).
    /// Different BGM positions pick different chart slices — not a fixed 4-tap metronome.
    /// </summary>
    public static class RhythmFormationDemo
    {
        public const int MaxNotes = 8;

        // Rhythm-game style windows (seconds). Roughly osu! OD4–5 casual.
        public const float PerfectWindow = 0.050f;
        public const float GoodWindow = 0.140f;
        public const float ApproachSec = 1.05f;

        const string SpriteRoot = "UI/Rhythm/";
        const int RhythmCanvasSort = 1200;

        static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>();

        public static float Bpm => BattleBeatmap.Bpm;
        public static float BeatOffsetSec => BattleBeatmap.OffsetSec;

        public static IEnumerator Run(
            RectTransform ignoredParent,
            Action<XinxiangQteGrade> onFinished)
        {
            var host = EnsureRhythmHost();
            if (host == null)
            {
                onFinished?.Invoke(XinxiangQteGrade.Miss);
                yield break;
            }

            if (GameAudio.I != null && GameAudio.I.IsBgmPaused)
                GameAudio.I.ResumeBgm();

            BattleBeatmap.Reload(); // pick up freshly extracted JSON

            var hudBlock = PushHudBlock();
            var ui = BuildUi(host);
            GameAudio.I?.PlayQteOpen();

            float songNow = GameAudio.I != null
                ? BattleBeatmap.NormalizeSongTime(GameAudio.I.GetBgmSongTime())
                : 0f;
            // Chart slice for THIS song position — intervals vary (accents / uneven beats).
            var chart = BattleBeatmap.PickChart(songNow, leadSec: 0.9f);
            float[] hitTimes = chart.Times;
            int[] kinds = chart.Kinds;
            int noteCount = hitTimes != null ? hitTimes.Length : 0;
            if (noteCount < 3)
            {
                hitTimes = BattleBeatmap.NextBeats(songNow, 4, 1);
                kinds = new int[hitTimes.Length];
                noteCount = hitTimes.Length;
            }
            noteCount = Mathf.Min(noteCount, MaxNotes);

            var notes = new Note[noteCount];
            var intervalLog = new System.Text.StringBuilder();
            for (int i = 0; i < noteCount; i++)
            {
                bool accent = kinds != null && i < kinds.Length && kinds[i] == 1;
                notes[i] = new Note
                {
                    HitSongTime = hitTimes[i],
                    IsAccent = accent,
                    Image = ui.NoteImages[i],
                    Approach = ui.ApproachImages != null && i < ui.ApproachImages.Length
                        ? ui.ApproachImages[i] : null,
                    Resolved = false,
                    Grade = XinxiangQteGrade.Miss
                };
                if (notes[i].Image != null)
                {
                    notes[i].Image.enabled = false;
                    // Accent notes: warmer pink; downbeats: cyan.
                    notes[i].Image.color = accent
                        ? new Color(1f, 0.55f, 0.85f, 1f)
                        : Color.white;
                    notes[i].Image.rectTransform.sizeDelta = accent
                        ? new Vector2(48f, 48f)
                        : new Vector2(56f, 56f);
                }
                if (notes[i].Approach != null)
                    notes[i].Approach.enabled = false;

                if (i > 0)
                {
                    if (intervalLog.Length > 0) intervalLog.Append(',');
                    intervalLog.Append((hitTimes[i] - hitTimes[i - 1]).ToString("F3"));
                }
            }

            // Hide unused note slots / dots
            if (ui.NoteImages != null)
            {
                for (int i = noteCount; i < ui.NoteImages.Length; i++)
                    if (ui.NoteImages[i] != null) ui.NoteImages[i].enabled = false;
            }
            if (ui.Dots != null)
            {
                for (int i = 0; i < ui.Dots.Length; i++)
                {
                    if (ui.Dots[i] == null) continue;
                    ui.Dots[i].enabled = i < noteCount;
                }
            }

            Debug.Log(
                $"[Rhythm] librosa chart#{chart.Index} tool={BattleBeatmap.Tool} bpm={BattleBeatmap.Bpm:F1} " +
                $"t={songNow:F2} notes={noteCount} intervals=[{intervalLog}] " +
                $"times=[{string.Join(",", Array.ConvertAll(hitTimes, t => t.ToString("F2")))}]");

            int cursor = 0;
            float safety = 12f;
            int resolvedCount = 0;

            while (safety > 0f && resolvedCount < noteCount)
            {
                safety -= Time.unscaledDeltaTime;
                songNow = GameAudio.I != null
                    ? BattleBeatmap.NormalizeSongTime(GameAudio.I.GetBgmSongTime())
                    : songNow + Time.unscaledDeltaTime;

                for (int i = 0; i < noteCount; i++)
                {
                    var n = notes[i];
                    if (n.Resolved)
                    {
                        if (n.Image != null) n.Image.enabled = false;
                        if (n.Approach != null) n.Approach.enabled = false;
                        continue;
                    }

                    float dt = BattleBeatmap.SignedDelta(songNow, n.HitSongTime);
                    bool show = dt <= ApproachSec + 0.05f && dt >= -GoodWindow;
                    if (n.Image != null)
                    {
                        n.Image.enabled = show;
                        if (show)
                        {
                            float approach = Mathf.Clamp01(1f - dt / ApproachSec);
                            float x = Mathf.Lerp(ui.SpawnX, ui.ReceptorX, approach);
                            n.Image.rectTransform.anchoredPosition = new Vector2(x, 0f);
                            float s = Mathf.Lerp(0.85f, 1.08f, 1f - Mathf.Clamp01(Mathf.Abs(dt) / GoodWindow));
                            n.Image.rectTransform.localScale = Vector3.one * s;
                        }
                    }

                    if (n.Approach != null)
                    {
                        bool showAp = dt > 0f && dt <= ApproachSec;
                        n.Approach.enabled = showAp;
                        if (showAp)
                        {
                            float t = 1f - Mathf.Clamp01(dt / ApproachSec);
                            float scale = Mathf.Lerp(2.2f, 1.0f, t);
                            n.Approach.rectTransform.anchoredPosition = new Vector2(ui.ReceptorX, 0f);
                            n.Approach.rectTransform.localScale = Vector3.one * scale;
                            var c = n.Approach.color;
                            c.a = Mathf.Lerp(0.25f, 0.95f, t);
                            // Accent approach slightly pinker
                            if (n.IsAccent)
                                n.Approach.color = new Color(1f, 0.6f, 0.9f, c.a);
                            else
                                n.Approach.color = new Color(0.5f, 0.9f, 1f, c.a);
                        }
                    }
                }

                while (cursor < noteCount && !notes[cursor].Resolved)
                {
                    float late = -BattleBeatmap.SignedDelta(songNow, notes[cursor].HitSongTime);
                    if (late > GoodWindow)
                    {
                        ResolveNote(notes, cursor, XinxiangQteGrade.Miss, ui, softSfx: true);
                        cursor++;
                        resolvedCount++;
                    }
                    else break;
                }

                if (WasPressed())
                {
                    int best = -1;
                    float bestErr = float.MaxValue;
                    for (int i = 0; i < noteCount; i++)
                    {
                        if (notes[i].Resolved) continue;
                        float err = Mathf.Abs(BattleBeatmap.SignedDelta(songNow, notes[i].HitSongTime));
                        if (err <= GoodWindow && err < bestErr)
                        {
                            bestErr = err;
                            best = i;
                        }
                    }

                    if (best >= 0)
                    {
                        var g = bestErr <= PerfectWindow
                            ? XinxiangQteGrade.Perfect
                            : XinxiangQteGrade.Good;
                        ResolveNote(notes, best, g, ui, softSfx: true);
                        resolvedCount++;
                        if (best == cursor) cursor = best + 1;
                        while (cursor < noteCount && notes[cursor].Resolved) cursor++;
                    }
                }

                UpdateProgress(ui, notes);
                if (AllResolved(notes)) break;
                yield return null;
            }

            for (int i = 0; i < noteCount; i++)
            {
                if (!notes[i].Resolved)
                    ResolveNote(notes, i, XinxiangQteGrade.Miss, ui, softSfx: false);
            }

            var finalGrade = Aggregate(notes);
            ShowFinal(ui, finalGrade);
            GameAudio.I?.PlayQteResult(finalGrade);
            yield return new WaitForSecondsRealtime(0.6f);

            PopHudBlock(hudBlock);
            if (ui.Root != null)
                Object.Destroy(ui.Root.gameObject);

            onFinished?.Invoke(finalGrade);
        }

        // --- Host canvas above DemoUI (ControlsLayer) ---

        static RectTransform EnsureRhythmHost()
        {
            var existing = GameObject.Find("RhythmOverlayCanvas");
            GameObject go;
            if (existing != null)
            {
                go = existing;
            }
            else
            {
                go = new GameObject("RhythmOverlayCanvas", typeof(RectTransform));
                if (Application.isPlaying)
                    Object.DontDestroyOnLoad(go);
            }

            var canvas = go.GetComponent<Canvas>();
            if (canvas == null) canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = RhythmCanvasSort;
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

            var rt = go.GetComponent<RectTransform>();
            GameScreenCanvas.StretchFull(rt);
            return rt;
        }

        /// <summary>Dim spirit pool + action bar and stop them eating clicks.</summary>
        static List<CanvasGroup> PushHudBlock()
        {
            var touched = new List<CanvasGroup>();
            var demo = GameObject.Find("DemoUIRoot");
            if (demo != null)
            {
                var cg = demo.GetComponent<CanvasGroup>();
                if (cg == null) cg = demo.AddComponent<CanvasGroup>();
                cg.alpha = 0.22f;
                cg.blocksRaycasts = false;
                cg.interactable = false;
                touched.Add(cg);
            }

            // Also mute ControlsLayer raycasts if present.
            if (GameScreenCanvas.ControlsLayer != null)
            {
                var cg = GameScreenCanvas.ControlsLayer.GetComponent<CanvasGroup>();
                if (cg == null) cg = GameScreenCanvas.ControlsLayer.gameObject.AddComponent<CanvasGroup>();
                // Don't zero alpha of whole controls if DemoUI already dimmed; just block rays.
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

        // --- Judgement ---

        static XinxiangQteGrade Aggregate(Note[] notes)
        {
            int perfect = 0, good = 0, miss = 0, n = notes.Length;
            for (int i = 0; i < n; i++)
            {
                switch (notes[i].Grade)
                {
                    case XinxiangQteGrade.Perfect: perfect++; break;
                    case XinxiangQteGrade.Good: good++; break;
                    default: miss++; break;
                }
            }

            // Scale thresholds with note count (3–6 typical).
            int perfectNeed = Mathf.Max(2, (n * 3 + 3) / 4); // ~75%
            int hitNeed = Mathf.Max(2, n - 1);
            if (miss == 0 && perfect >= perfectNeed) return XinxiangQteGrade.Perfect;
            if (miss <= 1 && (perfect + good) >= hitNeed) return XinxiangQteGrade.Good;
            return XinxiangQteGrade.Miss;
        }

        static void ResolveNote(Note[] notes, int index, XinxiangQteGrade grade, Ui ui, bool softSfx)
        {
            if (index < 0 || index >= notes.Length || notes[index].Resolved) return;
            notes[index].Resolved = true;
            notes[index].Grade = grade;
            if (notes[index].Image != null) notes[index].Image.enabled = false;
            if (notes[index].Approach != null) notes[index].Approach.enabled = false;

            if (ui.JudgementText != null)
            {
                ui.JudgementText.enabled = true;
                switch (grade)
                {
                    case XinxiangQteGrade.Perfect:
                        ui.JudgementText.text = "PERFECT";
                        ui.JudgementText.color = new Color(1f, 0.92f, 0.35f, 1f);
                        break;
                    case XinxiangQteGrade.Good:
                        ui.JudgementText.text = "GOOD";
                        ui.JudgementText.color = new Color(0.45f, 1f, 0.75f, 1f);
                        break;
                    default:
                        ui.JudgementText.text = "MISS";
                        ui.JudgementText.color = new Color(1f, 0.45f, 0.55f, 1f);
                        break;
                }
            }

            if (ui.Flash != null)
            {
                string key = grade == XinxiangQteGrade.Perfect ? "FlashPerfect"
                    : grade == XinxiangQteGrade.Good ? "FlashGood" : "FlashMiss";
                var spr = LoadSprite(key);
                if (spr != null) ui.Flash.sprite = spr;
                ui.Flash.enabled = true;
                ui.Flash.rectTransform.localScale = Vector3.one * 0.9f;
                ui.Flash.color = Color.white;
            }

            if (softSfx)
            {
                switch (grade)
                {
                    case XinxiangQteGrade.Perfect: GameAudio.I?.PlaySfx("QtePerfect", 0.55f); break;
                    case XinxiangQteGrade.Good: GameAudio.I?.PlaySfx("QteGood", 0.5f); break;
                    default: GameAudio.I?.PlaySfx("QteMiss", 0.4f); break;
                }
            }

            if (ui.Receptor != null)
            {
                float punch = grade == XinxiangQteGrade.Perfect ? 1.22f
                    : grade == XinxiangQteGrade.Good ? 1.12f : 0.9f;
                ui.Receptor.rectTransform.localScale = Vector3.one * punch;
            }
        }

        static void ShowFinal(Ui ui, XinxiangQteGrade grade)
        {
            if (ui.FinalBadge != null)
            {
                string key = grade == XinxiangQteGrade.Perfect ? "BadgePerfect"
                    : grade == XinxiangQteGrade.Good ? "BadgeGood" : "BadgeMiss";
                var spr = LoadSprite(key);
                if (spr != null)
                {
                    ui.FinalBadge.sprite = spr;
                    ui.FinalBadge.enabled = true;
                    ui.FinalBadge.color = Color.white;
                }
            }

            if (ui.FinalText != null)
            {
                ui.FinalText.enabled = true;
                switch (grade)
                {
                    case XinxiangQteGrade.Perfect:
                        ui.FinalText.text = "合阵 · PERFECT";
                        ui.FinalText.color = new Color(1f, 0.92f, 0.35f, 1f);
                        break;
                    case XinxiangQteGrade.Good:
                        ui.FinalText.text = "合阵 · GOOD";
                        ui.FinalText.color = new Color(0.45f, 1f, 0.75f, 1f);
                        break;
                    default:
                        ui.FinalText.text = "合阵 · MISS";
                        ui.FinalText.color = new Color(1f, 0.5f, 0.55f, 1f);
                        break;
                }
            }

            if (ui.JudgementText != null) ui.JudgementText.enabled = false;
            if (ui.Flash != null) ui.Flash.enabled = false;
        }

        static void UpdateProgress(Ui ui, Note[] notes)
        {
            if (ui.Dots == null) return;
            for (int i = 0; i < ui.Dots.Length && i < notes.Length; i++)
            {
                if (ui.Dots[i] == null) continue;
                if (!notes[i].Resolved)
                {
                    var off = LoadSprite("DotOff");
                    if (off != null) ui.Dots[i].sprite = off;
                    ui.Dots[i].color = new Color(1f, 1f, 1f, 0.55f);
                    continue;
                }

                var on = LoadSprite("DotOn");
                if (on != null) ui.Dots[i].sprite = on;
                switch (notes[i].Grade)
                {
                    case XinxiangQteGrade.Perfect:
                        ui.Dots[i].color = new Color(1f, 0.9f, 0.35f, 1f);
                        break;
                    case XinxiangQteGrade.Good:
                        ui.Dots[i].color = new Color(0.45f, 1f, 0.7f, 1f);
                        break;
                    default:
                        ui.Dots[i].color = new Color(1f, 0.4f, 0.5f, 0.9f);
                        break;
                }
            }

            if (ui.Receptor != null)
            {
                float s = ui.Receptor.rectTransform.localScale.x;
                float next = Mathf.Lerp(s, 1f, 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
                ui.Receptor.rectTransform.localScale = Vector3.one * next;
            }

            if (ui.Flash != null && ui.Flash.enabled)
            {
                var c = ui.Flash.color;
                c.a = Mathf.MoveTowards(c.a, 0f, Time.unscaledDeltaTime * 2.2f);
                ui.Flash.color = c;
                if (c.a <= 0.02f) ui.Flash.enabled = false;
            }
        }

        static bool AllResolved(Note[] notes)
        {
            for (int i = 0; i < notes.Length; i++)
                if (!notes[i].Resolved) return false;
            return true;
        }

        static bool WasPressed()
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.KeypadEnter))
                return true;
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
                return true;
            if (Input.touchCount > 0)
            {
                for (int i = 0; i < Input.touchCount; i++)
                    if (Input.GetTouch(i).phase == TouchPhase.Began)
                        return true;
            }
            return false;
        }

        // --- Anime UI build ---

        static Ui BuildUi(RectTransform parent)
        {
            var ui = new Ui();
            // Center-screen highway — clear of bottom spirit pool and right action dock.
            ui.ReceptorX = -200f;
            ui.SpawnX = 340f;

            var rootGo = new GameObject("RhythmFormationDemo", typeof(RectTransform));
            rootGo.transform.SetParent(parent, false);
            ui.Root = rootGo.GetComponent<RectTransform>();
            GameScreenCanvas.StretchFull(ui.Root);
            ui.Root.SetAsLastSibling();

            // Soft vignette so notes read without covering the whole scene.
            var dimGo = new GameObject("SoftDim", typeof(RectTransform), typeof(Image));
            dimGo.transform.SetParent(ui.Root, false);
            var dimRt = dimGo.GetComponent<RectTransform>();
            GameScreenCanvas.StretchFull(dimRt);
            var dimImg = dimGo.GetComponent<Image>();
            dimImg.color = new Color(0.05f, 0.02f, 0.1f, 0.28f);
            dimImg.raycastTarget = true; // soak clicks so nothing under receives them

            // Highway panel — vertical center (二次元音游 lane).
            var barGo = new GameObject("Highway", typeof(RectTransform), typeof(Image));
            barGo.transform.SetParent(ui.Root, false);
            var barRt = barGo.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0.5f, 0.5f);
            barRt.anchorMax = new Vector2(0.5f, 0.5f);
            barRt.pivot = new Vector2(0.5f, 0.5f);
            barRt.anchoredPosition = new Vector2(0f, 36f);
            barRt.sizeDelta = new Vector2(720f, 130f);
            var barImg = barGo.GetComponent<Image>();
            var hwy = LoadSprite("Highway");
            if (hwy != null)
            {
                barImg.sprite = hwy;
                barImg.type = Image.Type.Sliced;
                barImg.color = Color.white;
            }
            else
            {
                barImg.color = new Color(0.12f, 0.06f, 0.18f, 0.75f);
            }
            barImg.raycastTarget = false;

            var laneGo = new GameObject("Lane", typeof(RectTransform));
            laneGo.transform.SetParent(barGo.transform, false);
            var laneRt = laneGo.GetComponent<RectTransform>();
            laneRt.anchorMin = laneRt.anchorMax = laneRt.pivot = new Vector2(0.5f, 0.5f);
            laneRt.sizeDelta = new Vector2(680f, 80f);
            laneRt.anchoredPosition = Vector2.zero;

            // Receptor
            ui.Receptor = MakeImage("Receptor", laneGo.transform, LoadSprite("Receptor"),
                new Vector2(72f, 72f), new Vector2(ui.ReceptorX, 0f));
            if (ui.Receptor.sprite == null)
                ui.Receptor.color = new Color(1f, 0.85f, 0.4f, 0.95f);
            else
                ui.Receptor.color = Color.white;

            // Approach rings + notes (pool of MaxNotes)
            ui.ApproachImages = new Image[MaxNotes];
            for (int i = 0; i < MaxNotes; i++)
            {
                ui.ApproachImages[i] = MakeImage("Approach" + i, laneGo.transform,
                    LoadSprite("Approach") ?? LoadSprite("approachcircle"),
                    new Vector2(72f, 72f), new Vector2(ui.ReceptorX, 0f));
                ui.ApproachImages[i].color = new Color(0.5f, 0.9f, 1f, 0.7f);
                ui.ApproachImages[i].enabled = false;
            }

            ui.NoteImages = new Image[MaxNotes];
            var noteSpr = LoadSprite("Note");
            for (int i = 0; i < MaxNotes; i++)
            {
                ui.NoteImages[i] = MakeImage("Note" + i, laneGo.transform, noteSpr,
                    new Vector2(56f, 56f), new Vector2(ui.SpawnX, 0f));
                ui.NoteImages[i].color = noteSpr != null ? Color.white : new Color(0.6f, 0.85f, 1f, 1f);
                ui.NoteImages[i].enabled = false;
            }

            // Progress dots above highway
            var dotsGo = new GameObject("Dots", typeof(RectTransform));
            dotsGo.transform.SetParent(barGo.transform, false);
            var dotsRt = dotsGo.GetComponent<RectTransform>();
            dotsRt.anchorMin = new Vector2(0.5f, 1f);
            dotsRt.anchorMax = new Vector2(0.5f, 1f);
            dotsRt.pivot = new Vector2(0.5f, 0f);
            dotsRt.anchoredPosition = new Vector2(0f, 8f);
            dotsRt.sizeDelta = new Vector2(220f, 20f);
            ui.Dots = new Image[MaxNotes];
            float spacing = 26f;
            float startX = -((MaxNotes - 1) * spacing) * 0.5f;
            var dotOff = LoadSprite("DotOff");
            for (int i = 0; i < MaxNotes; i++)
            {
                ui.Dots[i] = MakeImage("Dot" + i, dotsGo.transform, dotOff,
                    new Vector2(16f, 16f), new Vector2(startX + i * spacing, 0f));
                ui.Dots[i].color = new Color(1f, 1f, 1f, 0.55f);
                ui.Dots[i].enabled = false;
            }

            // Hint under highway
            var hintGo = new GameObject("Hint", typeof(RectTransform));
            hintGo.transform.SetParent(barGo.transform, false);
            var hintRt = hintGo.GetComponent<RectTransform>();
            hintRt.anchorMin = new Vector2(0.5f, 0f);
            hintRt.anchorMax = new Vector2(0.5f, 0f);
            hintRt.pivot = new Vector2(0.5f, 1f);
            hintRt.anchoredPosition = new Vector2(0f, -6f);
            hintRt.sizeDelta = new Vector2(520f, 28f);
            var hint = hintGo.AddComponent<Text>();
            hint.font = RuntimeFont.Resolve(22);
            hint.fontSize = 18;
            hint.alignment = TextAnchor.MiddleCenter;
            hint.color = new Color(1f, 0.85f, 1f, 0.9f);
            hint.text = "♪ 空格 / 点击  ·  跟上节奏 ♪";
            hint.raycastTarget = false;

            // Flash
            ui.Flash = MakeImage("Flash", ui.Root, LoadSprite("FlashPerfect"),
                new Vector2(220f, 220f), new Vector2(0f, 36f));
            ui.Flash.enabled = false;

            // Judgement text
            var jGo = new GameObject("Judgement", typeof(RectTransform));
            jGo.transform.SetParent(ui.Root, false);
            var jRt = jGo.GetComponent<RectTransform>();
            jRt.anchorMin = jRt.anchorMax = jRt.pivot = new Vector2(0.5f, 0.5f);
            jRt.anchoredPosition = new Vector2(0f, 130f);
            jRt.sizeDelta = new Vector2(360f, 56f);
            ui.JudgementText = jGo.AddComponent<Text>();
            ui.JudgementText.font = RuntimeFont.Resolve(48);
            ui.JudgementText.fontSize = 40;
            ui.JudgementText.fontStyle = FontStyle.Bold;
            ui.JudgementText.alignment = TextAnchor.MiddleCenter;
            ui.JudgementText.raycastTarget = false;
            ui.JudgementText.enabled = false;
            var jOut = jGo.AddComponent<Outline>();
            jOut.effectColor = new Color(0.4f, 0.1f, 0.5f, 0.9f);
            jOut.effectDistance = new Vector2(2f, -2f);

            // Final badge + text
            ui.FinalBadge = MakeImage("FinalBadge", ui.Root, null,
                new Vector2(420f, 100f), new Vector2(0f, 200f));
            ui.FinalBadge.enabled = false;

            var fGo = new GameObject("FinalGrade", typeof(RectTransform));
            fGo.transform.SetParent(ui.Root, false);
            var fRt = fGo.GetComponent<RectTransform>();
            fRt.anchorMin = fRt.anchorMax = fRt.pivot = new Vector2(0.5f, 0.5f);
            fRt.anchoredPosition = new Vector2(0f, 200f);
            fRt.sizeDelta = new Vector2(480f, 64f);
            ui.FinalText = fGo.AddComponent<Text>();
            ui.FinalText.font = RuntimeFont.Resolve(48);
            ui.FinalText.fontSize = 40;
            ui.FinalText.fontStyle = FontStyle.Bold;
            ui.FinalText.alignment = TextAnchor.MiddleCenter;
            ui.FinalText.raycastTarget = false;
            ui.FinalText.enabled = false;
            var fOut = fGo.AddComponent<Outline>();
            fOut.effectColor = new Color(0f, 0f, 0f, 0.9f);
            fOut.effectDistance = new Vector2(2f, -2f);

            // Title chip
            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(ui.Root, false);
            var titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = titleRt.anchorMax = titleRt.pivot = new Vector2(0.5f, 0.5f);
            titleRt.anchoredPosition = new Vector2(0f, 130f);
            titleRt.sizeDelta = new Vector2(400f, 36f);
            // sit above highway slightly — actually place above bar
            titleRt.anchoredPosition = new Vector2(0f, 120f);
            var title = titleGo.AddComponent<Text>();
            title.font = RuntimeFont.Resolve(26);
            title.fontSize = 22;
            title.alignment = TextAnchor.MiddleCenter;
            title.color = new Color(1f, 0.75f, 0.95f, 0.95f);
            title.text = "心象合阵 · 节奏判定";
            title.raycastTarget = false;

            return ui;
        }

        static Image MakeImage(string name, Transform parent, Sprite spr, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var img = go.GetComponent<Image>();
            img.sprite = spr;
            img.raycastTarget = false;
            img.preserveAspect = true;
            return img;
        }

        static Sprite LoadSprite(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (SpriteCache.TryGetValue(name, out var cached) && cached != null)
                return cached;

            // Prefer Unity Sprite import; fall back to Texture2D → Sprite.
            var spr = Resources.Load<Sprite>(SpriteRoot + name);
            if (spr == null)
            {
                var tex = Resources.Load<Texture2D>(SpriteRoot + name);
                if (tex != null)
                {
                    spr = Sprite.Create(
                        tex,
                        new Rect(0f, 0f, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f),
                        100f);
                    spr.name = name;
                }
            }

            if (spr != null)
                SpriteCache[name] = spr;
            return spr;
        }

        struct Note
        {
            public float HitSongTime;
            public bool IsAccent;
            public Image Image;
            public Image Approach;
            public bool Resolved;
            public XinxiangQteGrade Grade;
        }

        class Ui
        {
            public RectTransform Root;
            public Image Receptor;
            public Image[] NoteImages;
            public Image[] ApproachImages;
            public Image[] Dots;
            public Image Flash;
            public Image FinalBadge;
            public Text JudgementText;
            public Text FinalText;
            public float ReceptorX;
            public float SpawnX;
        }
    }
}
