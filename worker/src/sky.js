// 모두의 하늘. 방문자들이 만든 별자리의 모양과 이름을 차례로 쌓아 두고, 번호("몇 번째 별자리")를 매긴다.
//  - 카드를 올릴 때 별자리 모양(points)이 함께 오면 목록에 더한다(card.js가 부른다).
//  - GET /sky  : 앱이 먼 하늘에 그릴 목록(JSON). 누구나 읽을 수 있다.
//  - GET /wall : 전시장 모니터에 띄우는 페이지. 15초마다 새로 고친다.
// 저장은 카드와 같은 KV(CARDS)의 키 하나(sky:index)에 한다. KV에는 원자적 증가가 없어서
// 여러 기기가 동시에 올리면 번호가 겹칠 수 있지만, 전시는 기기 한 대라 문제가 되지 않는다.

import { escapeHtml, html } from "./card.js";
import { json } from "./http.js";
import { KEYWORDS } from "./rules.js";

const SKY_KEY = "sky:index";
/** 목록에 남겨 두는 최근 별자리 수. 번호(count)는 계속 올라간다. */
export const SKY_MAX_ITEMS = 80;
export const POINTS_MIN = 2;
export const POINTS_MAX = 16;
const WALL_ITEMS = 24;

/**
 * 별자리 모양 검증. 0~1 범위의 [x, y] 점 2~16개(이은 순서, 위가 +y).
 * @returns {number[][] | null} 소수 셋째 자리로 다듬은 점들. 형식이 틀리면 null.
 */
export function normalizePoints(points) {
  if (!Array.isArray(points) || points.length < POINTS_MIN || points.length > POINTS_MAX) return null;
  const result = [];
  for (const p of points) {
    if (!Array.isArray(p) || p.length !== 2) return null;
    const [x, y] = p;
    if (typeof x !== "number" || typeof y !== "number" || !Number.isFinite(x) || !Number.isFinite(y)) return null;
    if (x < 0 || x > 1 || y < 0 || y > 1) return null;
    result.push([Math.round(x * 1000) / 1000, Math.round(y * 1000) / 1000]);
  }
  return result;
}

async function readIndex(env) {
  try {
    const index = JSON.parse((await env.CARDS.get(SKY_KEY)) ?? "null");
    if (index && Number.isInteger(index.count) && Array.isArray(index.items)) return index;
  } catch {
    // 손상된 값은 빈 목록으로 다시 시작한다.
  }
  return { count: 0, items: [] };
}

/**
 * 별자리 하나를 목록에 더하고 번호를 돌려준다. 저장소가 없거나 실패하면 null(카드는 번호 없이 만든다).
 */
export async function recordConstellation(env, { name, keyword, points }) {
  if (!env.CARDS || !points) return null;
  try {
    const index = await readIndex(env);
    const number = index.count + 1;
    index.count = number;
    index.items.push({ n: number, name, keyword, points });
    if (index.items.length > SKY_MAX_ITEMS) index.items = index.items.slice(-SKY_MAX_ITEMS);
    await env.CARDS.put(SKY_KEY, JSON.stringify(index));
    return number;
  } catch (err) {
    console.error("sky_record_failed", err?.message ?? String(err));
    return null;
  }
}

/** GET /sky */
export async function handleSkyList(env) {
  if (!env.CARDS) return json(503, { error: "sky_storage_unavailable" });
  const index = await readIndex(env);
  // xy는 점들을 한 줄로 편 것([x0, y0, x1, y1, …])이다. Unity의 JSON 읽기가 겹친 배열을 못 읽어서 함께 준다.
  const items = index.items.map((item) => ({ ...item, xy: Array.isArray(item.points) ? item.points.flat() : [] }));
  return json(200, { count: index.count, items });
}

/** GET /wall — 전시장 모니터용 */
export async function handleWall(env) {
  const index = env.CARDS ? await readIndex(env) : { count: 0, items: [] };
  const latest = index.items.slice(-WALL_ITEMS).reverse();
  const cells = latest.map((item, i) => wallCell(item, i === 0)).join("\n");
  const body = `<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta http-equiv="refresh" content="15">
<meta name="robots" content="noindex">
<title>모두의 하늘 · Constellation</title>
<style>${WALL_STYLE}</style>
</head>
<body>
<header>
<h1>모두의 하늘</h1>
<p class="count">${index.count > 0 ? `지금까지 <b>${index.count}</b>개의 별자리가 떴어요` : "첫 번째 별자리를 기다리고 있어요"}</p>
</header>
<main class="grid">
${cells}
</main>
<footer>Constellation · One star, One story</footer>
</body>
</html>`;
  return html(200, body, "no-store");
}

function wallCell(item, newest) {
  const points = normalizePoints(item.points) ?? [];
  // 화면은 아래가 +y라 세로를 뒤집고, 가장자리에 여백을 둔다.
  const at = ([x, y]) => `${(8 + x * 84).toFixed(1)},${(8 + (1 - y) * 84).toFixed(1)}`;
  const line = points.map(at).join(" ");
  const stars = points.map((p) => { const [cx, cy] = at(p).split(","); return `<circle cx="${cx}" cy="${cy}" r="2.2"/>`; }).join("");
  const keyword = KEYWORDS[item.keyword] ?? "";
  const number = Number.isInteger(item.n) ? item.n : 0;
  return `<article class="cell${newest ? " newest" : ""}">
<svg viewBox="0 0 100 100" role="img" aria-label="${escapeHtml(String(item.name ?? ""))}"><polyline points="${line}"/>${stars}</svg>
<h2>${escapeHtml(String(item.name ?? ""))}</h2>
<p>${number}번째${keyword ? ` · ${escapeHtml(keyword)}` : ""}</p>
</article>`;
}

const WALL_STYLE = `
  * { box-sizing: border-box; }
  html, body { margin: 0; min-height: 100%; }
  body {
    padding: 3vh 3vw; color: #f5f2ff; text-align: center;
    background: radial-gradient(ellipse at 50% 120%, #2f2163 0%, #0d0b2b 45%, #030513 100%) fixed;
    font-family: -apple-system, "Apple SD Gothic Neo", "Noto Sans KR", "Malgun Gothic", sans-serif;
  }
  h1 { margin: 0; font-size: 5vh; letter-spacing: 0.2em; color: #ffe094; font-weight: 700; }
  .count { margin: 1vh 0 3vh; font-size: 2.4vh; color: #c9c3f0; }
  .count b { color: #fff; font-size: 3.2vh; }
  .grid { display: grid; grid-template-columns: repeat(6, 1fr); gap: 2vh 1.5vw; }
  .cell { padding: 1vh 0.5vw 1.4vh; border-radius: 2vh; }
  .cell svg { width: 100%; max-height: 17vh; }
  .cell polyline { fill: none; stroke: #ffe094; stroke-width: 1.3; stroke-linecap: round; stroke-linejoin: round; opacity: 0.85; }
  .cell circle { fill: #fff; }
  .cell h2 { margin: 0.6vh 0 0.2vh; font-size: 2.3vh; font-weight: 600; word-break: keep-all; }
  .cell p { margin: 0; font-size: 1.5vh; color: #8f89bd; }
  .newest { background: rgba(255, 224, 148, 0.09); box-shadow: 0 0 6vh rgba(255, 224, 148, 0.25); }
  .newest h2 { color: #ffe094; }
  footer { margin-top: 3vh; font-size: 1.4vh; letter-spacing: 0.2em; color: #6e689c; }
  @media (max-width: 900px) { .grid { grid-template-columns: repeat(3, 1fr); } }
`;
