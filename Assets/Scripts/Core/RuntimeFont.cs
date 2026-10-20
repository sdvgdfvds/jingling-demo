using UnityEngine;

namespace NexusAnima
{
    public static class RuntimeFont
    {
        static Font _font;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _font = null;
        }

        public static Font Resolve(int size = 36)
        {
            if (_font != null) return _font;

            int atlas = size < 16 ? 16 : size;
            try
            {
                _font = Font.CreateDynamicFontFromOSFont(
                    new[] { "Microsoft YaHei UI", "Microsoft YaHei", "DengXian", "SimHei", "SimSun" }, atlas);
            }
            catch
            {
                _font = null;
            }
            if (_font == null)
            {
                try
                {
                    _font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", atlas);
                }
                catch
                {
                    _font = null;
                }
            }
            if (_font == null)
                _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null)
                _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _font;
        }
    }
}
