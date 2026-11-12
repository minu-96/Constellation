using TMPro;
using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 별자리 이름을 크게, 이야기를 그 아래에 하늘 속 글자로 보여 준다(F-11).
    /// 이야기가 길면 글자 크기를 자동으로 줄여 잘리지 않게 한다.
    /// </summary>
    public class ResultView : MonoBehaviour
    {
        ConstellationConfig m_Config;
        Transform m_Root;
        TextMeshPro m_Name;
        TextMeshPro m_Story;
        bool m_Visible;
        float m_ShowTime;
        float m_Alpha;

        const float k_NameFadeSeconds = 1.2f;
        const float k_StoryDelay = 1.4f;
        /// <summary>이름이 나온 뒤 이야기 글자가 나타나기 시작하기까지의 시간(초)</summary>
        public const float StoryDelaySeconds = k_StoryDelay;
        const float k_CharsPerSecond = 28f;

        public void Init(ConstellationConfig config)
        {
            m_Config = config;
            m_Root = new GameObject("Result Texts").transform;
            m_Root.SetParent(transform, false);

            float distance = config.resultDistance;
            float nameSize = config.resultNameSize;
            m_Name = TextFactory.Create("Constellation Name", m_Root, nameSize, new Vector2(config.resultStoryWidth * 1.3f, nameSize * 1.6f),
                TextAlignmentOptions.Center, config.textAccentColor);
            m_Name.textWrappingMode = TextWrappingModes.NoWrap;
            m_Name.transform.localPosition = new Vector3(0f, distance * 0.02f, distance);

            // 이야기는 이름 아래에서 시작해 아래로 흘러내린다. 글상자 높이 안에서 크기가 자동으로 맞춰진다.
            float storyHeight = distance * 0.42f;
            m_Story = TextFactory.Create("Constellation Story", m_Root, config.resultStorySize, new Vector2(config.resultStoryWidth, storyHeight),
                TextAlignmentOptions.Top, config.textColor);
            TextFactory.EnableAutoSize(m_Story, config.resultStorySize * 0.55f, config.resultStorySize);
            m_Story.lineSpacing = 12f;
            m_Story.transform.localPosition = new Vector3(0f, -nameSize * 0.9f - storyHeight * 0.5f, distance);

            m_Name.alpha = 0f;
            m_Story.alpha = 0f;
            m_Root.gameObject.SetActive(false);
        }

        /// <summary>이름과 이야기를 체험자 정면 하늘에 띄운다.</summary>
        public void Show(string constellationName, string story, Vector3 anchorPosition, Quaternion anchorRotation)
        {
            m_Root.position = anchorPosition;
            m_Root.rotation = anchorRotation;
            m_Name.text = constellationName ?? string.Empty;
            m_Story.text = story ?? string.Empty;
            m_Story.maxVisibleCharacters = 0;
            m_Name.alpha = 0f;
            m_Story.alpha = 0f;
            m_Alpha = 1f;
            m_ShowTime = Time.time;
            m_Visible = true;
            m_Root.gameObject.SetActive(true);
        }

        /// <summary>글자를 서서히 지운다.</summary>
        public void Hide()
        {
            m_Visible = false;
        }

        void Update()
        {
            if (m_Root == null || !m_Root.gameObject.activeSelf)
                return;

            if (!m_Visible)
            {
                m_Alpha = Mathf.MoveTowards(m_Alpha, 0f, Time.deltaTime * 2.5f);
                m_Name.alpha = Mathf.Min(m_Name.alpha, m_Alpha);
                m_Story.alpha = Mathf.Min(m_Story.alpha, m_Alpha);
                if (m_Alpha <= 0f)
                    m_Root.gameObject.SetActive(false);
                return;
            }

            float elapsed = Time.time - m_ShowTime;
            m_Name.alpha = Mathf.Clamp01(elapsed / k_NameFadeSeconds);

            // 이야기는 한 글자씩 나타난다.
            float storyElapsed = elapsed - k_StoryDelay;
            if (storyElapsed > 0f)
            {
                m_Story.alpha = Mathf.Clamp01(storyElapsed / 0.5f);
                m_Story.maxVisibleCharacters = Mathf.FloorToInt(storyElapsed * k_CharsPerSecond);
            }
        }
    }
}
