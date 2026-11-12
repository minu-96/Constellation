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

        ConstellationConfig m_Config;
        HandInput m_Hands;
        StarField m_StarField;
        LineRenderer m_Line;
        LineRenderer m_Preview;
        readonly List<int> m_Selected = new List<int>();
        bool m_Active;
        float m_Grow = 1f;
        float m_NextMaxNotice;
        // 별 잇기가 시작된 뒤에 새로 쥔 핀치만 끌어서 잇기에 쓴다(키워드를 고르던 핀치가 이어져 별이 잡히지 않게).
        readonly bool[] m_DragArmed = new bool[2];

        const float k_GrowSeconds = 0.22f;

        public int Count { get { return m_Selected.Count; } }
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
            m_DragArmed[0] = m_DragArmed[1] = false;
        }

        /// <summary>별 잇기를 멈춘다. 그어 둔 선은 남는다.</summary>
        public void End()
        {
            m_Active = false;
            m_Preview.positionCount = 0;
            if (m_StarField != null)
                m_StarField.SetHovered(-1, -1);
        }

        /// <summary>선과 선택을 모두 지운다.</summary>
        public void Clear()
        {
            m_Active = false;
            m_Selected.Clear();
            m_Grow = 1f;
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
            m_StarField.SetSelected(last, false);
            m_Grow = 1f;
            if (Undone != null)
                Undone(m_Selected.Count);
        }

        void OnPinchStarted(HandSide side, Vector3 position)
        {
            Debug.Log("[Drawer] 핀치 시작 active=" + m_Active + " 가장 가까운 별=" + m_StarField.FindNearest(position, 1f) + " 반경내=" + m_StarField.FindNearest(position, m_Config.starGrabRadius));
            if (!m_Active)
                return;
            m_DragArmed[(int)side] = true;
            TrySelect(position, m_Config.starGrabRadius);
        }

        void TrySelect(Vector3 position, float radius)
        {
            int index = m_StarField.FindNearest(position, radius);
            if (index < 0 || m_StarField.IsSelected(index))
                return;

            if (m_Selected.Count >= m_Config.maxStars)
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
            Debug.Log("[Drawer] 별 선택 " + index + " 누적 " + m_Selected.Count);
            m_StarField.SetSelected(index, true);
            m_Grow = 0f;
            if (StarAdded != null)
                StarAdded(m_Selected.Count);
        }

        void Update()
        {
            if (!m_Active || m_StarField == null || m_StarField.Count == 0)
                return;

            HandState left = m_Hands.Left;
            HandState right = m_Hands.Right;

            // 손이 가까이 간 별을 밝혀 어느 별이 잡힐지 보여 준다.
            int hoverLeft = left.isTracked ? m_StarField.FindNearest(left.pinchPoint, m_Config.starGrabRadius) : -1;
            int hoverRight = right.isTracked ? m_StarField.FindNearest(right.pinchPoint, m_Config.starGrabRadius) : -1;
            m_StarField.SetHovered(hoverLeft, hoverRight);

            // 핀치를 유지한 채 다른 별을 지나가면 그 별도 잇는다.
            if (m_Config.selectWhileDragging)
            {
                float dragRadius = m_Config.starGrabRadius * m_Config.dragGrabRadiusScale;
                if (left.isTracked && left.isPinching && m_DragArmed[0])
                    TrySelect(left.pinchPoint, dragRadius);
                if (right.isTracked && right.isPinching && m_DragArmed[1])
                    TrySelect(right.pinchPoint, dragRadius);
            }
            if (!left.isPinching)
                m_DragArmed[0] = false;
            if (!right.isPinching)
                m_DragArmed[1] = false;
        }

        void LateUpdate()
        {
            if (m_Line == null || m_StarField == null)
                return;

            // 별이 사라졌으면(초기화) 선도 지운다.
            if (m_Selected.Count > 0 && m_StarField.Count == 0)
                m_Selected.Clear();

            float scale = m_StarField.CurrentScale;
            int count = m_Selected.Count;
            m_Line.widthMultiplier = m_Config.lineWidth * scale;
            m_Line.positionCount = count >= 2 ? count : 0;
            if (count >= 2)
            {
                m_Grow = Mathf.MoveTowards(m_Grow, 1f, Time.deltaTime / k_GrowSeconds);
                for (int i = 0; i < count; i++)
                    m_Line.SetPosition(i, m_StarField.GetWorldPosition(m_Selected[i]));
                // 새로 그은 선은 앞 별에서 자라나듯 그린다.
                if (m_Grow < 1f)
                {
                    Vector3 from = m_StarField.GetWorldPosition(m_Selected[count - 2]);
                    Vector3 to = m_StarField.GetWorldPosition(m_Selected[count - 1]);
                    m_Line.SetPosition(count - 1, Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, m_Grow)));
                }
            }

            UpdatePreview(count);
        }

        // 마지막 별에서 핀치한 손까지 이어지는 흐린 선. 다음 별로 끌고 가는 느낌을 준다.
        void UpdatePreview(int count)
        {
            bool show = false;
            Vector3 target = Vector3.zero;
            if (m_Active && count > 0 && count < m_Config.maxStars)
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
