// 운영자 페이지(/admin). 전시 중에 별자리를 지우거나, QR을 다시 띄우거나, 전부 지운다.
//  - 비밀번호는 Worker 시크릿 ADMIN_PASSWORD에만 둔다. 없으면 페이지가 열리지 않는다.
//  - 로그인하면 12시간짜리 서명 쿠키를 준다(값에 비밀번호는 들어가지 않는다).
//  - 지우기 요청은 POST만 받고, 같은 사이트에서 온 요청인지(Origin) 확인한다.

import { escapeHtml, html as baseHtml } from "./card.js";
import { timingSafeEqual } from "./http.js";
import { encodeQr, qrSvg } from "./qr.js";
import { KEYWORDS } from "./rules.js";
import { SKY_KEY, readIndex, shapeSvg } from "./sky.js";

const COOKIE = "cadmin";

// 운영자 페이지는 폼을 보낼 때 Origin이 제대로 붙도록 Referrer-Policy를 same-origin으로 둔다.
function html(status, body, cacheControl) {
  const response = baseHtml(status, body, cacheControl);
  response.headers.set("Referrer-Policy", "same-origin");
  return response;
}
const SESSION_SECONDS = 12 * 60 * 60;
const LOGIN_LIMIT_PER_MINUTE = 5;
const CARD_ID_RE = /^[a-z0-9]{6,32}$/;

async function hmacHex(secret, text) {
  const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const sig = new Uint8Array(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(text)));
  return Array.from(sig, (b) => b.toString(16).padStart(2, "0")).join("");
}

async function sessionValue(env, expires) {
  return `${expires}.${await hmacHex(env.ADMIN_PASSWORD, `admin:${expires}`)}`;
}

async function isLoggedIn(request, env, nowMs) {
  const cookie = request.headers.get("Cookie") ?? "";
  const match = cookie.split(/;\s*/).find((c) => c.startsWith(`${COOKIE}=`));
  if (!match) return false;
  const value = match.slice(COOKIE.length + 1);
  const [expText] = value.split(".");
  const expires = Number.parseInt(expText, 10);
  if (!Number.isFinite(expires) || expires * 1000 < nowMs) return false;
  return timingSafeEqual(value, await sessionValue(env, expires));
}

// 다른 사이트에서 몰래 보낸 POST를 막는다. 브라우저는 POST에 Origin을 붙인다.
// 페이지에 Referrer-Policy: no-referrer가 붙으면 브라우저가 Origin을 "null"로 보내므로,
// 브라우저가 붙이는 Sec-Fetch-Site(same-origin)도 같은 사이트의 증거로 받는다.
function sameOrigin(request) {
  if (request.headers.get("Sec-Fetch-Site") === "same-origin") return true;
  const origin = request.headers.get("Origin");
  return origin !== null && origin === new URL(request.url).origin;
}

function redirect(location, setCookie) {
  const headers = { Location: location, "Cache-Control": "no-store" };
  if (setCookie) headers["Set-Cookie"] = setCookie;
  return new Response(null, { status: 303, headers });
}

async function readForm(request) {
  try {
    const text = await request.text();
    if (text.length > 2048) return null;
    return new URLSearchParams(text);
  } catch {
    return null;
  }
}

/**
 * /admin 아래 모든 요청.
 * @param {{rateLimiter: any, now: () => number}} deps
 */
export async function handleAdmin(request, env, pathname, deps) {
  if (!env.ADMIN_PASSWORD) return html(404, adminPage("운영자", `<p>운영자 비밀번호(ADMIN_PASSWORD)가 등록되지 않았어요.</p>`), "no-store");
  const method = request.method;
  const nowMs = deps.now();

  if (pathname === "/admin/login") {
    if (method !== "POST") return redirect("/admin");
    if (!sameOrigin(request)) return html(403, adminPage("운영자", "<p>잘못된 요청이에요.</p>"), "no-store");
    const ip = request.headers.get("CF-Connecting-IP") ?? "unknown";
    if (!deps.rateLimiter.allow(`admin:${ip}`, LOGIN_LIMIT_PER_MINUTE, nowMs)) {
      return html(429, loginPage("잠시 뒤에 다시 시도해 주세요."), "no-store");
    }
    const form = await readForm(request);
    const password = form?.get("password") ?? "";
    // 길이가 달라도 시간 차가 나지 않게 해시끼리 비교한다.
    const ok = timingSafeEqual(await hmacHex("pw", password), await hmacHex("pw", env.ADMIN_PASSWORD));
    if (!ok) return html(401, loginPage("비밀번호가 맞지 않아요."), "no-store");
    const expires = Math.floor(nowMs / 1000) + SESSION_SECONDS;
    const cookie = `${COOKIE}=${await sessionValue(env, expires)}; Path=/admin; Max-Age=${SESSION_SECONDS}; HttpOnly; Secure; SameSite=Strict`;
    return redirect("/admin", cookie);
  }

  if (pathname === "/admin/logout") {
    if (method !== "POST" || !sameOrigin(request)) return redirect("/admin");
    return redirect("/admin", `${COOKIE}=; Path=/admin; Max-Age=0; HttpOnly; Secure; SameSite=Strict`);
  }

  if (!(await isLoggedIn(request, env, nowMs))) {
    if (method !== "GET" && method !== "HEAD") return html(401, loginPage("다시 로그인해 주세요."), "no-store");
    return html(200, loginPage(""), "no-store");
  }

  if (!env.CARDS) return html(503, adminPage("운영자", "<p>카드 저장소(CARDS)가 연결되지 않았어요.</p>"), "no-store");

  if (pathname === "/admin" || pathname === "/admin/") {
    if (method !== "GET" && method !== "HEAD") return redirect("/admin");
    const url = new URL(request.url);
    return html(200, await listPage(env, url.origin, url.searchParams.get("done")), "no-store");
  }

  if (method !== "POST") return redirect("/admin");
  if (!sameOrigin(request)) return html(403, adminPage("운영자", "<p>잘못된 요청이에요.</p>"), "no-store");
  const form = await readForm(request);

  if (pathname === "/admin/delete") {
    const n = Number.parseInt(form?.get("n") ?? "", 10);
    const index = await readIndex(env);
    const item = index.items.find((it) => it.n === n);
    if (!item) return redirect("/admin?done=missing");
    index.items = index.items.filter((it) => it.n !== n);
    await env.CARDS.put(SKY_KEY, JSON.stringify(index));
    if (typeof item.id === "string" && CARD_ID_RE.test(item.id)) await env.CARDS.delete(`card:${item.id}`);
    return redirect("/admin?done=deleted");
  }

  if (pathname === "/admin/clear") {
    // 실수로 누르지 않게 '전부 삭제'를 직접 써야 한다.
    if ((form?.get("confirm") ?? "").trim() !== "전부 삭제") return redirect("/admin?done=confirm");
    const index = await readIndex(env);
    for (const item of index.items) {
      if (typeof item.id === "string" && CARD_ID_RE.test(item.id)) await env.CARDS.delete(`card:${item.id}`);
    }
    await env.CARDS.put(SKY_KEY, JSON.stringify({ count: 0, items: [] }));
    return redirect("/admin?done=cleared");
  }

  return redirect("/admin");
}

const NOTICES = {
  deleted: "별자리를 지웠어요.",
  cleared: "모든 별자리를 지웠어요. 번호도 1번부터 다시 시작해요.",
  missing: "이미 지워진 별자리예요.",
  confirm: "'전부 삭제'를 정확히 입력해야 지워져요.",
};

async function listPage(env, origin, done) {
  const index = await readIndex(env);
  const items = index.items.slice().reverse();
  const rows = items.map((item) => {
    const name = escapeHtml(String(item.name ?? ""));
    const keyword = escapeHtml(KEYWORDS[item.keyword] ?? "");
    const number = Number.isInteger(item.n) ? item.n : 0;
    let qr = "<span class=\"muted\">카드 없음</span>";
    if (typeof item.id === "string" && CARD_ID_RE.test(item.id)) {
      const link = `${origin}/card/${item.id}`;
      const svg = qrSvg(encodeQr(link));
      qr = `<details><summary>QR 보기</summary><div class="bigqr">${svg ?? ""}<a href="${escapeHtml(link)}" target="_blank" rel="noopener">${escapeHtml(link)}</a></div></details>`;
    }
    return `<tr>
<td class="shape">${shapeSvg(item)}</td>
<td><b>${number}번째</b><br>${name}<br><span class="muted">${keyword}</span></td>
<td>${qr}</td>
<td><form method="post" action="/admin/delete"><input type="hidden" name="n" value="${number}"><button class="danger" type="submit">삭제</button></form></td>
</tr>`;
  }).join("\n");

  const notice = NOTICES[done] ? `<p class="notice">${NOTICES[done]}</p>` : "";
  const table = items.length
    ? `<table><thead><tr><th>모양</th><th>별자리</th><th>QR</th><th></th></tr></thead><tbody>${rows}</tbody></table>`
    : `<p class="muted">아직 별자리가 없어요.</p>`;
  return adminPage("운영자 · Constellation", `
<header><h1>운영자</h1><form method="post" action="/admin/logout"><button type="submit">로그아웃</button></form></header>
${notice}
<p class="muted">지금까지 ${index.count}개 · 목록에는 최근 ${index.items.length}개가 있어요. 삭제하면 모니터(/wall)와 하늘에서 빠지고 카드 주소도 열리지 않아요.</p>
${table}
<section class="clear">
<h2>전부 삭제</h2>
<p class="muted">목록의 별자리와 카드를 모두 지우고 번호를 1번부터 다시 시작해요. 되돌릴 수 없어요.</p>
<form method="post" action="/admin/clear"><input name="confirm" placeholder="전부 삭제" autocomplete="off"><button class="danger" type="submit">전부 삭제</button></form>
</section>`);
}

function loginPage(message) {
  return adminPage("운영자 로그인 · Constellation", `
<h1>운영자 로그인</h1>
${message ? `<p class="notice">${escapeHtml(message)}</p>` : ""}
<form method="post" action="/admin/login" class="login">
<input type="password" name="password" placeholder="비밀번호" autocomplete="current-password" required>
<button type="submit">들어가기</button>
</form>`);
}

function adminPage(title, content) {
  return `<!doctype html>
<html lang="ko">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex">
<title>${escapeHtml(title)}</title>
<style>${ADMIN_STYLE}</style>
</head>
<body><main>${content}</main></body>
</html>`;
}

const ADMIN_STYLE = `
  * { box-sizing: border-box; }
  body { margin: 0; padding: 24px 16px; background: #0d0b2b; color: #f5f2ff;
    font-family: -apple-system, "Apple SD Gothic Neo", "Noto Sans KR", "Malgun Gothic", sans-serif; }
  main { max-width: 860px; margin: 0 auto; }
  header { display: flex; justify-content: space-between; align-items: center; }
  h1 { color: #ffe094; margin: 0 0 12px; }
  h2 { color: #ffb4b4; font-size: 18px; }
  .muted { color: #9a94c8; font-size: 14px; }
  .notice { padding: 10px 14px; border-radius: 10px; background: rgba(255, 224, 148, 0.12); color: #ffe094; }
  table { width: 100%; border-collapse: collapse; margin-top: 12px; }
  th, td { padding: 10px 8px; border-bottom: 1px solid rgba(255, 255, 255, 0.08); text-align: left; vertical-align: middle; }
  td.shape svg { width: 72px; height: 72px; }
  td.shape polyline { fill: none; stroke: #ffe094; stroke-width: 2; stroke-linejoin: round; }
  td.shape circle { fill: #fff; }
  button, input { font: inherit; font-size: 15px; padding: 8px 14px; border-radius: 8px; border: 1px solid rgba(255,255,255,0.2); background: #1d1850; color: #f5f2ff; }
  button { cursor: pointer; }
  .danger { background: #6b1f2f; border-color: #a83a52; }
  summary { cursor: pointer; color: #c9c3f0; }
  .bigqr { margin-top: 8px; }
  .bigqr .qr { width: 220px; height: 220px; display: block; background: #fff; border-radius: 8px; }
  .bigqr a { display: block; margin-top: 6px; font-size: 12px; color: #9a94c8; word-break: break-all; }
  .clear { margin-top: 36px; padding: 16px; border: 1px solid #a83a52; border-radius: 12px; }
  .clear form, .login { display: flex; gap: 8px; flex-wrap: wrap; }
`;
