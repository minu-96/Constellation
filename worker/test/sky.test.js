import { describe, expect, it } from "vitest";
import { SKY_MAX_ITEMS, normalizePoints } from "../src/sky.js";
import { GOOD_STORY, TEST_ENV, fakeJpegBase64, fakeKv, setup, storyRequest } from "./helpers.js";

const POINTS = [[0.1, 0.2], [0.5, 0.9], [0.9, 0.3]];
const cardBody = (overrides = {}) => ({ keyword: "wish", ...GOOD_STORY, image: fakeJpegBase64(), points: POINTS, ...overrides });
const post = (body, i = 0) => storyRequest(body, { path: "/card", deviceId: `quest-${i}` });
const get = (path) => new Request(`https://relay.test${path}`);

describe("normalizePoints", () => {
  it("0~1 범위의 점을 소수 셋째 자리로 다듬는다", () => {
    expect(normalizePoints([[0, 1], [0.12345, 0.98765]])).toEqual([[0, 1], [0.123, 0.988]]);
  });
  it.each([
    ["배열이 아님", "abc"],
    ["점이 하나", [[0.1, 0.1]]],
    ["점이 17개", Array.from({ length: 17 }, () => [0.5, 0.5])],
    ["범위 밖", [[0.1, 0.1], [1.2, 0.5]]],
    ["숫자가 아님", [[0.1, 0.1], ["0.5", 0.5]]],
    ["NaN", [[0.1, 0.1], [Number.NaN, 0.5]]],
    ["좌표가 셋", [[0.1, 0.1, 0.1], [0.5, 0.5, 0.5]]],
  ])("%s → null", (_name, value) => {
    expect(normalizePoints(value)).toBeNull();
  });
});

describe("번호 매기기", () => {
  it("카드를 올릴 때마다 번호가 하나씩 올라가고 카드 페이지에 보인다", async () => {
    const t = setup();
    const env = { ...TEST_ENV, CARDS: fakeKv() };
    const first = await (await t.send(post(cardBody(), 1), env)).json();
    const second = await (await t.send(post(cardBody({ keyword: "adventure" }), 2), env)).json();
    expect(first.number).toBe(1);
    expect(second.number).toBe(2);

    const page = await (await t.send(get(`/card/${second.id}`), env)).text();
    expect(page).toContain("2번째 별자리");
  });

  it("모양이 없으면 카드는 만들되 번호는 없고 하늘에도 더하지 않는다", async () => {
    const t = setup();
    const env = { ...TEST_ENV, CARDS: fakeKv() };
    const res = await t.send(post(cardBody({ points: undefined })), env);
    expect(res.status).toBe(200);
    const body = await res.json();
    expect(body.number).toBeNull();
    expect((await (await t.send(get("/sky"), env)).json()).count).toBe(0);
    expect(await (await t.send(get(`/card/${body.id}`), env)).text()).not.toContain("번째 별자리");
  });

  it("모양 형식이 틀리면 400", async () => {
    const t = setup();
    const CARDS = fakeKv();
    const res = await t.send(post(cardBody({ points: [[0.1, 0.1], [2, 2]] })), { ...TEST_ENV, CARDS });
    expect(res.status).toBe(400);
    expect(CARDS.store.size).toBe(0);
  });

  it("하늘 저장이 실패해도 카드는 번호 없이 만들어진다", async () => {
    const t = setup();
    const CARDS = fakeKv();
    const put = CARDS.put.bind(CARDS);
    CARDS.put = async (key, value, options) => {
      if (key === "sky:index") throw new Error("kv busy");
      return put(key, value, options);
    };
    const res = await t.send(post(cardBody()), { ...TEST_ENV, CARDS });
    expect(res.status).toBe(200);
    expect((await res.json()).number).toBeNull();
  });
});

describe("GET /sky", () => {
  it("토큰 없이 목록을 돌려준다", async () => {
    const t = setup();
    const env = { ...TEST_ENV, CARDS: fakeKv() };
    await t.send(post(cardBody(), 1), env);
    const res = await t.send(get("/sky"), env);
    expect(res.status).toBe(200);
    expect(await res.json()).toEqual({ count: 1, items: [{ n: 1, name: GOOD_STORY.name, keyword: "wish", points: POINTS, xy: POINTS.flat() }] });
  });

  it("최근 80개만 남기고 번호는 계속 올라간다", async () => {
    const t = setup();
    const env = { ...TEST_ENV, CARDS: fakeKv(), RATE_LIMIT_PER_MINUTE: "1000" };
    for (let i = 0; i < SKY_MAX_ITEMS + 5; i++) await t.send(post(cardBody(), 0), env);
    const sky = await (await t.send(get("/sky"), env)).json();
    expect(sky.count).toBe(SKY_MAX_ITEMS + 5);
    expect(sky.items).toHaveLength(SKY_MAX_ITEMS);
    expect(sky.items[0].n).toBe(6);
    expect(sky.items.at(-1).n).toBe(SKY_MAX_ITEMS + 5);
  });

  it("저장소가 없으면 503, 손상된 값은 빈 목록으로 본다", async () => {
    const t = setup();
    expect((await t.send(get("/sky"))).status).toBe(503);
    const CARDS = fakeKv();
    CARDS.store.set("sky:index", "{not json");
    expect(await (await t.send(get("/sky"), { ...TEST_ENV, CARDS })).json()).toEqual({ count: 0, items: [] });
  });

  it("POST /sky → 405", async () => {
    const t = setup();
    const res = await t.send(storyRequest({}, { path: "/sky" }), { ...TEST_ENV, CARDS: fakeKv() });
    expect(res.status).toBe(405);
  });
});

describe("GET /wall", () => {
  it("별자리 이름, 번호, 모양을 보여 주고 15초마다 새로 고친다", async () => {
    const t = setup();
    const env = { ...TEST_ENV, CARDS: fakeKv() };
    await t.send(post(cardBody(), 1), env);
    await t.send(post(cardBody({ name: "바람길별", keyword: "adventure" }), 2), env);
    const res = await t.send(get("/wall"), env);
    expect(res.status).toBe(200);
    expect(res.headers.get("Content-Security-Policy")).toContain("default-src 'none'");
    const page = await res.text();
    expect(page).toContain('http-equiv="refresh" content="15"');
    expect(page).toContain("<b>2</b>개의 별자리");
    expect(page).toContain("바람길별");
    expect(page).toContain("2번째 · 모험을 떠나는 별자리");
    expect(page).toContain("<polyline");
    // 가장 최근 것이 맨 앞에 강조되어 나온다.
    expect(page.indexOf("바람길별")).toBeLessThan(page.indexOf(GOOD_STORY.name));
    expect(page).not.toContain(TEST_ENV.APP_SHARED_TOKEN);
  });

  it("최근 3개에는 카드로 가는 QR을 붙이고, 목록(/sky)에는 카드 id를 내보내지 않는다", async () => {
    const t = setup();
    const env = { ...TEST_ENV, CARDS: fakeKv(), RATE_LIMIT_PER_MINUTE: "1000" };
    const ids = [];
    for (let i = 0; i < 5; i++) ids.push((await (await t.send(post(cardBody(), 0), env)).json()).id);
    const page = await (await t.send(get("/wall"), env)).text();
    expect(page.match(/class="qr"/g)).toHaveLength(3);
    expect(page).toContain("카드를 받아 가세요");

    const sky = await (await t.send(get("/sky"), env)).json();
    expect(sky.items.every((item) => item.id === undefined)).toBe(true);
    for (const id of ids) expect(JSON.stringify(sky)).not.toContain(id);
  });

  it("아직 하나도 없으면 기다리는 문구를 보여 준다", async () => {
    const t = setup();
    const page = await (await t.send(get("/wall"), { ...TEST_ENV, CARDS: fakeKv() })).text();
    expect(page).toContain("첫 번째 별자리를 기다리고 있어요");
  });

  it("저장된 이름에 마크업이 섞여 있어도 그대로 내보내지 않는다", async () => {
    const t = setup();
    const CARDS = fakeKv();
    CARDS.store.set("sky:index", JSON.stringify({ count: 1, items: [{ n: 1, name: "<script>x</script>", keyword: "wish", points: [[0, 0], ['"><script>', 1]] }] }));
    const page = await (await t.send(get("/wall"), { ...TEST_ENV, CARDS })).text();
    expect(page).not.toContain("<script>");
  });
});
