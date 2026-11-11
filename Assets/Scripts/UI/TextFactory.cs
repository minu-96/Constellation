using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Constellation
{
    /// <summary>
    /// 하늘에 띄우는 글자(TextMeshPro)를 코드로 만든다.
    /// 한글 폰트는 Resources/Fonts/ConstellationKorean.ttf 에서 읽어 실행 중에 동적 폰트 에셋으로 만든다.
    /// </summary>
    public static class TextFactory
    {
        public const string FontResourcePath = "Fonts/ConstellationKorean";

        // 글자 오브젝트의 배율. 글자 크기(m) = fontSize × 0.1 × 배율 이므로 fontSize = 크기(m) × 100.
        const float k_Scale = 0.1f;

        static TMP_FontAsset s_Font;
        static bool s_FontTried;

        public static TMP_FontAsset Font
        {
            get
            {
                if (!s_FontTried)
                {
                    s_FontTried = true;
                    s_Font = CreateKoreanFont();
                }
                return s_Font;
            }
        }

        static TMP_FontAsset CreateKoreanFont()
        {
            Font source = Resources.Load<Font>(FontResourcePath);
            if (source == null)
            {
                // 폰트 파일이 없을 때의 대비책: 기기에 깔린 한글 폰트를 찾는다.
                try
                {
                    source = UnityEngine.Font.CreateDynamicFontFromOSFont(
                        new[] { "Noto Sans CJK KR", "NotoSansCJK-Regular", "Malgun Gothic", "Apple SD Gothic Neo", "NanumGothic" }, 48);
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning("[Constellation] 기기 폰트를 찾지 못했습니다: " + e.Message);
                }
            }

            TMP_FontAsset asset = null;
            if (source != null)
            {
                try
                {
                    asset = TMP_FontAsset.CreateFontAsset(source, 48, 5, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
                }
                catch (System.Exception e)
                {
                    Debug.LogError("[Constellation] 한글 폰트 에셋을 만들지 못했습니다: " + e.Message);
                }
            }

            if (asset == null)
            {
                Debug.LogError("[Constellation] 한글 폰트를 쓸 수 없어 기본 폰트로 표시합니다. 한글이 네모로 보일 수 있습니다. Assets/Resources/Fonts/ConstellationKorean.ttf 를 확인하세요.");
                return TMP_Settings.defaultFontAsset;
            }

            asset.name = "Constellation Korean (runtime)";
            return asset;
        }

        /// <summary>
        /// 월드 공간 글자를 만든다.
        /// </summary>
        /// <param name="sizeMeters">글자 크기(대략 한 글자 높이, m)</param>
        /// <param name="boxMeters">글상자 크기(m). 이 너비에서 줄을 바꾼다.</param>
        public static TextMeshPro Create(string name, Transform parent, float sizeMeters, Vector2 boxMeters, TextAlignmentOptions alignment, Color color)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(k_Scale, k_Scale, k_Scale);

            TextMeshPro text = go.AddComponent<TextMeshPro>();
            TMP_FontAsset font = Font;
            if (font != null)
                text.font = font;
            text.fontSize = sizeMeters * 100f;
            text.alignment = alignment;
            text.color = color;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = false;
            text.raycastTarget = false;
            text.rectTransform.sizeDelta = boxMeters / k_Scale;
            text.text = string.Empty;
            return text;
        }

        /// <summary>글상자 안에 다 들어가도록 글자 크기를 자동으로 줄이게 한다.</summary>
        public static void EnableAutoSize(TextMeshPro text, float minSizeMeters, float maxSizeMeters)
        {
            text.enableAutoSizing = true;
            text.fontSizeMin = minSizeMeters * 100f;
            text.fontSizeMax = maxSizeMeters * 100f;
        }
    }
}
