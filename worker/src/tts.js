// 이야기 낭독(TTS). Quest 앱 → 이 Worker → Google Cloud Text-to-Speech.
// API 키는 Worker 시크릿(GOOGLE_TTS_API_KEY)에만 둔다. 만든 음성은 저장하지 않고 그대로 돌려준다.

import { checkToken, deviceKey, json, readJsonBody } from "./http.js";
import { toPositiveInt } from "./claude.js";

const ENDPOINT = "https://texttospeech.googleapis.com/v1/text:synthesize";
/** 낭독할 글 길이 범위. 이야기 규칙(최대 200자)보다 조금 넉넉하다. */
export const TTS_TEXT_MIN_CHARS = 2;
export const TTS_TEXT_MAX_CHARS = 260;
const TTS_BODY_MAX_CHARS = 4096;
const DEFAULT_VOICE = "ko-KR-Neural2-A";
const DEFAULT_RATE = 0.92;
const DEFAULT_TIMEOUT_MS = 8000;
const DEFAULT_LIMIT_PER_MINUTE = 6;
const SAMPLE_RATE = 24000;

/** 앱 → Worker 낭독 요청 검증. */
export function validateTtsRequest(body) {
  if (body === null || typeof body !== "object" || Array.isArray(body)) {
    return { ok: false, status: 400, error: "invalid_body" };
  }
  if (typeof body.text !== "string") return { ok: false, status: 400, error: "invalid_text" };
  const text = body.text.replace(/\s+/g, " ").trim();
  if (text.length < TTS_TEXT_MIN_CHARS || text.length > TTS_TEXT_MAX_CHARS) {
    return { ok: false, status: 400, error: "invalid_text" };
  }
  return { ok: true, value: { text } };
}

function base64ToBytes(b64) {
  const bin = atob(b64);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return bytes;
}

/**
 * POST /tts  { text } → audio/wav (LINEAR16, 24kHz)
 * @param {{rateLimiter: any, now: () => number, fetchImpl?: typeof fetch}} deps
 */
export async function handleTts(request, env, deps) {
  const denied = checkToken(request, env);
  if (denied) return denied;

  const body = await readJsonBody(request, TTS_BODY_MAX_CHARS);
  if (!body.ok) return body.response;
  const checked = validateTtsRequest(body.value);
  if (!checked.ok) return json(checked.status, { error: checked.error });

  const limit = toPositiveInt(env.TTS_RATE_LIMIT_PER_MINUTE, DEFAULT_LIMIT_PER_MINUTE);
  if (!deps.rateLimiter.allow(`tts:${deviceKey(request)}`, limit, deps.now())) {
    return json(429, { error: "rate_limited" });
  }

  if (!env.GOOGLE_TTS_API_KEY) return json(500, { error: "server_misconfigured" });

  // 속도: TTS_SPEAKING_RATE가 0.5~1.5면 적용하고, "0"이나 빈 값이면 보내지 않는다(음성이 속도 조절을 지원하지 않을 때).
  const rawRate = env.TTS_SPEAKING_RATE;
  const rate = Number.parseFloat(rawRate ?? "");
  let speakingRate = DEFAULT_RATE;
  if (rawRate !== undefined && (rawRate === "" || rate === 0)) speakingRate = undefined;
  else if (Number.isFinite(rate) && rate >= 0.5 && rate <= 1.5) speakingRate = rate;
  const audioConfig = { audioEncoding: "LINEAR16", sampleRateHertz: SAMPLE_RATE };
  if (speakingRate !== undefined) audioConfig.speakingRate = speakingRate;
  const payload = {
    input: { text: checked.value.text },
    voice: { languageCode: "ko-KR", name: env.TTS_VOICE || DEFAULT_VOICE },
    audioConfig,
  };

  const doFetch = deps.fetchImpl ?? fetch;
  const timeoutMs = toPositiveInt(env.TTS_TIMEOUT_MS, DEFAULT_TIMEOUT_MS);
  let audio;
  try {
    const res = await doFetch(ENDPOINT, {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-Goog-Api-Key": env.GOOGLE_TTS_API_KEY },
      body: JSON.stringify(payload),
      signal: AbortSignal.timeout(timeoutMs),
    });
    if (!res.ok) {
      console.error("tts_call_failed", res.status);
      return json(502, { error: "upstream_failed" });
    }
    const data = await res.json();
    if (typeof data?.audioContent !== "string" || data.audioContent.length === 0) {
      return json(502, { error: "invalid_upstream_output" });
    }
    audio = base64ToBytes(data.audioContent);
  } catch (err) {
    console.error("tts_call_failed", err?.name ?? "", err?.message ?? String(err));
    return json(502, { error: "upstream_failed" });
  }

  return new Response(audio, {
    status: 200,
    headers: { "Content-Type": "audio/wav", "Cache-Control": "no-store" },
  });
}
