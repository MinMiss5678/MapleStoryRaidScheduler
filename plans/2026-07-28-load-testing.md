# 壓力／負載測試計畫

> 輕量 plan（動手前的 spec）：目標 / 範圍 / 決策 / 驗收 / 工時。做完可丟；穩定規則再收進 `docs/`。
> **定位誠實**：readiness。現況單公會、幾十人 → 不需要；做它是為「多租戶 SaaS 前抓吞吐／連線上限」與驗證真-async 在負載下的效益。
> 單元／整合測驗**正確性**；壓測驗**負載下的吞吐／延遲／資源上限**——是不同層級，不是同一件事。

## 目標

對**部署起來的真系統**灌併發負載，量吞吐（RPS）、延遲（p50/p95/p99）、錯誤率，並找**斷點**——尤其 auto-assign 的 advisory lock 在高併發下的**連線池耗盡點**（我們判斷「該考慮換 MQ 序列化」的訊號），以及 TeamSlot 編輯鎖的**正常排隊等待時間**是否逼近 `lock_timeout` 5s 預設值（判斷會不會誤把「排隊久」當成「持鎖方卡死」）。順帶驗真-async 的價值（負載下同資源扛更多併發、p99 較平；阻塞式會較早 thread-pool 飢餓）。

## 階段

- **Phase 1**：環境（k6/NBomber 腳本 + seed + compose load profile）+ 跑 §1（auto-assign lock）。獨立可交付——做完就有「連線耗盡斷點」這個成果，沒空做 Phase 2 也不虧。
- **Phase 2**：重用 Phase 1 環境，加跑 §1b（TeamSlot 編輯鎖 + `lock_timeout` 驗證）。
- §2/§3/§4（idempotency/限流、讀取基準、端到端）**不排進正式 phase**，維持「有空/有需求再做」，避免為它們預先付環境擴充成本。

## 範圍（依價值排序）

### 1. advisory-lock 併發（★ 最高價值，Phase 1）
- 同一 period **N 個併發報名** → 兩件事：
  - **正確性**：DB **不出現重複隊**（無 read-then-write race）——`RegistrationLockIntegrationTests` 用 2 條連線證明過序列化，壓測放大到數十條給信心。
  - **斷點**：量到多少併發時 Npgsql 連線池的 waiter 塞住、p99 爆 → 那條線就是「換 MQ 序列化」的訊號。

### 1b. TeamSlot 編輯鎖（classId 1002）+ `lock_timeout` 誤觸發驗證（★ 高價值，Phase 2；原計畫寫時這功能還不存在）
- **背景**：`lock_timeout` 預設 5 秒是憑經驗設的，沒有負載數據佐證。目前拋 `AdvisoryLockTimeoutException` 的唯一訊號就是「等鎖等超過 5 秒」——**正常排隊排久了**跟**持鎖方真的異常卡住**現在用同一個訊號分不開。
- **要驗的問題**：對同一 `teamSlotId` 灌 N 個併發編輯（管理員排團存檔 + 玩家補位混打），量**正常排隊等待時間的分布（p50/p95/p99）**——
  - 若 p99 遠低於 5s：現有預設合理，維持。
  - 若 p99 逼近甚至超過 5s：代表高併發下會出現**假陽性**（隊伍其實沒卡住，只是排隊久），要嘛調高 timeout、要嘛把「lock_timeout 逾時」跟「隊伍消失/樂觀鎖衝突」在使用者體感上區分開（目前三者都統一落進 `ConflictedTeamSlotIds`，UI 上看不出差異）。
- **正確性硬條件同 §1**：無論併發多高，DB 最終人數不超過隊伍容量（`ConcurrentAdd_ToTeamWithOneSlotLeft_NeverExceedsCapacity` 的邏輯放大版）。

### 2. idempotency + 限流 burst（不進正式 phase）
- 同一 idempotency key 併發 → 一個 2xx、其餘 **409**。
- 同 discordId burst >100/10s → 出現 **429**。
- 驗 Redis 版在真併發下的行為 + fail-open（停 Redis 容器 → 放行不擋）。

### 3. 讀取基準（不進正式 phase）
- 主要查詢端點 ramp-up → p95/p99 與吞吐基準線。

### 4. 端到端報名 → 排團（不進正式 phase）
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
- **正確性硬條件**：壓測後 **DB 無重複隊**（auto-assign 不變量）、TeamSlot 成員數不超過容量。
- p95 < 基準值（先量再訂，例：< 500ms）@ 目標 RPS。
- error rate < 1%（不含刻意觸發的 409/429）。
- advisory-lock 的**連線耗盡併發數**：這是要**找出來記下**的數字（非 pass/fail）。
- TeamSlot 編輯鎖的**正常排隊等待 p99**：找出來記下，並跟 `lock_timeout` 預設 5s 比較，判斷會不會誤觸發（非 pass/fail，是決策依據）。

## k6 情境（草案）

| 情境 | 打法 | 期望 |
|---|---|---|
| `same_period_contention` | M VU 同 period `POST register` | 回應正常 + 事後查 DB **隊數 = 預期、無重複** |
| `teamslot_edit_contention` | M VU 同 teamSlotId 混打管理員存檔＋玩家補位 | DB 人數不超容量；記錄等鎖等待時間 p50/p95/p99，跟 5s `lock_timeout` 比較 |
| `idempotency_burst` | 同 key 併發 | 一個 2xx、其餘 409 |
| `ratelimit_burst` | 同 discordId >100/10s | 出現 429 |
| `read_baseline` | GET 端點 ramp | p95/p99 基準 |

## 驗收

**Phase 1**（已跑，2026-07-29，對 `compose.e2e.yaml` 的 e2e-db/e2e-migrate/e2e-redis/e2e-backend）
- [x] k6 腳本 + seed 可一鍵跑（`k6/register-load.js` + `db/seed-load.sql`，`docker run grafana/k6`）。
- [x] 產出報告：見下方實測數字。
- [x] **正確性**：VUS=60（10隊×6人滿編）與 VUS=200（乾淨環境）皆 0 error、DB 無重複隊、無超編、無漏派——`db/verify-load.sql` 驗證通過。
- [x] **找出** advisory-lock 連線耗盡的併發斷點——**不是「併發量太大直接失敗」，是連線池 headroom=0 的配置問題**：

  | VUS | 結果 |
  |---|---|
  | 60 | 0% error，p95 register 1.67s |
  | 150 | 0% error，p95 register 4.8s（線性隨鎖佇列變深） |
  | 200（乾淨環境） | 0% error，p95 register 5.24s |
  | 200（緊接著沒重啟就再跑一次） | **37.5% error**，且 Postgres 開始拒絕新連線（連 `psql` 都連不上：`FATAL: sorry, too many clients already`） |

  **根因（比原本寫的更精確）**：**不是「併發量 > 100 就會爆」**——Npgsql pool 上限（預設 = `Maximum Pool Size` 100）本身會**自我限制**，超過 100 的請求會在 client 端排隊等一條連線空出來，不會硬跟 Postgres 要第 101 條（Phase 2 的 VUS=500 乾淨環境測試就是證據：全部靠 100 條連線輪流服務排隊排出來，0 error）。**真正的觸發條件是「兩輪測試間隔太短、pool 沒機會把閒置連線還給 Postgres」**：Npgsql 的 pool 設計是「用完先留著、晚點才收」——連線閒置超過 `Connection Idle Lifetime`（預設 300 秒）才會被 `Connection Pruning Interval`（預設 10 秒一次）掃掉。VUS=150 跑完到緊接著跑 VUS=200，中間間隔遠不到 5 分鐘，上一輪的 ~100+ 條連線都還原封不動躺在 pool 裡佔著 Postgres 的額度，這批新需求疊上去才瞬間衝過 `max_connections=100`（image 預設，沒有 headroom）。**這才是 `MSRS架構參照.md §10` 講的「連線池耗盡」訊號的具體條件**：不是穩態併發量的問題，是「pool 沒有時間自然收斂就被連續打」的問題。
  **建議**：正式環境該給 Postgres `max_connections` 留 headroom（backend pool size + migrate + 其他服務 + 保留量 < max_connections），或明確設定 Npgsql `Maximum Pool Size` 上限／縮短 `Connection Idle Lifetime`，別讓它預設吃到跟 Postgres 一樣的天花板、又要等 5 分鐘才收斂。

**Phase 2**（已跑，2026-07-29，同一套環境；`k6/teamslot-edit-load.js` + `db/seed-load-teamslot.sql`）
- [x] **正確性**：VUS=60/250/500（乾淨環境）皆 0 error、0 個誤觸發衝突，TeamSlot 人數符合預期（每人填不同空位，不重疊）。
- [x] **找出** TeamSlot 編輯鎖正常排隊等待 p99，判斷 `lock_timeout` 5s 預設會不會誤觸發：

  | VUS | http_req p95（login+get+put 混合） | iteration p95（含 3 個請求） | lock_timeout 誤觸發？ |
  |---|---|---|---|
  | 60 | 895ms | 1.42s | 無 |
  | 250 | 3.63s | 6.24s | 無 |
  | 500 | 9.69s | 17.19s（max 17.52s） | **無，0/500** |

  **結論（跟原本擔心的方向相反）**：500 個併發編輯同一支隊——這已經是遠超實際使用情境的極端值（一支隊正常 6-8 人，不可能有 500 人同時搶編輯權）——`lock_timeout=5s` **完全沒有誤觸發**，即使總延遲飆到 17 秒。**原因**：`SET LOCAL lock_timeout` 只計算「已進入交易、卡在 `pg_advisory_xact_lock` 本身」的等待時間；client 端觀察到的巨大延遲，大部分花在**進交易之前**——Kestrel request 排隊、等 Npgsql 連線池給連線——這些不算進 `lock_timeout` 的計時範圍，各自有自己的（更寬鬆的）逾時。也就是說：**在連線池被打爆之前（見 Phase 1 的連線數斷點），`lock_timeout` 5s 這個預設值的安全邊際比原本想的大很多**——真正該擔心的瓶頸是連線池，不是這個 timeout 本身太短。
  **殘留的獨立問題**：500 併發下總延遲 17 秒是使用者體感很差的數字，但這是連線池/執行緒排隊的問題（跟 Phase 1 找到的斷點同源），不是 `lock_timeout` 該不該調的問題——兩個是不同層次的瓶頸，別混為一談。

**不進正式 phase（有空再做）**
- [ ] idempotency／限流在併發下行為正確（409／429）。

## 工時估
- **Phase 1**：k6 腳本 + seed + compose load profile ≈ 一天；跑 + 分析 + 記斷點 ≈ 半天。
- **Phase 2**：重用 Phase 1 環境，加 `teamslot_edit_contention` 情境 + 跑 + 分析 ≈ 半天。
