using System.Collections.Generic;
using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 후보 별을 배치하고 선택 상태를 관리한다(F-04).
    /// 별은 체험자 어깨를 중심으로 한 구면 위, 손이 닿는 거리에 겹치지 않게 흩뿌린다.
    /// </summary>
    public class StarField : MonoBehaviour
    {
        class Star
        {
            public Transform transform;
            public GlowSprite glow;
            public Vector3 localPosition;
            public float baseSize;
            public float phase;
            public float appearDelay;
            public bool selected;
            public bool hovered;
            public float size;
            public float intensity;
        }

        ConstellationConfig m_Config;
        Transform m_Root;
        readonly List<Star> m_Stars = new List<Star>();
        readonly List<Vector3> m_Directions = new List<Vector3>();
        Quaternion m_BaseRotation = Quaternion.identity;
        float m_SpawnTime;
        bool m_CandidatesVisible = true;
        bool m_Raised;
        float m_Raise;

        /// <summary>별들의 부모. 결과 화면에서 이 오브젝트를 키우고 기울여 별자리를 하늘로 올린다.</summary>
        public Transform Root { get { return m_Root; } }
        public int Count { get { return m_Stars.Count; } }
        /// <summary>현재 배율(결과 화면에서 커진다). 선 굵기를 맞추는 데 쓴다.</summary>
        public float CurrentScale { get { return m_Root != null ? m_Root.localScale.x : 1f; } }

        public void Init(ConstellationConfig config)
        {
            m_Config = config;
            m_Root = new GameObject("Stars").transform;
            m_Root.SetParent(transform, false);
        }

        /// <summary>후보 별을 뿌린다. 기준점은 체험자의 머리 위치와 바라보는 방향이다.</summary>
        public void Spawn(Vector3 anchorPosition, Quaternion anchorRotation)
        {
            Clear();

            m_BaseRotation = anchorRotation;
            m_Root.position = anchorPosition + Vector3.down * m_Config.starCenterDrop;
            m_Root.rotation = anchorRotation;
            m_Root.localScale = Vector3.one;
            m_SpawnTime = Time.time;

            PickDirections(Mathf.Max(1, m_Config.candidateStarCount));
            for (int i = 0; i < m_Directions.Count; i++)
            {
                Star star = new Star();
                star.localPosition = m_Directions[i] * m_Config.starDistance;
                star.baseSize = Random.Range(m_Config.starSizeRange.x, m_Config.starSizeRange.y);
                star.phase = Random.Range(0f, Mathf.PI * 2f);
                star.appearDelay = Random.Range(0f, 0.6f);
                star.glow = GlowSprite.Create("Star " + i, m_Root, star.baseSize, m_Config.starColor, 0f);
                star.transform = star.glow.transform;
                star.transform.localPosition = star.localPosition;
                m_Stars.Add(star);
            }
        }

        // 서로 일정 각도 이상 떨어지도록 방향을 고른다. 자리가 모자라면 간격을 조금씩 줄인다.
        void PickDirections(int count)
        {
            m_Directions.Clear();
            float separation = m_Config.starMinSeparation;
            int guard = 0;
            while (m_Directions.Count < count && guard < 20000)
            {
                bool placed = false;
                for (int attempt = 0; attempt < 40 && !placed; attempt++)
                {
                    guard++;
                    float azimuth = Random.Range(-m_Config.starAzimuthHalfRange, m_Config.starAzimuthHalfRange);
                    float elevation = Random.Range(m_Config.starElevationMin, m_Config.starElevationMax);
                    Vector3 direction = Quaternion.Euler(-elevation, azimuth, 0f) * Vector3.forward;

                    bool tooClose = false;
                    for (int i = 0; i < m_Directions.Count; i++)
                    {
                        if (Vector3.Angle(direction, m_Directions[i]) < separation)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                    if (!tooClose)
                    {
                        m_Directions.Add(direction);
                        placed = true;
                    }
                }
                if (!placed)
                    separation *= 0.9f;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < m_Stars.Count; i++)
            {
                if (m_Stars[i].transform != null)
                    Destroy(m_Stars[i].transform.gameObject);
            }
            m_Stars.Clear();
            m_CandidatesVisible = true;
            m_Raised = false;
            m_Raise = 0f;
            if (m_Root != null)
                m_Root.localScale = Vector3.one;
        }

        public Vector3 GetWorldPosition(int index)
        {
            return m_Stars[index].transform.position;
        }

        public bool IsSelected(int index)
        {
            return m_Stars[index].selected;
        }

        public void SetSelected(int index, bool selected)
        {
            if (index >= 0 && index < m_Stars.Count)
                m_Stars[index].selected = selected;
        }

        /// <summary>손이 가까이 있는 별을 밝힌다. 없으면 -1.</summary>
        public void SetHovered(int first, int second)
        {
            for (int i = 0; i < m_Stars.Count; i++)
                m_Stars[i].hovered = i == first || i == second;
        }

        /// <summary>주어진 위치에서 반경 안에 있는 가장 가까운 별. 없으면 -1.</summary>
        public int FindNearest(Vector3 worldPosition, float radius)
        {
            int best = -1;
            float bestSqr = radius * radius;
            for (int i = 0; i < m_Stars.Count; i++)
            {
                float sqr = (m_Stars[i].transform.position - worldPosition).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>선택하지 않은 후보 별을 보이거나 숨긴다(결과 화면에서는 숨긴다).</summary>
        public void SetCandidatesVisible(bool visible)
        {
            m_CandidatesVisible = visible;
        }

        /// <summary>별자리를 하늘 높이 올린다(결과 화면). 머리에서 본 모양은 그대로이고 멀어지기만 한다.</summary>
        public void RaiseToSky(bool raised)
        {
            m_Raised = raised;
        }

        /// <summary>선택한 별들의 가운데(월드).</summary>
        public Vector3 GetSelectionCenter(IList<int> indices)
        {
            if (indices == null || indices.Count == 0)
                return m_Root.position + m_Root.forward * m_Config.starDistance;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < indices.Count; i++)
                sum += m_Stars[indices[i]].transform.position;
            return sum / indices.Count;
        }

        /// <summary>선택한 별을 체험자 정면에서 본 2D 좌표(오른쪽 +x, 위 +y)로 돌려준다. 캡처에 쓴다.</summary>
        public List<Vector2> GetProjectedPoints(IList<int> indices)
        {
            List<Vector2> points = new List<Vector2>();
            if (indices == null)
                return points;
            for (int i = 0; i < indices.Count; i++)
            {
                Vector3 local = m_Stars[indices[i]].localPosition;
                points.Add(new Vector2(local.x, local.y));
            }
            return points;
        }

        void Update()
        {
            if (m_Config == null || m_Stars.Count == 0)
                return;

            float dt = Time.deltaTime;

            // 하늘로 올리기: 배율과 기울기를 함께 보간한다.
            m_Raise = Mathf.MoveTowards(m_Raise, m_Raised ? 1f : 0f, dt / 2.5f);
            float eased = Mathf.SmoothStep(0f, 1f, m_Raise);
            m_Root.localScale = Vector3.one * Mathf.Lerp(1f, m_Config.resultSkyScale, eased);
            m_Root.rotation = m_BaseRotation * Quaternion.Euler(-m_Config.resultSkyPitch * eased, 0f, 0f);

            float age = Time.time - m_SpawnTime;
            for (int i = 0; i < m_Stars.Count; i++)
            {
                Star star = m_Stars[i];
                float appear = Mathf.Clamp01((age - star.appearDelay) / 0.7f);
                float twinkle = 0.85f + 0.15f * Mathf.Sin(Time.time * 2.1f + star.phase);

                float targetSize = star.baseSize;
                float targetIntensity = 0.9f * twinkle;
                Color color = m_Config.starColor;
                if (star.selected)
                {
                    targetSize = star.baseSize * 1.7f;
                    targetIntensity = 2.4f;
                    color = m_Config.starSelectedColor;
                }
                else if (!m_CandidatesVisible)
                {
                    targetIntensity = 0f;
                }
                else if (star.hovered)
                {
                    targetSize = star.baseSize * 1.5f;
                    targetIntensity = 1.9f;
                    color = m_Config.starHoverColor;
                }

                float k = 1f - Mathf.Exp(-12f * dt);
                star.size = Mathf.Lerp(star.size, targetSize, k);
                star.intensity = Mathf.Lerp(star.intensity, targetIntensity * appear, k);
                star.glow.SetSize(star.size);
                star.glow.SetIntensity(star.intensity);
                star.glow.SetColor(color);
            }
        }
    }
}
