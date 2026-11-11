using System.Collections.Generic;
using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 완성된 별자리를 작은 JPEG로 만든다(F-08).
    /// 카메라로 찍지 않고 별 좌표로 2D 그림을 직접 그린다. 배경은 어둡고 선과 별만 밝아서
    /// AI가 모양을 읽기 쉽고, 파일이 작으며, 기기에서 실패할 일이 적다.
    /// </summary>
    public class CaptureService : MonoBehaviour
    {
        ConstellationConfig m_Config;

        static readonly Color k_Background = new Color(0.02f, 0.025f, 0.07f);
        const float k_Margin = 0.14f;

        public void Init(ConstellationConfig config)
        {
            m_Config = config;
        }

        /// <summary>
        /// 별자리를 그려 JPEG로 인코딩한다.
        /// </summary>
        /// <param name="points">이은 순서대로의 별 좌표(오른쪽 +x, 위 +y, 단위는 상관없음)</param>
        /// <param name="jpeg">전송용 JPEG 바이트</param>
        /// <param name="texture">카드 미리보기에 쓰는 텍스처. 쓴 쪽에서 Destroy 해야 한다.</param>
        public bool TryCapture(IList<Vector2> points, out byte[] jpeg, out Texture2D texture)
        {
            jpeg = null;
            texture = null;
            if (points == null || points.Count < 2)
                return false;

            int size = Mathf.Clamp(m_Config.captureSize, 128, 1024);
            float[] light = new float[size * size];
            Vector2[] pixels = FitToImage(points, size);

            float lineHalfWidth = size * 0.006f;
            float lineGlow = size * 0.022f;
            for (int i = 0; i < pixels.Length - 1; i++)
                DrawSegment(light, size, pixels[i], pixels[i + 1], lineHalfWidth, lineGlow);

            float starRadius = size * 0.017f;
            float starGlow = size * 0.05f;
            float[] starLight = new float[size * size];
            for (int i = 0; i < pixels.Length; i++)
                DrawStar(starLight, size, pixels[i], starRadius, starGlow);

            Color lineColor = m_Config.lineColor;
            Color32[] colors = new Color32[size * size];
            for (int i = 0; i < colors.Length; i++)
            {
                float l = light[i];
                float s = starLight[i];
                float r = k_Background.r + lineColor.r * l + s;
                float g = k_Background.g + lineColor.g * l + s;
                float b = k_Background.b + lineColor.b * l + s;
                colors[i] = new Color32(ToByte(r), ToByte(g), ToByte(b), 255);
            }

            texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            texture.name = "Constellation Capture";
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.SetPixels32(colors);
            texture.Apply(false, false);

            // 200KB를 넘으면 품질을 낮춰 다시 인코딩한다(어두운 그림이라 보통 수십 KB다).
            int quality = Mathf.Clamp(m_Config.captureJpegQuality, 30, 100);
            jpeg = texture.EncodeToJPG(quality);
            while (jpeg != null && jpeg.Length > m_Config.captureMaxBytes && quality > 30)
            {
                quality -= 15;
                jpeg = texture.EncodeToJPG(Mathf.Max(quality, 30));
            }

            if (jpeg == null || jpeg.Length == 0 || jpeg.Length > m_Config.captureMaxBytes)
            {
                Destroy(texture);
                texture = null;
                jpeg = null;
                return false;
            }
            return true;
        }

        // 별자리가 그림 가운데에 여백을 두고 꽉 차도록 좌표를 옮긴다. 가로세로 비율은 유지한다.
        static Vector2[] FitToImage(IList<Vector2> points, int size)
        {
            Vector2 min = points[0];
            Vector2 max = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                min = Vector2.Min(min, points[i]);
                max = Vector2.Max(max, points[i]);
            }
            Vector2 center = (min + max) * 0.5f;
            float extent = Mathf.Max(max.x - min.x, max.y - min.y);
            if (extent < 0.0001f)
                extent = 1f;
            float scale = size * (1f - k_Margin * 2f) / extent;

            Vector2[] result = new Vector2[points.Count];
            for (int i = 0; i < points.Count; i++)
                result[i] = (points[i] - center) * scale + new Vector2(size * 0.5f, size * 0.5f);
            return result;
        }

        static void DrawSegment(float[] light, int size, Vector2 a, Vector2 b, float halfWidth, float glow)
        {
            float reach = halfWidth + glow;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, b.x) - reach));
            int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, b.x) + reach));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, b.y) - reach));
            int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, b.y) + reach));

            Vector2 ab = b - a;
            float lengthSqr = Mathf.Max(ab.sqrMagnitude, 0.0001f);
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSqr);
                    float distance = (p - (a + ab * t)).magnitude;
                    float core = Mathf.Clamp01(halfWidth + 1f - distance);
                    float falloff = Mathf.Clamp01(1f - (distance - halfWidth) / glow);
                    float value = Mathf.Max(core, falloff * falloff * 0.35f);
                    int index = y * size + x;
                    if (value > light[index])
                        light[index] = value;
                }
            }
        }

        static void DrawStar(float[] light, int size, Vector2 center, float radius, float glow)
        {
            float reach = radius + glow;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(center.x - reach));
            int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(center.x + reach));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(center.y - reach));
            int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(center.y + reach));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x + 0.5f - center.x;
                    float dy = y + 0.5f - center.y;
                    float distance = Mathf.Sqrt(dx * dx + dy * dy);
                    float core = Mathf.Clamp01(radius + 1f - distance);
                    float falloff = Mathf.Clamp01(1f - (distance - radius) / glow);
                    float value = Mathf.Max(core, falloff * falloff * 0.5f);
                    int index = y * size + x;
                    if (value > light[index])
                        light[index] = value;
                }
            }
        }

        static byte ToByte(float value)
        {
            return (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
        }
    }
}
