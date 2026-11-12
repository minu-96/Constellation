// 작은 QR 코드 생성기(외부 라이브러리 없음). 모니터 페이지(/wall)에 카드 주소를 QR로 그릴 때 쓴다.
// 바이트 모드, 오류 정정 수준 M, 버전 1~10(최대 213바이트)만 지원한다.
// Unity 쪽 Assets/Scripts/UI/QrEncoder.cs 와 같은 알고리즘이다.

const MAX_VERSION = 10;
// 버전별 값(오류 정정 수준 M). 인덱스 0은 쓰지 않는다.
const TOTAL_CODEWORDS = [0, 26, 44, 70, 100, 134, 172, 196, 242, 292, 346];
const ECC_PER_BLOCK = [0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26];
const BLOCK_COUNT = [0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5];

const dataCodewords = (version) => TOTAL_CODEWORDS[version] - ECC_PER_BLOCK[version] * BLOCK_COUNT[version];

/**
 * 문자열을 QR 모듈 행렬로 바꾼다. modules[y][x]가 true면 검은 칸이다. 너무 길면 null.
 * @returns {boolean[][] | null}
 */
export function encodeQr(text) {
  if (typeof text !== "string" || text.length === 0) return null;
  const data = new TextEncoder().encode(text);

  let version = 0;
  for (let v = 1; v <= MAX_VERSION; v++) {
    if (4 + (v < 10 ? 8 : 16) + data.length * 8 <= dataCodewords(v) * 8) {
      version = v;
      break;
    }
  }
  if (version === 0) return null;

  const codewords = addErrorCorrection(buildDataCodewords(data, version), version);
  const size = version * 4 + 17;
  const modules = Array.from({ length: size }, () => new Array(size).fill(false));
  const isFunction = Array.from({ length: size }, () => new Array(size).fill(false));
  drawFunctionPatterns(modules, isFunction, version, size);
  drawCodewords(modules, isFunction, codewords, size);

  // 여덟 가지 마스크 중 벌점이 가장 낮은 것을 고른다.
  let bestMask = 0;
  let bestPenalty = Infinity;
  for (let mask = 0; mask < 8; mask++) {
    applyMask(modules, isFunction, mask, size);
    drawFormatBits(modules, isFunction, mask, size);
    const p = penalty(modules, size);
    if (p < bestPenalty) {
      bestPenalty = p;
      bestMask = mask;
    }
    applyMask(modules, isFunction, mask, size); // XOR이라 한 번 더 하면 원래대로 돌아온다.
  }
  applyMask(modules, isFunction, bestMask, size);
  drawFormatBits(modules, isFunction, bestMask, size);
  return modules;
}

/** QR 행렬을 SVG 문자열로 만든다. 가장자리에 흰 여백(4칸)을 둔다. */
export function qrSvg(modules, className = "qr") {
  if (!modules) return "";
  const quiet = 4;
  const size = modules.length + quiet * 2;
  let path = "";
  for (let y = 0; y < modules.length; y++) {
    for (let x = 0; x < modules.length; x++) {
      if (modules[y][x]) path += `M${x + quiet} ${y + quiet}h1v1h-1z`;
    }
  }
  return `<svg class="${className}" viewBox="0 0 ${size} ${size}" shape-rendering="crispEdges" role="img" aria-label="QR"><rect width="${size}" height="${size}" fill="#fff"/><path d="${path}" fill="#000"/></svg>`;
}

// 모드(바이트) + 길이 + 데이터 + 종료 표시 + 채움 바이트
function buildDataCodewords(data, version) {
  const capacity = dataCodewords(version);
  const bits = [];
  const append = (value, length) => {
    for (let i = length - 1; i >= 0; i--) bits.push(((value >>> i) & 1) !== 0);
  };
  append(0x4, 4);
  append(data.length, version < 10 ? 8 : 16);
  for (const byte of data) append(byte, 8);
  const capacityBits = capacity * 8;
  append(0, Math.min(4, capacityBits - bits.length));
  append(0, (8 - (bits.length % 8)) % 8);
  for (let pad = 0xec; bits.length < capacityBits; pad ^= 0xec ^ 0x11) append(pad, 8);

  const result = new Uint8Array(capacity);
  bits.forEach((bit, i) => {
    if (bit) result[i >>> 3] |= 1 << (7 - (i & 7));
  });
  return result;
}

// 데이터를 블록으로 나눠 리드-솔로몬 오류 정정 코드를 붙이고, 블록을 번갈아 섞는다.
function addErrorCorrection(data, version) {
  const blockCount = BLOCK_COUNT[version];
  const eccLength = ECC_PER_BLOCK[version];
  const total = TOTAL_CODEWORDS[version];
  const shortBlocks = blockCount - (total % blockCount);
  const shortBlockLength = Math.floor(total / blockCount);

  const divisor = reedSolomonDivisor(eccLength);
  const blocks = [];
  let offset = 0;
  for (let i = 0; i < blockCount; i++) {
    const dataLength = shortBlockLength - eccLength + (i < shortBlocks ? 0 : 1);
    const block = new Uint8Array(shortBlockLength + 1);
    block.set(data.subarray(offset, offset + dataLength));
    const ecc = reedSolomonRemainder(data.subarray(offset, offset + dataLength), divisor);
    offset += dataLength;
    // 짧은 블록은 데이터 끝에 빈칸 하나를 두어 길이를 맞춘다(섞을 때 건너뛴다).
    block.set(ecc, shortBlockLength + 1 - eccLength);
    blocks.push(block);
  }

  const result = new Uint8Array(total);
  let k = 0;
  for (let i = 0; i < shortBlockLength + 1; i++) {
    for (let j = 0; j < blockCount; j++) {
      if (i !== shortBlockLength - eccLength || j >= shortBlocks) result[k++] = blocks[j][i];
    }
  }
  return result;
}

function reedSolomonDivisor(degree) {
  const result = new Uint8Array(degree);
  result[degree - 1] = 1;
  let root = 1;
  for (let i = 0; i < degree; i++) {
    for (let j = 0; j < degree; j++) {
      result[j] = multiply(result[j], root);
      if (j + 1 < degree) result[j] ^= result[j + 1];
    }
    root = multiply(root, 0x02);
  }
  return result;
}

function reedSolomonRemainder(data, divisor) {
  const result = new Uint8Array(divisor.length);
  for (const byte of data) {
    const factor = byte ^ result[0];
    result.copyWithin(0, 1);
    result[result.length - 1] = 0;
    for (let i = 0; i < result.length; i++) result[i] ^= multiply(divisor[i], factor);
  }
  return result;
}

// GF(2^8) 곱셈, 기약 다항식 0x11D
function multiply(x, y) {
  let z = 0;
  for (let i = 7; i >= 0; i--) {
    z = (z << 1) ^ ((z >>> 7) * 0x11d);
    z ^= ((y >>> i) & 1) * x;
  }
  return z & 0xff;
}

function setFunction(modules, isFunction, x, y, dark) {
  modules[y][x] = dark;
  isFunction[y][x] = true;
}

function drawFunctionPatterns(modules, isFunction, version, size) {
  for (let i = 0; i < size; i++) {
    setFunction(modules, isFunction, 6, i, i % 2 === 0);
    setFunction(modules, isFunction, i, 6, i % 2 === 0);
  }
  for (const [cx, cy] of [[3, 3], [size - 4, 3], [3, size - 4]]) {
    for (let dy = -4; dy <= 4; dy++) {
      for (let dx = -4; dx <= 4; dx++) {
        const x = cx + dx;
        const y = cy + dy;
        if (x < 0 || x >= size || y < 0 || y >= size) continue;
        const distance = Math.max(Math.abs(dx), Math.abs(dy));
        setFunction(modules, isFunction, x, y, distance !== 2 && distance !== 4);
      }
    }
  }
  const positions = alignmentPositions(version, size);
  const last = positions.length - 1;
  for (let i = 0; i <= last; i++) {
    for (let j = 0; j <= last; j++) {
      if ((i === 0 && j === 0) || (i === 0 && j === last) || (i === last && j === 0)) continue;
      for (let dy = -2; dy <= 2; dy++) {
        for (let dx = -2; dx <= 2; dx++) {
          setFunction(modules, isFunction, positions[i] + dx, positions[j] + dy, Math.max(Math.abs(dx), Math.abs(dy)) !== 1);
        }
      }
    }
  }
  drawFormatBits(modules, isFunction, 0, size);
  drawVersion(modules, isFunction, version, size);
}

function alignmentPositions(version, size) {
  if (version === 1) return [];
  const count = Math.floor(version / 7) + 2;
  const step = Math.floor((version * 4 + count * 2 + 1) / (count * 2 - 2)) * 2;
  const result = new Array(count);
  result[0] = 6;
  let position = size - 7;
  for (let i = count - 1; i >= 1; i--) {
    result[i] = position;
    position -= step;
  }
  return result;
}

const bit = (value, index) => ((value >>> index) & 1) !== 0;

// 형식 정보 15비트(오류 정정 수준 M = 00, 마스크 번호)를 두 군데에 쓴다.
function drawFormatBits(modules, isFunction, mask, size) {
  const data = mask;
  let rem = data;
  for (let i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >>> 9) * 0x537);
  const bits = ((data << 10) | rem) ^ 0x5412;

  for (let i = 0; i <= 5; i++) setFunction(modules, isFunction, 8, i, bit(bits, i));
  setFunction(modules, isFunction, 8, 7, bit(bits, 6));
  setFunction(modules, isFunction, 8, 8, bit(bits, 7));
  setFunction(modules, isFunction, 7, 8, bit(bits, 8));
  for (let i = 9; i < 15; i++) setFunction(modules, isFunction, 14 - i, 8, bit(bits, i));
  for (let i = 0; i < 8; i++) setFunction(modules, isFunction, size - 1 - i, 8, bit(bits, i));
  for (let i = 8; i < 15; i++) setFunction(modules, isFunction, 8, size - 15 + i, bit(bits, i));
  setFunction(modules, isFunction, 8, size - 8, true); // 항상 검은 칸
}

// 버전 7 이상은 버전 정보 18비트를 두 군데에 쓴다.
function drawVersion(modules, isFunction, version, size) {
  if (version < 7) return;
  let rem = version;
  for (let i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >>> 11) * 0x1f25);
  const bits = (version << 12) | rem;
  for (let i = 0; i < 18; i++) {
    const a = size - 11 + (i % 3);
    const b = Math.floor(i / 3);
    setFunction(modules, isFunction, a, b, bit(bits, i));
    setFunction(modules, isFunction, b, a, bit(bits, i));
  }
}

// 오른쪽 아래에서 시작해 두 칸 폭으로 지그재그로 올라가며 데이터를 채운다.
function drawCodewords(modules, isFunction, codewords, size) {
  let i = 0;
  const totalBits = codewords.length * 8;
  for (let right = size - 1; right >= 1; right -= 2) {
    if (right === 6) right = 5;
    for (let vertical = 0; vertical < size; vertical++) {
      for (let j = 0; j < 2; j++) {
        const x = right - j;
        const upward = ((right + 1) & 2) === 0;
        const y = upward ? size - 1 - vertical : vertical;
        if (!isFunction[y][x] && i < totalBits) {
          modules[y][x] = bit(codewords[i >>> 3], 7 - (i & 7));
          i++;
        }
      }
    }
  }
}

function applyMask(modules, isFunction, mask, size) {
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      if (isFunction[y][x]) continue;
      let invert;
      switch (mask) {
        case 0: invert = (x + y) % 2 === 0; break;
        case 1: invert = y % 2 === 0; break;
        case 2: invert = x % 3 === 0; break;
        case 3: invert = (x + y) % 3 === 0; break;
        case 4: invert = (Math.floor(x / 3) + Math.floor(y / 2)) % 2 === 0; break;
        case 5: invert = ((x * y) % 2) + ((x * y) % 3) === 0; break;
        case 6: invert = (((x * y) % 2) + ((x * y) % 3)) % 2 === 0; break;
        default: invert = (((x + y) % 2) + ((x * y) % 3)) % 2 === 0; break;
      }
      if (invert) modules[y][x] = !modules[y][x];
    }
  }
}

// 읽기 어려운 무늬에 벌점을 준다(같은 색이 길게 이어짐, 2×2 덩어리, 흑백 비율 치우침).
function penalty(modules, size) {
  let result = 0;
  for (let y = 0; y < size; y++) {
    let runRow = 1;
    let runColumn = 1;
    for (let x = 1; x < size; x++) {
      if (modules[y][x] === modules[y][x - 1]) {
        runRow++;
        if (runRow === 5) result += 3;
        else if (runRow > 5) result++;
      } else runRow = 1;
      if (modules[x][y] === modules[x - 1][y]) {
        runColumn++;
        if (runColumn === 5) result += 3;
        else if (runColumn > 5) result++;
      } else runColumn = 1;
    }
  }
  let dark = 0;
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      if (modules[y][x]) dark++;
      if (x + 1 < size && y + 1 < size) {
        const c = modules[y][x];
        if (c === modules[y][x + 1] && c === modules[y + 1][x] && c === modules[y + 1][x + 1]) result += 3;
      }
    }
  }
  const total = size * size;
  result += Math.floor(Math.abs(dark * 20 - total * 10) / total) * 10;
  return result;
}
