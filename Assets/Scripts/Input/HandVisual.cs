using UnityEngine;

namespace Constellation
{
    /// <summary>
    /// 손을 윤곽이 읽히는 모양으로 그린다. 손 모델 에셋 없이 관절 위치만으로 만든다.
    ///  - 손가락: 관절을 잇는 굵은 빛 선(끝으로 갈수록 가늘다)
    ///  - 손바닥: 반투명 면과 테두리
    ///  - 엄지 끝과 검지 끝: 서로 다른 색의 큰 점. 두 끝이 가까워질수록 사이의 빛이 밝아지고,
    ///    핀치하는 순간 고리가 번쩍인다.
    /// 색은 별(푸른 흰색, 금색)과 헷갈리지 않게 민트 계열을 쓴다.
    /// </summary>
    public class HandVisual : MonoBehaviour
    {
        // XRHandJointID - 1 기준 인덱스
        const int k_Wrist = 0;
        const int k_Palm = 1;
        const int k_ThumbTip = 5;
        const int k_IndexTip = 10;

        // 손가락별 관절 사슬(뿌리 → 끝)
        static readonly int[][] k_Fingers =
        {
            new[] { 2, 3, 4, 5 },      // 엄지: 손허리뼈 → 끝
            new[] { 7, 8, 9, 10 },     // 검지: 첫마디 → 끝
            new[] { 12, 13, 14, 15 },  // 중지
            new[] { 17, 18, 19, 20 },  // 약지
            new[] { 22, 23, 24, 25 },  // 새끼
        };

        // 손바닥 둘레: 손목 → 엄지 뿌리 → 검지·중지·약지·새끼 첫마디 → 새끼 손허리뼈
        static readonly int[] k_PalmOutline = { 0, 2, 7, 12, 17, 22, 21 };

        class Hand
        {
            public LineRenderer[] fingers;
            public LineRenderer outline;
            public LineRenderer beam;
            public MeshRenderer palm;
            public Mesh palmMesh;
            public Vector3[] palmVertices;
            public GlowSprite thumbTip;
            public GlowSprite indexTip;
            public GlowSprite pinchGlow;
            public GlowSprite flash;
            public bool wasPinching;
            public float flashTime = 10f;
            public Vector3 flashPosition;
        }

        ConstellationConfig m_Config;
        HandInput m_Hands;
        readonly Hand[] m_Visuals = new Hand[2];

        const float k_FlashSeconds = 0.3f;

        public void Init(ConstellationConfig config, HandInput hands)
        {
            m_Config = config;
            m_Hands = hands;
            for (int h = 0; h < 2; h++)
                m_Visuals[h] = BuildHand(h == 0 ? "Left Hand" : "Right Hand");
        }

        Hand BuildHand(string handName)
        {
            Transform root = new GameObject(handName).transform;
            root.SetParent(transform, false);

            Hand hand = new Hand();
            float width = m_Config.handFingerWidth;
            Color color = m_Config.handColor;

            hand.fingers = new LineRenderer[k_Fingers.Length];
            for (int f = 0; f < k_Fingers.Length; f++)
            {
                LineRenderer line = VisualFactory.CreateLine("Finger " + f, root, width, color, true);
                // 뿌리는 굵고 끝은 가늘게
                line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.6f));
                line.sharedMaterial.SetFloat(VisualFactory.IntensityId, 0.75f);
                line.positionCount = k_Fingers[f].Length;
                line.enabled = false;
                hand.fingers[f] = line;
            }

            hand.outline = VisualFactory.CreateLine("Palm Outline", root, width * 0.45f, color, true);
            hand.outline.loop = true;
            hand.outline.sharedMaterial.SetFloat(VisualFactory.IntensityId, 0.6f);
            hand.outline.positionCount = k_PalmOutline.Length;
            hand.outline.enabled = false;

            // 손바닥 면: 손바닥 중심에서 둘레로 펼친 부채꼴
            hand.palmMesh = new Mesh();
            hand.palmMesh.name = handName + " Palm";
            hand.palmMesh.MarkDynamic();
            int rim = k_PalmOutline.Length;
            hand.palmVertices = new Vector3[rim + 1];
            Vector2[] uv = new Vector2[rim + 1];
            int[] triangles = new int[rim * 3];
            for (int i = 0; i < rim; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % rim + 1;
            }
            for (int i = 0; i < uv.Length; i++)
                uv[i] = new Vector2(0.5f, 0.5f);
            hand.palmMesh.vertices = hand.palmVertices;
            hand.palmMesh.uv = uv;
            hand.palmMesh.triangles = triangles;

            Color fill = color;
            fill.a = m_Config.handPalmOpacity;
            hand.palm = VisualFactory.CreatePanel("Palm", root, Vector2.one, fill, null);
            hand.palm.GetComponent<MeshFilter>().sharedMesh = hand.palmMesh;
            hand.palm.transform.localScale = Vector3.one;
            hand.palm.enabled = false;

            // 엄지 끝과 검지 끝 사이를 잇는 빛. 가까워질수록 밝아진다.
            hand.beam = VisualFactory.CreateLine("Pinch Beam", root, 0.004f, Color.white, true);
            hand.beam.positionCount = 2;
            hand.beam.enabled = false;

            hand.thumbTip = GlowSprite.Create("Thumb Tip", root, 0.03f, m_Config.thumbTipColor, 1.2f);
            hand.indexTip = GlowSprite.Create("Index Tip", root, 0.03f, m_Config.indexTipColor, 1.2f);
            hand.pinchGlow = GlowSprite.Create("Pinch Glow", root, 0.04f, Color.white, 0f);
            hand.flash = GlowSprite.Create("Pinch Flash", root, 0.05f, Color.white, 0f);
            hand.flash.SetRing(1f);
            hand.flash.SetCore(0.02f);
            hand.thumbTip.SetVisible(false);
            hand.indexTip.SetVisible(false);
            hand.pinchGlow.SetVisible(false);
            hand.flash.SetVisible(false);
            return hand;
        }

        void LateUpdate()
        {
            if (m_Hands == null)
                return;
            UpdateHand(m_Visuals[0], m_Hands.Left);
            UpdateHand(m_Visuals[1], m_Hands.Right);
        }

        void UpdateHand(Hand hand, HandState state)
        {
            bool tracked = state.isTracked;

            for (int f = 0; f < k_Fingers.Length; f++)
            {
                int[] chain = k_Fingers[f];
                LineRenderer line = hand.fingers[f];
                bool valid = tracked;
                for (int i = 0; i < chain.Length && valid; i++)
                    valid = state.jointValid[chain[i]];
                line.enabled = valid;
                if (!valid)
                    continue;
                for (int i = 0; i < chain.Length; i++)
                    line.SetPosition(i, state.joints[chain[i]]);
            }

            UpdatePalm(hand, state, tracked);
            UpdatePinch(hand, state, tracked);
            UpdateFlash(hand);
        }

        void UpdatePalm(Hand hand, HandState state, bool tracked)
        {
            bool valid = tracked && state.jointValid[k_Palm];
            for (int i = 0; i < k_PalmOutline.Length && valid; i++)
                valid = state.jointValid[k_PalmOutline[i]];

            hand.outline.enabled = valid;
            hand.palm.enabled = valid && m_Config.handPalmOpacity > 0.001f;
            if (!valid)
                return;

            hand.palmVertices[0] = state.joints[k_Palm];
            for (int i = 0; i < k_PalmOutline.Length; i++)
            {
                Vector3 position = state.joints[k_PalmOutline[i]];
                hand.outline.SetPosition(i, position);
                hand.palmVertices[i + 1] = position;
            }
            // 면의 꼭짓점은 월드 좌표다(오브젝트는 원점에 두고 메시만 움직인다).
            hand.palm.transform.position = Vector3.zero;
            hand.palm.transform.rotation = Quaternion.identity;
            hand.palmMesh.vertices = hand.palmVertices;
            hand.palmMesh.RecalculateBounds();
        }

        void UpdatePinch(Hand hand, HandState state, bool tracked)
        {
            bool valid = tracked && state.jointValid[k_ThumbTip] && state.jointValid[k_IndexTip];
            hand.thumbTip.SetVisible(valid);
            hand.indexTip.SetVisible(valid);
            hand.pinchGlow.SetVisible(valid);
            hand.beam.enabled = valid;
            if (!valid)
            {
                hand.wasPinching = false;
                return;
            }

            Vector3 thumb = state.joints[k_ThumbTip];
            Vector3 index = state.joints[k_IndexTip];
            float gap = Vector3.Distance(thumb, index);
            // 0: 멀다, 1: 핀치 직전. 핀치 시작 거리에서 hint 거리만큼 떨어진 곳부터 차오른다.
            float near = Mathf.Clamp01(1f - (gap - m_Config.pinchStartDistance) / Mathf.Max(0.001f, m_Config.pinchHintRange));
            bool pinching = state.isPinching;
            if (pinching)
                near = 1f;

            float tipSize = Mathf.Lerp(0.026f, 0.036f, near);
            hand.thumbTip.transform.position = thumb;
            hand.indexTip.transform.position = index;
            hand.thumbTip.SetSize(tipSize);
            hand.indexTip.SetSize(tipSize);
            hand.thumbTip.SetIntensity(Mathf.Lerp(1.1f, 2f, near));
            hand.indexTip.SetIntensity(Mathf.Lerp(1.1f, 2f, near));

            // 두 끝 사이로 빛이 모인다.
            Color beamColor = Color.Lerp(m_Config.handColor, Color.white, near);
            hand.beam.SetPosition(0, thumb);
            hand.beam.SetPosition(1, index);
            hand.beam.widthMultiplier = Mathf.Lerp(0.002f, 0.007f, near);
            hand.beam.sharedMaterial.SetColor(VisualFactory.ColorId, beamColor);
            hand.beam.sharedMaterial.SetFloat(VisualFactory.IntensityId, near * near * 2.2f);

            hand.pinchGlow.transform.position = state.pinchPoint;
            hand.pinchGlow.SetSize(pinching ? 0.055f : Mathf.Lerp(0.02f, 0.04f, near));
            hand.pinchGlow.SetIntensity(pinching ? 2.4f : near * near * 1.4f);
            hand.pinchGlow.SetColor(pinching ? Color.white : beamColor);

            if (pinching && !hand.wasPinching)
            {
                hand.flashTime = 0f;
                hand.flashPosition = state.pinchPoint;
            }
            hand.wasPinching = pinching;
        }

        // 핀치하는 순간 고리가 퍼지며 사라진다.
        void UpdateFlash(Hand hand)
        {
            if (hand.flashTime >= k_FlashSeconds)
            {
                hand.flash.SetVisible(false);
                return;
            }
            hand.flashTime += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(hand.flashTime / k_FlashSeconds);
            hand.flash.SetVisible(true);
            hand.flash.transform.position = hand.flashPosition;
            hand.flash.SetSize(Mathf.Lerp(0.03f, 0.11f, t));
            hand.flash.SetIntensity((1f - t) * (1f - t) * 3f);
        }

        void OnDestroy()
        {
            for (int h = 0; h < m_Visuals.Length; h++)
            {
                if (m_Visuals[h] != null)
                    VisualFactory.DestroyObject(m_Visuals[h].palmMesh);
            }
        }
    }
}
