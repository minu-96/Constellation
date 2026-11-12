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
#if UNITY_EDITOR
            // 시뮬레이션 손은 잡는 별이 놓인 거리에서 시작한다.
            m_SimDepth = config.ActiveStarZone.distance;
#endif
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
        //   화살표 키 또는 마우스 가운데 버튼 드래그: 둘러보기(헤드셋이 없을 때만)
        //   Space(누르고 있기) 또는 마우스 오른쪽 클릭: 왼손 꺼짐→떨어짐→모음 순환   Z: 되돌리기(GestureDetector가 처리)
        bool m_SimHandVisible = true; // 에디터에서는 기본으로 켜 둔다(H로 끄기)
        bool m_SimWarned;
        int m_SimBothMode;
        float m_SimLogTime;
        float m_SimDepth = 0.5f;
        // 두 손바닥이 약 5cm 떨어지게 왼손을 놓는 위치(손 모양이 좌우 대칭이라 오른손 쪽으로 겹쳐 놓는다).
        const float k_SimTogetherOffset = 0.044f;
        float m_SimYaw;
        float m_SimPitch;

        /// <summary>
        /// 자동 스크린샷용 대본. 켜져 있으면 마우스·키보드 대신 이 값으로 손과 시선을 움직인다(ScreenshotDirector).
        /// </summary>
        public class SimScript
        {
            public bool handVisible;
            public Vector3 point;
            public bool pinch;
            /// <summary>0: 왼손 없음, 1: 두 손 떨어짐, 2: 두 손 모음</summary>
            public int bothMode;
            public float yaw;
            public float pitch;
        }

        /// <summary>null이 아니면 대본대로 움직인다.</summary>
        public SimScript Script { get; set; }

        bool SimulateScripted()
        {
            if (m_Origin != null && !UnityEngine.XR.XRSettings.isDeviceActive)
                m_Origin.rotation = Quaternion.Euler(-Script.pitch, Script.yaw, 0f);

            if (!Script.handVisible || m_Camera == null)
            {
                m_Hands[0].ClearTracking();
                m_Hands[1].ClearTracking();
                return true;
            }

            FillSimulatedHand(m_Hands[1], Script.point, Script.pinch, false);
            if (Script.bothMode == 1)
                FillSimulatedHand(m_Hands[0], Script.point - m_Camera.transform.right * 0.3f, false, true);
            else if (Script.bothMode == 2)
                FillSimulatedHand(m_Hands[0], Script.point + m_Camera.transform.right * k_SimTogetherOffset, false, true);
            else
                m_Hands[0].ClearTracking();
            return true;
        }

        bool SimulateHands()
        {
            if (Script != null)
                return SimulateScripted();

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

            SimulateLook(keyboard, mouse);

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                m_SimDepth = Mathf.Clamp(m_SimDepth + Mathf.Sign(scroll) * 0.03f, 0.2f, 1.2f);

            Vector2 pointer = mouse.position.ReadValue();
            Ray ray = m_Camera.ScreenPointToRay(new Vector3(pointer.x, pointer.y, 0f));
            Vector3 point = ray.origin + ray.direction * m_SimDepth;

            HandState right = m_Hands[1];
            FillSimulatedHand(right, point, mouse.leftButton.isPressed, false);

            HandState left = m_Hands[0];
            // 마우스 오른쪽 클릭(키 입력이 안 먹는 환경용): 끔 → 두 손 떨어뜨림 → 두 손 모음 → 끔 순으로 돈다.
            // 완성 제스처는 한 번 떼었다가 모아야 인식하므로 '떨어뜨림' 단계가 필요하다.
            if (mouse.rightButton.wasPressedThisFrame)
                m_SimBothMode = (m_SimBothMode + 1) % 3;
            if (keyboard.spaceKey.isPressed)
                FillSimulatedHand(left, point + m_Camera.transform.right * k_SimTogetherOffset, false, true);
            else if (m_SimBothMode == 1)
                FillSimulatedHand(left, point - m_Camera.transform.right * 0.3f, false, true);
            else if (m_SimBothMode == 2)
                FillSimulatedHand(left, point + m_Camera.transform.right * k_SimTogetherOffset, false, true);
            else
                left.ClearTracking();
            return true;
        }

        // 헤드셋 없이도 위쪽 별을 볼 수 있게 리그를 돌린다. 헤드셋이 연결되어 있으면 건드리지 않는다.
        void SimulateLook(Keyboard keyboard, Mouse mouse)
        {
            if (m_Origin == null || UnityEngine.XR.XRSettings.isDeviceActive)
                return;

            float yaw = 0f;
            float pitch = 0f;
            float keyStep = 60f * Time.unscaledDeltaTime;
            if (keyboard.leftArrowKey.isPressed) yaw -= keyStep;
            if (keyboard.rightArrowKey.isPressed) yaw += keyStep;
            if (keyboard.upArrowKey.isPressed) pitch += keyStep;
            if (keyboard.downArrowKey.isPressed) pitch -= keyStep;
            if (mouse.middleButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                yaw += delta.x * 0.15f;
                pitch += delta.y * 0.15f;
            }
            if (yaw == 0f && pitch == 0f)
                return;

            m_SimYaw += yaw;
            m_SimPitch = Mathf.Clamp(m_SimPitch + pitch, -60f, 80f);
            m_Origin.rotation = Quaternion.Euler(-m_SimPitch, m_SimYaw, 0f);
        }

        // 손목을 원점으로 한 편 손 모양(m). x: 오른손 기준 새끼손가락 쪽이 +, y: 손끝 방향.
        // XRHandJointID 순서(Wrist=0 … LittleTip=25).
        static readonly Vector2[] k_SimPose =
        {
            new Vector2(0f, 0f), new Vector2(0f, 0.05f),
            new Vector2(-0.025f, 0.02f), new Vector2(-0.045f, 0.045f), new Vector2(-0.058f, 0.07f), new Vector2(-0.066f, 0.092f),
            new Vector2(-0.02f, 0.04f), new Vector2(-0.025f, 0.095f), new Vector2(-0.027f, 0.135f), new Vector2(-0.028f, 0.158f), new Vector2(-0.028f, 0.178f),
            new Vector2(-0.003f, 0.04f), new Vector2(-0.003f, 0.098f), new Vector2(-0.003f, 0.142f), new Vector2(-0.003f, 0.168f), new Vector2(-0.003f, 0.19f),
            new Vector2(0.013f, 0.038f), new Vector2(0.016f, 0.092f), new Vector2(0.018f, 0.13f), new Vector2(0.019f, 0.155f), new Vector2(0.019f, 0.175f),
            new Vector2(0.027f, 0.034f), new Vector2(0.033f, 0.08f), new Vector2(0.037f, 0.108f), new Vector2(0.039f, 0.127f), new Vector2(0.04f, 0.145f),
        };

        // 마우스 위치에 엄지·검지 끝이 오도록 가짜 손을 만든다. 손 표시를 에디터에서 볼 수 있게 관절을 모두 채운다.
        void FillSimulatedHand(HandState state, Vector3 point, bool pinching, bool leftHand)
        {
            Transform cam = m_Camera.transform;
            Vector3 right = leftHand ? -cam.right : cam.right;
            Vector3 up = cam.up;

            state.isTracked = true;
            state.isOpen = !pinching;
            state.isPinching = pinching;
            state.isMiddlePinching = false;

            // 엄지와 검지 끝의 가운데(자세 좌표로 약 (-0.047, 0.135))가 마우스 위치에 오게 손목을 놓는다.
            Vector3 wrist = point + right * 0.047f - up * 0.135f;
            for (int i = 0; i < HandState.JointCount; i++)
            {
                state.joints[i] = wrist + right * k_SimPose[i].x + up * k_SimPose[i].y;
                state.jointValid[i] = true;
            }

            // 엄지(2~5)와 검지(7~10)는 끝이 마우스 위치 양옆에 오도록 구부린다.
            float gap = pinching ? 0.004f : 0.03f;
            BendChain(state, 2, 5, point - right * gap, cam.forward);
            BendChain(state, 7, 10, point + right * gap, cam.forward);

            state.wrist = state.joints[XRHandJointID.Wrist.ToIndex()];
            state.palm = state.joints[XRHandJointID.Palm.ToIndex()];
            state.thumbTip = state.joints[XRHandJointID.ThumbTip.ToIndex()];
            state.indexTip = state.joints[XRHandJointID.IndexTip.ToIndex()];
            state.middleTip = state.joints[XRHandJointID.MiddleTip.ToIndex()];
        }

        // 사슬의 뿌리는 그대로 두고 끝을 목표로 옮긴다. 가운데 관절은 살짝 앞으로 굽힌다.
        static void BendChain(HandState state, int first, int last, Vector3 tip, Vector3 forward)
        {
            Vector3 root = state.joints[first];
            int steps = last - first;
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                state.joints[first + i] = Vector3.Lerp(root, tip, t) - forward * (0.012f * Mathf.Sin(t * Mathf.PI));
            }
        }
#endif
    }
}
