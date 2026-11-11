import Anthropic from "@anthropic-ai/sdk";
import { buildPrompt, STORY_SCHEMA } from "./prompt.js";

export const DEFAULT_MODEL = "claude-haiku-5-5";
const DEFAULT_EFFORT = "low";
const DEFAULT_TIMEOUT_MS = 9000; // 앱이 10초에 끊으므로 그보다 먼저 포기한다.
const MAX_TOKENS = 1024;

/** 실제 Anthropic 클라이언트를 만든다. 테스트에서는 가짜로 바꿔 끼운다. */
export function createAnthropicClient(env) {
  return new Anthropic({
    apiKey: env.ANTHROPIC_API_KEY,
    maxRetries: 0, // 재시도하면 대기 시간이 길어진다(명세: 재시도 없음).
    timeout: toPositiveInt(env.CLAUDE_TIMEOUT_MS, DEFAULT_TIMEOUT_MS),
  });
}

/**
 * Claude에 별자리 이미지를 보내고 모델이 낸 텍스트를 돌려준다.
 * 실패(네트워크, 거절, 빈 답)는 예외로 던진다. 호출한 쪽에서 502로 바꾼다.
 */
export async function requestStoryText(client, env, { keyword, starCount, image }) {
  const response = await client.messages.create({
    model: env.CLAUDE_MODEL || DEFAULT_MODEL,
    max_tokens: MAX_TOKENS,
    output_config: {
      effort: env.CLAUDE_EFFORT || DEFAULT_EFFORT,
      format: { type: "json_schema", schema: STORY_SCHEMA },
    },
    messages: [
      {
        role: "user",
        content: [
          { type: "image", source: { type: "base64", media_type: "image/jpeg", data: image } },
          { type: "text", text: buildPrompt(keyword, starCount) },
        ],
      },
    ],
  });

  if (response.stop_reason === "refusal" || response.stop_reason === "max_tokens") {
    throw new Error(`model_stop_${response.stop_reason}`);
  }
  const block = Array.isArray(response.content) ? response.content.find((b) => b.type === "text") : null;
  if (!block || typeof block.text !== "string" || block.text.length === 0) {
    throw new Error("model_empty");
  }
  return block.text;
}

/** 모델 텍스트에서 JSON 객체를 꺼낸다. 앞뒤에 군말이나 코드 펜스가 붙어도 견딘다. */
export function parseModelJson(text) {
  try {
    return JSON.parse(text);
  } catch {
    const start = text.indexOf("{");
    const end = text.lastIndexOf("}");
    if (start === -1 || end <= start) return null;
    try {
      return JSON.parse(text.slice(start, end + 1));
    } catch {
      return null;
    }
  }
}

export function toPositiveInt(value, fallback) {
  const n = Number.parseInt(value, 10);
  return Number.isFinite(n) && n > 0 ? n : fallback;
}
