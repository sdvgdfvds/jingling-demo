using System;
using System.Collections.Generic;
using UnityEngine;

namespace NexusAnima
{
    /// <summary>
    /// Beat grid extracted from Battle.wav via librosa (beat_track + onset accents).
    /// Not a uniform metronome — beat spacings and chart slices vary across the track.
    /// </summary>
    public static class BattleBeatmap
    {
        const string ResourcesPath = "Audio/Bgm/BattleBeatmap";
        const float MinNoteGap = 0.32f;
        const float MaxFirstWait = 2.15f;

        static bool _loaded;
        static float[] _beats = Array.Empty<float>();
        static float[] _beatStrengths = Array.Empty<float>();
        static float[] _chartStarts = Array.Empty<float>();
        static int[] _chartCounts = Array.Empty<int>();
        static float[] _chartTimes = Array.Empty<float>();
        static int[] _chartKinds = Array.Empty<int>(); // 0=beat 1=accent
        static float _bpm = 123f;
        static float _offsetSec;
        static float _periodSec = 0.485f;
        static float _durationSec = 42.1f;
        static string _tool = "fallback";

        [Serializable]
        class Data
        {
            public string source;
            public string tool;
            public float durationSec;
            public float bpm;
            public float offsetSec;
            public float beatPeriodSec;
            public string method;
            public float[] beats;
            public float[] beatStrengths;
            public float[] chartStarts;
            public int[] chartCounts;
            public float[] chartTimes;
            public int[] chartKinds;
        }

        public static float Bpm { get { EnsureLoaded(); return _bpm; } }
        public static float OffsetSec { get { EnsureLoaded(); return _offsetSec; } }
        public static float PeriodSec { get { EnsureLoaded(); return _periodSec; } }
        public static float DurationSec { get { EnsureLoaded(); return _durationSec; } }
        public static float[] Beats { get { EnsureLoaded(); return _beats; } }
        public static string Tool { get { EnsureLoaded(); return _tool; } }
        public static int ChartCount { get { EnsureLoaded(); return _chartStarts != null ? _chartStarts.Length : 0; } }

        public static void Reload()
        {
            _loaded = false;
            EnsureLoaded();
        }

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;

            var ta = Resources.Load<TextAsset>(ResourcesPath);
            if (ta == null || string.IsNullOrEmpty(ta.text))
            {
                Debug.LogWarning("[BattleBeatmap] missing JSON — fallback uniform grid.");
                BuildFallbackGrid();
                return;
            }

            try
            {
                var data = JsonUtility.FromJson<Data>(ta.text);
                if (data == null || data.beats == null || data.beats.Length < 4)
                {
                    Debug.LogWarning("[BattleBeatmap] invalid JSON — fallback.");
                    BuildFallbackGrid();
                    return;
                }

                _bpm = data.bpm > 1f ? data.bpm : 123f;
                _offsetSec = data.offsetSec;
                _periodSec = data.beatPeriodSec > 0.05f ? data.beatPeriodSec : 60f / _bpm;
                _durationSec = data.durationSec > 1f ? data.durationSec : data.beats[data.beats.Length - 1];
                _beats = data.beats;
                _beatStrengths = data.beatStrengths ?? Array.Empty<float>();
                _chartStarts = data.chartStarts ?? Array.Empty<float>();
                _chartCounts = data.chartCounts ?? Array.Empty<int>();
                _chartTimes = data.chartTimes ?? Array.Empty<float>();
                _chartKinds = data.chartKinds ?? Array.Empty<int>();
                _tool = string.IsNullOrEmpty(data.tool) ? data.method : data.tool;

                // Spacing variance — proof this is not a flat metronome.
                float minSp = 999f, maxSp = 0f;
                for (int i = 1; i < _beats.Length; i++)
                {
                    float d = _beats[i] - _beats[i - 1];
                    if (d < minSp) minSp = d;
                    if (d > maxSp) maxSp = d;
                }

                Debug.Log(
                    $"[BattleBeatmap] tool={_tool} beats={_beats.Length} charts={ChartCount} " +
                    $"bpm={_bpm:F2} spacing=[{minSp:F3}..{maxSp:F3}]s");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[BattleBeatmap] parse failed: " + e.Message);
                BuildFallbackGrid();
            }
        }

        static void BuildFallbackGrid()
        {
            _tool = "fallback-uniform";
            _bpm = 123f;
            _offsetSec = 0.95f;
            _periodSec = 60f / _bpm;
            _durationSec = 42.1f;
            int n = Mathf.FloorToInt((_durationSec - _offsetSec) / _periodSec) + 1;
            _beats = new float[Mathf.Max(8, n)];
            _beatStrengths = new float[_beats.Length];
            for (int i = 0; i < _beats.Length; i++)
            {
                _beats[i] = _offsetSec + i * _periodSec;
                _beatStrengths[i] = 0.5f;
            }
            _chartStarts = Array.Empty<float>();
            _chartCounts = Array.Empty<int>();
            _chartTimes = Array.Empty<float>();
            _chartKinds = Array.Empty<int>();
        }

        public static float NormalizeSongTime(float songTime)
        {
            EnsureLoaded();
            float d = Mathf.Max(0.5f, _durationSec);
            if (songTime < 0f) songTime = 0f;
            if (songTime >= d) songTime %= d;
            return songTime;
        }

        /// <summary>
        /// Pick the chart slice that best matches the current BGM position.
        /// Different song times → different interval patterns (not always 4 equal beats).
        /// Never wrap to a chart that would leave the player waiting several seconds.
        /// Extracted beats end ~35s while the loop is ~42s — schedule from now in that gap.
        /// </summary>
        public static ChartSlice PickChart(float songNow, float leadSec = 0.85f)
        {
            EnsureLoaded();
            songNow = NormalizeSongTime(songNow);

            if (_chartStarts != null && _chartStarts.Length > 0
                && _chartTimes != null && _chartCounts != null)
            {
                int best = -1;
                float bestWait = float.MaxValue;
                bool bestWrapped = false;
                for (int i = 0; i < _chartStarts.Length; i++)
                {
                    float wait = _chartStarts[i] - songNow;
                    bool wrapped = false;
                    if (wait < 0.45f)
                    {
                        wait += _durationSec;
                        wrapped = true;
                    }
                    // Near the loop point, wrapping to chart 0 leaves a long empty approach.
                    if (wrapped && (_durationSec - songNow) < 1.25f)
                        continue;
                    if (wait < 0.45f || wait > MaxFirstWait)
                        continue;
                    if (wait < bestWait)
                    {
                        bestWait = wait;
                        best = i;
                        bestWrapped = wrapped;
                    }
                }

                if (best >= 0)
                {
                    int offset = 0;
                    for (int i = 0; i < best; i++)
                        offset += Mathf.Max(0, _chartCounts[i]);

                    int count = Mathf.Clamp(_chartCounts[best], 3, 8);
                    if (offset + count > _chartTimes.Length)
                        count = Mathf.Max(0, _chartTimes.Length - offset);

                    if (count >= 3)
                    {
                        var times = new float[count];
                        var kinds = new int[count];
                        for (int i = 0; i < count; i++)
                        {
                            float t = _chartTimes[offset + i];
                            if (bestWrapped) t += _durationSec;
                            times[i] = t;
                            kinds[i] = (_chartKinds != null && offset + i < _chartKinds.Length)
                                ? _chartKinds[offset + i] : 0;
                        }

                        var slice = SanitizeSlice(new ChartSlice
                        {
                            Index = best,
                            Times = times,
                            Kinds = kinds
                        }, songNow);
                        if (slice.Times != null && slice.Times.Length >= 3
                            && SignedDelta(songNow, slice.Times[0]) <= MaxFirstWait)
                            return slice;
                    }
                }
            }

            return FallbackFromBeats(songNow, 4, 1);
        }

        /// <summary>Legacy helper — next consecutive extracted beats.</summary>
        public static float[] NextBeats(float songNow, int count, int leadBeats = 1)
        {
            var slice = FallbackFromBeats(songNow, count, leadBeats);
            return slice.Times ?? Array.Empty<float>();
        }

        static ChartSlice FallbackFromBeats(float songNow, int count, int leadBeats)
        {
            EnsureLoaded();
            count = Mathf.Clamp(count, 3, 8);
            leadBeats = Mathf.Max(0, leadBeats);
            songNow = NormalizeSongTime(songNow);

            if (_beats == null || _beats.Length == 0)
                return ScheduleFromNow(songNow, count, leadBeats);

            int idx = 0;
            while (idx < _beats.Length && _beats[idx] <= songNow + 0.001f)
                idx++;
            idx += leadBeats;

            // Extracted beats stop ~7s before the BGM loop. Don't emit a past/far chart.
            if (idx < 0 || idx + count > _beats.Length)
                return ScheduleFromNow(songNow, count, leadBeats);

            float firstWait = _beats[idx] - songNow;
            if (firstWait < 0.45f || firstWait > MaxFirstWait)
                return ScheduleFromNow(songNow, count, leadBeats);

            var times = new float[count];
            var kinds = new int[count];
            for (int i = 0; i < count; i++)
            {
                times[i] = _beats[idx + i];
                kinds[i] = 0;
            }

            return SanitizeSlice(new ChartSlice { Index = -1, Times = times, Kinds = kinds }, songNow);
        }

        static ChartSlice ScheduleFromNow(float songNow, int count, int leadBeats)
        {
            count = Mathf.Clamp(count, 3, 6);
            float period = Mathf.Clamp(_periodSec, 0.36f, 0.62f);
            float lead = Mathf.Max(0.75f, (leadBeats + 1) * period * 0.55f);
            float t0 = songNow + lead;
            if (period > 0.05f)
            {
                float phase = (t0 - _offsetSec) % period;
                if (phase < 0f) phase += period;
                if (phase > 0.001f)
                    t0 += period - phase;
                if (t0 < songNow + 0.7f)
                    t0 += period;
            }

            var times = new float[count];
            var kinds = new int[count];
            for (int i = 0; i < count; i++)
            {
                times[i] = t0 + i * period;
                kinds[i] = i == 0 ? 1 : 0;
            }
            return new ChartSlice { Index = -1, Times = times, Kinds = kinds };
        }

        /// <summary>
        /// Drop unhittable stacked onsets (gap &lt; Good window) and reject a first
        /// note that is already past / several seconds away.
        /// </summary>
        static ChartSlice SanitizeSlice(ChartSlice slice, float songNow)
        {
            if (slice.Times == null || slice.Times.Length == 0)
                return ScheduleFromNow(songNow, 4, 1);

            var times = new List<float>(slice.Times.Length);
            var kinds = new List<int>(slice.Times.Length);
            float last = float.NegativeInfinity;
            for (int i = 0; i < slice.Times.Length; i++)
            {
                float t = slice.Times[i];
                if (t - last < MinNoteGap)
                    continue;
                times.Add(t);
                int kind = (slice.Kinds != null && i < slice.Kinds.Length) ? slice.Kinds[i] : 0;
                kinds.Add(kind);
                last = t;
            }

            if (times.Count < 3)
                return ScheduleFromNow(songNow, 4, 1);

            float wait = SignedDelta(songNow, times[0]);
            if (wait < 0.4f || wait > MaxFirstWait)
                return ScheduleFromNow(songNow, 4, 1);

            return new ChartSlice
            {
                Index = slice.Index,
                Times = times.ToArray(),
                Kinds = kinds.ToArray()
            };
        }

        public static float SignedDelta(float songTime, float beatTime)
        {
            EnsureLoaded();
            float d = Mathf.Max(0.5f, _durationSec);
            float a = NormalizeSongTime(songTime);
            float b = beatTime;
            // Allow beatTime past one loop for scheduled notes.
            while (b - a > d) b -= d;
            while (a - b > d * 0.5f && b + d - a < a - b) b += d;

            float delta = b - a;
            // If still huge, wrap once.
            if (delta > d) delta -= d;
            if (delta < -d * 0.5f) delta += d;
            return delta;
        }

        public struct ChartSlice
        {
            public int Index;
            public float[] Times;
            public int[] Kinds; // 0 beat, 1 accent
        }
    }
}
