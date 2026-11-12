using System;
using TMPro;
using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// "완성" 별. 별을 충분히 이으면 별 구역 아래에 나타나고, 핀치로 집으면 완성을 알린다.
    /// 양손 모으기가 잘 인식되지 않거나 한 손만 쓰는 사람을 위한 또 하나의 완성 방법이다.
    /// 완성 여부는 판단하지 않고 Pressed 이벤트로만 알린다.
    /// </summary>
    public class FinishButton : MonoBehaviour
    {
        public event Action Pressed;

        ConstellationConfig m_Config;
        HandInput m_Hands;
        Transform m_Root;
        GlowSprite m_Glow;
        GlowSprite m_Ring;
        TextMeshPro m_Label;
        bool m_Visible;
        float m_Level;

        const float k_Size = 0.07f;

        public void Init(ConstellationConfig config, HandInput hands)
        {
            m_Config = config;
            m_Hands = hands;
            m_Hands.PinchStarted += OnPinchStarted;

            m_Root = new GameObject("Finish Star").transform;
            m_Root.SetParent(transform, false);
            m_Glow = GlowSprite.Create("Glow", m_Root, k_Size, config.textAccentColor, 0f);
            m_Ring = GlowSprite.Create("Ring", m_Root, k_Size * 1.9f, config.textAccentColor, 0f);
            m_Ring.SetRing(1f);
            m_Ring.SetCore(0.02f);
            m_Label = TextFactory.Create("Label", m_Root, 0.026f, new Vector2(0.3f, 0.05f), TextAlignmentOptions.Center, config.textAccentColor);
            m_Label.transform.localPosition = new Vector3(0f, -k_Size * 1.25f, 0f);
            m_Label.text = config.msgFinishLabel;
            m_Label.alpha = 0f;
            m_Root.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (m_Hands != null)
                m_Hands.PinchStarted -= OnPinchStarted;
        }

        /// <summary>주어진 위치에 나타난다. rotation의 앞쪽이 체험자가 보는 방향이다.</summary>
        public void Show(Vector3 position, Quaternion rotation)
        {
            m_Root.position = position;
            m_Root.rotation = rotation;
            m_Visible = true;
            m_Root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            m_Visible = false;
        }

        public Vector3 Position { get { return m_Root.position; } }

        /// <summary>별 아래 글자를 바꾼다('완성' 대신 '남기기', '다시 만들기' 등으로 쓸 때).</summary>
        public void SetLabel(string label)
        {
            m_Label.text = label ?? string.Empty;
        }

        void OnPinchStarted(HandSide side, Vector3 position)
        {
            if (!m_Visible || m_Level < 0.5f)
                return;
            if (Vector3.Distance(position, m_Root.position) > m_Config.finishButtonRadius)
                return;
            if (Pressed != null)
                Pressed();
        }

        void Update()
        {
            if (m_Root == null || !m_Root.gameObject.activeSelf)
                return;

            m_Level = Mathf.MoveTowards(m_Level, m_Visible ? 1f : 0f, Time.deltaTime * 3f);
            if (!m_Visible && m_Level <= 0f)
            {
                m_Root.gameObject.SetActive(false);
                return;
            }

            // 손이 가까이 오면 커지고 밝아진다.
            float nearest = float.MaxValue;
            if (m_Hands.Left.isTracked)
                nearest = Mathf.Min(nearest, Vector3.Distance(m_Hands.Left.pinchPoint, m_Root.position));
            if (m_Hands.Right.isTracked)
                nearest = Mathf.Min(nearest, Vector3.Distance(m_Hands.Right.pinchPoint, m_Root.position));
            float near = Mathf.Clamp01(1f - (nearest - m_Config.finishButtonRadius) / 0.1f);

            float pulse = 1f + 0.05f * Mathf.Sin(Time.time * 3f);
            m_Glow.SetSize(k_Size * (1f + 0.3f * near) * pulse);
            m_Glow.SetIntensity(m_Level * (1.3f + 0.9f * near));
            m_Ring.SetSize(k_Size * 1.9f * pulse);
            m_Ring.SetIntensity(m_Level * (0.7f + 0.9f * near));
            m_Label.alpha = m_Level;
        }
    }
}
