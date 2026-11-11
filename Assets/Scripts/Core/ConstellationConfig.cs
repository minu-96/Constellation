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
        public float starGrabRadius = 0.06f;
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

        [Header("별 배치 (기기 테스트 후 조정)")]
        [Tooltip("머리(어깨)에서 별까지의 거리(m). 손이 닿아야 한다")]
        public float starDistance = 0.5f;
        [Tooltip("별 배치 중심을 머리보다 얼마나 낮출지(m). 어깨 높이에 맞춘다")]
        public float starCenterDrop = 0.12f;
        [Tooltip("좌우로 퍼지는 각도의 절반(도)")]
        public float starAzimuthHalfRange = 50f;
        [Tooltip("아래쪽 한계 각도(도)")]
        public float starElevationMin = -20f;
        [Tooltip("위쪽 한계 각도(도)")]
        public float starElevationMax = 32f;
        [Tooltip("별끼리 최소 각거리(도). 겹치지 않게 한다")]
        public float starMinSeparation = 9f;
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
        public float resultSkyPitch = 30f;
        [Tooltip("결과 화면에서 별자리를 멀리 보내는 배율(거리 = starDistance × 배율)")]
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
        public Color skyHorizonColor = new Color(0.16f, 0.11f, 0.36f);
        public Color hillColor = new Color(0.006f, 0.008f, 0.025f);
        public Color starColor = new Color(0.78f, 0.86f, 1.0f);
        public Color starHoverColor = new Color(1.0f, 1.0f, 1.0f);
        public Color starSelectedColor = new Color(1.0f, 0.86f, 0.5f);
        public Color lineColor = new Color(1.0f, 0.9f, 0.62f);
        public Color textColor = new Color(0.96f, 0.95f, 1.0f);
        public Color textAccentColor = new Color(1.0f, 0.88f, 0.58f);
        public Color handColor = new Color(0.6f, 0.78f, 1.0f);

        [Header("후처리 (기기 성능 테스트 후 조정)")]
        [Tooltip("블룸·비네트·색 보정을 켠다. Quest에서 프레임이 떨어지면 끈다")]
        public bool usePostProcessing = true;
        public float bloomIntensity = 0.9f;
        public float bloomThreshold = 0.8f;
        public float vignetteIntensity = 0.28f;
        [Tooltip("하늘에 떠다니는 먼지 별빛 입자 수. 0이면 끈다")]
        public int ambientParticleCount = 120;

        // ------------------------------------------------------------------ 사운드

        [Header("사운드 (F-14)")]
        [Range(0f, 1f)] public float masterVolume = 0.8f;
        [Range(0f, 1f)] public float ambientVolume = 0.35f;
        [Tooltip("결과 화면에서 줄어든 배경음 크기")]
        [Range(0f, 1f)] public float ambientDuckedVolume = 0.12f;
        [Range(0f, 1f)] public float effectVolume = 0.7f;
        [Tooltip("비워 두면 코드로 만든 소리를 쓴다. 파일로 바꾸려면 여기에 넣는다")]
        public AudioClip ambientClip;
        public AudioClip starGrabClip;
        public AudioClip lineConnectClip;
        public AudioClip undoClip;
        public AudioClip keywordSelectClip;
        public AudioClip completeClip;
        public AudioClip resultClip;

        // ------------------------------------------------------------------ 개발용

        [Header("개발용")]
        [Tooltip("에디터에서 헤드셋 없이 마우스·키보드로 손을 흉내 낸다")]
        public bool editorSimulation = true;
        [Tooltip("상태 전환을 콘솔에 남긴다")]
        public bool logStateChanges = true;

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
