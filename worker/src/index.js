// Constellation 중계 서버 (Cloudflare Worker)
// Quest 앱 → 이 Worker → Claude API. API 키는 Worker 시크릿에만 둔다.
// 처리 순서(docs/spec.md): 요청 검증 → 호출 제한 → Claude 호출 → 응답 검증.

import { handleCardUpload, handleCardView } from "./card.js";
import { createAnthropicClient, requestStoryText, parseModelJson, toPositiveInt } from "./claude.js";
import { checkToken, deviceKey, json, readJsonBody } from "./http.js";
import { handleSkyList, handleWall } from "./sky.js";
import { createRateLimiter } from "./rateLimit.js";
import { validateStory, validateStoryRequest } from "./validate.js";

const DEFAULT_RATE_LIMIT_PER_MINUTE = 6;

/**
 * 의존성을 주입할 수 있는 핸들러 생성기. 테스트에서 Claude 클라이언트와 시계를 바꿔 끼운다.
 * @param {{createClient?: (env: any) => any, rateLimiter?: ReturnType<typeof createRateLimiter>, now?: () => number}} [deps]
 */
export function createHandler(deps = {}) {
  const createClient = deps.createClient ?? createAnthropicClient;
  const rateLimiter = deps.rateLimiter ?? createRateLimiter();
  const now = deps.now ?? (() => Date.now());

  async function handleStory(request, env) {
    const denied = checkToken(request, env);
    if (denied) return denied;

    // 1. 요청 검증
    const body = await readJsonBody(request);
    if (!body.ok) return body.response;
    const checked = validateStoryRequest(body.value);
    if (!checked.ok) return json(checked.status, { error: checked.error });

    // 2. 호출 횟수 제한
    const limit = toPositiveInt(env.RATE_LIMIT_PER_MINUTE, DEFAULT_RATE_LIMIT_PER_MINUTE);
    if (!rateLimiter.allow(deviceKey(request), limit, now())) {
      return json(429, { error: "rate_limited" });
    }

    // 3. Claude 호출
    if (!env.ANTHROPIC_API_KEY) return json(500, { error: "server_misconfigured" });
    let text;
    try {
      text = await requestStoryText(createClient(env), env, checked.value);
    } catch (err) {
      console.error("claude_call_failed", err?.status ?? "", err?.message ?? String(err));
      return json(502, { error: "upstream_failed" });
    }

    // 4~5. 응답 검증. 파싱 실패나 규칙 위반이면 502(앱은 폴백으로 넘어간다).
    const story = validateStory(parseModelJson(text));
    if (!story.ok) {
      console.warn("model_output_rejected", story.error);
      return json(502, { error: "invalid_model_output" });
    }
    return json(200, story.value);
  }

  return {
    async fetch(request, env) {
      const { pathname } = new URL(request.url);
      if (pathname === "/" || pathname === "/story") {
        if (request.method !== "POST") return json(405, { error: "method_not_allowed" });
        return handleStory(request, env);
      }
      if (pathname === "/card") {
        if (request.method !== "POST") return json(405, { error: "method_not_allowed" });
        return handleCardUpload(request, env, { rateLimiter, now });
      }
      if (pathname.startsWith("/card/")) {
        if (request.method !== "GET" && request.method !== "HEAD") return json(405, { error: "method_not_allowed" });
        return handleCardView(pathname.slice("/card/".length), env);
      }
      if (pathname === "/sky" || pathname === "/wall") {
        if (request.method !== "GET" && request.method !== "HEAD") return json(405, { error: "method_not_allowed" });
        return pathname === "/sky" ? handleSkyList(env) : handleWall(env, new URL(request.url).origin);
      }
      return json(404, { error: "not_found" });
    },
  };
}

export default createHandler();
