using System.Collections.Generic;
using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 후보 별을 배치하고 선택 상태를 관리한다(F-04).
    /// 별은 체험 시작 때의 눈 위치를 중심으로 한 구면 위, 능선보다 높고 고개를 젖히지 않아도 되는 구역에
    /// 줄과 칸을 나눠 고르게 놓는다(설정의 별 구역 프리셋).
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
            /// <summary>손이 얼마나 가까운지(0~1). 가까울수록 별이 커진다.</summary>
            public float proximity;
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
        float m_Distance = 0.45f;
        // 잡힐 별을 알려 주는 고리(손마다 하나)
        readonly GlowSprite[] m_Rings = new GlowSprite[2];
        readonly int[] m_RingTargets = { -1, -1 };
        readonly float[] m_RingLevels = new float[2];

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
            for (int i = 0; i < m_Rings.Length; i++)
            {
                m_Rings[i] = GlowSprite.Create("Grab Ring " + i, m_Root, 0.1f, config.starHoverColor, 0f);
                m_Rings[i].SetRing(1f);
                m_Rings[i].SetCore(0.02f);
                m_Rings[i].SetVisible(false);
            }
        }

        /// <summary>후보 별을 뿌린다. 기준점은 체험자의 머리 위치와 바라보는 방향이다.</summary>
        public void Spawn(Vector3 anchorPosition, Quaternion anchorRotation)
        {
            Clear();

            StarZonePreset zone = m_Config.ActiveStarZone;
            m_Distance = zone.distance;
            m_BaseRotation = anchorRotation;
            // 각도 기준이 눈높이이므로 구역의 중심은 눈 위치다.
            m_Root.position = anchorPosition;
            m_Root.rotation = anchorRotation;
            m_Root.localScale = Vector3.one;
            m_SpawnTime = Time.time;

            PickDirections(Mathf.Max(1, m_Config.candidateStarCount), zone);
            for (int i = 0; i < m_Directions.Count; i++)
            {
                Star star = new Star();
                star.localPosition = m_Directions[i] * m_Distance;
                star.baseSize = Random.Range(m_Config.starSizeRange.x, m_Config.starSizeRange.y);
                star.phase = Random.Range(0f, Mathf.PI * 2f);
                star.appearDelay = Random.Range(0f, 0.6f);
                star.glow = GlowSprite.Create("Star " + i, m_Root, star.baseSize, m_Config.starColor, 0f);
                star.transform = star.glow.transform;
                star.transform.localPosition = star.localPosition;
                m_Stars.Add(star);
            }
        }

        // 구역을 줄과 칸으로 나눠 칸마다 별 하나를 놓고 조금씩 흔든다.
        // 무작위로 뿌리는 것보다 간격이 고르고, 정면 아래쪽에도 반드시 별이 온다.
        void PickDirections(int count, StarZonePreset zone)
        {
            m_Directions.Clear();
            int rows = Mathf.Clamp(m_Config.starRows, 1, count);
            int columns = Mathf.CeilToInt(count / (float)rows);
            float cellWidth = zone.azimuthHalfRange * 2f / columns;
            float cellHeight = (zone.elevationMax - zone.elevationMin) / rows;
            float jitter = Mathf.Clamp(m_Config.starJitter, 0f, 0.8f);

            for (int row = 0; row < rows; row++)
            {
                // 마지막 줄에 칸이 남으면 가운데로 모은다.
                int inRow = Mathf.Min(columns, count - m_Directions.Count);
                if (inRow <= 0)
                    break;
                float rowStart = -zone.azimuthHalfRange + (columns - inRow) * cellWidth * 0.5f;
                // 줄마다 반 칸씩 어긋나게 해서 격자처럼 보이지 않게 한다.
                float stagger = (row % 2 == 0 ? -0.25f : 0.25f) * cellWidth;
                for (int column = 0; column < inRow; column++)
                {
                    float azimuth = rowStart + (column + 0.5f) * cellWidth + stagger + Random.Range(-0.5f, 0.5f) * jitter * cellWidth;
                    float elevation = zone.elevationMin + (row + 0.5f) * cellHeight + Random.Range(-0.5f, 0.5f) * jitter * cellHeight;
                    azimuth = Mathf.Clamp(azimuth, -zone.azimuthHalfRange, zone.azimuthHalfRange);
                    elevation = Mathf.Clamp(elevation, zone.elevationMin, zone.elevationMax);
                    m_Directions.Add(Quaternion.Euler(-elevation, azimuth, 0f) * Vector3.forward);
                }
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
            m_RingTargets[0] = m_RingTargets[1] = -1;
            for (int i = 0; i < m_Rings.Length; i++)
            {
                m_RingLevels[i] = 0f;
                if (m_Rings[i] != null)
                    m_Rings[i].SetVisible(false);
            }
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

        /// <summary>지금 잡으면 선택될 별(손마다 하나)을 밝히고 고리로 표시한다. 없으면 -1.</summary>
        public void SetHovered(int first, int second)
        {
            m_RingTargets[0] = first;
            m_RingTargets[1] = second;
            for (int i = 0; i < m_Stars.Count; i++)
                m_Stars[i].hovered = i == first || i == second;
        }

        /// <summary>
        /// 손이 다가올수록 근처 별이 조금씩 커지게 한다. 잡기 반경 안이면 1, 힌트 거리 밖이면 0.
        /// </summary>
        public void SetHandPoints(bool hasFirst, Vector3 first, bool hasSecond, Vector3 second)
        {
            float grab = m_Config.starGrabRadius;
            float range = Mathf.Max(0.001f, m_Config.starHintRange);
            for (int i = 0; i < m_Stars.Count; i++)
            {
                Star star = m_Stars[i];
                float nearest = float.MaxValue;
                Vector3 position = star.transform.position;
                if (hasFirst)
                    nearest = Mathf.Min(nearest, Vector3.Distance(position, first));
                if (hasSecond)
                    nearest = Mathf.Min(nearest, Vector3.Distance(position, second));
                star.proximity = Mathf.Clamp01(1f - (nearest - grab) / range);
            }
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

        /// <summary>
        /// 머리에서 본 방향이 손과 가까운 별을 찾는다(끌어서 잇기용). 손 깊이가 조금 달라도 붙도록
        /// 각도로 비교하고, 손이 별보다 너무 멀거나 가까우면(깊이 차이) 제외한다. 없으면 -1.
        /// </summary>
        public int FindNearestByAngle(Vector3 handPosition, Vector3 headPosition, float maxAngleDegrees, float depthTolerance, bool skipSelected, int excludeIndex = -1)
        {
            Vector3 handDir = handPosition - headPosition;
            float handDist = handDir.magnitude;
            if (handDist < 0.01f)
                return -1;
            handDir /= handDist;

            int best = -1;
            float bestAngle = maxAngleDegrees;
            for (int i = 0; i < m_Stars.Count; i++)
            {
                if (i == excludeIndex || (skipSelected && IsSelected(i)))
                    continue;
                Vector3 toStar = m_Stars[i].transform.position - headPosition;
                float starDist = toStar.magnitude;
                if (starDist < 0.01f || Mathf.Abs(starDist - handDist) > depthTolerance)
                    continue;
                float angle = Vector3.Angle(handDir, toStar / starDist);
                if (angle <= bestAngle)
                {
                    bestAngle = angle;
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
                return m_Root.position + m_Root.forward * m_Distance;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < indices.Count; i++)
                sum += m_Stars[indices[i]].transform.position;
            return sum / indices.Count;
        }

        /// <summary>
        /// 선택한 별을 체험자가 본 모양 그대로 2D 좌표(오른쪽 +x, 위 +y)로 돌려준다. 캡처에 쓴다.
        /// 별이 좌우로 넓게 퍼져 있어서, 평면에 눌러 찍지 않고 눈에서 본 각도(방위, 고도)로 편다.
        /// </summary>
        public List<Vector2> GetProjectedPoints(IList<int> indices)
        {
            List<Vector2> points = new List<Vector2>();
            if (indices == null || indices.Count == 0)
                return points;

            float elevationSum = 0f;
            for (int i = 0; i < indices.Count; i++)
            {
                Vector3 local = m_Stars[indices[i]].localPosition;
                float azimuth = Mathf.Atan2(local.x, local.z);
                float elevation = Mathf.Asin(Mathf.Clamp(local.y / Mathf.Max(0.0001f, local.magnitude), -1f, 1f));
                points.Add(new Vector2(azimuth, elevation));
                elevationSum += elevation;
            }
            // 위로 갈수록 같은 방위 차이가 실제로는 좁아 보이므로 평균 고도로 가로를 보정한다.
            float horizontalScale = Mathf.Cos(elevationSum / indices.Count);
            for (int i = 0; i < points.Count; i++)
                points[i] = new Vector2(points[i].x * horizontalScale, points[i].y);
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

                // 잡는 별은 배경 별과 헷갈리지 않게 기본부터 밝게 두고, 손이 다가오면 더 키운다.
                float near = m_CandidatesVisible ? star.proximity : 0f;
                float targetSize = star.baseSize * (1f + 0.3f * near);
                float targetIntensity = 1.15f * twinkle * (1f + 0.5f * near);
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
                    targetSize = star.baseSize * 1.6f;
                    targetIntensity = 2.1f;
                    color = m_Config.starHoverColor;
                }

                float k = 1f - Mathf.Exp(-12f * dt);
                star.size = Mathf.Lerp(star.size, targetSize, k);
                star.intensity = Mathf.Lerp(star.intensity, targetIntensity * appear, k);
                star.glow.SetSize(star.size);
                star.glow.SetIntensity(star.intensity);
                star.glow.SetColor(color);
            }

            UpdateRings(dt);
        }

        // 지금 핀치하면 잡힐 별에 고리를 띄운다. 이미 고른 별에는 띄우지 않는다.
        void UpdateRings(float dt)
        {
            for (int i = 0; i < m_Rings.Length; i++)
            {
                int target = m_RingTargets[i];
                bool show = m_CandidatesVisible && target >= 0 && target < m_Stars.Count && !m_Stars[target].selected
                    && !(i == 1 && target == m_RingTargets[0]);
                m_RingLevels[i] = Mathf.MoveTowards(m_RingLevels[i], show ? 1f : 0f, dt * 8f);
                GlowSprite ring = m_Rings[i];
                ring.SetVisible(m_RingLevels[i] > 0.01f);
                ring.SetIntensity(1.6f * m_RingLevels[i]);
                if (!show)
                    continue;
                Star star = m_Stars[target];
                ring.transform.localPosition = star.localPosition;
                // 숨 쉬듯 살짝 커졌다 작아진다.
                float pulse = 1f + 0.06f * Mathf.Sin(Time.time * 6f);
                ring.SetSize(star.baseSize * 2.6f * pulse);
            }
        }
    }
}
