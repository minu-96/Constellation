// 요청·응답 규칙 (docs/spec.md "AI 요청·응답 규격")
// 수치를 바꿀 때는 Unity 쪽 StoryRules.cs / ConstellationConfig도 함께 맞춘다.

/** 허용 키워드와 프롬프트에 들어갈 문구. 화면 문구와 값은 분리해 둔다. */
export const KEYWORDS = Object.freeze({
  wish: "소원을 비는 별자리",
  adventure: "모험을 떠나는 별자리",
  loved_one: "소중한 사람을 위한 별자리",
});

/**
 * 이야기 말투. 앱이 보내지 않으면 folk(옛이야기)다.
 * teller: 누가 이야기하는지, style: 어떻게 들려주는지, example: 말투만 참고하게 보여 주는 한 토막.
 */
export const TONES = Object.freeze({
  folk: {
    teller: "옛이야기꾼",
    style: "한국 옛이야기처럼 들려줍니다. '~했대요', '~였답니다'처럼 전해 들은 말로 맺습니다",
    example: "옛날, 바닷가 작은 마을에 물동이를 이고 다니던 아이가 있었대요. 아이가 쏟은 물이 하늘로 올라가 반짝반짝 별이 되었답니다.",
  },
  fairy: {
    teller: "동화 작가",
    style: "어린이에게 읽어 주는 동화처럼 다정하게 들려줍니다. '~했어요'로 맺습니다",
    example: "작은 다람쥐가 도토리를 들고 은하수 다리를 건너고 있었어요. 별들이 하나둘 등불을 켜 주자, 다람쥐는 무사히 집에 닿았어요.",
  },
  poetic: {
    teller: "시인",
    style: "시처럼 고요하게, 장면 하나를 그리듯 씁니다. 문장은 짧게, 마침표로 맺습니다",
    example: "바람이 멎은 밤, 학 한 마리가 날개를 폈다. 깃 끝마다 오래된 소원이 조용히 반짝인다.",
  },
});
export const DEFAULT_TONE = "folk";

/** 모양 좌표(xy: [x0, y0, x1, y1, …], 0~1)로 받을 수 있는 점의 개수 */
export const SHAPE_POINTS_MIN = 2;
export const SHAPE_POINTS_MAX = 24;

export const STAR_COUNT_MIN = 3;
export const STAR_COUNT_MAX = 12;

/** 이미지(디코딩 후 JPEG 바이트) 상한 300KB */
export const IMAGE_MAX_BYTES = 300 * 1024;
/** 요청 본문 전체 상한. base64는 원본의 약 4/3배이므로 여유를 둔다. */
export const BODY_MAX_CHARS = Math.ceil((IMAGE_MAX_BYTES * 4) / 3) + 4096;

export const NAME_MIN_CHARS = 2;
export const NAME_MAX_CHARS = 8;
export const STORY_MIN_CHARS = 20;
export const STORY_MAX_CHARS = 200;
export const STORY_MIN_SENTENCES = 2;
export const STORY_MAX_SENTENCES = 3;

/**
 * 어린이 전시에 맞지 않는 표현. 프롬프트에서 이미 금지하지만 한 번 더 거른다.
 * 오탐을 줄이려고 뜻이 분명한 낱말만 둔다(예: "피"는 "피어나다"와 겹쳐서 넣지 않는다).
 */
export const BANNED_WORDS = Object.freeze([
  "살인", "살해", "죽이", "죽였", "죽여", "시체", "송장", "귀신", "유령", "저주",
  "피투성이", "핏빛", "지옥", "악마", "괴물", "칼로", "총으로", "자살", "학살",
]);
