import { DEFAULT_TONE, KEYWORDS, TONES } from "./rules.js";
import { describeShape } from "./shape.js";

// 이름 짓는 법을 보여 주는 예. 요청마다 두 개를 골라 넣어, 예에 쓴 낱말로 이름이 쏠리지 않게 한다.
const NAME_EXAMPLES = Object.freeze([
  ["물동이", "물동이별"], ["날개를 편 학", "학날개자리"], ["누운 고양이", "낮잠고양이별"], ["징검다리", "징검다리별"],
  ["연을 날리는 아이", "연날리기자리"], ["버선", "꽃버선별"], ["돛단배", "돛단배자리"], ["다람쥐 꼬리", "다람쥐꼬리별"],
  ["부채", "바람부채별"], ["사다리", "하늘사다리"], ["주전자", "찻주전자별"], ["고래", "고래등별"],
  ["활", "은빛활자리"], ["나비", "흰나비별"], ["우산", "비우산자리"], ["산봉우리", "세봉우리별"],
]);

function pickExamples(random) {
  const first = Math.floor(random() * NAME_EXAMPLES.length);
  let second = Math.floor(random() * (NAME_EXAMPLES.length - 1));
  if (second >= first) second++;
  return [NAME_EXAMPLES[first], NAME_EXAMPLES[second]].map(([like, name]) => `${like} → ${name}`).join(", ");
}

/**
 * 별자리 이름과 이야기를 부탁하는 프롬프트. 기획서(docs/planning.md)의 초안을 바탕으로,
 *  - 모양을 먼저 정하게 하고(looks_like), 그 모양을 담아 이름을 짓게 한다.
 *  - 소리 내어 읽어 줄 글이라 설명하는 말투 대신 입말로 들려주게 한다.
 * @param {{points?: number[][] | null, recentNames?: string[], random?: () => number}} [options]
 */
export function buildPrompt(keyword, starCount, tone = DEFAULT_TONE, options = {}) {
  const { teller, style, example } = TONES[tone] ?? TONES[DEFAULT_TONE];
  const random = options.random ?? Math.random;
  const lines = [
    `당신은 밤하늘의 별자리에 이름을 붙여 주는 ${teller}입니다.`,
    `첨부한 그림은 체험자가 방금 손으로 별 ${starCount}개를 이어 만든 별자리입니다. 체험자는 '${KEYWORDS[keyword]}'를 떠올리며 그렸습니다.`,
  ];

  if (options.points && options.points.length >= 2) {
    lines.push("", "[모양 정보]", ...describeShape(options.points));
  }

  lines.push(
    "",
    "[할 일]",
    "1) looks_like: 그림과 모양 정보를 보고 이 별자리가 무엇을 닮았는지 구체적인 것 하나를 고릅니다. 동물, 물건, 자연, 사람의 몸짓 가운데 선의 개수, 꺾임, 닫힘이 실제로 맞는 것으로 고릅니다. '별', '빛', '하늘', '소원'처럼 막연한 말은 안 됩니다.",
    `2) name: looks_like를 담아 한국어 2~8자 이름을 짓습니다(예: ${pickExamples(random)}). 키워드 낱말(소원, 모험, 사랑, 마음)이나 희망·꿈·빛·반짝 같은 막연한 말만으로 이름을 짓지 않습니다.`,
  );

  const recent = (options.recentNames ?? []).filter((name) => typeof name === "string" && name.length > 0);
  if (recent.length > 0) {
    lines.push(`   최근 다른 사람들이 받은 이름: ${recent.join(", ")}. 이 이름들과 같거나 비슷하지 않게 짓습니다.`);
  }

  lines.push(
    `3) story: ${style}. 이 별자리가 왜 그 모양이 되었는지를 키워드와 엮어 들려줍니다(한국어 2~3문장, 총 120자 안팎).`,
    "",
    "[말투] 소리 내어 읽어 줄 글입니다.",
    "- 잠자리에서 아이에게 들려주듯 입말로 씁니다. 언제, 어디서 있었던 일인지로 시작하되 매번 다른 때와 곳을 고릅니다.",
    "- '이 별자리는 ~입니다', '~을 상징합니다', '~를 의미합니다', '~를 나타냅니다'처럼 설명하거나 정의하는 말은 쓰지 않습니다.",
    "- 한 문장에 한 장면만 담고, 소리·빛·바람·온기 같은 감각을 하나쯤 넣습니다.",
    "- 어린이도 보는 전시이므로 무섭거나 폭력적인 표현은 쓰지 않습니다.",
    `- 말투 예시(말투만 참고하고 내용과 낱말은 따라 하지 않습니다): "${example}"`,
    "",
    '아래 JSON 형식으로만 답합니다: {"looks_like":"","name":"","story":""}',
  );
  return lines.join("\n");
}

/** 구조화 출력 스키마. looks_like를 먼저 쓰게 해서 모양을 정한 뒤 이름을 짓게 한다. */
export const STORY_SCHEMA = Object.freeze({
  type: "object",
  properties: {
    looks_like: { type: "string", description: "별자리가 닮은 구체적인 것 하나(예: 날개를 편 학)" },
    name: { type: "string", description: "looks_like를 담은 별자리 이름. 한국어 2~8자" },
    story: { type: "string", description: "소리 내어 들려주는 짧은 이야기. 한국어 2~3문장, 120자 안팎" },
  },
  required: ["looks_like", "name", "story"],
  additionalProperties: false,
});
