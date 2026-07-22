import { useRef, useCallback } from 'react';

/**
 * 同一邏輯操作固定一把 idempotency key。
 *
 * 用途：讓「雙擊 / 重送」帶同一把 key，後端 IdempotencyMiddleware 才 de-dup 得掉、
 * 收斂成單一寫入。原本 apiClient 每次呼叫都現產一把隨機 key，雙擊會送出兩把不同 key、
 * 兩把都放行 → 重複寫入，等於防護失效。
 *
 * 生命週期：
 * - next()：取目前操作的 key。第一次呼叫時產生，之後（同一操作的重送）沿用同一把。
 * - reset()：收到「終端回應」（成功或失敗）後呼叫 → 下一次操作換新 key。
 *   關鍵：務必在失敗後也 reset，否則使用者修正資料重試時，會被自己 60 秒前的舊 key 誤擋成 409。
 */
export function useIdempotencyKey() {
    const keyRef = useRef<string | null>(null);

    const next = useCallback(() => (keyRef.current ??= crypto.randomUUID()), []);
    const reset = useCallback(() => { keyRef.current = null; }, []);

    return { next, reset };
}
