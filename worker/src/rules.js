// 요청·응답 규칙 (docs/spec.md "AI 요청·응답 규격")
// 수치를 바꿀 때는 Unity 쪽 StoryRules.cs / ConstellationConfig도 함께 맞춘다.

/** 허용 키워드와 프롬프트에 들어갈 문구. 화면 문구와 값은 분리해 둔다. */
export const KEYWORDS = Object.freeze({
  wish: "소원을 비는 별자리",
  adventure: "모험을 떠나는 별자리",
  loved_one: "소중한 사람을 위한 별자리",
});

export const STAR_COUNT_MIN = 3;
export const STAR_COUNT_MAX = 8;

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
