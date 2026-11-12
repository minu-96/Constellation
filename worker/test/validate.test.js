import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { base64DecodedLength, countSentences, validateStory } from "../src/validate.js";

describe("base64DecodedLength", () => {
  it.each([1, 2, 3, 4, 5, 100, 1023, 1024])("%i바이트", (n) => {
    const b64 = Buffer.from(new Uint8Array(n)).toString("base64");
    expect(base64DecodedLength(b64)).toBe(n);
  });
});

describe("countSentences", () => {
  it.each([
    ["하나예요.", 1],
    ["하나예요. 둘이에요.", 2],
    ["오늘 밤, 누구에게 보낼까요? 별이 대신 전해 준대요!", 2],
    ["마침표 없이 끝나는 문장", 1],
    ["말줄임표… 그리고 끝.", 2],
  ])("%s → %i", (text, expected) => {
    expect(countSentences(text)).toBe(expected);
  });
});

describe("validateStory", () => {
  it("공백이 낀 이름을 허용한다", () => {
    const r = validateStory({ name: "소원을 듣는 별", story: "옛날 옛적 작은 별이 살았어요. 그 별은 밤마다 소원을 들어 주었대요." });
    expect(r.ok).toBe(true);
  });
});

// 폴백 이야기는 모델 응답과 같은 규칙을 통과해야 한다.
// Unity 쪽 데이터 파일이 이 브랜치보다 뒤에 추가되므로, 파일이 있을 때만 검사한다.
describe("폴백 이야기 15세트", () => {
  let data = null;
  try {
    data = JSON.parse(readFileSync(new URL("../../Assets/Data/fallback_stories.json", import.meta.url), "utf8"));
  } catch {
    data = null;
  }
  it.skipIf(data === null)("모두 응답 규칙을 통과한다", () => {
    const failures = [];
    const counts = {};
    for (const s of data.stories) {
      counts[s.keyword] = (counts[s.keyword] ?? 0) + 1;
      const r = validateStory(s);
      if (!r.ok) failures.push(`${s.keyword}/${s.name}: ${r.error}`);
    }
    expect(failures).toEqual([]);
    expect(counts).toEqual({ wish: 5, adventure: 5, loved_one: 5 });
  });
});
