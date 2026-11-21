import { DEFAULT_TONE, KEYWORDS, TONES } from "./rules.js";
import { describeShape } from "./shape.js";

// 자연스러운 이름의 예. 실제 별자리 이름처럼 짧고, 소리 내어 부르기 쉬운 것들이다.
// 요청마다 네 개를 골라 넣어, 예에 쓴 낱말로 이름이 쏠리지 않게 한다.
const GOOD_NAMES = Object.freeze([
  "고래자리", "작은 국자", "춤추는 학", "물동이자리", "잠든 고양이", "돛단배자리", "은빛 활", "기도하는 손",
  "꽃신자리", "까치다리", "외뿔 사슴", "등불자리", "아기 거북", "바람개비", "부엉이자리", "징검다리",
  "두레박자리", "날개 편 새", "눈사람자리", "초승달 배",
]);

// 피해야 할 이름과 그 까닭. 모두 체험에서 실제로 나오기 쉬운 꼴이다.
const BAD_NAMES = Object.freeze([
  ["학날개구름자리", "낱말을 셋 넘게 붙여 부르기 어렵다"],
  ["소원빛별", "무엇을 닮았는지 없이 막연하다"],
  ["지그재그별", "모양을 그대로 말했을 뿐 무엇을 닮았는지가 없다"],
  ["다섯별자리", "별 개수를 이름에 넣었다"],
]);

function pickGoodNames(random) {
  const pool = [...GOOD_NAMES];
  const picked = [];
  for (let i = 0; i < 4 && pool.length > 0; i++) {
    picked.push(pool.splice(Math.floor(random() * pool.length), 1)[0]);
  }
  return picked.map((name) => `'${name}'`).join(", ");
}

/**
 * 별자리 이름과 이야기를 부탁하는 프롬프트. 기획서(docs/planning.md)의 초안을 바탕으로,
 *  - 떠오르는 것 세 가지를 먼저 적고(candidates), 가장 어울리는 하나를 골라(looks_like) 그것을 담아 이름을 짓게 한다.
 *  - 모양은 정확히 맞추기보다 실제 별자리처럼 얼핏 보아 떠오르는 것을 고르게 한다.
 *  - 이름은 실제 별자리 이름처럼 짧게, 좋은 예와 피할 예를 함께 보여 준다.
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
    "1) candidates: 이 모양을 보고 떠오르는 것 세 가지를 서로 다른 갈래(동물, 물건, 자연이나 사람의 몸짓)에서 하나씩 적습니다.",
    "   정확히 맞을 필요는 없습니다. 밤하늘의 실제 별자리처럼, 별 몇 개만 보고도 '아, 그렇게 보이네' 하고 고개가 끄덕여지는 것이면 됩니다.",
    "   선 하나하나보다 전체 윤곽과 기운(길게 뻗음, 웅크림, 날아오름, 무언가를 담음, 기울어짐)을 봅니다.",
    "2) looks_like: 세 가지 가운데 이 모양에 가장 잘 어울리고 키워드와도 이야기가 이어지는 것 하나를 고릅니다. '별', '빛', '하늘', '소원'처럼 막연한 말은 안 됩니다.",
    "3) name: looks_like를 담아 실제 별자리 이름처럼 짧고 부르기 쉬운 이름을 짓습니다(한국어 2~8자, 낱말 둘까지).",
    "   이름 꼴은 셋 가운데 하나입니다: '닮은 것 + 자리', '꾸밈말 + 닮은 것', 우리말 별 이름처럼 '~별'.",
    `   좋은 예: ${pickGoodNames(random)}`,
    `   피할 예: ${BAD_NAMES.map(([name, why]) => `'${name}'(${why})`).join(", ")}`,
    "   키워드 낱말(소원, 모험, 사랑, 마음)이나 희망·꿈·빛·반짝 같은 막연한 말만으로 이름을 짓지 않습니다.",
  );

  const recent = (options.recentNames ?? []).filter((name) => typeof name === "string" && name.length > 0);
  if (recent.length > 0) {
    lines.push(`   최근 다른 사람들이 받은 이름: ${recent.join(", ")}. 이 이름들과 같거나 비슷하지 않게 짓습니다.`);
  }

  lines.push(
    `4) story: ${style}. 이 별자리가 왜 그 모양이 되었는지를 키워드와 엮어 들려줍니다(한국어 2~3문장, 총 120자 안팎).`,
    "",
    "[말투] 소리 내어 읽어 줄 글입니다.",
    "- 잠자리에서 아이에게 들려주듯 입말로 씁니다. 언제, 어디서 있었던 일인지로 시작하되 매번 다른 때와 곳을 고릅니다.",
    "- '이 별자리는 ~입니다', '~을 상징합니다', '~를 의미합니다', '~를 나타냅니다'처럼 설명하거나 정의하는 말은 쓰지 않습니다.",
    "- 한 문장에 한 장면만 담고, 소리·빛·바람·온기 같은 감각을 하나쯤 넣습니다.",
    "- 어린이도 보는 전시이므로 무섭거나 폭력적인 표현은 쓰지 않습니다.",
    `- 말투 예시(말투만 참고하고 내용과 낱말은 따라 하지 않습니다): "${example}"`,
    "",
    '아래 JSON 형식으로만 답합니다: {"candidates":["","",""],"looks_like":"","name":"","story":""}',
  );
  return lines.join("\n");
}

/**
 * 구조화 출력 스키마. 떠오르는 것 세 가지(candidates)를 먼저 적고, 그중 하나(looks_like)를 고른 뒤 이름을 짓게 한다.
 * 앞의 두 항목은 앱에 돌려주지 않는다(이름을 고르는 과정에만 쓴다).
 */
export const STORY_SCHEMA = Object.freeze({
  type: "object",
  properties: {
    candidates: { type: "array", items: { type: "string" }, description: "모양을 보고 떠오르는 것 세 가지(서로 다른 갈래)" },
    looks_like: { type: "string", description: "별자리가 닮은 구체적인 것 하나(예: 날개를 편 학)" },
    name: { type: "string", description: "looks_like를 담은 별자리 이름. 한국어 2~8자" },
    story: { type: "string", description: "소리 내어 들려주는 짧은 이야기. 한국어 2~3문장, 120자 안팎" },
  },
  required: ["candidates", "looks_like", "name", "story"],
  additionalProperties: false,
});
