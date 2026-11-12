using System;
using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 하늘에 쓰는 데이터를 코드로 만든다(외부 이미지 없음).
    ///  - 능선 높이: 방위각마다 세 겹(먼 산, 가운데, 가까운 언덕)의 높이
    ///  - 은하수 그림: 노이즈로 만든 띠, 먼지 띠, 밝은 중심부
    /// 은하수 계산은 무거워서 백그라운드 스레드에서 돌릴 수 있게 Unity API를 쓰지 않는다.
    /// </summary>
    public static class SkyData
    {
        public const int RidgeResolution = 1024;
        public const int RidgeLayers = 3;
        public const int MilkyWayWidth = 512;
        public const int MilkyWayHeight = 256;

        // 은하수 띠는 이 축에 수직인 큰 원을 따라 놓인다.
        const float k_AxisX = 0.3504f;
        const float k_AxisY = 0.5006f;
        const float k_AxisZ = 0.7916f;

        /// <summary>은하수 띠에 얼마나 가까운지(0~1). 별 밀도를 정할 때도 쓴다.</summary>
        public static float MilkyWayBand(float x, float y, float z)
        {
            float s = (x * k_AxisX + y * k_AxisY + z * k_AxisZ) * 3f;
            return (float)Math.Exp(-s * s);
        }

        // ------------------------------------------------------------------ 능선

        // 겹마다 (가장 낮은 높이, 가장 높은 높이, 거칠기). 높이는 최고 높이에 대한 비율이다.
        static readonly float[,] k_RidgeShape =
        {
            { 0.42f, 1.00f, 0.75f }, // 먼 산: 가장 높고 뾰족하다
            { 0.20f, 0.60f, 0.85f }, // 가운데
            { 0.03f, 0.30f, 1.00f }, // 가까운 언덕: 낮고 완만하다
        };

        static readonly int[] k_RidgeFrequencies = { 2, 3, 5, 8, 13, 21, 34, 55 };

        /// <summary>
        /// 세 겹의 능선 높이(0~1)를 만든다. 정수 주파수의 사인파를 겹쳐 360도에서 이음매 없이 이어진다.
        /// </summary>
        public static float[][] BuildRidges(int seed)
        {
            System.Random random = new System.Random(seed);
            float[][] ridges = new float[RidgeLayers][];
            for (int layer = 0; layer < RidgeLayers; layer++)
            {
                float[] heights = new float[RidgeResolution];
                float rough = k_RidgeShape[layer, 2];
                for (int f = 0; f < k_RidgeFrequencies.Length; f++)
                {
                    int frequency = k_RidgeFrequencies[f];
                    double phase = random.NextDouble() * Math.PI * 2.0;
                    double amplitude = 1.0 / Math.Pow(frequency, rough);
                    for (int i = 0; i < RidgeResolution; i++)
                        heights[i] += (float)(Math.Sin(Math.PI * 2.0 * frequency * i / RidgeResolution + phase) * amplitude);
                }

                float min = float.MaxValue;
                float max = float.MinValue;
                for (int i = 0; i < RidgeResolution; i++)
                {
                    min = Math.Min(min, heights[i]);
                    max = Math.Max(max, heights[i]);
                }
                float low = k_RidgeShape[layer, 0];
                float high = k_RidgeShape[layer, 1];
                float range = Math.Max(0.0001f, max - min);
                for (int i = 0; i < RidgeResolution; i++)
                    heights[i] = low + (high - low) * (heights[i] - min) / range;
                ridges[layer] = heights;
            }
            return ridges;
        }

        /// <summary>능선 높이를 셰이더가 읽는 1줄짜리 텍스처로 만든다(R: 먼 산, G: 가운데, B: 가까운 언덕).</summary>
        public static Texture2D BuildRidgeTexture(float[][] ridges)
        {
            Texture2D texture = new Texture2D(RidgeResolution, 1, TextureFormat.RGBA32, false, true);
            texture.name = "Ridge Heights";
            texture.wrapModeU = TextureWrapMode.Repeat;
            texture.wrapModeV = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            Color32[] pixels = new Color32[RidgeResolution];
            for (int i = 0; i < RidgeResolution; i++)
            {
                pixels[i] = new Color32(
                    (byte)Mathf.RoundToInt(ridges[0][i] * 255f),
                    (byte)Mathf.RoundToInt(ridges[1][i] * 255f),
                    (byte)Mathf.RoundToInt(ridges[2][i] * 255f),
                    255);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>주어진 방위각(라디안, 정면이 0)에서 가장 높은 능선의 높이(0~1).</summary>
        public static float HighestRidgeAt(float[][] ridges, float azimuth)
        {
            float u = azimuth / (Mathf.PI * 2f) + 0.5f;
            u = (u - Mathf.Floor(u)) * RidgeResolution;
            int i0 = Mathf.FloorToInt(u) % RidgeResolution;
            int i1 = (i0 + 1) % RidgeResolution;
            float t = u - Mathf.Floor(u);
            float best = 0f;
            for (int layer = 0; layer < RidgeLayers; layer++)
                best = Mathf.Max(best, Mathf.Lerp(ridges[layer][i0], ridges[layer][i1], t));
            return best;
        }

        // ------------------------------------------------------------------ 은하수

        /// <summary>
        /// 은하수 그림의 픽셀을 만든다. 가로는 방위각, 세로는 방향 벡터의 y(-1~1)다.
        /// Unity API를 쓰지 않으므로 백그라운드 스레드에서 불러도 된다.
        /// </summary>
        public static Color32[] BuildMilkyWayPixels()
        {
            int width = MilkyWayWidth;
            int height = MilkyWayHeight;
            Color32[] pixels = new Color32[width * height];

            // 밝은 중심부의 방향: 띠 위에서 하늘 쪽으로 올라온 한 점
            float cx = -k_AxisZ, cy = 0.45f, cz = k_AxisX;
            float along = cx * k_AxisX + cy * k_AxisY + cz * k_AxisZ;
            cx -= k_AxisX * along; cy -= k_AxisY * along; cz -= k_AxisZ * along;
            float length = (float)Math.Sqrt(cx * cx + cy * cy + cz * cz);
            cx /= length; cy /= length; cz /= length;

            for (int v = 0; v < height; v++)
            {
                float y = (v + 0.5f) / height * 2f - 1f;
                float radius = (float)Math.Sqrt(Math.Max(0f, 1f - y * y));
                for (int u = 0; u < width; u++)
                {
                    double azimuth = ((u + 0.5) / width - 0.5) * Math.PI * 2.0;
                    float x = (float)Math.Sin(azimuth) * radius;
                    float z = (float)Math.Cos(azimuth) * radius;

                    float band = MilkyWayBand(x, y, z);
                    float r = 0f, g = 0f, b = 0f;
                    if (band > 0.003f)
                    {
                        float cloud = Fbm(x * 3.2f + 5f, y * 3.2f + 5f, z * 3.2f + 5f, 4);
                        float lanes = Fbm(x * 7f + 31f, y * 7f + 31f, z * 7f + 31f, 3);
                        float core = (float)Math.Exp(-(1f - Clamp(x * cx + y * cy + z * cz, -1f, 1f)) * 5f);
                        float dust = Clamp((lanes - 0.45f) * 3f, 0f, 1f);
                        float intensity = band * (0.3f + cloud) * (1f - 0.65f * dust) * (0.55f + 0.9f * core) * 0.5f;
                        // 띠는 푸르스름하고 중심부는 따뜻한 색이다.
                        float warm = core * 0.75f;
                        r = (0.50f + (1.00f - 0.50f) * warm) * intensity;
                        g = (0.56f + (0.84f - 0.56f) * warm) * intensity;
                        b = (0.95f + (0.70f - 0.95f) * warm) * intensity;
                    }

                    // 하늘 전체에 아주 옅은 보랏빛 얼룩을 깔아 단색으로 보이지 않게 한다.
                    float haze = Fbm(x * 1.6f + 77f, y * 1.6f + 77f, z * 1.6f + 77f, 3);
                    haze = haze * haze * 0.25f;
                    r += 0.10f * haze;
                    g += 0.05f * haze;
                    b += 0.16f * haze;

                    pixels[v * width + u] = new Color32(ToByte(r), ToByte(g), ToByte(b), 255);
                }
            }
            return pixels;
        }

        static byte ToByte(float value)
        {
            return (byte)(Clamp(value, 0f, 1f) * 255f + 0.5f);
        }

        static float Clamp(float value, float min, float max)
        {
            return value < min ? min : value > max ? max : value;
        }

        static float Fbm(float x, float y, float z, int octaves)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float total = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amplitude * ValueNoise(x, y, z);
                total += amplitude;
                x = x * 2.03f + 11.7f;
                y = y * 2.03f + 11.7f;
                z = z * 2.03f + 11.7f;
                amplitude *= 0.5f;
            }
            return sum / total;
        }

        // 격자점의 난수를 부드럽게 이은 3차원 노이즈(0~1)
        static float ValueNoise(float x, float y, float z)
        {
            int ix = (int)Math.Floor(x);
            int iy = (int)Math.Floor(y);
            int iz = (int)Math.Floor(z);
            float fx = x - ix;
            float fy = y - iy;
            float fz = z - iz;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            fz = fz * fz * (3f - 2f * fz);

            float a = Lerp(Hash(ix, iy, iz), Hash(ix + 1, iy, iz), fx);
            float b = Lerp(Hash(ix, iy + 1, iz), Hash(ix + 1, iy + 1, iz), fx);
            float c = Lerp(Hash(ix, iy, iz + 1), Hash(ix + 1, iy, iz + 1), fx);
            float d = Lerp(Hash(ix, iy + 1, iz + 1), Hash(ix + 1, iy + 1, iz + 1), fx);
            return Lerp(Lerp(a, b, fy), Lerp(c, d, fy), fz);
        }

        static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        static float Hash(int x, int y, int z)
        {
            unchecked
            {
                uint n = (uint)x * 374761393u + (uint)y * 668265263u + (uint)z * 1274126177u;
                n = (n ^ (n >> 13)) * 1274126177u;
                n ^= n >> 16;
                return (n & 0xFFFFFFu) / 16777216f;
            }
        }
    }
}
