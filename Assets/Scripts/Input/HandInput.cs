using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace Constellation
{
    public enum HandSide
    {
        Left = 0,
        Right = 1
    }

    /// <summary>한 손의 현재 상태. 좌표는 모두 월드 기준이다.</summary>
    public class HandState
    {
        public const int JointCount = 26;

        public bool isTracked;
        public bool isPinching;
        public bool isMiddlePinching;
        /// <summary>손가락 네 개 중 세 개 이상을 편 상태</summary>
        public bool isOpen;
        public Vector3 thumbTip;
        public Vector3 indexTip;
        public Vector3 middleTip;
        public Vector3 palm;
        public Vector3 wrist;
        /// <summary>엄지와 검지 끝의 가운데. 별을 잡는 위치로 쓴다.</summary>
        public Vector3 pinchPoint;
        public Vector3 palmVelocity;
        /// <summary>XRHandJointID 순서(Wrist=0 … LittleTip=25)의 관절 위치</summary>
        public readonly Vector3[] joints = new Vector3[JointCount];
        public readonly bool[] jointValid = new bool[JointCount];

        internal Vector3 lastPalm;
        internal bool hadPalm;

        internal void ClearTracking()
        {
            isTracked = false;
            isPinching = false;
            isMiddlePinching = false;
            isOpen = false;
            hadPalm = false;
            palmVelocity = Vector3.zero;
            for (int i = 0; i < JointCount; i++)
                jointValid[i] = false;
        }
    }

    /// <summary>
    /// XR Hands에서 손 관절을 읽어 핀치 위치와 인식 여부를 이벤트로 알린다(F-02).
    /// 상태는 바꾸지 않는다. 판단은 SessionController가 한다.
    /// </summary>
    public class HandInput : MonoBehaviour
    {
        /// <summary>한 손 이상이 보이기 시작하거나 모두 사라졌을 때</summary>
        public event Action<bool> TrackingChanged;
        public event Action<HandSide, Vector3> PinchStarted;
        public event Action<HandSide> PinchEnded;
        public event Action<HandSide> MiddlePinchStarted;

        public HandState Left { get { return m_Hands[0]; } }
        public HandState Right { get { return m_Hands[1]; } }
        public bool AnyHandTracked { get; private set; }
        public bool BothHandsTracked { get { return m_Hands[0].isTracked && m_Hands[1].isTracked; } }
        /// <summary>에디터에서 마우스로 손을 흉내 내는 중인지</summary>
        public bool IsSimulated { get; private set; }

        readonly HandState[] m_Hands = { new HandState(), new HandState() };
        static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();

        ConstellationConfig m_Config;
        Transform m_Origin;
        Camera m_Camera;
        XRHandSubsystem m_Subsystem;
        float m_NextSubsystemSearch;
        bool m_NullWarned;

        public void Init(ConstellationConfig config, Transform trackingOrigin, Camera headCamera)
        {
            m_Config = config;
            m_Origin = trackingOrigin;
            m_Camera = headCamera;
        }

        public HandState Get(HandSide side)
        {
            return m_Hands[(int)side];
        }

        void Update()
        {
            if (m_Config == null)
            {
                if (!m_NullWarned)
                {
                    m_NullWarned = true;
                    Debug.LogWarning("[HandInput] Init이 호출되지 않았거나 config가 없음");
                }
                return;
            }

            FindSubsystem();

            bool simulated = false;
#if UNITY_EDITOR
            // 에디터에서는 H로 켠 시뮬레이션 손이 있으면 OpenXR 런타임보다 우선한다.
            simulated = m_Config.editorSimulation && SimulateHands();
#endif
            bool hasRealHands = !simulated && m_Subsystem != null && m_Subsystem.running;
            if (simulated)
            {
                IsSimulated = true;
            }
            else if (hasRealHands)
            {
                IsSimulated = false;
                ReadHand(m_Subsystem.leftHand, m_Hands[0]);
                ReadHand(m_Subsystem.rightHand, m_Hands[1]);
            }
            else
            {
                IsSimulated = false;
                m_Hands[0].ClearTracking();
                m_Hands[1].ClearTracking();
            }

            UpdateDerived(HandSide.Left);
            UpdateDerived(HandSide.Right);

            bool any = m_Hands[0].isTracked || m_Hands[1].isTracked;
            if (any != AnyHandTracked)
            {
                AnyHandTracked = any;
                if (TrackingChanged != null)
                    TrackingChanged(any);
            }
        }

        void FindSubsystem()
        {
            if (m_Subsystem != null && m_Subsystem.running)
                return;
            if (Time.unscaledTime < m_NextSubsystemSearch)
                return;
            m_NextSubsystemSearch = Time.unscaledTime + 1f;

            SubsystemManager.GetSubsystems(s_Subsystems);
            m_Subsystem = null;
            for (int i = 0; i < s_Subsystems.Count; i++)
            {
                if (s_Subsystems[i] != null && s_Subsystems[i].running)
                {
                    m_Subsystem = s_Subsystems[i];
                    break;
                }
            }
        }

        void ReadHand(XRHand hand, HandState state)
        {
            if (!hand.isTracked)
            {
                state.ClearTracking();
                return;
            }

            int valid = 0;
            for (int i = 0; i < HandState.JointCount; i++)
            {
                // XRHandJointID는 Wrist(=1)부터 LittleTip(=26)까지 이어진다.
                XRHandJointID id = (XRHandJointID)(i + 1);
                Pose pose;
                if (hand.GetJoint(id).TryGetPose(out pose))
                {
                    state.joints[i] = m_Origin != null ? m_Origin.TransformPoint(pose.position) : pose.position;
                    state.jointValid[i] = true;
                    valid++;
                }
                else
                {
                    state.jointValid[i] = false;
                }
            }

            int thumb = XRHandJointID.ThumbTip.ToIndex();
            int index = XRHandJointID.IndexTip.ToIndex();
            int palm = XRHandJointID.Palm.ToIndex();
            int wrist = XRHandJointID.Wrist.ToIndex();
            // 핀치 판정에 필요한 관절이 없으면 인식되지 않은 것으로 본다.
            if (valid == 0 || !state.jointValid[thumb] || !state.jointValid[index] || !state.jointValid[wrist])
            {
                state.ClearTracking();
                return;
            }

            state.isTracked = true;
            state.thumbTip = state.joints[thumb];
            state.indexTip = state.joints[index];
            state.wrist = state.joints[wrist];
            state.palm = state.jointValid[palm] ? state.joints[palm] : state.wrist;
            int middle = XRHandJointID.MiddleTip.ToIndex();
            state.middleTip = state.jointValid[middle] ? state.joints[middle] : state.indexTip;
            state.isOpen = CountExtendedFingers(state) >= 3;
        }

        static int CountExtendedFingers(HandState state)
        {
            int count = 0;
            if (IsExtended(state, XRHandJointID.IndexProximal, XRHandJointID.IndexTip)) count++;
            if (IsExtended(state, XRHandJointID.MiddleProximal, XRHandJointID.MiddleTip)) count++;
            if (IsExtended(state, XRHandJointID.RingProximal, XRHandJointID.RingTip)) count++;
            if (IsExtended(state, XRHandJointID.LittleProximal, XRHandJointID.LittleTip)) count++;
            return count;
        }

        // 손가락을 펴면 손끝이 손목에서 멀어진다. 손 크기와 상관없도록 비율로 본다.
        static bool IsExtended(HandState state, XRHandJointID proximalId, XRHandJointID tipId)
        {
            int p = proximalId.ToIndex();
            int t = tipId.ToIndex();
            if (!state.jointValid[p] || !state.jointValid[t])
                return false;
            float toProximal = Vector3.Distance(state.wrist, state.joints[p]);
            float toTip = Vector3.Distance(state.wrist, state.joints[t]);
            return toProximal > 0.001f && toTip > toProximal * 1.5f;
        }

        void UpdateDerived(HandSide side)
        {
            HandState state = m_Hands[(int)side];
            if (!state.isTracked)
            {
                if (state.isPinching)
                {
                    state.isPinching = false;
                    if (PinchEnded != null)
                        PinchEnded(side);
                }
                state.isMiddlePinching = false;
                state.hadPalm = false;
                return;
            }

            state.pinchPoint = (state.thumbTip + state.indexTip) * 0.5f;

            float dt = Time.unscaledDeltaTime;
            if (state.hadPalm && dt > 0.0001f)
            {
                Vector3 raw = (state.palm - state.lastPalm) / dt;
                state.palmVelocity = Vector3.Lerp(state.palmVelocity, raw, 0.5f);
            }
            else
            {
                state.palmVelocity = Vector3.zero;
            }
            state.lastPalm = state.palm;
            state.hadPalm = true;

            // 시뮬레이션에서는 핀치 여부를 직접 정했으므로 거리로 다시 판정하지 않는다.
            bool pinching = state.isPinching;
            bool middlePinching = state.isMiddlePinching;
            if (!IsSimulated)
            {
                float indexGap = Vector3.Distance(state.thumbTip, state.indexTip);
                float middleGap = Vector3.Distance(state.thumbTip, state.middleTip);
                pinching = Hysteresis(m_PinchLatched[(int)side], indexGap);
                // 엄지가 중지에 더 가까우면 검지 핀치로 치지 않는다.
                middlePinching = Hysteresis(m_MiddleLatched[(int)side], middleGap) && middleGap < indexGap;
                if (middlePinching)
                    pinching = false;
            }

            bool wasPinching = m_PinchLatched[(int)side];
            bool wasMiddle = m_MiddleLatched[(int)side];
            m_PinchLatched[(int)side] = pinching;
            m_MiddleLatched[(int)side] = middlePinching;
            state.isPinching = pinching;
            state.isMiddlePinching = middlePinching;

            if (pinching && !wasPinching && PinchStarted != null)
                PinchStarted(side, state.pinchPoint);
            if (!pinching && wasPinching && PinchEnded != null)
                PinchEnded(side);
            if (middlePinching && !wasMiddle && MiddlePinchStarted != null)
                MiddlePinchStarted(side);
        }

        readonly bool[] m_PinchLatched = new bool[2];
        readonly bool[] m_MiddleLatched = new bool[2];

        bool Hysteresis(bool latched, float distance)
        {
            if (latched)
                return distance < m_Config.pinchEndDistance;
            return distance < m_Config.pinchStartDistance;
        }

#if UNITY_EDITOR
        // ---------------------------------------------------------------- 에디터 시뮬레이션
        // 헤드셋 없이 흐름을 확인하기 위한 것. 빌드에는 들어가지 않는다.
        //   마우스 이동: 오른손 위치      마우스 왼쪽 버튼: 핀치
        //   휠: 손까지의 거리            H: 손 숨기기/보이기(기본은 보임)
        //   Space(누르고 있기) 또는 마우스 오른쪽 클릭(토글): 양손 모으기   Z: 되돌리기(GestureDetector가 처리)
        bool m_SimHandVisible = true; // 에디터에서는 기본으로 켜 둔다(H로 끄기)
        bool m_SimWarned;
        bool m_SimBothHands;
        float m_SimLogTime;
        float m_SimDepth = 0.5f;

        bool SimulateHands()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard == null || mouse == null || m_Camera == null)
            {
                if (!m_SimWarned)
                {
                    m_SimWarned = true;
                    Debug.LogWarning("[HandInput] 시뮬레이션 불가: keyboard=" + (keyboard != null) + " mouse=" + (mouse != null) + " camera=" + (m_Camera != null));
                }
                return false;
            }

            if (keyboard.hKey.wasPressedThisFrame)
            {
                m_SimHandVisible = !m_SimHandVisible;
                Debug.Log("[HandInput] 시뮬레이션 손 " + (m_SimHandVisible ? "켜짐" : "꺼짐"));
            }
            if (!m_SimHandVisible)
                return false;

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                m_SimDepth = Mathf.Clamp(m_SimDepth + Mathf.Sign(scroll) * 0.03f, 0.2f, 1.2f);

            Vector2 pointer = mouse.position.ReadValue();
            Ray ray = m_Camera.ScreenPointToRay(new Vector3(pointer.x, pointer.y, 0f));
            Vector3 point = ray.origin + ray.direction * m_SimDepth;

            HandState right = m_Hands[1];
            FillSimulatedHand(right, point, mouse.leftButton.isPressed);

            HandState left = m_Hands[0];
            // 마우스 오른쪽 클릭으로도 양손 모으기를 켜고 끌 수 있다(키 입력이 안 먹는 환경용).
            if (mouse.rightButton.wasPressedThisFrame)
                m_SimBothHands = !m_SimBothHands;
            if (keyboard.spaceKey.isPressed || m_SimBothHands)
                FillSimulatedHand(left, point - m_Camera.transform.right * 0.05f, false);
            else
                left.ClearTracking();
            return true;
        }

        void FillSimulatedHand(HandState state, Vector3 point, bool pinching)
        {
            Transform cam = m_Camera.transform;
            state.isTracked = true;
            state.isOpen = !pinching;
            state.isPinching = pinching;
            state.isMiddlePinching = false;
            float gap = pinching ? 0.004f : 0.03f;
            state.thumbTip = point - cam.right * gap;
            state.indexTip = point + cam.right * gap;
            state.middleTip = state.indexTip + cam.up * 0.02f;
            state.palm = point - cam.up * 0.08f;
            state.wrist = point - cam.up * 0.15f;
            for (int i = 0; i < HandState.JointCount; i++)
                state.jointValid[i] = false;
            SetSimJoint(state, XRHandJointID.Wrist, state.wrist);
            SetSimJoint(state, XRHandJointID.Palm, state.palm);
            SetSimJoint(state, XRHandJointID.ThumbTip, state.thumbTip);
            SetSimJoint(state, XRHandJointID.IndexTip, state.indexTip);
            SetSimJoint(state, XRHandJointID.MiddleTip, state.middleTip);
        }

        static void SetSimJoint(HandState state, XRHandJointID id, Vector3 position)
        {
            int i = id.ToIndex();
            state.joints[i] = position;
            state.jointValid[i] = true;
        }
#endif
    }
}
