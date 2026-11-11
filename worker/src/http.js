import { BODY_MAX_CHARS } from "./rules.js";

export function json(status, body) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" },
  });
}

/** 길이가 같은 두 문자열을 시간 차 없이 비교한다. */
export function timingSafeEqual(a, b) {
  if (typeof a !== "string" || typeof b !== "string" || a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

/**
 * 공유 토큰 헤더 검사. 통과하면 null, 아니면 돌려줄 응답.
 * 토큰 값은 시크릿(APP_SHARED_TOKEN)으로만 읽는다.
 */
export function checkToken(request, env) {
  if (!env.APP_SHARED_TOKEN) return json(500, { error: "server_misconfigured" });
  const token = request.headers.get("X-App-Token") ?? "";
  if (!timingSafeEqual(token, env.APP_SHARED_TOKEN)) return json(401, { error: "unauthorized" });
  return null;
}

/**
 * 본문을 읽어 JSON으로 파싱한다.
 * @returns {Promise<{ok: true, value: any} | {ok: false, response: Response}>}
 */
export async function readJsonBody(request, maxChars = BODY_MAX_CHARS) {
  const declared = Number.parseInt(request.headers.get("Content-Length") ?? "", 10);
  if (Number.isFinite(declared) && declared > maxChars) {
    return { ok: false, response: json(413, { error: "body_too_large" }) };
  }
  let text;
  try {
    text = await request.text();
  } catch {
    return { ok: false, response: json(400, { error: "invalid_body" }) };
  }
  if (text.length > maxChars) return { ok: false, response: json(413, { error: "body_too_large" }) };
  try {
    return { ok: true, value: JSON.parse(text) };
  } catch {
    return { ok: false, response: json(400, { error: "invalid_json" }) };
  }
}

/** 호출 제한에 쓸 기기 식별자. 앱이 보내는 X-Device-Id가 없으면 접속 IP를 쓴다. */
export function deviceKey(request) {
  const id = (request.headers.get("X-Device-Id") ?? "").trim();
  if (/^[A-Za-z0-9_-]{4,64}$/.test(id)) return `d:${id}`;
  return `ip:${request.headers.get("CF-Connecting-IP") ?? "unknown"}`;
}
