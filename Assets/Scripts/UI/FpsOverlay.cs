using TMPro;
using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 프레임 수와 프레임 시간을 시야 아래쪽에 띄운다. 기기에서 성능을 볼 때만 켠다(설정의 showFpsOverlay).
    /// 0.5초마다 평균과 가장 느린 프레임을 갱신한다. 72fps면 한 프레임이 13.9ms다.
    /// </summary>
    public class FpsOverlay : MonoBehaviour
    {
        Transform m_Head;
        TextMeshPro m_Text;
        int m_Frames;
        float m_Elapsed;
        float m_Worst;

        public void Init(ConstellationConfig config, Transform head)
        {
            m_Head = head;
            m_Text = TextFactory.Create("FPS", transform, 0.03f, new Vector2(0.8f, 0.08f), TextAlignmentOptions.Center, config.textAccentColor);
        }

        void LateUpdate()
        {
            if (m_Head == null)
                return;

            // 머리에 고정해 어디를 보든 보이게 한다(시선 아래 18도, 1m 앞).
            Vector3 direction = m_Head.rotation * Quaternion.Euler(18f, 0f, 0f) * Vector3.forward;
            transform.position = m_Head.position + direction;
            transform.rotation = Quaternion.LookRotation(direction, m_Head.up);

            float dt = Time.unscaledDeltaTime;
            m_Frames++;
            m_Elapsed += dt;
            m_Worst = Mathf.Max(m_Worst, dt);
            if (m_Elapsed < 0.5f)
                return;

            float average = m_Elapsed / m_Frames;
            m_Text.text = Mathf.RoundToInt(1f / average) + " fps   평균 " + (average * 1000f).ToString("0.0") + " ms   최악 " + (m_Worst * 1000f).ToString("0.0") + " ms";
            m_Frames = 0;
            m_Elapsed = 0f;
            m_Worst = 0f;
        }
    }
}
