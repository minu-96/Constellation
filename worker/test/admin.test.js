import { describe, expect, it } from "vitest";
import { GOOD_STORY, TEST_ENV, fakeJpegBase64, fakeKv, setup, storyRequest } from "./helpers.js";

const ORIGIN = "https://relay.test";
const PASSWORD = "test-admin-password";
const POINTS = [[0.1, 0.2], [0.5, 0.9], [0.9, 0.3]];

function env() {
  return { ...TEST_ENV, ADMIN_PASSWORD: PASSWORD, CARDS: fakeKv() };
}
const form = (path, data, { cookie, origin = ORIGIN } = {}) => {
  const headers = { "Content-Type": "application/x-www-form-urlencoded", "CF-Connecting-IP": "1.2.3.4" };
  if (origin) headers.Origin = origin;
  if (cookie) headers.Cookie = cookie;
  return new Request(`${ORIGIN}${path}`, { method: "POST", headers, body: new URLSearchParams(data).toString() });
};
const get = (path, cookie) => new Request(`${ORIGIN}${path}`, { headers: cookie ? { Cookie: cookie } : {} });

async function login(t, e) {
  const res = await t.send(form("/admin/login", { password: PASSWORD }), e);
  expect(res.status).toBe(303);
  return res.headers.get("Set-Cookie").split(";")[0];
}
async function addCard(t, e, i) {
  const body = { keyword: "wish", ...GOOD_STORY, image: fakeJpegBase64(), points: POINTS };
  return (await t.send(storyRequest(body, { path: "/card", deviceId: `quest-${i}` }), e)).json();
}

describe("/admin", () => {
  it("비밀번호가 등록되지 않았으면 열리지 않는다", async () => {
    const t = setup();
    expect((await t.send(get("/admin"), { ...TEST_ENV, CARDS: fakeKv() })).status).toBe(404);
  });

  it("로그인 전에는 로그인 화면만 보이고, 틀린 비밀번호는 401", async () => {
    const t = setup();
    const e = env();
    const page = await (await t.send(get("/admin"), e)).text();
    expect(page).toContain("운영자 로그인");
    expect((await t.send(form("/admin/login", { password: "wrong" }), e)).status).toBe(401);
  });

  it("다른 사이트에서 온 로그인·삭제 요청은 막는다", async () => {
    const t = setup();
    const e = env();
    expect((await t.send(form("/admin/login", { password: PASSWORD }, { origin: "https://evil.test" }), e)).status).toBe(403);
    const cookie = await login(t, e);
    expect((await t.send(form("/admin/clear", { confirm: "전부 삭제" }, { cookie, origin: null }), e)).status).toBe(403);
  });

  it("Origin이 null이어도 Sec-Fetch-Site가 same-origin이면 로그인된다", async () => {
    const t = setup();
    const e = env();
    const req = form("/admin/login", { password: PASSWORD }, { origin: "null" });
    req.headers.set("Sec-Fetch-Site", "same-origin");
    expect((await t.send(req, e)).status).toBe(303);
    const cross = form("/admin/login", { password: PASSWORD }, { origin: "null" });
    cross.headers.set("Sec-Fetch-Site", "cross-site");
    expect((await t.send(cross, e)).status).toBe(403);
  });

  it("쿠키 없이 삭제하면 401, 위조 쿠키도 401", async () => {
    const t = setup();
    const e = env();
    expect((await t.send(form("/admin/delete", { n: "1" }), e)).status).toBe(401);
    expect((await t.send(form("/admin/delete", { n: "1" }, { cookie: "cadmin=9999999999.abcd" }), e)).status).toBe(401);
  });

  it("로그인하면 목록과 QR이 보이고, 하나를 지우면 카드와 /wall에서 빠진다", async () => {
    const t = setup();
    const e = env();
    const first = await addCard(t, e, 1);
    const second = await addCard(t, e, 2);
    const cookie = await login(t, e);

    const page = await (await t.send(get("/admin", cookie), e)).text();
    expect(page).toContain("1번째");
    expect(page).toContain("2번째");
    expect(page).toContain(`/card/${first.id}`);
    expect(page).toContain("<svg");

    const res = await t.send(form("/admin/delete", { n: "1" }, { cookie }), e);
    expect(res.status).toBe(303);
    expect((await t.send(get(`/card/${first.id}`), e)).status).toBe(404);
    expect((await t.send(get(`/card/${second.id}`), e)).status).toBe(200);
    const sky = await (await t.send(get("/sky"), e)).json();
    expect(sky.items.map((it) => it.n)).toEqual([2]);
  });

  it("전부 삭제는 '전부 삭제'를 입력해야 하고, 지우면 번호가 1부터 다시 시작한다", async () => {
    const t = setup();
    const e = env();
    const card = await addCard(t, e, 1);
    await addCard(t, e, 2);
    const cookie = await login(t, e);

    await t.send(form("/admin/clear", { confirm: "아니" }, { cookie }), e);
    expect((await (await t.send(get("/sky"), e)).json()).items).toHaveLength(2);

    await t.send(form("/admin/clear", { confirm: "전부 삭제" }, { cookie }), e);
    const sky = await (await t.send(get("/sky"), e)).json();
    expect(sky).toEqual({ count: 0, items: [] });
    expect((await t.send(get(`/card/${card.id}`), e)).status).toBe(404);
    expect((await addCard(t, e, 3)).number).toBe(1);
  });

  it("로그인 시도는 분당 5번까지", async () => {
    const t = setup();
    const e = env();
    for (let i = 0; i < 5; i++) await t.send(form("/admin/login", { password: "wrong" }), e);
    expect((await t.send(form("/admin/login", { password: PASSWORD }), e)).status).toBe(429);
  });
});
