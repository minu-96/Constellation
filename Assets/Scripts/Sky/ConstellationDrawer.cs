using System;
using System.Collections.Generic;
using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 핀치로 잡은 별을 차례로 이어 선을 긋는다(F-05). 마지막 선을 되돌릴 수 있다(F-06).
    /// 완성 여부나 상태 전환은 판단하지 않고, 일어난 일을 이벤트로만 알린다.
    /// </summary>
    public class ConstellationDrawer : MonoBehaviour
    {
        /// <summary>별 하나를 이었을 때. 인자는 지금까지 이은 별 개수.</summary>
        public event Action<int> StarAdded;
        /// <summary>최대 개수를 넘겨 더 잇지 못했을 때.</summary>
        public event Action MaxReached;
        /// <summary>마지막 별과 선을 지웠을 때. 인자는 남은 별 개수.</summary>
        public event Action<int> Undone;
        /// <summary>핀치했지만 잡기 반경 안에 별이 없었을 때. 인자는 연달아 빗나간 횟수.</summary>
        public event Action<int> Missed;

        ConstellationConfig m_Config;
        HandInput m_Hands;
        StarField m_StarField;
        LineRenderer m_Line;
        // 이미 그은 구간을 다시 지나면 그 구간은 그리지 않는다. 그래서 선이 여러 조각(획)으로 나뉠 수 있다.
        readonly List<LineRenderer> m_Strokes = new List<LineRenderer>();
        readonly List<Vector3> m_Points = new List<Vector3>();
        readonly List<Vector3> m_StrokePoints = new List<Vector3>();
        LineRenderer m_Preview;
        // 끌고 있는 손이 가리키는 별(손마다). 핀치를 놓을 때 이 별로 잇는다.
        readonly int[] m_DragTarget = { -1, -1 };
        // 지나간 순서대로의 별 번호. 닫힌 모양을 그릴 수 있게 같은 별이 다시 나올 수 있다.
        readonly List<int> m_Selected = new List<int>();
        int m_Unique;
        int m_DwellIndex = -1;
        float m_DwellStart;
        bool m_Active;
        float m_Grow = 1f;
        float m_NextMaxNotice;
        // 연달아 빗나간 횟수. 빗나갈수록 잡기 반경을 조금씩 넓혀 주고, 별을 잡으면 처음으로 돌아간다.
        int m_MissStreak;
        // 별 잇기가 시작된 뒤에 새로 쥔 핀치만 끌어서 잇기에 쓴다(키워드를 고르던 핀치가 이어져 별이 잡히지 않게).
        readonly bool[] m_DragArmed = new bool[2];
        // 선 위에서 각 별이 놓인 자리(0~1, 길이 기준). 빛을 별에서 별로 옮길 때 쓴다.
        readonly float[] m_StarUv = new float[16];
        bool m_Engraved;
        float m_EngraveLevel;
        bool m_ExternalPulse;
        float m_ExternalPulsePosition;
        float m_ExternalPulseStrength;

        const float k_GrowSeconds = 0.22f;
        // 이미 이은 별로 다시 이으려면 끌던 손이 이 시간 동안 그 별에 머물러야 한다(지나가다 붙지 않게).
        const float k_RevisitDwellSeconds = 0.3f;

        /// <summary>이은 별의 개수(같은 별을 다시 지나도 한 번만 센다).</summary>
        public int Count { get { return m_Unique; } }
        public IList<int> SelectedIndices { get { return m_Selected; } }

        public void Init(ConstellationConfig config, HandInput hands, StarField starField)
        {
            m_Config = config;
            m_Hands = hands;
            m_StarField = starField;
            m_Line = VisualFactory.CreateLine("Constellation Line", transform, config.lineWidth, config.lineColor, true);
            Color previewColor = config.lineColor;
            previewColor.a = 0.45f;
            m_Preview = VisualFactory.CreateLine("Preview Line", transform, config.lineWidth * 0.6f, previewColor, true);
            m_Strokes.Add(m_Line);
            m_Hands.PinchStarted += OnPinchStarted;
            m_Hands.PinchEnded += OnPinchEnded;
        }

        void OnDestroy()
        {
            if (m_Hands != null)
            {
                m_Hands.PinchStarted -= OnPinchStarted;
                m_Hands.PinchEnded -= OnPinchEnded;
            }
        }

        /// <summary>별 잇기를 받기 시작한다.</summary>
        public void Begin()
        {
            m_Active = true;
            m_MissStreak = 0;
            m_DragArmed[0] = m_DragArmed[1] = false;
            m_DragTarget[0] = m_DragTarget[1] = -1;
        }

        /// <summary>별 잇기를 멈춘다. 그어 둔 선은 남는다.</summary>
        public void End()
        {
            m_Active = false;
            m_Preview.positionCount = 0;
            if (m_StarField != null)
            {
                m_StarField.SetHovered(-1, -1);
                m_StarField.SetHandPoints(false, Vector3.zero, false, Vector3.zero);
            }
        }

        /// <summary>선과 선택을 모두 지운다.</summary>
        public void Clear()
        {
            m_Active = false;
            m_Selected.Clear();
            m_Unique = 0;
            m_DwellIndex = -1;
            m_Grow = 1f;
            m_Engraved = false;
            m_EngraveLevel = 0f;
            m_ExternalPulse = false;
            for (int i = 0; i < m_Strokes.Count; i++)
                m_Strokes[i].positionCount = 0;
            m_DragTarget[0] = m_DragTarget[1] = -1;
            if (m_Preview != null)
                m_Preview.positionCount = 0;
        }

        /// <summary>마지막으로 이은 별과 선을 지운다. 선이 없으면(별 1개 이하) 아무 일도 없다.</summary>
        public void Undo()
        {
            if (!m_Active || m_Selected.Count < 2)
                return;
            int last = m_Selected[m_Selected.Count - 1];
            m_Selected.RemoveAt(m_Selected.Count - 1);
            // 다른 구간에서 아직 지나는 별이면 밝은 채로 둔다.
            if (!m_Selected.Contains(last))
            {
                m_StarField.SetSelected(last, false);
                m_Unique--;
            }
            m_DwellIndex = -1;
            m_Grow = 1f;
            if (Undone != null)
                Undone(m_Unique);
        }

        /// <summary>이은 별과 선을 모두 지운다(별 잇기는 계속된다). 지운 게 없으면 아무 일도 없다.</summary>
        public void ClearAll()
        {
            if (!m_Active || m_Selected.Count == 0)
                return;
            for (int i = 0; i < m_Selected.Count; i++)
                m_StarField.SetSelected(m_Selected[i], false);
            m_Selected.Clear();
            m_Unique = 0;
            m_DwellIndex = -1;
            m_Grow = 1f;
            m_DragArmed[0] = m_DragArmed[1] = false;
            if (Undone != null)
                Undone(0);
        }

        bool LeftHandCancels
        {
            get { return m_Config.undoGesture == UndoGestureType.LeftPinch; }
        }

        /// <summary>이은 순서로 n번째 별이 선 위에서 놓인 자리(0~1).</summary>
        public float GetStarUv(int order)
        {
            if (order < 0 || order >= m_Selected.Count || order >= m_StarUv.Length)
                return 1f;
            return m_StarUv[order];
        }

        /// <summary>선 위의 한 자리에 밝은 빛 덩어리를 둔다(완성 연출이 쓴다). 세기가 0이면 끈다.</summary>
        public void SetPulse(float position, float strength)
        {
            m_ExternalPulse = strength > 0f;
            m_ExternalPulsePosition = position;
            m_ExternalPulseStrength = strength;
        }

        /// <summary>별자리가 하늘에 새겨진 상태. 선이 더 밝고 조금 굵어진다.</summary>
        public void SetEngraved(bool engraved)
        {
            m_Engraved = engraved;
        }

        void OnPinchStarted(HandSide side, Vector3 position)
        {
            if (m_Config.logStateChanges)
                Debug.Log("[Drawer] 핀치 시작 active=" + m_Active + " 가장 가까운 별=" + m_StarField.FindNearest(position, 1f) + " 반경내=" + m_StarField.FindNearest(position, m_Config.starGrabRadius));
            if (!m_Active)
                return;
            // 왼손이 취소 담당이면 왼손 핀치로는 별을 잡지 않는다.
            if (LeftHandCancels && side == HandSide.Left)
                return;
            m_DragArmed[(int)side] = true;

            int index = m_StarField.FindNearest(position, GrabRadius);
            if (index < 0)
            {
                // 별이 없는 곳을 집었다. 처음 쓰는 사람이 자주 겪으므로 알려서 힌트를 띄우게 한다.
                m_MissStreak++;
                if (Missed != null)
                    Missed(m_MissStreak);
                return;
            }
            m_MissStreak = 0;
            // 처음 핀치로 잡을 때는 이미 이은 별도 바로 이을 수 있다.
            TrySelectIndex(index, false);
        }

        // 끌던 핀치를 놓았다: 놓은 곳이 별이면 마지막 별에서 그 별로 잇는다. 지나간 별은 잇지 않는다.
        void OnPinchEnded(HandSide side)
        {
            int i = (int)side;
            int target = m_DragTarget[i];
            bool armed = m_DragArmed[i];
            m_DragTarget[i] = -1;
            m_DragArmed[i] = false;
            if (!m_Active || !armed || !m_Config.selectWhileDragging || target < 0 || target >= m_StarField.Count)
                return;
            if (LeftHandCancels && side == HandSide.Left)
                return;
            // 추적이 끊겨서 핀치가 풀린 경우는 놓은 것으로 보지 않는다.
            if (!m_Hands.Get(side).isTracked)
                return;
            TrySelectIndex(target, false);
        }

        /// <summary>지금의 잡기 반경. 연달아 빗나가면 조금씩 넓어지고(잡기 보조), 별을 잡으면 원래대로 돌아간다.</summary>
        public float GrabRadius
        {
            get
            {
                int steps = Mathf.Min(m_MissStreak, 3);
                return m_Config.starGrabRadius * (1f + steps * Mathf.Max(0f, m_Config.grabAssistPerMiss));
            }
        }

        // requireDwell: 이미 이은 별로 다시 이을 때 잠시 머물러야 하는지.
        void TrySelectIndex(int index, bool requireDwell)
        {
            if (index < 0)
                return;

            bool already = m_StarField.IsSelected(index);
            if (already)
            {
                // 방금 이은 별로 또 잇지는 않는다.
                if (m_Selected.Count == 0 || m_Selected[m_Selected.Count - 1] == index)
                    return;
                if (requireDwell)
                {
                    if (index != m_DwellIndex)
                    {
                        m_DwellIndex = index;
                        m_DwellStart = Time.unscaledTime;
                        return;
                    }
                    if (Time.unscaledTime - m_DwellStart < k_RevisitDwellSeconds)
                        return;
                }
            }

            // 새 별은 별 개수 한도로, 이미 이은 별을 다시 이을 때는 선 개수 한도(별 한도의 두 배)로 막는다.
            if ((!already && m_Unique >= m_Config.maxStars) || m_Selected.Count >= m_Config.maxStars * 2)
            {
                // 끌고 있는 동안 매 프레임 알리지 않도록 간격을 둔다.
                if (Time.unscaledTime >= m_NextMaxNotice)
                {
                    m_NextMaxNotice = Time.unscaledTime + 2f;
                    if (MaxReached != null)
                        MaxReached();
                }
                return;
            }

            m_Selected.Add(index);
            m_DwellIndex = -1;
            if (!already)
                m_Unique++;
            if (m_Config.logStateChanges)
                Debug.Log("[Drawer] 별 선택 " + index + (already ? " (다시 이음)" : "") + " 별 " + m_Unique + "개, 선 " + m_Selected.Count + "점");
            m_StarField.SetSelected(index, true);
            m_Grow = 0f;
            if (StarAdded != null)
                StarAdded(m_Unique);
        }

        void Update()
        {
            if (!m_Active || m_StarField == null || m_StarField.Count == 0)
                return;

            HandState left = m_Hands.Left;
            HandState right = m_Hands.Right;

            // 손이 가까이 간 별을 밝혀 어느 별이 잡힐지 보여 준다.
            int hoverLeft = left.isTracked && !LeftHandCancels ? m_StarField.FindNearest(left.pinchPoint, GrabRadius) : -1;
            int hoverRight = right.isTracked ? m_StarField.FindNearest(right.pinchPoint, GrabRadius) : -1;
            m_StarField.SetHovered(hoverLeft, hoverRight);
            m_StarField.SetHandPoints(left.isTracked && !LeftHandCancels, left.pinchPoint, right.isTracked, right.pinchPoint);

            // 핀치를 유지한 채 끌면 가리키는 별을 표시만 한다. 실제로 잇는 건 핀치를 놓을 때다.
            if (m_Config.selectWhileDragging)
            {
                m_DragTarget[0] = left.isTracked && left.isPinching && m_DragArmed[0] && !LeftHandCancels ? DragTarget(left.pinchPoint) : -1;
                m_DragTarget[1] = right.isTracked && right.isPinching && m_DragArmed[1] ? DragTarget(right.pinchPoint) : -1;
                if (m_DragTarget[0] >= 0)
                    hoverLeft = m_DragTarget[0];
                if (m_DragTarget[1] >= 0)
                    hoverRight = m_DragTarget[1];
                m_StarField.SetHovered(hoverLeft, hoverRight);
            }
        }

        // 끌고 있는 손이 머리에서 본 방향으로 가리키는 별. 방금 이은 별은 뺀다.
        int DragTarget(Vector3 handPoint)
        {
            Camera cam = Camera.main;
            if (cam == null)
                return -1;
            int exclude = m_Selected.Count > 0 ? m_Selected[m_Selected.Count - 1] : -1;
            return m_StarField.FindNearestByAngle(handPoint, cam.transform.position, m_Config.dragSnapAngle, m_Config.dragSnapDepthTolerance, false, exclude);
        }

        void LateUpdate()
        {
            if (m_Line == null || m_StarField == null)
                return;

            // 별이 사라졌으면(초기화) 선도 지운다.
            if (m_Selected.Count > 0 && m_StarField.Count == 0)
            {
                m_Selected.Clear();
                m_Unique = 0;
            }

            float scale = m_StarField.CurrentScale;
            int count = m_Selected.Count;
            m_EngraveLevel = Mathf.MoveTowards(m_EngraveLevel, m_Engraved ? 1f : 0f, Time.deltaTime / 0.8f);
            int used = 0;
            if (count >= 2)
            {
                m_Grow = Mathf.MoveTowards(m_Grow, 1f, Time.deltaTime / k_GrowSeconds);
                used = BuildStrokes(count, scale);
            }
            for (int i = used; i < m_Strokes.Count; i++)
                m_Strokes[i].positionCount = 0;

            UpdatePreview(count);
        }

        // 이은 순서대로 점을 놓되, 이미 그은 구간(방향 무관)을 다시 지나는 곳은 건너뛰어 획을 나눈다.
        // 반환값은 쓴 획의 개수.
        int BuildStrokes(int count, float scale)
        {
            m_Points.Clear();
            for (int i = 0; i < count; i++)
            {
                Vector3 position = m_StarField.GetWorldPosition(m_Selected[i]);
                // 새로 그은 선은 앞 별에서 자라나듯 그린다.
                if (i == count - 1 && m_Grow < 1f)
                    position = Vector3.Lerp(m_Points[i - 1], position, Mathf.SmoothStep(0f, 1f, m_Grow));
                m_Points.Add(position);
            }

            // 보이는 구간 길이로 각 별의 자리(0~1)를 정한다. 다시 지나는 구간은 길이 0.
            float total = 0f;
            if (m_StarUv.Length > 0)
                m_StarUv[0] = 0f;
            for (int i = 1; i < count; i++)
            {
                if (IsNewSegment(i))
                    total += Vector3.Distance(m_Points[i - 1], m_Points[i]);
                if (i < m_StarUv.Length)
                    m_StarUv[i] = total;
            }
            float inverse = total > 0.0001f ? 1f / total : 0f;
            for (int i = 0; i < count && i < m_StarUv.Length; i++)
                m_StarUv[i] *= inverse;

            bool lastGrowing = m_Grow < 1f && IsNewSegment(count - 1);
            int strokeCount = 0;
            float covered = 0f;
            int s = 1;
            while (s < count)
            {
                if (!IsNewSegment(s))
                {
                    s++;
                    continue;
                }
                // s부터 이어지는 새 구간들을 한 획으로 모은다.
                m_StrokePoints.Clear();
                m_StrokePoints.Add(m_Points[s - 1]);
                float length = 0f;
                int e = s;
                while (e < count && IsNewSegment(e))
                {
                    m_StrokePoints.Add(m_Points[e]);
                    length += Vector3.Distance(m_Points[e - 1], m_Points[e]);
                    e++;
                }
                LineRenderer line = GetStroke(strokeCount);
                line.widthMultiplier = m_Config.lineWidth * scale * (1f + 0.25f * m_EngraveLevel);
                line.positionCount = m_StrokePoints.Count;
                for (int k = 0; k < m_StrokePoints.Count; k++)
                    line.SetPosition(k, m_StrokePoints[k]);
                bool endsAtLast = e == count;
                ApplyStrokeLight(line, covered * inverse, (covered + length) * inverse, length, scale, endsAtLast && lastGrowing);
                covered += length;
                strokeCount++;
                s = e;
            }
            return strokeCount;
        }

        // i번째 구간(별 i-1 → 별 i)이 앞에서 그은 적 없는 구간인지
        bool IsNewSegment(int i)
        {
            int a = m_Selected[i - 1];
            int b = m_Selected[i];
            for (int j = 1; j < i; j++)
            {
                int c = m_Selected[j - 1];
                int d = m_Selected[j];
                if ((c == a && d == b) || (c == b && d == a))
                    return false;
            }
            return true;
        }

        LineRenderer GetStroke(int index)
        {
            while (m_Strokes.Count <= index)
            {
                LineRenderer line = VisualFactory.CreateLine("Constellation Line " + m_Strokes.Count, transform, m_Config.lineWidth, m_Config.lineColor, true);
                m_Strokes.Add(line);
            }
            return m_Strokes[index];
        }

        // 선을 따라 흐르는 빛과 빛 덩어리. 빛 덩어리 자리는 전체 선 기준(0~1)이라 획마다 제 구간으로 바꿔 준다.
        void ApplyStrokeLight(LineRenderer line, float from, float to, float length, float scale, bool growing)
        {
            Material material = line.sharedMaterial;
            material.SetFloat(VisualFactory.IntensityId, 1.4f + 0.9f * m_EngraveLevel);

            float span = Mathf.Max(0.0001f, to - from);
            float pulsePosition = 0f;
            float pulseStrength = 0f;
            if (m_ExternalPulse)
            {
                float local = (m_ExternalPulsePosition - from) / span;
                if (local >= -0.02f && local <= 1.02f)
                {
                    pulsePosition = Mathf.Clamp01(local);
                    pulseStrength = m_ExternalPulseStrength;
                }
            }
            else if (growing && m_Config.enableLineFlow)
            {
                // 자라나는 선의 끝에 빛을 얹는다.
                pulsePosition = 1f;
                pulseStrength = 2.5f * (1f - m_Grow * m_Grow);
            }
            material.SetFloat(VisualFactory.PulsePositionId, pulsePosition);
            material.SetFloat(VisualFactory.PulseStrengthId, pulseStrength);

            if (!m_Config.enableLineFlow)
            {
                material.SetFloat(VisualFactory.FlowStrengthId, 0f);
                return;
            }
            // 빛 띠 사이의 간격이 선 길이와 상관없이 약 12cm가 되게 한다(별자리가 커지면 함께 늘어난다).
            float bandLength = 0.12f * Mathf.Max(0.01f, scale);
            material.SetFloat(VisualFactory.FlowDensityId, Mathf.Clamp(length / bandLength, 1f, 64f));
            material.SetFloat(VisualFactory.FlowStrengthId, m_Config.lineFlowStrength);
            // 빛 덩어리의 길이는 약 6cm
            float inverse = length > 0.0001f ? 1f / length : 0f;
            material.SetFloat(VisualFactory.PulseWidthId, Mathf.Clamp(0.06f * scale * inverse, 0.01f, 0.5f));
        }

        // 마지막 별에서 핀치한 손까지 이어지는 흐린 선. 다음 별로 끌고 가는 느낌을 준다.
        void UpdatePreview(int count)
        {
            bool show = false;
            Vector3 target = Vector3.zero;
            if (m_Active && count > 0 && count < m_Config.maxStars * 2)
            {
                HandState right = m_Hands.Right;
                HandState left = m_Hands.Left;
                if (right.isTracked && right.isPinching)
                {
                    show = true;
                    target = right.pinchPoint;
                }
                else if (left.isTracked && left.isPinching)
                {
                    show = true;
                    target = left.pinchPoint;
                }
            }

            if (!show)
            {
                m_Preview.positionCount = 0;
                return;
            }
            m_Preview.positionCount = 2;
            m_Preview.SetPosition(0, m_StarField.GetWorldPosition(m_Selected[count - 1]));
            m_Preview.SetPosition(1, target);
        }
    }
}
