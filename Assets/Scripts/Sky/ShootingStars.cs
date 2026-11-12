using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 가끔 하늘을 가로지르는 별똥별. 선 하나를 잠깐 그렸다 지운다.
    /// </summary>
    public class ShootingStars : MonoBehaviour
    {
        const float k_Distance = 60f;
        const float k_Seconds = 0.7f;

        ConstellationConfig m_Config;
        Transform m_Head;
        LineRenderer m_Line;
        float m_NextTime;
        float m_Age = 10f;
        Vector3 m_Start;
        Vector3 m_Travel;

        public void Init(ConstellationConfig config, Transform head)
        {
            m_Config = config;
            m_Head = head;
            m_Line = VisualFactory.CreateLine("Shooting Star", transform, 0.14f, Color.white, true);
            m_Line.numCapVertices = 0;
            m_Line.positionCount = 2;
            // 머리는 밝고 꼬리는 사라진다.
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 1f) });
            m_Line.colorGradient = gradient;
            m_Line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(1f, 1f));
            m_Line.enabled = false;
            Schedule();
        }

        void Schedule()
        {
            Vector2 range = m_Config.shootingStarInterval;
            m_NextTime = Time.time + Random.Range(Mathf.Max(2f, range.x), Mathf.Max(3f, range.y));
        }

        void Update()
        {
            if (m_Config == null || m_Head == null)
                return;

            if (m_Age >= k_Seconds)
            {
                m_Line.enabled = false;
                if (Time.time >= m_NextTime)
                    Launch();
                return;
            }

            m_Age += Time.deltaTime;
            float t = Mathf.Clamp01(m_Age / k_Seconds);
            // 빠르게 나타났다가 꼬리를 끌며 사라진다.
            Vector3 head = m_Head.position + m_Start + m_Travel * t;
            Vector3 tail = m_Head.position + m_Start + m_Travel * Mathf.Max(0f, t - 0.35f);
            m_Line.SetPosition(0, tail);
            m_Line.SetPosition(1, head);
            float fade = Mathf.Sin(t * Mathf.PI);
            m_Line.sharedMaterial.SetFloat(VisualFactory.IntensityId, fade * 1.6f);
        }

        void Launch()
        {
            // 하늘 높은 곳에서 시작해 비스듬히 아래로 떨어진다.
            float azimuth = Random.Range(0f, 360f);
            float elevation = Random.Range(28f, 62f);
            Vector3 direction = Quaternion.Euler(-elevation, azimuth, 0f) * Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, direction).normalized * (Random.value < 0.5f ? -1f : 1f);
            Vector3 fall = (side + Vector3.down * Random.Range(0.35f, 0.8f)).normalized;

            m_Start = direction * k_Distance;
            m_Travel = fall * k_Distance * Random.Range(0.14f, 0.24f);
            m_Age = 0f;
            m_Line.enabled = true;
            Schedule();
        }
    }
}
