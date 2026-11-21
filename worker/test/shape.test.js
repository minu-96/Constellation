import { describe, expect, it } from "vitest";
import { analyzeShape, describeShape } from "../src/shape.js";

describe("analyzeShape", () => {
  it("닫힌 삼각형", () => {
    const s = analyzeShape([[0, 0], [1, 0], [0.5, 1], [0, 0]]);
    expect(s.closed).toBe(true);
    expect(s.stars).toBe(3);
    expect(s.segments).toBe(3);
    expect(s.crossings).toBe(0);
  });

  it("곧은 선", () => {
    const s = analyzeShape([[0, 0.5], [0.33, 0.5], [0.66, 0.52], [1, 0.5]]);
    expect(s.closed).toBe(false);
    expect(s.sharp).toBe(0);
    expect(s.straight).toBe(2);
    expect(describeShape([[0, 0.5], [0.33, 0.5], [0.66, 0.52], [1, 0.5]])[1]).toContain("거의 곧은 선");
  });

  it("지그재그와 가로로 긴 모양", () => {
    const zigzag = [[0, 0.4], [0.25, 0.6], [0.5, 0.4], [0.75, 0.6], [1, 0.4]];
    const text = describeShape(zigzag)[1];
    expect(text).toContain("가로로 길쭉함");
    expect(text).toContain("지그재그");
  });

  it("엇갈리는 선(나비 모양)", () => {
    const s = analyzeShape([[0, 0], [1, 1], [1, 0], [0, 1]]);
    expect(s.crossings).toBe(1);
    expect(describeShape([[0, 0], [1, 1], [1, 0], [0, 1]])[1]).toContain("엇갈리는 곳 1군데");
  });

  it("좌표는 0~10 격자로 보여 준다", () => {
    expect(describeShape([[0, 0], [0.54, 1]])[0]).toContain("(0,0) → (5,10)");
  });
});
