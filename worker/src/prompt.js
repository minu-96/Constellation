import { DEFAULT_TONE, KEYWORDS, TONES } from "./rules.js";

/** 기획서(docs/planning.md)의 프롬프트 초안. {keyword} 자리에 키워드 문구가 들어간다. */
export function buildPrompt(keyword, starCount, tone = DEFAULT_TONE) {
  const { teller, style } = TONES[tone] ?? TONES[DEFAULT_TONE];
  return [
    `당신은 별자리에 이름을 붙이는 ${teller}입니다. 첨부한 그림은 체험자가 밤하늘에서 별 ${starCount}개를 이어 만든 별자리입니다. 키워드는 '${KEYWORDS[keyword]}'입니다.`,
    "1) 그림이 무엇을 닮았는지 한 가지로 정해 별자리 이름을 짓습니다(한국어 2~8자).",
    `2) 키워드를 녹여 ${style}(한국어 2~3문장, 총 120자 안팎).`,
    "어린이도 보는 전시이므로 무섭거나 폭력적인 표현은 쓰지 않습니다.",
    '아래 JSON 형식으로만 답합니다: {"name":"","story":""}',
  ].join("\n");
}

/** 구조화 출력 스키마. 모델이 JSON 한 개만 내도록 강제한다. */
export const STORY_SCHEMA = Object.freeze({
  type: "object",
  properties: {
    name: { type: "string", description: "별자리 이름. 한국어 2~8자" },
    story: { type: "string", description: "짧은 이야기. 한국어 2~3문장, 120자 안팎" },
  },
  required: ["name", "story"],
  additionalProperties: false,
});
