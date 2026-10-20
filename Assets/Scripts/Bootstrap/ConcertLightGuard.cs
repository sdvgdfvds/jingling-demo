using UnityEngine;

namespace NexusAnima
{
    /// <summary>
    /// Keeps concert additional lights killed except the KEEP mix:
    /// DiscoLight04 headlights + DiscoLight03 washes, intensity capped dark.
    /// </summary>
    public class ConcertLightGuard : MonoBehaviour
    {
        public enum PreviewGroup
        {
            None = 0,
            DiscoLight04 = 1,
            DiscoLight03 = 2,
            DiscoLight05 = 3,
            MirrorBall = 4,
            Other = 5,
        }

        [Header("KEEP 灯组（头灯+染色，暗）")]
        [Tooltip("移动头灯 DiscoLight04")]
        public bool previewDisco04 = true;

        [Tooltip("染色 DiscoLight03")]
        public bool previewDisco03 = true;

        [Tooltip("频闪 DiscoLight05")]
        public bool previewDisco05;

        [Tooltip("镜球 Point")]
        public bool previewMirror;

        [Tooltip("其它漏网 Point")]
        public bool previewOther;

        [Tooltip("-1 = 已勾选组里全部；0、1、2… = 只开这一盏（跨已勾选组）。")]
        public int previewIndex = -1;

        [Range(0.05f, 0.8f)]
        [Tooltip("DiscoLight04 头灯亮度上限。KEEP 默认暗 0.18。")]
        public float previewIntensity = 0.18f;

        [Range(0.05f, 0.8f)]
        [Tooltip("DiscoLight03 染色亮度上限。比头灯更暗，避免 8 盏 100° 叠白。")]
        public float previewDisco03Intensity = 0.07f;

        Light[] lights;
        int[] kinds;
        int[] indexInKind;
        float[] authoredRange;
        float[] authoredIntensity;
        float[] authoredAngle;
        float fitScale = 0.02f;
        Transform[] killedBeams;

        public void CaptureTargets()
        {
            CaptureTargets(fitScale);
        }

        public void CaptureTargets(float scale)
        {
            fitScale = Mathf.Max(0.001f, scale);
            lights = GetComponentsInChildren<Light>(true);
            int n = lights.Length;
            kinds = new int[n];
            indexInKind = new int[n];
            authoredRange = new float[n];
            authoredIntensity = new float[n];
            authoredAngle = new float[n];

            int[] nextIndex = new int[6];
            for (int i = 0; i < n; i++)
            {
                var light = lights[i];
                if (light == null || light.type == LightType.Directional)
                {
                    kinds[i] = 0;
                    continue;
                }

                int kind = Classify(light);
                kinds[i] = kind;
                indexInKind[i] = nextIndex[kind];
                nextIndex[kind]++;

                authoredRange[i] = Mathf.Max(light.range, 0.05f);
                authoredIntensity[i] = Mathf.Max(light.intensity, 0.05f);
                authoredAngle[i] = light.type == LightType.Spot ? light.spotAngle : 0f;
            }

            CacheKilledBeams();
            ApplyPreview();
            ConcertLightPreviewHud.Hide();
        }

        public void ApplyKeepMix()
        {
            previewDisco04 = true;
            previewDisco03 = true;
            previewDisco05 = false;
            previewMirror = false;
            previewOther = false;
            previewIndex = -1;
            previewIntensity = 0.18f;
            previewDisco03Intensity = PlayerPrefs.GetFloat(HudPanelHub.PrefIntensityDisco03, 0.07f);
        }

        public void HideHud()
        {
            ConcertLightPreviewHud.Hide();
        }

        public void ClearPreview()
        {
            previewDisco04 = false;
            previewDisco03 = false;
            previewDisco05 = false;
            previewMirror = false;
            previewOther = false;
        }

        public void SetAllPreview(bool on)
        {
            previewDisco04 = on;
            previewDisco03 = on;
            previewDisco05 = on;
            previewMirror = on;
            previewOther = on;
        }

        public void ToggleGroup(int kind)
        {
            switch (kind)
            {
                case (int)PreviewGroup.DiscoLight04: previewDisco04 = !previewDisco04; break;
                case (int)PreviewGroup.DiscoLight03: previewDisco03 = !previewDisco03; break;
                case (int)PreviewGroup.DiscoLight05: previewDisco05 = !previewDisco05; break;
                case (int)PreviewGroup.MirrorBall: previewMirror = !previewMirror; break;
                case (int)PreviewGroup.Other: previewOther = !previewOther; break;
            }
        }

        public bool GroupEnabled(int kind)
        {
            switch (kind)
            {
                case (int)PreviewGroup.DiscoLight04: return previewDisco04;
                case (int)PreviewGroup.DiscoLight03: return previewDisco03;
                case (int)PreviewGroup.DiscoLight05: return previewDisco05;
                case (int)PreviewGroup.MirrorBall: return previewMirror;
                case (int)PreviewGroup.Other: return previewOther;
                default: return false;
            }
        }

        public bool AnyPreview()
        {
            return previewDisco04 || previewDisco03 || previewDisco05 || previewMirror || previewOther;
        }

        public bool AllPreview()
        {
            return previewDisco04 && previewDisco03 && previewDisco05 && previewMirror && previewOther;
        }

        public int PreviewMask()
        {
            int mask = 0;
            if (previewDisco04) mask |= 1;
            if (previewDisco03) mask |= 2;
            if (previewDisco05) mask |= 4;
            if (previewMirror) mask |= 8;
            if (previewOther) mask |= 16;
            return mask;
        }

        void LateUpdate()
        {
            HideKilledBeams();
            ApplyPreview();
        }

        void ApplyPreview()
        {
            if (lights == null) return;
            float cap04 = Mathf.Clamp(previewIntensity, 0.05f, 0.8f);
            float cap03 = Mathf.Clamp(previewDisco03Intensity, 0.05f, 0.8f);
            float rangeMul = fitScale * 4f;

            for (int i = 0; i < lights.Length; i++)
            {
                var light = lights[i];
                if (light == null || light.type == LightType.Directional)
                    continue;

                light.cookie = null;
                light.shadows = LightShadows.None;

                bool on = GroupEnabled(kinds[i])
                    && (previewIndex < 0 || indexInKind[i] == previewIndex);

                if (!on)
                {
                    KillVolume(light);
                    continue;
                }

                light.cullingMask = ~0;
                light.range = Mathf.Clamp(authoredRange[i] * rangeMul, 0.6f, 2.2f);
                if (light.type == LightType.Spot)
                {
                    float ang = authoredAngle[i] > 1f ? authoredAngle[i] : 10f;
                    light.spotAngle = ang;
                }

                float cap = kinds[i] == (int)PreviewGroup.DiscoLight03 ? cap03 : cap04;
                if (light.intensity > cap)
                    light.intensity = cap;
                if (light.intensity < 0.02f)
                    light.intensity = cap;
                light.enabled = true;
            }
        }

        static void KillVolume(Light light)
        {
            light.enabled = false;
            light.intensity = 0f;
            light.range = 0.05f;
            if (light.type == LightType.Spot)
            {
                light.spotAngle = 1f;
                light.innerSpotAngle = 0f;
            }
            light.cullingMask = 0;
        }

        void CacheKilledBeams()
        {
            var transforms = GetComponentsInChildren<Transform>(true);
            int beamCount = 0;
            for (int i = 0; i < transforms.Length; i++)
            {
                if (IsKilledBeam(transforms[i].gameObject.name))
                    beamCount++;
            }
            killedBeams = new Transform[beamCount];
            int w = 0;
            for (int i = 0; i < transforms.Length; i++)
            {
                if (!IsKilledBeam(transforms[i].gameObject.name)) continue;
                killedBeams[w] = transforms[i];
                transforms[i].gameObject.SetActive(false);
                w++;
            }
        }

        void HideKilledBeams()
        {
            if (killedBeams == null) return;
            for (int i = 0; i < killedBeams.Length; i++)
            {
                if (killedBeams[i] != null && killedBeams[i].gameObject.activeSelf)
                    killedBeams[i].gameObject.SetActive(false);
            }
        }

        static bool IsKilledBeam(string n)
        {
            return n == "Cone" || n == "Lines" || n == "LaserBeams01"
                || n == "LightBeam01" || n.StartsWith("LightBeam");
        }

        static int Classify(Light light)
        {
            Transform t = light.transform;
            int guard = 0;
            while (t != null && guard < 12)
            {
                string n = t.name;
                if (n.StartsWith("DiscoLight04")) return (int)PreviewGroup.DiscoLight04;
                if (n.StartsWith("DiscoLight03")) return (int)PreviewGroup.DiscoLight03;
                if (n.StartsWith("DiscoLight05")) return (int)PreviewGroup.DiscoLight05;
                if (n.StartsWith("MirrorBall")) return (int)PreviewGroup.MirrorBall;
                t = t.parent;
                guard++;
            }
            return (int)PreviewGroup.Other;
        }
    }
}
