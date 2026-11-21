import { describe, expect, it } from "vitest";
import { analyzeShape, describeShape, sketchShape } from "../src/shape.js";

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

describe("sketchShape", () => {
  it("별은 o, 세로선은 |, 가로선은 - 로 그린다", () => {
    // 아래에서 위로 곧게 올라갔다가 오른쪽으로 꺾이는 모양
    const rows = sketchShape([[0, 0], [0, 1], [1, 1]]);
    expect(rows).toHaveLength(13);
    expect(rows.every((row) => row.length === 25)).toBe(true);
    expect(rows[0]).toBe("o" + "-".repeat(23) + "o");
    expect(rows[12][0]).toBe("o");
    expect(rows[6][0]).toBe("|");
  });

  it("오른쪽 위로 가는 선은 /, 오른쪽 아래로 가는 선은 \\", () => {
    const up = sketchShape([[0, 0], [1, 1]]).join("");
    const down = sketchShape([[0, 1], [1, 0]]).join("");
    expect(up).toContain("/");
    expect(down).toContain("\\");
  });
});
