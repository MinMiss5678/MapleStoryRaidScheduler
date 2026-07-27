# 壓力／負載測試計畫

> 輕量 plan（動手前的 spec）：目標 / 範圍 / 決策 / 驗收 / 工時。做完可丟；穩定規則再收進 `docs/`。
> **定位誠實**：readiness。現況單公會、幾十人 → 不需要；做它是為「多租戶 SaaS 前抓吞吐／連線上限」與驗證真-async 在負載下的效益。
> 單元／整合測驗**正確性**；壓測驗**負載下的吞吐／延遲／資源上限**——是不同層級，不是同一件事。

## 目標

對**部署起來的真系統**灌併發負載，量吞吐（RPS）、延遲（p50/p95/p99）、錯誤率，並找**斷點**——尤其 auto-assign 的 advisory lock 在高併發下的**連線池耗盡點**（我們判斷「該考慮換 MQ 序列化」的訊號）。順帶驗真-async 的價值（負載下同資源扛更多併發、p99 較平；阻塞式會較早 thread-pool 飢餓）。

## 範圍（依價值排序）

### 1. advisory-lock 併發（★ 最高價值）
- 同一 period **N 個併發報名** → 兩件事：
  - **正確性**：DB **不出現重複隊**（無 read-then-write race）——`RegistrationLockIntegrationTests` 用 2 條連線證明過序列化，壓測放大到數十條給信心。
  - **斷點**：量到多少併發時 Npgsql 連線池的 waiter 塞住、p99 爆 → 那條線就是「換 MQ 序列化」的訊號。

### 2. idempotency + 限流 burst
- 同一 idempotency key 併發 → 一個 2xx、其餘 **409**。
- 同 discordId burst >100/10s → 出現 **429**。
- 驗 Redis 版在真併發下的行為 + fail-open（停 Redis 容器 → 放行不擋）。

### 3. 讀取基準
- 主要查詢端點 ramp-up → p95/p99 與吞吐基準線。

### 4. 端到端報名 → 排團
- 完整流程 p95/p99、錯誤率。

### 非範圍（YAGNI）
- 不做全端點覆蓋；先做 #1 一個就有價值。
- **不對 prod 壓**（對 staging / compose stack）。
- 不建常設 Grafana/InfluxDB 儀表（先用 k6 內建 summary）。

## 關鍵決策

### 工具：k6（建議）
- 產業標準、腳本化（JS）、容器化好、內建 p95/p99/RPS/threshold 報告、可進 CI。
- **.NET 原生替代：NBomber**（C# 寫、能跟測試專案共用）——想用 C# 而非 JS 才選。
- 煙霧快打：`bombardier` / `wrk`。

### 環境
- 對 **docker compose stack**（仿 e2e 的 profile；非 prod）。seed 一個未來 period + boss 模板 + N 隻角色（比照 `db/seed-e2e.sql`）。
- k6 以容器跑在 compose 網路內（比照 `compose.e2e.yaml` 的 e2e-playwright）。

### Auth
- 壓測要帶已驗證身分。用 **Development 的 test-login 端點**（e2e 已用）鑄 JWT，或預先產一批 token。
- 限流／advisory-lock 都按 discordId 分 → VU 身分要**刻意選**：測**鎖**用**同 period**（不同 discordId 也會撞同一 period 鎖）；測**限流**用**同 discordId**。

### 量測
- k6 summary：RPS、p50/p95/p99、error rate、threshold pass/fail。
- DB：`pg_stat_activity` 看 active/waiting 連線；記 Npgsql pool 設定（`Maximum Pool Size`）。
- 錯誤：Seq；資源：CPU / 記憶體 / GC（容器 `docker stats`）。

### 通過門檻（SLO，先訂再測）
- **正確性硬條件**：壓測後 **DB 無重複隊**（auto-assign 不變量）。
- p95 < 基準值（先量再訂，例：< 500ms）@ 目標 RPS。
- error rate < 1%（不含刻意觸發的 409/429）。
- advisory-lock 的**連線耗盡併發數**：這是要**找出來記下**的數字（非 pass/fail）。

## k6 情境（草案）

| 情境 | 打法 | 期望 |
|---|---|---|
| `same_period_contention` | M VU 同 period `POST register` | 回應正常 + 事後查 DB **隊數 = 預期、無重複** |
| `idempotency_burst` | 同 key 併發 | 一個 2xx、其餘 409 |
| `ratelimit_burst` | 同 discordId >100/10s | 出現 429 |
| `read_baseline` | GET 端點 ramp | p95/p99 基準 |

## 驗收

- [ ] k6 腳本 + compose load profile 可一鍵跑。
- [ ] 產出報告：RPS / p95 / p99 / error rate。
- [ ] **正確性**：高併發同 period 報名後，DB 無重複隊。
- [ ] **找出** advisory-lock 連線耗盡的併發斷點（數字記下來）。
- [ ] idempotency／限流在併發下行為正確（409／429）。

## 工時估
- k6 腳本 + seed + compose load profile ≈ 一天。
- 跑 + 分析 + 記斷點 ≈ 半天。
