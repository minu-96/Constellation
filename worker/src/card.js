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

  const id = newCardId();
  // 모두의 하늘에 더하고 번호를 받는다. 실패해도 카드는 번호 없이 만든다.
  // 카드 id도 함께 남겨 모니터 페이지가 QR을 그릴 수 있게 한다.
  const number = await recordConstellation(env, { name: story.value.name, keyword: input.keyword, points, id });

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
  // 저장 버튼의 스크립트만 실행되도록 응답마다 다른 nonce를 붙인다.
  const nonce = newNonce();
  return html(200, cardPage(record, nonce), "private, max-age=600", nonce);
}

export function escapeHtml(text) {
  return String(text)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#39;");
}

export function html(status, body, cacheControl, scriptNonce) {
  return new Response(body, {
    status,
    headers: {
      "Content-Type": "text/html; charset=utf-8",
      // 외부 자원을 모두 막는다. 이미지는 data: 주소만 쓰고, 스크립트는 nonce가 붙은 것(카드 저장 버튼)만 허용한다.
      "Content-Security-Policy":
        "default-src 'none'; img-src data: blob:; style-src 'unsafe-inline'" + (scriptNonce ? `; script-src 'nonce-${scriptNonce}'` : ""),
      "X-Content-Type-Options": "nosniff",
      "Referrer-Policy": "no-referrer",
      "Cache-Control": cacheControl ?? (status === 200 ? "public, max-age=3600" : "no-store"),
    },
  });
}

function newNonce() {
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  let text = "";
  for (const b of bytes) text += b.toString(16).padStart(2, "0");
  return text;
}

// 별이 흩어진 밤하늘 배경. 작은 점들을 그러데이션으로 찍는다(이미지 파일 없음).
const STAR_DOTS = [
  [8, 12, 1.2], [22, 6, 0.8], [35, 18, 1], [48, 9, 0.7], [63, 15, 1.1], [77, 7, 0.8], [90, 20, 1],
  [14, 34, 0.7], [29, 42, 1], [56, 38, 0.8], [71, 30, 1.2], [86, 44, 0.7], [5, 58, 1], [19, 66, 0.8],
  [41, 61, 0.7], [67, 70, 1], [82, 63, 0.9], [94, 76, 0.7], [11, 84, 0.9], [33, 90, 0.7], [52, 82, 1.1],
  [74, 92, 0.8], [89, 88, 1],
].map(([x, y, r]) => `radial-gradient(circle at ${x}% ${y}%, rgba(255,255,255,0.9) 0, rgba(255,255,255,0) ${r}px)`).join(", ");

const PAGE_STYLE = `
  * { box-sizing: border-box; }
  html { margin: 0; background: #130f3a; }
  body {
    margin: 0; min-height: 100vh;
    display: flex; flex-direction: column; align-items: center; justify-content: center; padding: 24px 16px 36px;
    background: ${STAR_DOTS}, radial-gradient(ellipse at 50% 115%, #3a2a78 0%, #130f3a 45%, #030513 100%);
    color: #f5f2ff; font-family: -apple-system, "Apple SD Gothic Neo", "Noto Sans KR", "Malgun Gothic", sans-serif;
  }
  .card {
    position: relative; width: 100%; max-width: 420px; padding: 30px 26px 24px; border-radius: 28px; text-align: center;
    background: linear-gradient(180deg, rgba(14, 16, 46, 0.86) 0%, rgba(10, 10, 34, 0.92) 100%);
    border: 1px solid rgba(255, 224, 150, 0.35);
    box-shadow: 0 0 0 6px rgba(255, 224, 150, 0.05), 0 0 70px rgba(120, 110, 255, 0.3);
  }
  .card::before, .card::after {
    content: "✦"; position: absolute; top: 14px; font-size: 14px; color: rgba(255, 224, 150, 0.7);
  }
  .card::before { left: 18px; }
  .card::after { right: 18px; }
  .number {
    display: inline-block; margin: 0 0 10px; padding: 5px 16px; border-radius: 999px; font-size: 14px; font-weight: 700;
    letter-spacing: 0.06em; color: #1a1440; background: linear-gradient(90deg, #ffe094, #ffc978);
  }
  .keyword { margin: 0 0 16px; font-size: 14px; letter-spacing: 0.1em; color: #c9c3f0; }
  .frame {
    padding: 6px; border-radius: 22px;
    background: linear-gradient(135deg, rgba(255, 224, 150, 0.55), rgba(140, 130, 255, 0.35) 55%, rgba(255, 224, 150, 0.4));
  }
  .frame img { width: 100%; aspect-ratio: 1 / 1; border-radius: 17px; display: block; }
  h1 {
    margin: 22px 0 6px; font-size: 36px; font-weight: 800; color: #ffe094; word-break: keep-all;
    text-shadow: 0 0 22px rgba(255, 224, 150, 0.45);
  }
  .rule { margin: 0 auto 14px; width: 54px; height: 1px; background: rgba(255, 224, 150, 0.55); }
  .story { margin: 0; font-size: 18px; line-height: 1.8; word-break: keep-all; }
  .date { margin: 20px 0 0; font-size: 13px; color: #a8a2d6; }
  .footer { margin: 6px 0 0; font-size: 12px; letter-spacing: 0.14em; color: #8f89bd; }
  .empty { font-size: 18px; line-height: 1.7; }
  .save {
    display: none; margin: 22px 0 0; padding: 14px 30px; border: 0; border-radius: 999px; font: inherit; font-size: 17px; font-weight: 700;
    color: #1a1440; background: linear-gradient(90deg, #ffe094, #ffc978); box-shadow: 0 6px 24px rgba(255, 201, 120, 0.3);
  }
  .save.ready { display: inline-block; }
  .tip { margin: 10px 0 0; min-height: 18px; font-size: 13px; color: #a8a2d6; }
`;

function page(title, content, after = "") {
  return `<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex">
<meta name="theme-color" content="#030513">
<title>${escapeHtml(title)}</title>
<style>${PAGE_STYLE}</style>
</head>
<body>
<main class="card" id="card">
${content}
<p class="footer">Constellation · One star, One story</p>
</main>
${after}
</body>
</html>`;
}

// "2026-12-03T05:12:00.000Z" → "2026.12.03" (한국 시간 기준)
function formatDate(iso) {
  const time = Date.parse(iso);
  if (!Number.isFinite(time)) return "";
  const local = new Date(time + 9 * 60 * 60 * 1000);
  const pad = (n) => String(n).padStart(2, "0");
  return `${local.getUTCFullYear()}.${pad(local.getUTCMonth() + 1)}.${pad(local.getUTCDate())}`;
}

function cardPage(record, nonce) {
  const keyword = KEYWORDS[record.keyword] ?? "";
  // 이미지는 저장할 때 base64 글자만 통과시켰지만, 한 번 더 걸러 속성 밖으로 새지 않게 한다.
  const image = typeof record.image === "string" && /^[A-Za-z0-9+/=]+$/.test(record.image) ? record.image : "";
  const number = Number.isInteger(record.number) && record.number > 0 ? record.number : 0;
  const date = formatDate(record.createdAt);
  return page(
    `${record.name} · Constellation`,
    `${number ? `<p class="number" id="number">${number}번째 별자리</p>` : ""}
${keyword ? `<p class="keyword" id="keyword">${escapeHtml(keyword)}</p>` : ""}
${image ? `<div class="frame"><img id="image" src="data:image/jpeg;base64,${image}" alt="내가 그린 별자리"></div>` : ""}
<h1 id="name">${escapeHtml(record.name)}</h1>
<div class="rule"></div>
<p class="story" id="story">${escapeHtml(record.story)}</p>
${date ? `<p class="date" id="date">${date}</p>` : ""}`,
    `<button class="save" id="save" type="button">카드 이미지로 저장</button>
<p class="tip" id="tip"></p>
<script nonce="${nonce}">${SAVE_SCRIPT}</script>`,
  );
}

// 카드를 한 장의 그림(1080×1350)으로 그려 저장하거나 공유한다.
// 내용은 페이지에 이미 나온 글자와 그림에서 읽는다(서버 값을 스크립트에 끼워 넣지 않는다).
const SAVE_SCRIPT = `
(function () {
  var button = document.getElementById("save");
  var tip = document.getElementById("tip");
  if (!button || !document.createElement("canvas").getContext) return;
  var text = function (id) { var el = document.getElementById(id); return el ? el.textContent : ""; };
  var FONT = '-apple-system, "Apple SD Gothic Neo", "Noto Sans KR", "Malgun Gothic", sans-serif';

  function roundRect(c, x, y, w, h, r) {
    c.beginPath(); c.moveTo(x + r, y); c.arcTo(x + w, y, x + w, y + h, r); c.arcTo(x + w, y + h, x, y + h, r);
    c.arcTo(x, y + h, x, y, r); c.arcTo(x, y, x + w, y, r); c.closePath();
  }
  function wrap(c, value, maxWidth) {
    var words = value.split(" "), lines = [], line = "";
    for (var i = 0; i < words.length; i++) {
      var next = line ? line + " " + words[i] : words[i];
      if (c.measureText(next).width <= maxWidth || !line) line = next;
      else { lines.push(line); line = words[i]; }
    }
    if (line) lines.push(line);
    return lines;
  }
  function draw() {
    var W = 1080, H = 1350, canvas = document.createElement("canvas");
    canvas.width = W; canvas.height = H;
    var c = canvas.getContext("2d");
    var sky = c.createRadialGradient(W / 2, H * 1.15, 0, W / 2, H * 1.15, H * 1.25);
    sky.addColorStop(0, "#3a2a78"); sky.addColorStop(0.45, "#130f3a"); sky.addColorStop(1, "#030513");
    c.fillStyle = sky; c.fillRect(0, 0, W, H);
    var seed = 7;
    for (var i = 0; i < 140; i++) {
      seed = (seed * 16807) % 2147483647; var x = seed % W;
      seed = (seed * 16807) % 2147483647; var y = seed % H;
      seed = (seed * 16807) % 2147483647; var r = 0.6 + (seed % 100) / 60;
      c.globalAlpha = 0.25 + (seed % 60) / 100; c.fillStyle = "#fff";
      c.beginPath(); c.arc(x, y, r, 0, 6.2832); c.fill();
    }
    c.globalAlpha = 1;
    c.fillStyle = "rgba(10, 10, 34, 0.82)"; roundRect(c, 70, 70, W - 140, H - 140, 56); c.fill();
    c.strokeStyle = "rgba(255, 224, 150, 0.45)"; c.lineWidth = 2; c.stroke();

    c.textAlign = "center"; c.textBaseline = "middle";
    var y0 = 150, number = text("number"), keyword = text("keyword");
    if (number) {
      c.font = "700 34px " + FONT; var w = c.measureText(number).width + 64;
      var badge = c.createLinearGradient(W / 2 - w / 2, 0, W / 2 + w / 2, 0);
      badge.addColorStop(0, "#ffe094"); badge.addColorStop(1, "#ffc978");
      c.fillStyle = badge; roundRect(c, W / 2 - w / 2, y0 - 30, w, 60, 30); c.fill();
      c.fillStyle = "#1a1440"; c.fillText(number, W / 2, y0 + 2); y0 += 62;
    }
    if (keyword) { c.font = "400 30px " + FONT; c.fillStyle = "#c9c3f0"; c.fillText(keyword, W / 2, y0); }

    var image = document.getElementById("image"), size = 560, top = 250, left = (W - size) / 2;
    c.fillStyle = "rgba(255, 224, 150, 0.5)"; roundRect(c, left - 10, top - 10, size + 20, size + 20, 44); c.fill();
    if (image && image.complete && image.naturalWidth) {
      c.save(); roundRect(c, left, top, size, size, 36); c.clip(); c.drawImage(image, left, top, size, size); c.restore();
    }

    c.fillStyle = "#ffe094"; c.font = "800 78px " + FONT; c.shadowColor = "rgba(255, 224, 150, 0.5)"; c.shadowBlur = 30;
    c.fillText(text("name"), W / 2, top + size + 95); c.shadowBlur = 0;
    c.fillRect(W / 2 - 60, top + size + 150, 120, 2);

    c.fillStyle = "#f5f2ff"; c.font = "400 38px " + FONT;
    var lines = wrap(c, text("story"), W - 300), lineY = top + size + 205;
    for (var k = 0; k < lines.length && k < 6; k++) c.fillText(lines[k], W / 2, lineY + k * 58);

    c.font = "400 26px " + FONT; c.fillStyle = "#a8a2d6"; c.fillText(text("date"), W / 2, H - 165);
    c.fillStyle = "#8f89bd"; c.fillText("Constellation · One star, One story", W / 2, H - 120);
    return canvas;
  }

  button.className = "save ready";
  button.addEventListener("click", function () {
    var canvas = draw();
    canvas.toBlob(function (blob) {
      if (!blob) { tip.textContent = "저장하지 못했어요. 화면을 캡처해 주세요."; return; }
      var name = "constellation-" + (text("name") || "card") + ".png";
      var file = null;
      try { file = new File([blob], name, { type: "image/png" }); } catch (e) { file = null; }
      if (file && navigator.canShare && navigator.canShare({ files: [file] })) {
        navigator.share({ files: [file], title: text("name") }).catch(function () {});
        return;
      }
      var url = URL.createObjectURL(blob), link = document.createElement("a");
      link.href = url; link.download = name; document.body.appendChild(link); link.click(); link.remove();
      setTimeout(function () { URL.revokeObjectURL(url); }, 4000);
      tip.textContent = "카드를 저장했어요.";
    }, "image/png");
  });
})();
`;

function notFoundPage() {
  return page("Constellation", `<p class="empty">카드를 찾을 수 없어요.<br>보관 기간이 지났거나 주소가 잘못되었어요.</p>`);
}
