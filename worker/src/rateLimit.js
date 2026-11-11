// 기기당 분당 호출 제한.
// Worker 인스턴스(isolate) 메모리에 두는 간단한 구현이다. 인스턴스가 바뀌면 기록이 사라지므로
// 엄밀한 제한은 아니지만, 전시용 기기 한 대의 과도한 호출을 막는 데는 충분하다.
// 더 엄밀하게 하려면 Cloudflare Rate Limiting 바인딩이나 Durable Object로 바꾼다.

const WINDOW_MS = 60_000;
const MAX_TRACKED_KEYS = 1000;

export function createRateLimiter() {
  /** @type {Map<string, number[]>} */
  const hits = new Map();

  return {
    /**
     * @param {string} key 기기 식별자
     * @param {number} limit 분당 허용 횟수
     * @param {number} [now] 현재 시각(ms). 테스트에서 주입한다.
     * @returns {boolean} 허용이면 true
     */
    allow(key, limit, now = Date.now()) {
      const recent = (hits.get(key) ?? []).filter((t) => now - t < WINDOW_MS);
      if (recent.length >= limit) {
        hits.set(key, recent);
        return false;
      }
      recent.push(now);
      hits.set(key, recent);
      if (hits.size > MAX_TRACKED_KEYS) {
        // 가장 오래된 키부터 버린다(Map은 삽입 순서를 유지한다).
        hits.delete(hits.keys().next().value);
      }
      return true;
    },
    reset() {
      hits.clear();
    },
  };
}
