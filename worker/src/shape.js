// 별자리 모양을 글로 풀어 프롬프트에 넣는다. 모델이 그림만 보고 막연한 이름을 짓지 않게,
// 이은 순서대로의 별 위치와 몇 가지 특징(닫힘, 가로세로 비율, 꺾임, 교차)을 함께 알려 준다.

const CLOSE_EPSILON = 0.04;

function sub(a, b) {
  return [a[0] - b[0], a[1] - b[1]];
}

function length(v) {
  return Math.hypot(v[0], v[1]);
}

function cross(a, b) {
  return a[0] * b[1] - a[1] * b[0];
}

// 두 선분이 끝점이 아닌 곳에서 서로 지나가는지
function segmentsCross(p1, p2, p3, p4) {
  const d1 = cross(sub(p4, p3), sub(p1, p3));
  const d2 = cross(sub(p4, p3), sub(p2, p3));
  const d3 = cross(sub(p2, p1), sub(p3, p1));
  const d4 = cross(sub(p2, p1), sub(p4, p1));
  return d1 * d2 < 0 && d3 * d4 < 0;
}

/**
 * 모양의 특징을 계산한다.
 * @param {number[][]} points 0~1 범위의 [x, y](위가 +y), 이은 순서. 같은 별을 다시 지나면 같은 점이 또 나온다.
 */
export function analyzeShape(points) {
  const n = points.length;
  const unique = [];
  for (const p of points) {
    if (!unique.some((q) => length(sub(p, q)) < 0.02)) unique.push(p);
  }
  const closed = n >= 4 && length(sub(points[0], points[n - 1])) < CLOSE_EPSILON;

  let minX = 1, minY = 1, maxX = 0, maxY = 0;
  for (const [x, y] of points) {
    minX = Math.min(minX, x); maxX = Math.max(maxX, x);
    minY = Math.min(minY, y); maxY = Math.max(maxY, y);
  }
  const width = maxX - minX;
  const height = maxY - minY;

  // 꺾임: 들어오는 방향과 나가는 방향 사이의 각도
  let sharp = 0;
  let bent = 0;
  let straight = 0;
  let signChanges = 0;
  let lastSign = 0;
  const vertexCount = closed ? n - 1 : n;
  for (let i = 1; i < n - 1 || (closed && i === n - 1); i++) {
    const prev = points[i - 1];
    const here = points[i];
    const next = closed && i === n - 1 ? points[1] : points[i + 1];
    const a = sub(here, prev);
    const b = sub(next, here);
    if (length(a) < 1e-6 || length(b) < 1e-6) continue;
    const turn = (Math.acos(Math.max(-1, Math.min(1, (a[0] * b[0] + a[1] * b[1]) / (length(a) * length(b))))) * 180) / Math.PI;
    if (turn > 100) sharp++;
    else if (turn < 25) straight++;
    else bent++;
    const sign = Math.sign(cross(a, b));
    if (sign !== 0) {
      if (lastSign !== 0 && sign !== lastSign) signChanges++;
      lastSign = sign;
    }
    if (i >= vertexCount) break;
  }

  let crossings = 0;
  for (let i = 0; i < n - 1; i++) {
    for (let j = i + 2; j < n - 1; j++) {
      if (closed && i === 0 && j === n - 2) continue;
      if (segmentsCross(points[i], points[i + 1], points[j], points[j + 1])) crossings++;
    }
  }

  return { segments: n - 1, stars: unique.length, closed, width, height, sharp, bent, straight, signChanges, crossings };
}

const SKETCH_COLUMNS = 25;
const SKETCH_ROWS = 13;

/**
 * 모양을 글자로 그린 작은 그림. 별은 o, 선은 기울기에 따라 - | / \ 로 찍는다(위쪽이 하늘 위).
 * 글자는 세로로 길쭉하므로 가로 칸을 세로의 두 배쯤 둔다.
 */
export function sketchShape(points) {
  const grid = Array.from({ length: SKETCH_ROWS }, () => new Array(SKETCH_COLUMNS).fill("."));
  const cell = ([x, y]) => [Math.round(x * (SKETCH_COLUMNS - 1)), Math.round((1 - y) * (SKETCH_ROWS - 1))];
  for (let i = 0; i < points.length - 1; i++) {
    const [c0, r0] = cell(points[i]);
    const [c1, r1] = cell(points[i + 1]);
    // 화면 비율(가로 칸이 좁다)을 감안해 기울기 글자를 고른다.
    const dx = (c1 - c0) / 2;
    const dy = r0 - r1; // 위로 갈수록 +
    let mark = "/";
    if (Math.abs(dx) > Math.abs(dy) * 2) mark = "-";
    else if (Math.abs(dy) > Math.abs(dx) * 2) mark = "|";
    else if (dx * dy < 0) mark = "\\";
    const steps = Math.max(Math.abs(c1 - c0), Math.abs(r1 - r0), 1);
    for (let k = 1; k < steps; k++) {
      const c = Math.round(c0 + ((c1 - c0) * k) / steps);
      const r = Math.round(r0 + ((r1 - r0) * k) / steps);
      if (grid[r][c] === ".") grid[r][c] = mark;
    }
  }
  for (const p of points) {
    const [c, r] = cell(p);
    grid[r][c] = "o";
  }
  return grid.map((row) => row.join(""));
}

/** 프롬프트에 넣을 모양 설명 줄들. */
export function describeShape(points) {
  const s = analyzeShape(points);
  const grid = (v) => Math.round(v * 10);
  const path = points.map(([x, y]) => `(${grid(x)},${grid(y)})`).join(" → ");

  const traits = [];
  traits.push(s.closed ? "처음 별로 돌아와 닫힌 모양" : "끝이 열린 모양");
  const ratio = s.height > 0.001 ? s.width / s.height : 99;
  if (ratio > 1.8) traits.push("가로로 길쭉함");
  else if (ratio < 0.55) traits.push("세로로 길쭉함");
  else traits.push("가로세로가 비슷함");
  if (s.segments >= 2 && s.sharp === 0 && s.bent === 0) traits.push("거의 곧은 선");
  else {
    if (s.sharp > 0) traits.push(`뾰족하게 꺾인 곳 ${s.sharp}군데`);
    if (s.bent > 0) traits.push(`완만하게 꺾인 곳 ${s.bent}군데`);
  }
  if (s.signChanges >= 2 && s.sharp + s.bent >= 3) traits.push("지그재그로 오르내림");
  else if (!s.closed && s.signChanges === 0 && s.sharp + s.bent >= 2) traits.push("한쪽으로 휘어 감기는 모양");
  if (s.crossings > 0) traits.push(`선이 서로 엇갈리는 곳 ${s.crossings}군데`);
  if (s.segments > s.stars - (s.closed ? 0 : 1)) traits.push("같은 별을 두 번 지나는 곳이 있음");

  return [
    `- 이은 순서대로의 별 위치(왼쪽 아래가 (0,0), 오른쪽 위가 (10,10)): ${path}`,
    `- 특징: 별 ${s.stars}개, 선 ${s.segments}개, ${traits.join(", ")}`,
    "- 글자로 그린 모양(o는 별, 나머지는 선, 위쪽이 하늘 위):",
    ...sketchShape(points).map((row) => `    ${row}`),
  ];
}
