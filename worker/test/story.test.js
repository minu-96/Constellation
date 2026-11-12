import { describe, expect, it } from "vitest";
import { IMAGE_MAX_BYTES } from "../src/rules.js";
import { GOOD_STORY, TEST_ENV, fakeJpegBase64, goodBody, modelReply, setup, storyRequest } from "./helpers.js";

describe("요청 검증", () => {
  it.each([
    ["허용되지 않은 키워드", goodBody({ keyword: "love" })],
    ["키워드 누락", goodBody({ keyword: undefined })],
    ["프로토타입 속성 이름을 키워드로 보냄", goodBody({ keyword: "toString" })],
    ["별 개수 2개(최소 미만)", goodBody({ starCount: 2 })],
    ["별 개수 9개(최대 초과)", goodBody({ starCount: 9 })],
    ["별 개수가 정수가 아님", goodBody({ starCount: 4.5 })],
    ["별 개수가 문자열", goodBody({ starCount: "5" })],
    ["이미지 누락", goodBody({ image: undefined })],
    ["이미지가 JPEG가 아님(PNG)", goodBody({ image: Buffer.from([0x89, 0x50, 0x4e, 0x47, 0, 0]).toString("base64") })],
    ["이미지가 base64가 아님", goodBody({ image: "/9j/!!!!" })],
    ["본문이 배열", []],
  ])("%s → 400", async (_name, body) => {
    const t = setup();
    const res = await t.send(storyRequest(body));
    expect(res.status).toBe(400);
    expect(t.create).not.toHaveBeenCalled();
  });

  it("JSON이 아닌 본문 → 400", async () => {
    const t = setup();
    const res = await t.send(storyRequest("{keyword:"));
    expect(res.status).toBe(400);
    expect(await res.json()).toEqual({ error: "invalid_json" });
  });

  it("300KB를 넘는 이미지 → 413", async () => {
    const t = setup();
    const res = await t.send(storyRequest(goodBody({ image: fakeJpegBase64(IMAGE_MAX_BYTES + 1) })));
    expect(res.status).toBe(413);
    expect(t.create).not.toHaveBeenCalled();
  });

  it("딱 300KB인 이미지는 통과", async () => {
    const t = setup();
    const res = await t.send(storyRequest(goodBody({ image: fakeJpegBase64(IMAGE_MAX_BYTES) })));
    expect(res.status).toBe(200);
  });

  it("지나치게 큰 본문 → 413", async () => {
    const t = setup();
    const res = await t.send(storyRequest(goodBody({ image: fakeJpegBase64(IMAGE_MAX_BYTES * 3) })));
    expect(res.status).toBe(413);
  });
});

describe("토큰과 경로", () => {
  it("토큰 헤더가 없으면 401", async () => {
    const t = setup();
    const res = await t.send(storyRequest(goodBody(), { token: null }));
    expect(res.status).toBe(401);
    expect(t.create).not.toHaveBeenCalled();
  });

  it("토큰이 틀리면 401", async () => {
    const t = setup();
    const res = await t.send(storyRequest(goodBody(), { token: "wrong-token" }));
    expect(res.status).toBe(401);
  });

  it("서버에 토큰 시크릿이 없으면 500(열어 두지 않는다)", async () => {
    const t = setup();
    const res = await t.send(storyRequest(goodBody()), { ...TEST_ENV, APP_SHARED_TOKEN: undefined });
    expect(res.status).toBe(500);
    expect(t.create).not.toHaveBeenCalled();
  });

  it("API 키 시크릿이 없으면 500", async () => {
    const t = setup();
    const res = await t.send(storyRequest(goodBody()), { ...TEST_ENV, ANTHROPIC_API_KEY: undefined });
    expect(res.status).toBe(500);
    expect(t.create).not.toHaveBeenCalled();
  });

  it("GET → 405", async () => {
    const t = setup();
    const res = await t.send(storyRequest(null, { method: "GET" }));
    expect(res.status).toBe(405);
  });

  it("모르는 경로 → 404", async () => {
    const t = setup();
    const res = await t.send(storyRequest(goodBody(), { path: "/nope" }));
    expect(res.status).toBe(404);
  });

  it("루트 경로(/)로 보내도 처리한다", async () => {
    const t = setup();
    const res = await t.send(storyRequest(goodBody(), { path: "/" }));
    expect(res.status).toBe(200);
  });
});

describe("정상 흐름", () => {
  it("이름과 이야기를 200으로 돌려준다", async () => {
    const t = setup();
    const res = await t.send(storyRequest(goodBody()));
    expect(res.status).toBe(200);
    expect(await res.json()).toEqual(GOOD_STORY);
  });

  it.each(["wish", "adventure", "loved_one"])("키워드 %s 허용", async (keyword) => {
    const t = setup();
    const res = await t.send(storyRequest(goodBody({ keyword })));
    expect(res.status).toBe(200);
  });

  it("Claude에 모델명, 이미지, 키워드 문구를 넘긴다", async () => {
    const t = setup();
    const body = goodBody({ keyword: "adventure", starCount: 7 });
    await t.send(storyRequest(body), { ...TEST_ENV, CLAUDE_MODEL: "claude-sonnet-5-5" });

    expect(t.createClient).toHaveBeenCalledTimes(1);
    const params = t.create.mock.calls[0][0];
    expect(params.model).toBe("claude-sonnet-5-5");
    expect(params.output_config.format.type).toBe("json_schema");
    const [image, text] = params.messages[0].content;
    expect(image).toEqual({ type: "image", source: { type: "base64", media_type: "image/jpeg", data: body.image } });
    expect(text.text).toContain("모험을 떠나는 별자리");
    expect(text.text).toContain("별 7개");
    expect(text.text).toContain("무섭거나 폭력적인 표현은 쓰지 않습니다");
  });

  it("모델명 환경 변수가 없으면 claude-haiku-5-5를 쓴다", async () => {
    const t = setup();
    await t.send(storyRequest(goodBody()), { ...TEST_ENV, CLAUDE_MODEL: undefined });
    expect(t.create.mock.calls[0][0].model).toBe("claude-haiku-5-5");
  });

  it("앞뒤 공백을 다듬고, 코드 펜스에 싸인 JSON도 읽는다", async () => {
    const wrapped = "```json\n" + JSON.stringify({ name: " 바람길별 ", story: GOOD_STORY.story + "\n" }) + "\n```";
    const t = setup(async () => modelReply(wrapped));
    const res = await t.send(storyRequest(goodBody()));
    expect(res.status).toBe(200);
    expect(await res.json()).toEqual({ name: "바람길별", story: GOOD_STORY.story });
  });

  it("응답에 API 키나 토큰이 섞이지 않는다", async () => {
    const t = setup();
    const res = await t.send(storyRequest(goodBody()));
    const raw = await res.text();
    expect(raw).not.toContain(TEST_ENV.ANTHROPIC_API_KEY);
    expect(raw).not.toContain(TEST_ENV.APP_SHARED_TOKEN);
  });
});

describe("모델 응답 이상 → 502", () => {
  const long = "별이 반짝였어요. ".repeat(30);
  it.each([
    ["JSON이 아닌 답", "별자리 이름은 소원나무별입니다."],
    ["빈 객체", "{}"],
    ["story 누락", JSON.stringify({ name: "소원나무별" })],
    ["이름이 1자", JSON.stringify({ ...GOOD_STORY, name: "별" })],
    ["이름이 9자", JSON.stringify({ ...GOOD_STORY, name: "아주아주긴이름의별" })],
    ["이름이 영어", JSON.stringify({ ...GOOD_STORY, name: "Wish Star" })],
    ["이야기가 200자 초과", JSON.stringify({ ...GOOD_STORY, story: long })],
    ["이야기가 한 문장", JSON.stringify({ ...GOOD_STORY, story: "깊은 산골 아이가 별 하나에 소원을 빌었더니 그 마음이 하늘에 닿았대요." })],
    ["이야기가 다섯 문장", JSON.stringify({ ...GOOD_STORY, story: "별이 떴어요. 달도 떴어요. 바람이 불었어요. 아이가 웃었어요. 모두 잠들었대요." })],
    ["이야기가 영어", JSON.stringify({ ...GOOD_STORY, story: "Once upon a time a child made a wish. The star listened." })],
    ["무서운 표현", JSON.stringify({ ...GOOD_STORY, story: "깊은 밤 귀신이 별을 훔쳐 갔어요. 마을 사람들은 밤마다 떨었대요." })],
    ["타입이 문자열이 아님", JSON.stringify({ name: 123, story: ["a"] })],
  ])("%s", async (_name, text) => {
    const t = setup(async () => modelReply(text));
    const res = await t.send(storyRequest(goodBody()));
    expect(res.status).toBe(502);
    expect(await res.json()).toEqual({ error: "invalid_model_output" });
  });

  it("Claude 호출이 실패하면 502이고 내부 오류 내용을 흘리지 않는다", async () => {
    const t = setup(async () => {
      throw Object.assign(new Error("secret upstream detail"), { status: 529 });
    });
    const res = await t.send(storyRequest(goodBody()));
    expect(res.status).toBe(502);
    expect(await res.text()).not.toContain("secret upstream detail");
  });

  it("모델이 거절하면 502", async () => {
    const t = setup(async () => ({ stop_reason: "refusal", content: [] }));
    const res = await t.send(storyRequest(goodBody()));
    expect(res.status).toBe(502);
  });

  it("텍스트 블록이 없으면 502", async () => {
    const t = setup(async () => ({ stop_reason: "end_turn", content: [{ type: "thinking", thinking: "" }] }));
    const res = await t.send(storyRequest(goodBody()));
    expect(res.status).toBe(502);
  });
});

describe("호출 횟수 제한", () => {
  it("분당 한도를 넘으면 429, 1분이 지나면 다시 허용", async () => {
    const t = setup();
    for (let i = 0; i < 3; i++) {
      expect((await t.send(storyRequest(goodBody()))).status).toBe(200);
    }
    expect((await t.send(storyRequest(goodBody()))).status).toBe(429);
    expect(t.create).toHaveBeenCalledTimes(3);

    t.advance(61_000);
    expect((await t.send(storyRequest(goodBody()))).status).toBe(200);
  });

  it("기기별로 따로 센다", async () => {
    const t = setup();
    for (let i = 0; i < 3; i++) await t.send(storyRequest(goodBody(), { deviceId: "quest-a" }));
    expect((await t.send(storyRequest(goodBody(), { deviceId: "quest-a" }))).status).toBe(429);
    expect((await t.send(storyRequest(goodBody(), { deviceId: "quest-b" }))).status).toBe(200);
  });

  it("검증에 실패한 요청은 횟수에 넣지 않는다", async () => {
    const t = setup();
    for (let i = 0; i < 5; i++) await t.send(storyRequest(goodBody({ keyword: "bad" })));
    expect((await t.send(storyRequest(goodBody()))).status).toBe(200);
  });
});
