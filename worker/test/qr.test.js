import { describe, expect, it } from "vitest";
import { encodeQr, qrSvg } from "../src/qr.js";

// 디코더로 읽히는지는 구현할 때 따로 확인했다(버전 1~10). 여기서는 구조가 깨지지 않는지만 본다.
describe("encodeQr", () => {
  it.each([
    ["A", 21],
    ["https://constellation-relay.example.workers.dev/card/abcdefghijkm", 37],
    ["x".repeat(213), 57],
  ])("%s → %i칸", (text, size) => {
    const modules = encodeQr(text);
    expect(modules).toHaveLength(size);
    expect(modules.every((row) => row.length === size)).toBe(true);
  });

  it("세 모서리에 위치 찾기 무늬가 있다", () => {
    const m = encodeQr("https://example.com/card/abc");
    const n = m.length;
    for (const [x, y] of [[0, 0], [n - 7, 0], [0, n - 7]]) {
      expect(m[y][x]).toBe(true);
      expect(m[y + 1][x + 1]).toBe(false);
      expect(m[y + 3][x + 3]).toBe(true);
      expect(m[y + 6][x + 6]).toBe(true);
    }
  });

  it("같은 입력은 같은 결과를 낸다", () => {
    expect(encodeQr("hello")).toEqual(encodeQr("hello"));
  });

  it("너무 길거나 비어 있으면 null", () => {
    expect(encodeQr("z".repeat(214))).toBeNull();
    expect(encodeQr("")).toBeNull();
  });
});

describe("qrSvg", () => {
  it("흰 여백을 둔 SVG를 만든다", () => {
    const svg = qrSvg(encodeQr("A"));
    expect(svg).toContain('viewBox="0 0 29 29"');
    expect(svg).toContain('fill="#fff"');
    expect(svg).not.toContain("<script");
  });
  it("행렬이 없으면 빈 문자열", () => {
    expect(qrSvg(null)).toBe("");
  });
});
