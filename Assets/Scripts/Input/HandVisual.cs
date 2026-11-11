using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 손을 별빛 점으로 보여 준다. 손 모델 에셋 없이, 손끝과 손바닥에 빛나는 점을 찍는다.
    /// 핀치하면 엄지·검지 사이의 점이 밝아져 잡는 위치를 알 수 있다.
    /// </summary>
    public class HandVisual : MonoBehaviour
    {
        // 손목, 손바닥, 다섯 손끝 (XRHandJointID - 1 기준 인덱스)
        static readonly int[] k_Joints = { 0, 1, 5, 10, 15, 20, 25 };

        ConstellationConfig m_Config;
        HandInput m_Hands;
        GlowSprite[][] m_Dots;
        GlowSprite[] m_PinchDots;

        public void Init(ConstellationConfig config, HandInput hands)
        {
            m_Config = config;
            m_Hands = hands;
            m_Dots = new GlowSprite[2][];
            m_PinchDots = new GlowSprite[2];
            for (int h = 0; h < 2; h++)
            {
                m_Dots[h] = new GlowSprite[k_Joints.Length];
                for (int j = 0; j < k_Joints.Length; j++)
                {
                    float size = j < 2 ? 0.03f : 0.022f;
                    m_Dots[h][j] = GlowSprite.Create("Hand Dot", transform, size, config.handColor, 0.8f);
                    m_Dots[h][j].SetVisible(false);
                }
                m_PinchDots[h] = GlowSprite.Create("Pinch Dot", transform, 0.03f, config.starHoverColor, 1f);
                m_PinchDots[h].SetVisible(false);
            }
        }

        void LateUpdate()
        {
            if (m_Hands == null)
                return;
            UpdateHand(0, m_Hands.Left);
            UpdateHand(1, m_Hands.Right);
        }

        void UpdateHand(int h, HandState state)
        {
            for (int j = 0; j < k_Joints.Length; j++)
            {
                int joint = k_Joints[j];
                bool visible = state.isTracked && state.jointValid[joint];
                m_Dots[h][j].SetVisible(visible);
                if (visible)
                    m_Dots[h][j].transform.position = state.joints[joint];
            }

            GlowSprite pinch = m_PinchDots[h];
            pinch.SetVisible(state.isTracked);
            if (state.isTracked)
            {
                pinch.transform.position = state.pinchPoint;
                pinch.SetSize(state.isPinching ? 0.05f : 0.022f);
                pinch.SetIntensity(state.isPinching ? 2.2f : 0.7f);
                pinch.SetColor(state.isPinching ? m_Config.starSelectedColor : m_Config.starHoverColor);
            }
        }
    }
}
