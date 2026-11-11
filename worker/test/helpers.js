import { vi } from "vitest";
import { createHandler } from "../src/index.js";
import { createRateLimiter } from "../src/rateLimit.js";

// 테스트 전용 가짜 값. 실제 키나 토큰이 아니다.
export const TEST_TOKEN = "test-shared-token";
export const TEST_ENV = Object.freeze({
  ANTHROPIC_API_KEY: "test-api-key",
  APP_SHARED_TOKEN: TEST_TOKEN,
  CLAUDE_MODEL: "claude-haiku-5-5",
  RATE_LIMIT_PER_MINUTE: "3",
});

/** JPEG 시작 바이트(FF D8 FF)를 가진 base64 문자열을 만든다. */
export function fakeJpegBase64(byteLength = 1024) {
  const bytes = new Uint8Array(byteLength);
  bytes.set([0xff, 0xd8, 0xff, 0xe0]);
  return Buffer.from(bytes).toString("base64");
}

export const GOOD_STORY = {
  name: "소원나무별",
  story: "깊은 산골 아이가 별 하나에 소원을 빌었어요. 그 마음이 하늘에 닿아, 밤마다 소원나무별이 반짝이며 마을을 지켜 주었대요.",
};

/** Claude Messages API 응답 모양의 가짜 객체 */
export function modelReply(text, stopReason = "end_turn") {
  return { stop_reason: stopReason, content: [{ type: "text", text }] };
}

/** Claude 호출을 모킹한 핸들러. create에 넣은 구현이 모델 응답을 정한다. */
export function setup(createImpl = async () => modelReply(JSON.stringify(GOOD_STORY))) {
  const create = vi.fn(createImpl);
  const createClient = vi.fn(() => ({ messages: { create } }));
  let clock = 1_000_000;
  const handler = createHandler({
    createClient,
    rateLimiter: createRateLimiter(),
    now: () => clock,
  });
  return {
    create,
    createClient,
    advance: (ms) => { clock += ms; },
    send: (request, env = TEST_ENV) => handler.fetch(request, env),
  };
}

export function storyRequest(body, { token = TEST_TOKEN, deviceId = "quest-test-01", path = "/story", method = "POST" } = {}) {
  const headers = { "Content-Type": "application/json" };
  if (token !== null) headers["X-App-Token"] = token;
  if (deviceId !== null) headers["X-Device-Id"] = deviceId;
  return new Request(`https://relay.test${path}`, {
    method,
    headers,
    body: method === "POST" ? (typeof body === "string" ? body : JSON.stringify(body)) : undefined,
  });
}

export function goodBody(overrides = {}) {
  return { keyword: "wish", starCount: 5, image: fakeJpegBase64(), ...overrides };
}
