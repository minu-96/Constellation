import { describe, expect, it, vi } from "vitest";
import { createHandler } from "../src/index.js";
import { createRateLimiter } from "../src/rateLimit.js";
import { TEST_ENV, TEST_TOKEN, storyRequest } from "./helpers.js";

const ENV = { ...TEST_ENV, GOOGLE_TTS_API_KEY: "test-tts-key" };
const AUDIO_B64 = Buffer.from([0x52, 0x49, 0x46, 0x46, 1, 2, 3]).toString("base64");

function setup(fetchImpl) {
  const fetchMock = vi.fn(fetchImpl ?? (async () => new Response(JSON.stringify({ audioContent: AUDIO_B64 }), { status: 200 })));
  const handler = createHandler({ rateLimiter: createRateLimiter(), now: () => 1_000_000, fetchImpl: fetchMock });
  return { fetchMock, send: (req, env = ENV) => handler.fetch(req, env) };
}
const ttsReq = (body, opts) => storyRequest(body, { path: "/tts", ...opts });

describe("POST /tts", () => {
  it("글을 받아 WAV 바이트를 돌려준다", async () => {
    const { send, fetchMock } = setup();
    const res = await send(ttsReq({ text: "옛날 옛적에 별 하나가 있었대요." }));
    expect(res.status).toBe(200);
    expect(res.headers.get("Content-Type")).toBe("audio/wav");
    expect(Array.from(new Uint8Array(await res.arrayBuffer()))).toEqual([0x52, 0x49, 0x46, 0x46, 1, 2, 3]);
    const [, init] = fetchMock.mock.calls[0];
    expect(init.headers["X-Goog-Api-Key"]).toBe("test-tts-key");
    expect(JSON.parse(init.body).audioConfig.audioEncoding).toBe("LINEAR16");
  });

  it("토큰이 없거나 틀리면 401", async () => {
    const { send, fetchMock } = setup();
    expect((await send(ttsReq({ text: "안녕하세요" }, { token: null }))).status).toBe(401);
    expect((await send(ttsReq({ text: "안녕하세요" }, { token: "wrong" }))).status).toBe(401);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("글이 없거나 너무 길면 400", async () => {
    const { send, fetchMock } = setup();
    expect((await send(ttsReq({}))).status).toBe(400);
    expect((await send(ttsReq({ text: "가".repeat(300) }))).status).toBe(400);
    expect((await send(ttsReq({ text: 123 }))).status).toBe(400);
    expect((await send(ttsReq("not json"))).status).toBe(400);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("GET은 405", async () => {
    const { send } = setup();
    expect((await send(ttsReq(null, { method: "GET" }))).status).toBe(405);
  });

  it("호출 제한을 넘으면 429", async () => {
    const { send } = setup();
    const env = { ...ENV, TTS_RATE_LIMIT_PER_MINUTE: "3" };
    for (let i = 0; i < 3; i++) expect((await send(ttsReq({ text: "안녕하세요" }), env)).status).toBe(200);
    expect((await send(ttsReq({ text: "안녕하세요" }), env)).status).toBe(429);
  });

  it("키가 없으면 500, Google 오류와 이상한 응답은 502", async () => {
    expect((await setup().send(ttsReq({ text: "안녕하세요" }), TEST_ENV)).status).toBe(500);
    const bad = setup(async () => new Response("{}", { status: 403 }));
    expect((await bad.send(ttsReq({ text: "안녕하세요" }))).status).toBe(502);
    const empty = setup(async () => new Response("{}", { status: 200 }));
    expect((await empty.send(ttsReq({ text: "안녕하세요" }))).status).toBe(502);
    const boom = setup(async () => { throw new Error("network"); });
    expect((await boom.send(ttsReq({ text: "안녕하세요" }))).status).toBe(502);
  });
});
