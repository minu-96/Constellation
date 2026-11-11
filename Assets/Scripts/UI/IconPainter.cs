using System.Collections.Generic;
using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 키워드 아이콘을 코드로 그린다(외부 이미지 없음).
    /// wish: 반짝이는 별, adventure: 나침반, loved_one: 하트.
    /// 텍스처의 알파가 모양이고, 색은 Glow 셰이더에서 입힌다.
    /// </summary>
    public static class IconPainter
    {
        const int k_Size = 128;
        const int k_Samples = 3;

        static readonly Dictionary<string, Texture2D> s_Cache = new Dictionary<string, Texture2D>();

        public static Texture2D Get(string keywordId)
        {
            string key = keywordId ?? string.Empty;
            Texture2D texture;
            if (s_Cache.TryGetValue(key, out texture) && texture != null)
                return texture;

            texture = new Texture2D(k_Size, k_Size, TextureFormat.RGBA32, false);
            texture.name = "Icon " + key;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Color32[] pixels = new Color32[k_Size * k_Size];
            for (int y = 0; y < k_Size; y++)
            {
                for (int x = 0; x < k_Size; x++)
                {
                    // 가장자리를 부드럽게 하려고 한 픽셀을 여러 번 찍어 평균한다.
                    int hits = 0;
                    for (int sy = 0; sy < k_Samples; sy++)
                    {
                        for (int sx = 0; sx < k_Samples; sx++)
                        {
                            float u = ((x + (sx + 0.5f) / k_Samples) / k_Size) * 2f - 1f;
                            float v = ((y + (sy + 0.5f) / k_Samples) / k_Size) * 2f - 1f;
                            if (Inside(key, u, v))
                                hits++;
                        }
                    }
                    byte alpha = (byte)(255 * hits / (k_Samples * k_Samples));
                    pixels[y * k_Size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            s_Cache[key] = texture;
            return texture;
        }

        // u, v는 -1~1. 위쪽이 +v.
        static bool Inside(string key, float u, float v)
        {
            switch (key)
            {
                case "wish": return Sparkle(u, v);
                case "adventure": return Compass(u, v);
                case "loved_one": return Heart(u, v);
                default: return u * u + v * v < 0.5f;
            }
        }

        // 네 갈래로 뻗는 별빛 + 작은 대각선 빛
        static bool Sparkle(float u, float v)
        {
            float au = Mathf.Abs(u);
            float av = Mathf.Abs(v);
            bool main = Mathf.Sqrt(au) + Mathf.Sqrt(av) < 0.95f;
            float du = Mathf.Abs(u + v) * 0.7071f;
            float dv = Mathf.Abs(u - v) * 0.7071f;
            bool diagonal = Mathf.Sqrt(du) + Mathf.Sqrt(dv) < 0.66f;
            return main || diagonal;
        }

        // 둥근 테두리 + 비스듬한 바늘
        static bool Compass(float u, float v)
        {
            float r = Mathf.Sqrt(u * u + v * v);
            bool ring = r > 0.8f && r < 0.92f;
            // 바늘을 30도 기울인다.
            const float cos = 0.8660f;
            const float sin = 0.5f;
            float nu = u * cos - v * sin;
            float nv = u * sin + v * cos;
            bool needle = Mathf.Abs(nu) / 0.2f + Mathf.Abs(nv) / 0.68f < 1f;
            bool hub = r < 0.07f;
            return ring || (needle && !hub);
        }

        // 하트 곡선 (x² + y² − 1)³ − x²y³ < 0
        static bool Heart(float u, float v)
        {
            float x = u * 1.25f;
            float y = v * 1.25f + 0.15f;
            float a = x * x + y * y - 1f;
            return a * a * a - x * x * y * y * y < 0f;
        }
    }
}
