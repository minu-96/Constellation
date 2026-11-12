using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 처음 쓰는 사람에게 손 모양을 시범으로 보여 준다. 반투명한 시범 손이 동작을 되풀이한다.
    ///  - 핀치: 목표(키워드 아이콘이나 별) 앞에서 엄지와 검지를 맞댔다 뗀다.
    ///  - 양손 모으기: 두 손이 벌어졌다 모인다.
    /// 언제 보여 줄지는 SessionController가 정한다(한동안 입력이 없을 때).
    /// </summary>
    public class GestureGuide : MonoBehaviour
    {
        enum Demo
        {
            None,
            Pinch,
            HandsTogether
        }

        Transform m_Head;
        HandState m_Right;
        HandState m_Left;
        Demo m_Demo = Demo.None;
        Vector3 m_Target;
        float m_Time;

        const float k_PinchCycle = 2.0f;
        const float k_TogetherCycle = 2.4f;

        public bool IsShowing { get { return m_Demo != Demo.None; } }

        public void Init(HandVisual handVisual, Transform head)
        {
            m_Head = head;
            m_Right = handVisual.CreateGhost("Guide Hand Right");
            m_Left = handVisual.CreateGhost("Guide Hand Left");
        }

        /// <summary>목표 위치에서 핀치하는 시범을 되풀이한다.</summary>
        public void ShowPinch(Vector3 target)
        {
            if (m_Demo != Demo.Pinch)
                m_Time = 0f;
            m_Demo = Demo.Pinch;
            m_Target = target;
        }

        /// <summary>주어진 위치를 가운데로 두 손을 모으는 시범을 되풀이한다.</summary>
        public void ShowHandsTogether(Vector3 center)
        {
            if (m_Demo != Demo.HandsTogether)
                m_Time = 0f;
            m_Demo = Demo.HandsTogether;
            m_Target = center;
        }

        public void Hide()
        {
            m_Demo = Demo.None;
            if (m_Right != null)
                m_Right.ClearTracking();
            if (m_Left != null)
                m_Left.ClearTracking();
        }

        void Update()
        {
            if (m_Demo == Demo.None || m_Head == null)
                return;

            m_Time += Time.deltaTime;

            // 시범 손은 체험자와 같은 방향을 보고 선다(손등이 보인다).
            Vector3 forward = m_Target - m_Head.position;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
                forward = m_Head.forward;
            forward.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            if (m_Demo == Demo.Pinch)
            {
                float t = (m_Time % k_PinchCycle) / k_PinchCycle;
                // 다가온다 → 맞댄다 → 잠깐 유지 → 뗀다
                float approach = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.3f));
                float pinch = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.35f) / 0.15f)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.75f) / 0.15f)));
                // 목표보다 조금 앞(체험자 쪽)에서 시작해 목표에 닿는다.
                Vector3 point = m_Target - forward * (0.05f * (1f - approach));
                HandPose.Fill(m_Right, point, right, Vector3.up, forward, pinch, false);
                m_Left.ClearTracking();
            }
            else
            {
                float t = (m_Time % k_TogetherCycle) / k_TogetherCycle;
                // 벌어져 있다가 모이고, 모은 채 잠깐 머문다.
                float together = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.15f) / 0.35f)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.85f) / 0.15f)));
                float spread = Mathf.Lerp(0.2f, 0.02f, together);
                // 엄지·검지 가운데가 기준점이라, 손바닥이 가운데로 오도록 살짝 올린다.
                Vector3 center = m_Target + Vector3.up * 0.05f;
                HandPose.Fill(m_Right, center + right * (spread + 0.047f), right, Vector3.up, forward, 0f, false);
                HandPose.Fill(m_Left, center - right * (spread + 0.047f), right, Vector3.up, forward, 0f, true);
            }
        }
    }
}
