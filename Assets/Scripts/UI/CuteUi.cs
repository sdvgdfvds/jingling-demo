using System.Collections.Generic;
using UnityEngine;

namespace NexusAnima
{
    /// <summary>
    /// Runtime loader for cute / anime glass UI under Resources/UI/CuteUi.
    /// </summary>
    public static class CuteUi
    {
        static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

        public static Sprite LoadAnimeGlass(string fileNameWithoutExt)
        {
            return LoadSprite("UI/CuteUi/AnimeGlass/" + fileNameWithoutExt, border: 18f);
        }

        public static Sprite LoadKenneySprite(string fileNameWithoutExt)
        {
            return LoadSprite("UI/CuteUi/Kenney/" + fileNameWithoutExt, border: 24f);
        }

        public static Sprite LoadPocketSprite(string fileNameWithoutExt)
        {
            return LoadSprite("UI/CuteUi/PocketGui/" + fileNameWithoutExt, border: 4f);
        }

        public static Sprite LoadSprite(string resourcesPath, float border = 16f)
        {
            if (string.IsNullOrEmpty(resourcesPath)) return null;
            if (Cache.TryGetValue(resourcesPath, out var cached) && cached != null)
                return cached;

            var sprite = Resources.Load<Sprite>(resourcesPath);
            if (sprite != null)
            {
                Cache[resourcesPath] = sprite;
                return sprite;
            }

            var tex = Resources.Load<Texture2D>(resourcesPath);
            if (tex == null) return null;

            tex.filterMode = FilterMode.Bilinear;
            var b = new Vector4(border, border, border, border);
            sprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                b);
            Cache[resourcesPath] = sprite;
            return sprite;
        }
    }
}
