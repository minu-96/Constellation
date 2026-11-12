import { describe, expect, it } from "vitest";
import { escapeHtml, newCardId } from "../src/card.js";
import { IMAGE_MAX_BYTES } from "../src/rules.js";
import { GOOD_STORY, TEST_ENV, fakeJpegBase64, fakeKv, setup, storyRequest } from "./helpers.js";

const cardBody = (overrides = {}) => ({ keyword: "wish", ...GOOD_STORY, image: fakeJpegBase64(), ...overrides });
const post = (body, options = {}) => storyRequest(body, { path: "/card", ...options });
const view = (path) => new Request(`https://relay.test${path}`);

describe("카드 올리기", () => {
  it("저장하고 열 수 있는 주소를 돌려준다", async () => {
    const t = setup();
    const CARDS = fakeKv();
    const res = await t.send(post(cardBody()), { ...TEST_ENV, CARDS });
    expect(res.status).toBe(200);
    const { id, url } = await res.json();
    expect(id).toMatch(/^[a-z2-9]{12}$/);
    expect(url).toBe(`https://relay.test/card/${id}`);
    expect(CARDS.puts[0].key).toBe(`card:${id}`);
    expect(CARDS.puts[0].options.expirationTtl).toBe(30 * 24 * 60 * 60);
    expect(t.create).not.toHaveBeenCalled(); // 카드는 Claude를 부르지 않는다
  });

  it("토큰이 없으면 401", async () => {
    const t = setup();
    const res = await t.send(post(cardBody(), { token: null }), { ...TEST_ENV, CARDS: fakeKv() });
    expect(res.status).toBe(401);
  });

  it.each([
    ["허용되지 않은 키워드", { keyword: "nope" }],
    ["이름 규칙 위반", { name: "Star" }],
    ["이야기 규칙 위반", { story: "짧아요." }],
    ["마크업이 섞인 이야기", { story: "별이 떴어요. <script>alert(1)</script> 달도 떴대요." }],
    ["JPEG가 아닌 이미지", { image: "AAAA" }],
  ])("%s → 400", async (_name, overrides) => {
    const t = setup();
    const CARDS = fakeKv();
    const res = await t.send(post(cardBody(overrides)), { ...TEST_ENV, CARDS });
    expect(res.status).toBe(400);
    expect(CARDS.store.size).toBe(0);
  });

  it("300KB를 넘는 이미지 → 413", async () => {
    const t = setup();
    const res = await t.send(post(cardBody({ image: fakeJpegBase64(IMAGE_MAX_BYTES + 1) })), { ...TEST_ENV, CARDS: fakeKv() });
    expect(res.status).toBe(413);
  });

  it("저장소가 연결되지 않았으면 503", async () => {
    const t = setup();
    const res = await t.send(post(cardBody()));
    expect(res.status).toBe(503);
  });

  it("저장에 실패하면 503", async () => {
    const t = setup();
    const CARDS = { put: async () => { throw new Error("kv down"); }, get: async () => null };
    const res = await t.send(post(cardBody()), { ...TEST_ENV, CARDS });
    expect(res.status).toBe(503);
  });

  it("분당 한도를 넘으면 429, 이야기 요청 횟수와는 따로 센다", async () => {
    const t = setup();
    const env = { ...TEST_ENV, CARDS: fakeKv() };
    for (let i = 0; i < 3; i++) expect((await t.send(post(cardBody()), env)).status).toBe(200);
    expect((await t.send(post(cardBody()), env)).status).toBe(429);
    expect((await t.send(storyRequest({ keyword: "wish", starCount: 5, image: fakeJpegBase64() }), env)).status).toBe(200);
  });

  it("GET /card → 405", async () => {
    const t = setup();
    const res = await t.send(view("/card"), { ...TEST_ENV, CARDS: fakeKv() });
    expect(res.status).toBe(405);
  });
});

describe("카드 보기", () => {
  it("올린 카드를 토큰 없이 HTML로 보여 준다", async () => {
    const t = setup();
    const env = { ...TEST_ENV, CARDS: fakeKv() };
    const body = cardBody({ keyword: "loved_one" });
    const { id } = await (await t.send(post(body), env)).json();

    const res = await t.send(view(`/card/${id}`), env);
    expect(res.status).toBe(200);
    expect(res.headers.get("Content-Type")).toContain("text/html");
    expect(res.headers.get("Content-Security-Policy")).toContain("default-src 'none'");
    const page = await res.text();
    expect(page).toContain(GOOD_STORY.name);
    expect(page).toContain(GOOD_STORY.story);
    expect(page).toContain("소중한 사람을 위한 별자리");
    expect(page).toContain(`data:image/jpeg;base64,${body.image}`);
    expect(page).not.toContain(TEST_ENV.APP_SHARED_TOKEN);
  });

  it("번호, 날짜, 저장 버튼이 있고 스크립트는 nonce가 붙은 것만 허용한다", async () => {
    const t = setup();
    const env = { ...TEST_ENV, CARDS: fakeKv() };
    const { id } = await (await t.send(post(cardBody({ points: [[0.1, 0.1], [0.9, 0.9]] })), env)).json();
    const res = await t.send(view(`/card/${id}`), env);
    const page = await res.text();
    expect(page).toContain("1번째 별자리");
    expect(page).toMatch(/<p class="date" id="date">\d{4}\.\d{2}\.\d{2}<\/p>/);
    expect(page).toContain("카드 이미지로 저장");

    const csp = res.headers.get("Content-Security-Policy");
    const nonce = /script-src 'nonce-([0-9a-f]{32})'/.exec(csp)?.[1];
    expect(nonce).toBeTruthy();
    expect(page).toContain(`<script nonce="${nonce}">`);
    // 스크립트 태그는 저장 버튼용 하나뿐이다.
    expect(page.match(/<script/g)).toHaveLength(1);
    // 요청마다 nonce가 다르다.
    const again = (await t.send(view(`/card/${id}`), env)).headers.get("Content-Security-Policy");
    expect(again).not.toBe(csp);
  });

  it("없는 카드는 404", async () => {
    const t = setup();
    const res = await t.send(view("/card/abcdefghijkm"), { ...TEST_ENV, CARDS: fakeKv() });
    expect(res.status).toBe(404);
    expect(await res.text()).toContain("카드를 찾을 수 없어요");
  });

  it("형식이 다른 id는 저장소를 조회하지 않고 404", async () => {
    const t = setup();
    const CARDS = { get: async () => { throw new Error("should not be called"); } };
    const res = await t.send(view("/card/../../etc"), { ...TEST_ENV, CARDS });
    expect(res.status).toBe(404);
  });

  it("저장된 값이 손상되었어도 스크립트가 그대로 나가지 않는다", async () => {
    const t = setup();
    const CARDS = fakeKv();
    CARDS.store.set("card:abcdefghijkm", JSON.stringify({ keyword: "wish", name: "<b>별</b>", story: "<script>alert(1)</script>", image: '"><script>' }));
    const page = await (await t.send(view("/card/abcdefghijkm"), { ...TEST_ENV, CARDS })).text();
    expect(page).not.toContain("<script>");
    expect(page).toContain("&lt;script&gt;");
    expect(page).not.toContain("<img");
  });
});

describe("보조 함수", () => {
  it("escapeHtml", () => {
    expect(escapeHtml(`<a href="x">'&'</a>`)).toBe("&lt;a href=&quot;x&quot;&gt;&#39;&amp;&#39;&lt;/a&gt;");
  });

  it("newCardId는 매번 다른 12자 id를 만든다", () => {
    const ids = new Set(Array.from({ length: 200 }, () => newCardId()));
    expect(ids.size).toBe(200);
    for (const id of ids) expect(id).toMatch(/^[a-z2-9]{12}$/);
  });
});
