using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 관절 26개짜리 손 자세를 코드로 만든다. 실제 손 추적 없이 손 모양이 필요한 곳에서 쓴다.
    ///  - 에디터 시뮬레이션의 가짜 손(HandInput)
    ///  - 처음 쓰는 사람에게 보여 주는 시범 손(GestureGuide)
    /// </summary>
    public static class HandPose
    {
        // 손목을 원점으로 한 편 손 모양(m). x: 오른손 기준 새끼손가락 쪽이 +, y: 손끝 방향.
        // XRHandJointID 순서(Wrist=0 … LittleTip=25).
        static readonly Vector2[] k_Open =
        {
            new Vector2(0f, 0f), new Vector2(0f, 0.05f),
            new Vector2(-0.025f, 0.02f), new Vector2(-0.045f, 0.045f), new Vector2(-0.058f, 0.07f), new Vector2(-0.066f, 0.092f),
            new Vector2(-0.02f, 0.04f), new Vector2(-0.025f, 0.095f), new Vector2(-0.027f, 0.135f), new Vector2(-0.028f, 0.158f), new Vector2(-0.028f, 0.178f),
            new Vector2(-0.003f, 0.04f), new Vector2(-0.003f, 0.098f), new Vector2(-0.003f, 0.142f), new Vector2(-0.003f, 0.168f), new Vector2(-0.003f, 0.19f),
            new Vector2(0.013f, 0.038f), new Vector2(0.016f, 0.092f), new Vector2(0.018f, 0.13f), new Vector2(0.019f, 0.155f), new Vector2(0.019f, 0.175f),
            new Vector2(0.027f, 0.034f), new Vector2(0.033f, 0.08f), new Vector2(0.037f, 0.108f), new Vector2(0.039f, 0.127f), new Vector2(0.04f, 0.145f),
        };

        const int k_Wrist = 0;
        const int k_Palm = 1;
        const int k_ThumbRoot = 2;
        const int k_ThumbTip = 5;
        const int k_IndexRoot = 7;
        const int k_IndexTip = 10;
        const int k_MiddleTip = 15;

        // 엄지와 검지 끝이 벌어진 정도(m)
        const float k_OpenGap = 0.03f;
        const float k_PinchedGap = 0.004f;

        /// <summary>
        /// 엄지·검지 끝의 가운데가 point에 오도록 손을 놓고 관절을 모두 채운다.
        /// </summary>
        /// <param name="right">보는 사람 기준 오른쪽</param>
        /// <param name="up">손끝이 향하는 쪽</param>
        /// <param name="forward">보는 사람이 바라보는 쪽(손등이 향하는 쪽)</param>
        /// <param name="pinch">0이면 편 손, 1이면 엄지와 검지를 맞댄 손</param>
        public static void Fill(HandState state, Vector3 point, Vector3 right, Vector3 up, Vector3 forward, float pinch, bool leftHand)
        {
            if (leftHand)
                right = -right;
            pinch = Mathf.Clamp01(pinch);

            // 엄지와 검지 끝의 가운데는 자세 좌표로 약 (-0.047, 0.135)이다.
            Vector3 wrist = point + right * 0.047f - up * 0.135f;
            for (int i = 0; i < HandState.JointCount; i++)
            {
                state.joints[i] = wrist + right * k_Open[i].x + up * k_Open[i].y;
                state.jointValid[i] = true;
            }

            // 엄지와 검지는 끝이 point 양옆에 오도록 구부린다.
            float gap = Mathf.Lerp(k_OpenGap, k_PinchedGap, pinch);
            Bend(state, k_ThumbRoot, k_ThumbTip, point - right * gap, forward);
            Bend(state, k_IndexRoot, k_IndexTip, point + right * gap, forward);

            state.isTracked = true;
            state.isPinching = pinch > 0.9f;
            state.isMiddlePinching = false;
            state.isOpen = pinch < 0.5f;
            state.wrist = state.joints[k_Wrist];
            state.palm = state.joints[k_Palm];
            state.thumbTip = state.joints[k_ThumbTip];
            state.indexTip = state.joints[k_IndexTip];
            state.middleTip = state.joints[k_MiddleTip];
            state.pinchPoint = (state.thumbTip + state.indexTip) * 0.5f;
        }

        // 사슬의 뿌리는 그대로 두고 끝을 목표로 옮긴다. 가운데 관절은 살짝 앞으로 굽힌다.
        static void Bend(HandState state, int first, int last, Vector3 tip, Vector3 forward)
        {
            Vector3 root = state.joints[first];
            int steps = last - first;
            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                state.joints[first + i] = Vector3.Lerp(root, tip, t) - forward * (0.012f * Mathf.Sin(t * Mathf.PI));
            }
        }
    }
}
