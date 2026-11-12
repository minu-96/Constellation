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
        LineRenderer m_Preview;
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
            m_Hands.PinchStarted += OnPinchStarted;
        }

        void OnDestroy()
        {
            if (m_Hands != null)
                m_Hands.PinchStarted -= OnPinchStarted;
        }

        /// <summary>별 잇기를 받기 시작한다.</summary>
        public void Begin()
        {
            m_Active = true;
            m_MissStreak = 0;
            m_DragArmed[0] = m_DragArmed[1] = false;
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
            if (m_Line != null)
                m_Line.positionCount = 0;
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
            int hoverLeft = left.isTracked ? m_StarField.FindNearest(left.pinchPoint, GrabRadius) : -1;
            int hoverRight = right.isTracked ? m_StarField.FindNearest(right.pinchPoint, GrabRadius) : -1;
            m_StarField.SetHovered(hoverLeft, hoverRight);
            m_StarField.SetHandPoints(left.isTracked, left.pinchPoint, right.isTracked, right.pinchPoint);

            // 핀치를 유지한 채 다른 별 쪽으로 끌면 그 별에 붙어 이어진다(머리에서 본 방향 기준).
            if (m_Config.selectWhileDragging)
            {
                if (left.isTracked && left.isPinching && m_DragArmed[0])
                    DragSelect(left.pinchPoint, ref hoverLeft);
                if (right.isTracked && right.isPinching && m_DragArmed[1])
                    DragSelect(right.pinchPoint, ref hoverRight);
                m_StarField.SetHovered(hoverLeft, hoverRight);
            }
            if (!left.isPinching)
                m_DragArmed[0] = false;
            if (!right.isPinching)
                m_DragArmed[1] = false;
        }

        // 끌고 있는 손이 가리키는 별을 찾아 잇는다. 이미 이은 별도 후보지만 방금 이은 별은 뺀다.
        void DragSelect(Vector3 handPoint, ref int hover)
        {
            Camera cam = Camera.main;
            if (cam == null)
                return;
            int exclude = m_Selected.Count > 0 ? m_Selected[m_Selected.Count - 1] : -1;
            int index = m_StarField.FindNearestByAngle(handPoint, cam.transform.position, m_Config.dragSnapAngle, m_Config.dragSnapDepthTolerance, false, exclude);
            hover = index;
            if (index < 0)
            {
                m_DwellIndex = -1;
                return;
            }
            bool already = m_StarField.IsSelected(index);
            if (!already)
                m_DwellIndex = -1;
            TrySelectIndex(index, already);
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
            m_Line.widthMultiplier = m_Config.lineWidth * scale * (1f + 0.25f * m_EngraveLevel);
            m_Line.positionCount = count >= 2 ? count : 0;
            if (count >= 2)
            {
                m_Grow = Mathf.MoveTowards(m_Grow, 1f, Time.deltaTime / k_GrowSeconds);
                float total = 0f;
                Vector3 previous = Vector3.zero;
                for (int i = 0; i < count; i++)
                {
                    Vector3 position = m_StarField.GetWorldPosition(m_Selected[i]);
                    // 새로 그은 선은 앞 별에서 자라나듯 그린다.
                    if (i == count - 1 && m_Grow < 1f)
                        position = Vector3.Lerp(previous, position, Mathf.SmoothStep(0f, 1f, m_Grow));
                    m_Line.SetPosition(i, position);
                    if (i > 0)
                        total += Vector3.Distance(previous, position);
                    if (i < m_StarUv.Length)
                        m_StarUv[i] = total;
                    previous = position;
                }
                UpdateLineLight(count, total, scale);
            }

            UpdatePreview(count);
        }

        // 선을 따라 흐르는 빛과, 새 선이 이어질 때 앞 별에서 새 별로 지나가는 빛.
        void UpdateLineLight(int count, float totalLength, float scale)
        {
            Material material = m_Line.sharedMaterial;
            float inverse = totalLength > 0.0001f ? 1f / totalLength : 0f;
            for (int i = 0; i < count && i < m_StarUv.Length; i++)
                m_StarUv[i] *= inverse;

            material.SetFloat(VisualFactory.IntensityId, 1.4f + 0.9f * m_EngraveLevel);
            if (!m_Config.enableLineFlow)
            {
                material.SetFloat(VisualFactory.FlowStrengthId, 0f);
                material.SetFloat(VisualFactory.PulseStrengthId, m_ExternalPulse ? m_ExternalPulseStrength : 0f);
                material.SetFloat(VisualFactory.PulsePositionId, m_ExternalPulsePosition);
                return;
            }

            // 빛 띠 사이의 간격이 선 길이와 상관없이 약 12cm가 되게 한다(별자리가 커지면 함께 늘어난다).
            float bandLength = 0.12f * Mathf.Max(0.01f, scale);
            material.SetFloat(VisualFactory.FlowDensityId, Mathf.Clamp(totalLength / bandLength, 1f, 64f));
            material.SetFloat(VisualFactory.FlowStrengthId, m_Config.lineFlowStrength);

            float pulsePosition = m_ExternalPulsePosition;
            float pulseStrength = m_ExternalPulse ? m_ExternalPulseStrength : 0f;
            if (!m_ExternalPulse && m_Grow < 1f && count - 2 < m_StarUv.Length)
            {
                // 자라나는 선의 끝에 빛을 얹는다.
                pulsePosition = 1f;
                pulseStrength = 2.5f * (1f - m_Grow * m_Grow);
            }
            material.SetFloat(VisualFactory.PulsePositionId, pulsePosition);
            material.SetFloat(VisualFactory.PulseStrengthId, pulseStrength);
            // 빛 덩어리의 길이는 약 6cm
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
