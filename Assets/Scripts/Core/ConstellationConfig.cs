using System;
using UnityEngine;

namespace Constellation
{
    /// <summary>키워드 하나. 서버로 보내는 값(id)과 화면 문구(label)를 분리해 둔다.</summary>
    [Serializable]
    public class KeywordDefinition
    {
        [Tooltip("서버로 보내는 값. wish, adventure, loved_one 중 하나")]
        public string id;
        [Tooltip("아이콘 아래에 보이는 짧은 문구")]
        public string label;
        [Tooltip("선택 직후 안내에 쓰는 긴 문구")]
        public string description;
        public Color color = Color.white;
    }

    /// <summary>되돌리기 제스처 종류(명세: 미정, 테스트 후 결정).</summary>
    public enum UndoGestureType
    {
        /// <summary>손바닥을 펴고 옆으로 빠르게 쓸기</summary>
        PalmSwipe,
        /// <summary>엄지와 중지를 맞대기</summary>
        MiddlePinch
    }

    /// <summary>품질 단계. 기기에서 프레임이 떨어지면 낮춘다.</summary>
    public enum QualityTier
    {
        /// <summary>가장 가볍다. 먼지 입자와 후처리를 끈다.</summary>
        Low,
        /// <summary>기본. 추가 화면 패스 없이 셰이더만으로 그린다.</summary>
        Medium,
        /// <summary>URP 후처리(Bloom 등)를 허용한다. 기기에서 72fps를 확인한 뒤에만 쓴다.</summary>
        High
    }

    /// <summary>체험 자세. 잡는 별의 높이와 거리가 달라진다.</summary>
    public enum PlayPosture
    {
        Standing,
        Seated,
        Custom
    }

    /// <summary>잡는 별을 놓는 구역. 각도는 체험 시작 때의 눈높이와 정면을 기준으로 한다.</summary>
    [Serializable]
    public class StarZonePreset
    {
        [Tooltip("아래쪽 한계(도). 눈높이가 0, 위가 +")]
        public float elevationMin = 10f;
        [Tooltip("위쪽 한계(도). 45도를 넘으면 45도로 자른다")]
        public float elevationMax = 35f;
        [Tooltip("좌우로 퍼지는 각도의 절반(도)")]
        public float azimuthHalfRange = 55f;
        [Tooltip("눈에서 별까지의 거리(m). 손이 닿아야 한다")]
        public float distance = 0.45f;
    }

    /// <summary>
    /// 모든 수치와 문구를 모아 둔 설정 파일(docs/spec.md "설정값").
    /// Assets/Resources/ConstellationConfig.asset 을 인스펙터에서 고치면 된다.
    /// "기기 테스트 후 조정"이라고 적힌 값은 Quest 3 실기기에서 맞춘다.
    /// </summary>
    [CreateAssetMenu(fileName = "ConstellationConfig", menuName = "Constellation/Config")]
    public class ConstellationConfig : ScriptableObject
    {
        public const string ResourcePath = "ConstellationConfig";

        static ConstellationConfig s_Instance;

        /// <summary>Resources에서 설정을 읽는다. 에셋이 없으면 기본값으로 만든다.</summary>
        public static ConstellationConfig Load()
        {
            if (s_Instance != null)
                return s_Instance;
            s_Instance = Resources.Load<ConstellationConfig>(ResourcePath);
            if (s_Instance == null)
            {
                Debug.LogWarning("[Constellation] Resources/ConstellationConfig.asset 이 없어 기본값을 씁니다.");
                s_Instance = CreateInstance<ConstellationConfig>();
            }
            return s_Instance;
        }

        // ------------------------------------------------------------------ 품질

        [Header("품질 (기기 성능 테스트 후 조정)")]
        [Tooltip("Low: 가장 가벼움 / Medium: 기본 / High: URP 후처리 허용")]
        public QualityTier qualityTier = QualityTier.Medium;

        [Header("하늘과 지형 (효과별 켜고 끄기)")]
        [Tooltip("배경 별을 메시로 그린다. 끄면 배경 별이 없다")]
        public bool enableStarDome = true;
        [Tooltip("배경 별 개수. 품질 단계가 Low면 절반만 쓴다")]
        public int backgroundStarCount = 3200;
        [Tooltip("배경 별 밝기")]
        [Range(0f, 3f)] public float backgroundStarBrightness = 1f;
        [Tooltip("은하수를 그린다")]
        public bool enableMilkyWay = true;
        [Tooltip("은하수 밝기")]
        [Range(0f, 3f)] public float milkyWayStrength = 1f;
        [Tooltip("능선을 세 겹으로 그린다. 끄면 가까운 언덕 한 겹만 그린다")]
        public bool enableRidgeLayers = true;
        [Tooltip("지평선 아래에 호수와 반사를 그린다. 끄면 어두운 땅만 그린다")]
        public bool enableLake = true;
        [Tooltip("호수가 끝나고 발밑의 땅이 시작되는 각도(도, 음수). 안내 글자가 물 위에 오게 하려면 더 낮춘다")]
        public float lakeShoreElevation = -25f;
        [Tooltip("시선 가장자리를 어둡게 하는 정도(하늘 셰이더 안에서 처리, 추가 비용 없음)")]
        [Range(0f, 1f)] public float skyVignette = 0.35f;
        [Tooltip("하늘 전체 밝기. 헤드셋에서 어둡거나 밝으면 조정한다")]
        [Range(0.3f, 3f)] public float skyExposure = 1f;
        [Tooltip("이은 선을 따라 빛이 흐르고, 새 선이 이어질 때 빛이 지나간다")]
        public bool enableLineFlow = true;
        [Tooltip("선을 따라 흐르는 빛의 세기")]
        [Range(0f, 2f)] public float lineFlowStrength = 0.6f;
        [Tooltip("완성 순간 별이 차례로 번쩍이고 빛이 선을 훑은 뒤 별자리가 새겨지는 연출")]
        public bool enableCompletionEffect = true;
        [Tooltip("완성 연출에서 별 하나에서 다음 별로 넘어가는 시간(초)")]
        public float completionStepSeconds = 0.18f;
        [Tooltip("가끔 별똥별이 지나간다")]
        public bool enableShootingStars = true;
        [Tooltip("별똥별 사이의 간격(초, 최소~최대)")]
        public Vector2 shootingStarInterval = new Vector2(20f, 45f);

        // ------------------------------------------------------------------ 확정값

        [Header("별 (확정)")]
        [Tooltip("하늘에 뿌리는 후보 별 개수")]
        public int candidateStarCount = 30;
        [Tooltip("완성에 필요한 최소 별 개수")]
        public int minStars = 3;
        [Tooltip("이을 수 있는 최대 별 개수")]
        public int maxStars = 8;

        [Header("키워드 (확정, 문구와 색은 조정 가능)")]
        public KeywordDefinition[] keywords =
        {
            new KeywordDefinition { id = "wish", label = "소원", description = "소원을 비는 별자리", color = new Color(1.00f, 0.85f, 0.45f) },
            new KeywordDefinition { id = "adventure", label = "모험", description = "모험을 떠나는 별자리", color = new Color(0.45f, 0.85f, 1.00f) },
            new KeywordDefinition { id = "loved_one", label = "소중한 사람", description = "소중한 사람을 위한 별자리", color = new Color(1.00f, 0.60f, 0.75f) },
        };

        [Header("AI 요청 (확정)")]
        [Tooltip("AI 응답을 기다리는 최대 시간(초)")]
        public float aiTimeoutSeconds = 10f;
        [Tooltip("응답이 빨리 와도 별빛 연출을 보여 주는 최소 시간(초). 기기 테스트 후 조정")]
        public float aiMinWaitSeconds = 3f;

        [Header("캡처 이미지 (확정, 품질은 테스트)")]
        [Tooltip("캡처 이미지 한 변 크기(px)")]
        public int captureSize = 512;
        [Tooltip("JPEG 품질(1~100). 기기 테스트 후 조정")]
        [Range(30, 100)] public int captureJpegQuality = 80;
        [Tooltip("앱이 보내는 이미지 크기 상한(바이트)")]
        public int captureMaxBytes = 200 * 1000;

        [Header("폴백 이야기 (확정)")]
        [Tooltip("Assets/Data/fallback_stories.json")]
        public TextAsset fallbackStoriesJson;

        // ------------------------------------------------------------------ 손 입력 (미정 → 초깃값)

        [Header("핀치 (기기 테스트 후 조정)")]
        [Tooltip("엄지·검지 끝 거리가 이보다 가까우면 핀치 시작(m)")]
        public float pinchStartDistance = 0.022f;
        [Tooltip("이보다 멀어지면 핀치 끝(m). 시작 값보다 커야 떨림이 없다")]
        public float pinchEndDistance = 0.04f;

        [Header("별 잡기 (기기 테스트 후 조정)")]
        [Tooltip("핀치 위치에서 이 거리 안의 별을 잡는다(m)")]
        public float starGrabRadius = 0.05f;
        [Tooltip("핀치를 유지한 채 지나가는 별도 잇는다")]
        public bool selectWhileDragging = true;
        [Tooltip("끌어서 이을 때의 잡기 반경 비율(실수로 잡히는 것을 줄인다)")]
        [Range(0.2f, 1f)] public float dragGrabRadiusScale = 0.6f;
        [Tooltip("키워드 아이콘 잡기 반경(m)")]
        public float keywordGrabRadius = 0.09f;

        [Header("양손 모으기 (기기 테스트 후 조정)")]
        [Tooltip("두 손바닥 거리가 이보다 가까우면 모은 것으로 본다(m)")]
        public float handsTogetherDistance = 0.11f;
        [Tooltip("이보다 멀어져야 다시 인식한다(m)")]
        public float handsTogetherReleaseDistance = 0.2f;
        [Tooltip("모은 채 유지해야 하는 시간(초)")]
        public float handsTogetherHoldSeconds = 0.7f;

        [Header("되돌리기 제스처 (기기 테스트 후 조정)")]
        public UndoGestureType undoGesture = UndoGestureType.PalmSwipe;
        [Tooltip("손바닥 쓸기로 인정하는 최소 속도(m/s)")]
        public float undoSwipeSpeed = 1.0f;
        [Tooltip("손바닥 쓸기로 인정하는 최소 이동 거리(m)")]
        public float undoSwipeDistance = 0.22f;
        [Tooltip("되돌리기 뒤 다시 인식하기까지의 시간(초)")]
        public float undoCooldownSeconds = 1.0f;

        [Header("손 인식 (기기 테스트 후 조정)")]
        [Tooltip("손이 이 시간만큼 계속 보이면 시작한다(초). 명세: 1초 안에 넘어가야 한다")]
        public float handCheckHoldSeconds = 0.4f;
        [Tooltip("손이 이 시간만큼 안 보이면 손 인식 안내를 띄운다(초)")]
        public float handLostGuideDelay = 1.0f;
        [Tooltip("HandCheck에서 손이 계속 안 보이면 Idle로 돌아가는 시간(초)")]
        public float handCheckTimeoutSeconds = 15f;

        // ------------------------------------------------------------------ 시간 (미정 → 초깃값)

        [Header("상태별 시간 (기기 테스트 후 조정)")]
        [Tooltip("키워드 선택에서 무입력이면 Idle로 돌아가는 시간(초)")]
        public float keywordIdleTimeoutSeconds = 45f;
        [Tooltip("별 잇기에서 무입력이면 Idle로 돌아가는 시간(초)")]
        public float drawingIdleTimeoutSeconds = 90f;
        [Tooltip("Idle 복귀 몇 초 전에 무입력 안내를 띄울지(초)")]
        public float idleWarningLeadSeconds = 12f;
        [Tooltip("키워드 선택 효과를 보여 주는 시간(초)")]
        public float keywordSelectEffectSeconds = 0.9f;
        [Tooltip("결과 화면 유지 시간(초)")]
        public float resultSeconds = 14f;
        [Tooltip("결과 카드(QR) 화면 유지 시간(초)")]
        public float cardSeconds = 20f;
        [Tooltip("카드 업로드를 기다리는 최대 시간(초)")]
        public float cardUploadTimeoutSeconds = 8f;
        [Tooltip("체험이 끝난 뒤 다음 체험을 받기까지 쉬는 시간(초)")]
        public float endCooldownSeconds = 5f;
        [Tooltip("일시 안내(별 부족, 별 초과 등)를 보여 주는 시간(초)")]
        public float alertSeconds = 3f;

        // ------------------------------------------------------------------ 배치 (기기 테스트 후 조정)

        [Header("별 구역 (기기 테스트 후 조정)")]
        [Tooltip("체험 자세. Standing/Seated는 아래 프리셋을, Custom은 customZone을 쓴다")]
        public PlayPosture posture = PlayPosture.Standing;
        [Tooltip("서서 하는 경우")]
        public StarZonePreset standingZone = new StarZonePreset { elevationMin = 10f, elevationMax = 35f, azimuthHalfRange = 55f, distance = 0.45f };
        [Tooltip("앉아서 하는 경우. 덜 뻗고 덜 올려도 닿게 한다")]
        public StarZonePreset seatedZone = new StarZonePreset { elevationMin = 10f, elevationMax = 32f, azimuthHalfRange = 55f, distance = 0.40f };
        [Tooltip("직접 정하는 경우")]
        public StarZonePreset customZone = new StarZonePreset();
        [Tooltip("능선(산 실루엣)의 최고 높이(도). 잡는 별은 이보다 위에만 놓는다")]
        public float ridgeMaxElevation = 8f;
        [Tooltip("능선과 잡는 별 사이에 두는 여유(도)")]
        public float starRidgeMargin = 2f;
        [Tooltip("별을 놓는 줄 수. 한 줄의 칸 수는 후보 별 개수에서 정해진다")]
        [Range(1, 6)] public int starRows = 3;
        [Tooltip("칸 안에서 별을 흔드는 정도(0이면 반듯한 격자, 클수록 불규칙). 크면 별끼리 가까워진다")]
        [Range(0f, 0.8f)] public float starJitter = 0.35f;
        [Tooltip("손이 이 거리 안으로 오면 별이 조금씩 커지기 시작한다(잡기 반경 바깥으로 더한 거리, m)")]
        public float starHintRange = 0.1f;
        [Tooltip("별 크기 범위(m)")]
        public Vector2 starSizeRange = new Vector2(0.028f, 0.05f);
        [Tooltip("이은 선의 굵기(m)")]
        public float lineWidth = 0.008f;

        [Header("키워드 아이콘 배치 (기기 테스트 후 조정)")]
        [Tooltip("머리에서 아이콘까지의 거리(m)")]
        public float keywordDistance = 0.45f;
        [Tooltip("아이콘 사이 간격(m)")]
        public float keywordSpacing = 0.2f;
        [Tooltip("아이콘 크기(m)")]
        public float keywordIconSize = 0.11f;
        [Tooltip("아이콘 높이를 머리보다 얼마나 낮출지(m)")]
        public float keywordDrop = 0.18f;

        [Header("글자 배치 (기기 테스트 후 조정)")]
        [Tooltip("안내 문구까지의 거리(m)")]
        public float guideDistance = 1.6f;
        [Tooltip("대기 화면에서 안내 문구의 높이 각도(도)")]
        public float guideElevationIdle = 2f;
        [Tooltip("체험 중 안내 문구의 높이 각도(도). 음수면 시선보다 아래. 별과 겹치지 않게 한다")]
        public float guideElevation = -27f;
        [Tooltip("안내 문구 글자 크기(m)")]
        public float guideTextSize = 0.075f;
        [Tooltip("안내 문구가 시선을 따라오는 빠르기")]
        public float guideFollowSpeed = 2.5f;
        [Tooltip("결과 글자까지의 거리(m)")]
        public float resultDistance = 2.5f;
        [Tooltip("별자리 이름 글자 크기(m)")]
        public float resultNameSize = 0.26f;
        [Tooltip("이야기 글자 크기(m). 길면 자동으로 줄어든다")]
        public float resultStorySize = 0.095f;
        [Tooltip("이야기 글상자 너비(m)")]
        public float resultStoryWidth = 2.1f;
        [Tooltip("결과 화면에서 별자리를 하늘로 올리는 각도(도)")]
        public float resultSkyPitch = 5f;
        [Tooltip("결과 화면에서 별자리를 멀리 보내는 배율(거리 = 별 구역 거리 × 배율)")]
        public float resultSkyScale = 5f;
        [Tooltip("카드 미리보기까지의 거리(m)")]
        public float cardDistance = 1.3f;

        // ------------------------------------------------------------------ 문구

        [Header("안내 문구 (F-15)")]
        public string msgIdle = "손을 눈앞으로 들어 보세요";
        public string msgTitle = "Constellation";
        public string msgHandCheck = "손을 찾고 있어요";
        public string msgHandLost = "손이 보이지 않아요. 눈앞으로 들어 주세요";
        public string msgKeywordQuestion = "어떤 별자리를 만들까요?";
        public string msgKeywordHint = "엄지와 검지로 하나를 집어 보세요";
        public string msgDrawingHint = "엄지와 검지로 별을 집어 이어 보세요";
        public string msgDrawingHintCanFinish = "두 손을 모으면 별자리가 완성돼요";
        public string msgNeedMoreStars = "별을 더 이어주세요";
        [Tooltip("{0} 자리에 최대 별 개수가 들어간다")]
        public string msgTooManyStars = "별은 {0}개까지 이을 수 있어요. 두 손을 모아 완성해 보세요";
        public string msgUndoHintSwipe = "손바닥을 옆으로 쓸면 마지막 별을 지워요";
        public string msgUndoHintMiddlePinch = "엄지와 중지를 맞대면 마지막 별을 지워요";
        public string msgIdleWarning = "아직 계신가요? 손을 움직여 주세요";
        public string msgAiWaiting = "별빛이 이야기를 모으고 있어요";
        public string msgCardWaiting = "카드를 준비하고 있어요";
        public string msgCardReady = "QR을 찍으면 카드를 받을 수 있어요";
        public string msgGoodbye = "함께해 주셔서 고마워요";

        // ------------------------------------------------------------------ 비주얼

        [Header("색")]
        public Color skyZenithColor = new Color(0.012f, 0.02f, 0.075f);
        public Color skyHorizonColor = new Color(0.11f, 0.09f, 0.27f);
        [Tooltip("지평선의 대기광과 먼 능선에 섞이는 안개 색")]
        public Color skyHazeColor = new Color(0.34f, 0.27f, 0.50f);
        [Tooltip("호수에 비친 하늘에 곱하는 색")]
        public Color waterTint = new Color(0.42f, 0.48f, 0.70f);
        [Tooltip("발밑 땅의 색")]
        public Color groundColor = new Color(0.004f, 0.005f, 0.014f);
        public Color hillColor = new Color(0.006f, 0.008f, 0.025f);
        public Color starColor = new Color(0.78f, 0.86f, 1.0f);
        public Color starHoverColor = new Color(1.0f, 1.0f, 1.0f);
        public Color starSelectedColor = new Color(1.0f, 0.86f, 0.5f);
        public Color lineColor = new Color(1.0f, 0.9f, 0.62f);
        public Color textColor = new Color(0.96f, 0.95f, 1.0f);
        public Color textAccentColor = new Color(1.0f, 0.88f, 0.58f);
        [Tooltip("손 색. 별(푸른 흰색)·선(금색)과 헷갈리지 않게 민트 계열로 둔다")]
        public Color handColor = new Color(0.35f, 1.0f, 0.78f);
        [Tooltip("엄지 끝 강조색")]
        public Color thumbTipColor = new Color(1.0f, 0.45f, 0.75f);
        [Tooltip("검지 끝 강조색")]
        public Color indexTipColor = new Color(0.55f, 1.0f, 0.45f);

        [Header("손 표시 (기기 테스트 후 조정)")]
        [Tooltip("손가락 선의 굵기(m)")]
        public float handFingerWidth = 0.013f;
        [Tooltip("손바닥 면의 불투명도(0이면 테두리만)")]
        [Range(0f, 0.6f)] public float handPalmOpacity = 0.14f;
        [Tooltip("엄지·검지 끝이 핀치 시작 거리에서 이만큼 떨어진 곳부터 빛이 모이기 시작한다(m)")]
        public float pinchHintRange = 0.06f;

        [Header("후처리 (기기 성능 테스트 후 조정)")]
        [Tooltip("URP 후처리(블룸·비네트·색 보정)를 켠다. 품질 단계가 High일 때만 적용된다. Quest에서는 화면 전체를 여러 번 다시 그려 비용이 크므로 기본은 끔")]
        public bool usePostProcessing = false;
        public float bloomIntensity = 0.9f;
        public float bloomThreshold = 0.8f;
        public float vignetteIntensity = 0.28f;
        [Tooltip("하늘에 떠다니는 먼지 별빛 입자 수. 0이면 끈다. 품질 단계가 Low면 꺼진다")]
        public int ambientParticleCount = 120;

        // ------------------------------------------------------------------ 사운드

        [Header("사운드 (F-14)")]
        [Range(0f, 1f)] public float masterVolume = 0.8f;
        [Range(0f, 1f)] public float ambientVolume = 0.35f;
        [Tooltip("결과 화면에서 줄어든 배경음 크기")]
        [Range(0f, 1f)] public float ambientDuckedVolume = 0.12f;
        [Range(0f, 1f)] public float effectVolume = 0.7f;
        [Tooltip("핀치할 때 나는 작은 틱 소리의 크기. 0이면 끈다")]
        [Range(0f, 1f)] public float pinchVolume = 0.3f;
        [Tooltip("비워 두면 코드로 만든 소리를 쓴다. 파일로 바꾸려면 여기에 넣는다")]
        public AudioClip ambientClip;
        public AudioClip starGrabClip;
        public AudioClip lineConnectClip;
        public AudioClip undoClip;
        public AudioClip pinchClip;
        public AudioClip keywordSelectClip;
        public AudioClip completeClip;
        public AudioClip resultClip;

        // ------------------------------------------------------------------ 개발용

        [Header("개발용")]
        [Tooltip("에디터에서 헤드셋 없이 마우스·키보드로 손을 흉내 낸다")]
        public bool editorSimulation = true;
        [Tooltip("상태 전환을 콘솔에 남긴다")]
        public bool logStateChanges = true;

        /// <summary>잡는 별의 고도 상한. 고개를 크게 젖히지 않게 이보다 위에는 두지 않는다.</summary>
        public const float StarElevationHardMax = 45f;

        readonly StarZonePreset m_ActiveZone = new StarZonePreset();

        /// <summary>
        /// 지금 자세에 맞는 별 구역. 상한은 45도로 자르고, 하한은 능선 위로 올린다.
        /// </summary>
        public StarZonePreset ActiveStarZone
        {
            get
            {
                StarZonePreset source = posture == PlayPosture.Seated ? seatedZone : posture == PlayPosture.Custom ? customZone : standingZone;
                if (source == null)
                    source = m_ActiveZone;
                float max = Mathf.Min(source.elevationMax, StarElevationHardMax);
                float min = Mathf.Max(source.elevationMin, ridgeMaxElevation + starRidgeMargin);
                // 구역이 너무 얇아지지 않게 최소 8도의 높이를 남긴다.
                if (min > max - 8f)
                    min = max - 8f;
                m_ActiveZone.elevationMin = min;
                m_ActiveZone.elevationMax = max;
                m_ActiveZone.azimuthHalfRange = Mathf.Clamp(source.azimuthHalfRange, 10f, 90f);
                m_ActiveZone.distance = Mathf.Clamp(source.distance, 0.2f, 1f);
                return m_ActiveZone;
            }
        }

        public KeywordDefinition FindKeyword(string id)
        {
            if (keywords == null)
                return null;
            for (int i = 0; i < keywords.Length; i++)
            {
                if (keywords[i] != null && keywords[i].id == id)
                    return keywords[i];
            }
            return null;
        }
    }
}
