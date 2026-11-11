using TMPro;
using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 상황별 안내 문구를 하늘에 띄운다(F-15).
    /// 문구는 두 줄이다. 위는 지금 할 일(hint), 아래는 경고나 알림(alert).
    /// 글자판은 시선을 천천히 따라와 어느 쪽을 보든 읽을 수 있다.
    /// </summary>
    public class GuideUI : MonoBehaviour
    {
        ConstellationConfig m_Config;
        Transform m_Head;
        Transform m_Panel;
        TextMeshPro m_Title;
        TextMeshPro m_Hint;
        TextMeshPro m_Alert;

        string m_HintText = string.Empty;
        string m_TitleText = string.Empty;
        string m_StickyAlert = string.Empty;
        string m_FlashAlert = string.Empty;
        float m_FlashUntil;
        float m_Elevation;
        bool m_Placed;

        float m_HintAlpha;
        float m_AlertAlpha;
        float m_TitleAlpha;
        string m_ShownHint = string.Empty;
        string m_ShownAlert = string.Empty;
        string m_ShownTitle = string.Empty;

        const float k_FadeSpeed = 4f;

        public void Init(ConstellationConfig config, Transform head)
        {
            m_Config = config;
            m_Head = head;
            m_Elevation = config.guideElevationIdle;

            m_Panel = new GameObject("Guide Panel").transform;
            m_Panel.SetParent(transform, false);

            float size = config.guideTextSize;
            Vector2 box = new Vector2(size * 26f, size * 3f);
            m_Title = TextFactory.Create("Title", m_Panel, size * 2.2f, new Vector2(size * 40f, size * 4f), TextAlignmentOptions.Center, config.textAccentColor);
            m_Title.transform.localPosition = new Vector3(0f, size * 3.4f, 0f);
            m_Hint = TextFactory.Create("Hint", m_Panel, size, box, TextAlignmentOptions.Center, config.textColor);
            m_Hint.transform.localPosition = Vector3.zero;
            m_Alert = TextFactory.Create("Alert", m_Panel, size * 1.1f, box, TextAlignmentOptions.Center, config.textAccentColor);
            m_Alert.transform.localPosition = new Vector3(0f, -size * 2.4f, 0f);

            m_Title.alpha = 0f;
            m_Hint.alpha = 0f;
            m_Alert.alpha = 0f;
        }

        /// <summary>큰 제목(대기 화면). 빈 문자열이면 숨긴다.</summary>
        public void SetTitle(string text)
        {
            m_TitleText = text ?? string.Empty;
        }

        /// <summary>지금 할 일 안내. 빈 문자열이면 숨긴다.</summary>
        public void SetHint(string text)
        {
            m_HintText = text ?? string.Empty;
        }

        /// <summary>상황이 풀릴 때까지 계속 보이는 알림(손 인식 실패, 무입력).</summary>
        public void SetAlert(string text)
        {
            m_StickyAlert = text ?? string.Empty;
        }

        public void ClearAlert()
        {
            m_StickyAlert = string.Empty;
        }

        /// <summary>잠깐 보였다 사라지는 알림(별 부족, 별 초과).</summary>
        public void FlashAlert(string text, float seconds)
        {
            m_FlashAlert = text ?? string.Empty;
            m_FlashUntil = Time.unscaledTime + seconds;
        }

        /// <summary>모든 문구를 지운다.</summary>
        public void ClearAll()
        {
            m_TitleText = string.Empty;
            m_HintText = string.Empty;
            m_StickyAlert = string.Empty;
            m_FlashAlert = string.Empty;
        }

        /// <summary>글자판의 높이 각도(도). 별과 겹치지 않게 상태에 따라 바꾼다.</summary>
        public void SetElevation(float degrees)
        {
            m_Elevation = degrees;
        }

        void LateUpdate()
        {
            if (m_Config == null || m_Head == null)
                return;

            FollowHead();

            string alert = m_StickyAlert;
            if (m_FlashAlert.Length > 0)
            {
                if (Time.unscaledTime < m_FlashUntil)
                    alert = m_FlashAlert;
                else
                    m_FlashAlert = string.Empty;
            }

            float dt = Time.unscaledDeltaTime;
            Fade(m_Title, m_TitleText, ref m_ShownTitle, ref m_TitleAlpha, dt);
            Fade(m_Hint, m_HintText, ref m_ShownHint, ref m_HintAlpha, dt);
            Fade(m_Alert, alert, ref m_ShownAlert, ref m_AlertAlpha, dt);
        }

        // 문구가 바뀌면 먼저 흐려졌다가 새 문구로 다시 나타난다.
        static void Fade(TextMeshPro label, string target, ref string shown, ref float alpha, float dt)
        {
            if (shown != target)
            {
                alpha = Mathf.MoveTowards(alpha, 0f, dt * k_FadeSpeed * 2f);
                if (alpha <= 0.001f)
                {
                    shown = target;
                    label.text = target;
                }
            }
            else if (target.Length > 0)
            {
                alpha = Mathf.MoveTowards(alpha, 1f, dt * k_FadeSpeed);
            }
            else
            {
                alpha = 0f;
            }
            label.alpha = alpha;
        }

        void FollowHead()
        {
            Vector3 forward = m_Head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = m_Panel.forward;
            forward.Normalize();

            Quaternion yaw = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 direction = yaw * Quaternion.Euler(-m_Elevation, 0f, 0f) * Vector3.forward;
            Vector3 targetPosition = m_Head.position + direction * m_Config.guideDistance;

            if (!m_Placed)
            {
                m_Panel.position = targetPosition;
                m_Placed = true;
            }
            else
            {
                float k = 1f - Mathf.Exp(-m_Config.guideFollowSpeed * Time.unscaledDeltaTime);
                m_Panel.position = Vector3.Lerp(m_Panel.position, targetPosition, k);
            }

            // 글자는 읽는 사람 쪽을 봐야 하므로 머리에서 글자판으로 향하는 방향을 앞으로 둔다.
            Vector3 away = m_Panel.position - m_Head.position;
            if (away.sqrMagnitude > 0.0001f)
                m_Panel.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
    }
}
