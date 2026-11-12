// 결과 카드(F-12). 앱이 이름·이야기·별자리 이미지를 올리면 KV에 저장하고 주소를 돌려준다.
// 휴대폰으로 QR을 찍어 그 주소를 열면 카드 페이지를 보여 준다.
// KV 네임스페이스(CARDS)가 연결되지 않았으면 503을 돌려주고, 앱은 카드 없이 체험을 끝낸다.

import { toPositiveInt } from "./claude.js";
import { checkToken, deviceKey, json, readJsonBody } from "./http.js";
import { KEYWORDS } from "./rules.js";
import { normalizePoints, recordConstellation } from "./sky.js";
import { validateJpegBase64, validateStory } from "./validate.js";

const DEFAULT_CARD_TTL_DAYS = 30;
const DEFAULT_RATE_LIMIT_PER_MINUTE = 6;
const ID_LENGTH = 12;
// 헷갈리는 글자(0, o, 1, l)를 뺀 소문자와 숫자
const ID_ALPHABET = "abcdefghijkmnpqrstuvwxyz23456789";
const ID_RE = new RegExp(`^[${ID_ALPHABET}]{${ID_LENGTH}}$`);

export function newCardId() {
  const bytes = crypto.getRandomValues(new Uint8Array(ID_LENGTH));
  let id = "";
  for (const b of bytes) id += ID_ALPHABET[b % ID_ALPHABET.length];
  return id;
}

/** POST /card */
export async function handleCardUpload(request, env, { rateLimiter, now }) {
  const denied = checkToken(request, env);
  if (denied) return denied;

  const body = await readJsonBody(request);
  if (!body.ok) return body.response;
  const input = body.value;
  if (input === null || typeof input !== "object" || Array.isArray(input)) {
    return json(400, { error: "invalid_body" });
  }
  if (typeof input.keyword !== "string" || !Object.hasOwn(KEYWORDS, input.keyword)) {
    return json(400, { error: "invalid_keyword" });
  }
  const story = validateStory(input);
  if (!story.ok) return json(400, { error: `invalid_story_${story.error}` });
  const image = validateJpegBase64(input.image);
  if (!image.ok) return json(image.status, { error: image.error });

  const limit = toPositiveInt(env.RATE_LIMIT_PER_MINUTE, DEFAULT_RATE_LIMIT_PER_MINUTE);
  if (!rateLimiter.allow(`card:${deviceKey(request)}`, limit, now())) {
    return json(429, { error: "rate_limited" });
  }

  // 별자리 모양은 선택 항목이다. 있으면 형식을 확인하고, 틀리면 요청을 돌려보낸다.
  let points = null;
  if (input.points !== undefined && input.points !== null) {
    points = normalizePoints(input.points);
    if (!points) return json(400, { error: "invalid_points" });
  }

  if (!env.CARDS) return json(503, { error: "card_storage_unavailable" });

  // 모두의 하늘에 더하고 번호를 받는다. 실패해도 카드는 번호 없이 만든다.
  const number = await recordConstellation(env, { name: story.value.name, keyword: input.keyword, points });

  const id = newCardId();
  const record = {
    keyword: input.keyword,
    name: story.value.name,
    story: story.value.story,
    image: input.image,
    number,
    createdAt: new Date(now()).toISOString(),
  };
  const ttlDays = toPositiveInt(env.CARD_TTL_DAYS, DEFAULT_CARD_TTL_DAYS);
  try {
    await env.CARDS.put(`card:${id}`, JSON.stringify(record), { expirationTtl: ttlDays * 24 * 60 * 60 });
  } catch (err) {
    console.error("card_store_failed", err?.message ?? String(err));
    return json(503, { error: "card_storage_unavailable" });
  }

  return json(200, { id, url: `${new URL(request.url).origin}/card/${id}`, number });
}

/** GET /card/:id — 누구나 열 수 있다(주소를 아는 사람만). */
export async function handleCardView(id, env) {
  if (!ID_RE.test(id) || !env.CARDS) return html(404, notFoundPage());
  let record = null;
  try {
    record = JSON.parse((await env.CARDS.get(`card:${id}`)) ?? "null");
  } catch {
    record = null;
  }
  if (!record || typeof record.name !== "string" || typeof record.story !== "string") {
    return html(404, notFoundPage());
  }
  return html(200, cardPage(record));
}

export function escapeHtml(text) {
  return String(text)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

export function html(status, body, cacheControl) {
  return new Response(body, {
    status,
    headers: {
      "Content-Type": "text/html; charset=utf-8",
      // 스크립트와 외부 자원을 모두 막는다. 이미지는 data: 주소만 쓴다.
      "Content-Security-Policy": "default-src 'none'; img-src data:; style-src 'unsafe-inline'",
      "X-Content-Type-Options": "nosniff",
      "Referrer-Policy": "no-referrer",
      "Cache-Control": cacheControl ?? (status === 200 ? "public, max-age=3600" : "no-store"),
    },
  });
}

const PAGE_STYLE = `
  * { box-sizing: border-box; }
  html, body { margin: 0; min-height: 100%; }
  body {
    display: flex; align-items: center; justify-content: center; padding: 24px 16px;
    background: linear-gradient(180deg, #030513 0%, #0d0b2b 55%, #291c5c 100%);
    color: #f5f2ff; font-family: -apple-system, "Apple SD Gothic Neo", "Noto Sans KR", "Malgun Gothic", sans-serif;
  }
  .card {
    width: 100%; max-width: 420px; padding: 28px 24px 24px; border-radius: 24px; text-align: center;
    background: rgba(8, 10, 32, 0.72); border: 1px solid rgba(255, 224, 150, 0.28);
    box-shadow: 0 0 60px rgba(120, 110, 255, 0.25);
  }
  .card img { width: 100%; aspect-ratio: 1 / 1; border-radius: 16px; display: block; }
  .number { margin: 0 0 6px; font-size: 15px; font-weight: 600; letter-spacing: 0.06em; color: #ffe094; }
  .keyword { margin: 0 0 14px; font-size: 14px; letter-spacing: 0.08em; color: #c9c3f0; }
  h1 { margin: 22px 0 12px; font-size: 34px; font-weight: 700; color: #ffe094; word-break: keep-all; }
  .story { margin: 0; font-size: 18px; line-height: 1.75; word-break: keep-all; }
  .footer { margin: 26px 0 0; font-size: 12px; letter-spacing: 0.12em; color: #8f89bd; }
  .empty { font-size: 18px; line-height: 1.7; }
`;

function page(title, content) {
  return `<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex">
<title>${escapeHtml(title)}</title>
<style>${PAGE_STYLE}</style>
</head>
<body>
<main class="card">
${content}
<p class="footer">Constellation · One star, One story</p>
</main>
</body>
</html>`;
}

function cardPage(record) {
  const keyword = KEYWORDS[record.keyword] ?? "";
  // 이미지는 저장할 때 base64 글자만 통과시켰지만, 한 번 더 걸러 속성 밖으로 새지 않게 한다.
  const image = typeof record.image === "string" && /^[A-Za-z0-9+/=]+$/.test(record.image) ? record.image : "";
  const number = Number.isInteger(record.number) && record.number > 0 ? record.number : 0;
  return page(
    `${record.name} · Constellation`,
    `${number ? `<p class="number">${number}번째 별자리</p>` : ""}
${keyword ? `<p class="keyword">${escapeHtml(keyword)}</p>` : ""}
${image ? `<img src="data:image/jpeg;base64,${image}" alt="내가 그린 별자리">` : ""}
<h1>${escapeHtml(record.name)}</h1>
<p class="story">${escapeHtml(record.story)}</p>`,
  );
}

function notFoundPage() {
  return page("Constellation", `<p class="empty">카드를 찾을 수 없어요.<br>보관 기간이 지났거나 주소가 잘못되었어요.</p>`);
}
