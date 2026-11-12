import {
  KEYWORDS, STAR_COUNT_MIN, STAR_COUNT_MAX, IMAGE_MAX_BYTES,
  NAME_MIN_CHARS, NAME_MAX_CHARS, STORY_MIN_CHARS, STORY_MAX_CHARS,
  STORY_MIN_SENTENCES, STORY_MAX_SENTENCES, BANNED_WORDS,
} from "./rules.js";

const BASE64_RE = /^[A-Za-z0-9+/]+={0,2}$/;
// JPEG는 FF D8 FF로 시작하고, 이를 base64로 바꾸면 항상 "/9j/"로 시작한다.
const JPEG_BASE64_PREFIX = "/9j/";

/** base64 문자열 길이로 디코딩 후 바이트 수를 계산한다(실제 디코딩 없이, CPU 절약). */
export function base64DecodedLength(b64) {
  let padding = 0;
  if (b64.endsWith("==")) padding = 2;
  else if (b64.endsWith("=")) padding = 1;
  return Math.floor((b64.length * 3) / 4) - padding;
}

/**
 * base64 JPEG 검증. 이야기 요청과 카드 업로드가 함께 쓴다.
 * @returns {{ok: true} | {ok: false, status: number, error: string}}
 */
export function validateJpegBase64(image) {
  if (typeof image !== "string" || image.length === 0) {
    return fail(400, "invalid_image");
  }
  // 크기를 먼저 본다. 큰 문자열에 정규식을 돌리지 않기 위해서다.
  if (base64DecodedLength(image) > IMAGE_MAX_BYTES) {
    return fail(413, "image_too_large");
  }
  if (!image.startsWith(JPEG_BASE64_PREFIX) || image.length % 4 !== 0 || !BASE64_RE.test(image)) {
    return fail(400, "invalid_image");
  }
  return { ok: true };
}

/**
 * 앱 → Worker 이야기 요청 검증.
 * @returns {{ok: true, value: {keyword: string, starCount: number, image: string}} | {ok: false, status: number, error: string}}
 */
export function validateStoryRequest(body) {
  if (body === null || typeof body !== "object" || Array.isArray(body)) {
    return fail(400, "invalid_body");
  }
  const { keyword, starCount, image } = body;
  if (typeof keyword !== "string" || !Object.hasOwn(KEYWORDS, keyword)) {
    return fail(400, "invalid_keyword");
  }
  if (!Number.isInteger(starCount) || starCount < STAR_COUNT_MIN || starCount > STAR_COUNT_MAX) {
    return fail(400, "invalid_star_count");
  }
  const img = validateJpegBase64(image);
  if (!img.ok) return img;
  return { ok: true, value: { keyword, starCount, image } };
}

/** 글자 수는 코드 포인트 기준으로 센다. */
function charCount(s) {
  return [...s].length;
}

/** 문장 수. 마침표·물음표·느낌표·말줄임표로 끝나는 덩어리를 센다. */
export function countSentences(story) {
  return story
    .split(/[.!?。…]+["'”’)\]]*\s*/u)
    .filter((part) => part.trim().length > 0).length;
}

/**
 * 이름·이야기 규칙 검사(모델 응답과 카드 업로드에 함께 쓴다).
 * @returns {{ok: true, value: {name: string, story: string}} | {ok: false, error: string}}
 */
export function validateStory(input) {
  if (input === null || typeof input !== "object") return { ok: false, error: "not_object" };
  if (typeof input.name !== "string" || typeof input.story !== "string") {
    return { ok: false, error: "missing_fields" };
  }
  const name = input.name.trim();
  const story = input.story.replace(/\s+/gu, " ").trim();

  const nameLen = charCount(name);
  if (nameLen < NAME_MIN_CHARS || nameLen > NAME_MAX_CHARS) return { ok: false, error: "name_length" };
  // 한글 음절과 낱말 사이 공백만 허용한다.
  if (!/^[가-힣]+( [가-힣]+)*$/u.test(name)) return { ok: false, error: "name_not_korean" };

  const storyLen = charCount(story);
  if (storyLen < STORY_MIN_CHARS || storyLen > STORY_MAX_CHARS) return { ok: false, error: "story_length" };
  if (!/[가-힣]/u.test(story)) return { ok: false, error: "story_not_korean" };
  // 제어 문자나 마크업이 섞인 답은 버린다.
  if (/[\u0000-\u001f<>{}]/u.test(story)) return { ok: false, error: "story_bad_chars" };

  const sentences = countSentences(story);
  if (sentences < STORY_MIN_SENTENCES || sentences > STORY_MAX_SENTENCES) {
    return { ok: false, error: "story_sentences" };
  }
  const text = `${name} ${story}`;
  if (BANNED_WORDS.some((w) => text.includes(w))) return { ok: false, error: "banned_word" };

  return { ok: true, value: { name, story } };
}

function fail(status, error) {
  return { ok: false, status, error };
}
