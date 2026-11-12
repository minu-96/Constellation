#if UNITY_EDITOR || UNITY_STANDALONE
#define CONSTELLATION_PC_SIM
#endif
using System;
using UnityEngine;
#if CONSTELLATION_PC_SIM
using UnityEngine.InputSystem;
#endif

namespace Constellation
{
    /// <summary>
    /// 양손 모으기(F-07)와 되돌리기(F-06) 제스처를 판정해 이벤트로 알린다.
    /// 별 개수 같은 조건은 여기서 보지 않는다. SessionController가 판단한다.
    /// </summary>
    public class GestureDetector : MonoBehaviour
    {
        public event Action HandsTogether;
        public event Action UndoRequested;
        /// <summary>이은 선을 모두 지우라는 요청(LeftPinch: 왼손 핀치를 길게 쥐기).</summary>
        public event Action ClearRequested;

        /// <summary>꺼 두면 아무 제스처도 알리지 않는다. Drawing 상태에서만 켠다.</summary>
        public bool Detecting { get; set; }

        ConstellationConfig m_Config;
        HandInput m_Hands;
        Transform m_Head;

        float m_TogetherTime;
        float m_TogetherLostTime;
        // 손을 맞대면 서로 가려 추적이 잠깐 끊기기 쉽다. 이 시간 안의 끊김은 계속 모은 것으로 본다.
        const float k_TogetherLostGrace = 0.5f;
        bool m_TogetherFired;
        float m_UndoReadyTime;
        readonly float[] m_SwipeTravel = new float[2];
        readonly float[] m_SwipeSign = new float[2];
        // 왼손 핀치(LeftPinch): 쥔 시각과 길게 쥐기로 이미 지웠는지
        bool m_LeftHolding;
        float m_LeftHoldStart;
        bool m_LeftClearFired;

        /// <summary>왼손을 길게 쥐는 중이면 0~1로 얼마나 찼는지. 아니면 0.</summary>
        public float ClearHoldProgress
        {
            get
            {
                if (!Detecting || !m_LeftHolding || m_LeftClearFired || m_Config == null)
                    return 0f;
                return Mathf.Clamp01((Time.unscaledTime - m_LeftHoldStart) / Mathf.Max(0.1f, m_Config.clearHoldSeconds));
            }
        }

        public void Init(ConstellationConfig config, HandInput hands, Transform head)
        {
            m_Config = config;
            m_Hands = hands;
            m_Head = head;
            m_Hands.MiddlePinchStarted += OnMiddlePinch;
            m_Hands.PinchStarted += OnPinchStarted;
            m_Hands.PinchEnded += OnPinchEnded;
        }

        void OnDestroy()
        {
            if (m_Hands != null)
            {
                m_Hands.MiddlePinchStarted -= OnMiddlePinch;
                m_Hands.PinchStarted -= OnPinchStarted;
                m_Hands.PinchEnded -= OnPinchEnded;
            }
        }

        /// <summary>진행 중이던 판정을 지운다. 상태가 바뀔 때 부른다.</summary>
        public void ResetGestures()
        {
            m_TogetherTime = 0f;
            // 이미 손을 모은 채 들어왔다면 한 번 떼었다가 다시 모아야 인식한다.
            m_TogetherFired = true;
            m_SwipeTravel[0] = m_SwipeTravel[1] = 0f;
            m_UndoReadyTime = Time.unscaledTime + 0.5f;
            m_LeftHolding = false;
            m_LeftClearFired = false;
        }

        void Update()
        {
            if (m_Config == null || !Detecting)
                return;

            UpdateHandsTogether();
            UpdateLeftHold();
            if (m_Config.undoGesture == UndoGestureType.PalmSwipe)
            {
                UpdateSwipe(HandSide.Left);
                UpdateSwipe(HandSide.Right);
            }

#if CONSTELLATION_PC_SIM
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.zKey.wasPressedThisFrame)
                FireUndo();
            if (keyboard != null && keyboard.xKey.wasPressedThisFrame && ClearRequested != null)
                ClearRequested();
#endif
        }

        void UpdateHandsTogether()
        {
            if (!m_Hands.BothHandsTracked)
            {
                // 모으는 중에 추적이 끊겼으면 잠깐은 이어서 센다.
                m_TogetherLostTime += Time.unscaledDeltaTime;
                if (m_TogetherTime > 0f && !m_TogetherFired && m_TogetherLostTime <= k_TogetherLostGrace)
                    AdvanceTogether();
                else
                    m_TogetherTime = 0f;
                return;
            }
            m_TogetherLostTime = 0f;

            float distance = Vector3.Distance(m_Hands.Left.palm, m_Hands.Right.palm);
            if (m_TogetherFired)
            {
                if (distance > m_Config.handsTogetherReleaseDistance)
                    m_TogetherFired = false;
                return;
            }

            if (distance < m_Config.handsTogetherDistance)
                AdvanceTogether();
            else
                m_TogetherTime = 0f;
        }

        void AdvanceTogether()
        {
            m_TogetherTime += Time.unscaledDeltaTime;
            if (m_TogetherTime < m_Config.handsTogetherHoldSeconds)
                return;
            m_TogetherFired = true;
            m_TogetherTime = 0f;
            if (HandsTogether != null)
                HandsTogether();
        }

        // 손바닥을 편 채 좌우로 빠르게 움직인 거리를 쌓아, 기준을 넘으면 되돌리기로 본다.
        void UpdateSwipe(HandSide side)
        {
            int i = (int)side;
            HandState hand = m_Hands.Get(side);
            if (!hand.isTracked || !hand.isOpen || hand.isPinching || m_Head == null)
            {
                m_SwipeTravel[i] = 0f;
                return;
            }

            Vector3 right = m_Head.right;
            right.y = 0f;
            if (right.sqrMagnitude < 0.0001f)
                return;
            right.Normalize();

            float lateral = Vector3.Dot(hand.palmVelocity, right);
            float speed = Mathf.Abs(lateral);
            // 좌우 성분이 주된 움직임일 때만 인정한다(별 사이를 대각선으로 오가는 동작과 구분).
            bool mostlySideways = speed > hand.palmVelocity.magnitude * 0.75f;
            float sign = Mathf.Sign(lateral);

            if (speed >= m_Config.undoSwipeSpeed && mostlySideways)
            {
                if (m_SwipeTravel[i] > 0f && sign != m_SwipeSign[i])
                    m_SwipeTravel[i] = 0f;
                m_SwipeSign[i] = sign;
                m_SwipeTravel[i] += speed * Time.unscaledDeltaTime;
                if (m_SwipeTravel[i] >= m_Config.undoSwipeDistance)
                {
                    m_SwipeTravel[i] = 0f;
                    FireUndo();
                }
            }
            else
            {
                m_SwipeTravel[i] = 0f;
            }
        }

        bool LeftPinchMode
        {
            get { return m_Config != null && m_Config.undoGesture == UndoGestureType.LeftPinch; }
        }

        void OnPinchStarted(HandSide side, Vector3 position)
        {
            if (!Detecting || !LeftPinchMode || side != HandSide.Left)
                return;
            m_LeftHolding = true;
            m_LeftHoldStart = Time.unscaledTime;
            m_LeftClearFired = false;
        }

        // 짧게 쥐었다 놓으면 마지막 선 취소. 길게 쥐어 이미 전부 지웠으면 놓아도 아무 일 없다.
        void OnPinchEnded(HandSide side)
        {
            if (side != HandSide.Left || !m_LeftHolding)
                return;
            m_LeftHolding = false;
            if (!Detecting || !LeftPinchMode || m_LeftClearFired)
                return;
            FireUndo();
        }

        void UpdateLeftHold()
        {
            if (!m_LeftHolding || m_LeftClearFired || !LeftPinchMode)
                return;
            if (!m_Hands.Left.isTracked)
            {
                // 쥔 채로 추적이 끊기면 없던 일로 한다.
                m_LeftHolding = false;
                return;
            }
            if (Time.unscaledTime - m_LeftHoldStart < m_Config.clearHoldSeconds)
                return;
            m_LeftClearFired = true;
            if (ClearRequested != null)
                ClearRequested();
        }

        void OnMiddlePinch(HandSide side)
        {
            if (m_Config == null || !Detecting || m_Config.undoGesture != UndoGestureType.MiddlePinch)
                return;
            FireUndo();
        }

        void FireUndo()
        {
            if (Time.unscaledTime < m_UndoReadyTime)
                return;
            // 왼손 탭은 실수로 두 번 나기 어려우므로 짧게 쉰다(연달아 여러 선을 지울 수 있게).
            m_UndoReadyTime = Time.unscaledTime + (LeftPinchMode ? 0.25f : m_Config.undoCooldownSeconds);
            if (UndoRequested != null)
                UndoRequested();
        }
    }
}
