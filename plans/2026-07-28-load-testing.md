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
- **背景**：`lock_timeout` 預設 5 秒是憑經驗設的，沒有負載資料佐證。目前拋 `AdvisoryLockTimeoutException` 的唯一訊號就是「等鎖等超過 5 秒」——**正常排隊排久了**跟**持鎖方真的異常卡住**現在用同一個訊號分不開。
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
- **實測環境（誠實記錄，數字怎麼來的）**：
  - **環境 A：本機 Windows**，Intel Core i7-7700K @ 4.20GHz（實體 4 核心/8 邏輯執行緒）、約 32GB RAM；Docker Desktop（WSL2）VM 分配到 8 CPU／15.6GB RAM，但**跟 host 上其他所有東西共用同一組實體核心**（沒有 `--cpus`/`--memory` 釘死、沒有專用機器）。
  - **環境 B：AWS Lightsail/EC2**（ap-northeast-1），4 vCPU／15GB RAM，獨立執行個體、無其他負載，但 Lightsail 入門規格的 vCPU 可能是共享／有節流的虛擬核心（見下方 Phase 2 觀察）。
- **這代表報告裡的數字是方向性的、量級對，不是可信賴的 benchmark**——足夠回答「lock_timeout 5s 有沒有安全邊際」「連線池斷點大概在哪」這種是非題，不足夠拿來說「正式環境 p95 就是這個數字」。

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

**測試方法（兩個 phase 一致）**：每個 VUS 等級跑 **5 輪**，每輪之間**重啟 `e2e-backend`**（清空 Npgsql 連線池，避免上一輪殘留連線污染下一輪數字）＋ reseed，跑完再重啟一次才查 DB（reseed/查詢都是外部連線，pool 滿載時會被拒絕，必須先清空）。自動化腳本：`loadtest-multiround.sh`（repo 根目錄）；本機用 Git Bash 跑 docker 需加 `MSYS_NO_PATHCONV=1`，不然 `-v` 掛載路徑會被誤轉成 Windows 路徑。下表數字皆為 **5 輪的範圍**（min–max），不是單次量測。

**Phase 1**（2026-07-29，`k6/register-load.js` + `db/seed-load.sql`；每輪跑序：重啟+reseed → VUS=60 → VUS=150（沿用同批連線，不重啟）→ 重啟+reseed → VUS=200（乾淨）→ VUS=200（不重啟，`OFFSET` 打全新 200 人）→ 重啟 → 查 DB）
- [x] 兩環境各 5 輪、共 40 次 k6 執行，**checks_failed 全部 0.00%**。
- [x] **正確性**：每輪查 DB 皆 `registered=400 overcap=0 dup=0`（驗證的是每輪最後 200+200=400 那組），兩環境、5 輪次無例外。
- [x] **連線耗盡斷點**：每輪「查 DB 前」都必須先重啟 backend，不重啟的話 `psql` 直接 `FATAL: sorry, too many clients already`——兩環境、5 輪、10 次驗證動作全部重現，不是偶發。

  **環境 A：本機 Windows**（i7-7700K，4 核 8 緒，跟 host 上其他東西共用資源）

  | VUS | http_req p95（5 輪範圍） | iteration p95（5 輪範圍） |
  |---|---|---|
  | 60 | 1.45s–1.58s | 2.23s–2.53s |
  | 150（沿用連線，不重啟） | 2.60s–2.83s | 3.20s–3.55s |
  | 200（乾淨：重啟+reseed） | 3.24s–3.67s | 4.43s–4.91s |
  | 200（不重啟，`OFFSET` 全新 200 人） | 3.49s–4.25s | 3.96s–4.78s |

  **環境 B：AWS Lightsail/EC2**（ap-northeast-1，4 vCPU／15GB RAM，2026-07-29）

  | VUS | http_req p95（5 輪範圍） | iteration p95（5 輪範圍） |
  |---|---|---|
  | 60 | 0.91s–1.01s | 1.55s–1.60s |
  | 150（沿用連線，不重啟） | 1.63s–1.73s | 2.10s–2.24s |
  | 200（乾淨：重啟+reseed） | 2.30s–2.48s | 3.42s–3.52s |
  | 200（不重啟，`OFFSET` 全新 200 人） | 2.73s–2.90s | 3.14s–3.27s |

  **觀察**：VUS 60 到 200 這個範圍，兩環境 5 輪的數字區間完全不重疊，EC2 穩定比本機快，符合「專用機器」預期；但領先幅度不是固定值，**隨 VUS 上升而收斂**——用 5 輪中位數算，VUS=60/150 時 EC2 快 49%–58%，VUS=200（乾淨）收斂到 33%–37%，VUS=200（不重啟）只剩 28%–29%。「200（不重啟）」這步在兩環境的 5 輪裡**全部 100% 成功**（checks_failed 0.00%）——連線池滿載、外部 `psql` 被拒絕的同一時刻，app 自己的請求仍全數排隊完成，這個結論在兩台機器、共 10 輪測試裡沒有一次例外。

  **根因**：Npgsql pool 上限（預設 `Maximum Pool Size`=100）本身會**自我限制**——超過 100 的請求在 client 端排隊等連線空出來，不會硬跟 Postgres 要第 101 條；只有**繞過 app 自己 pool 的外部新連線**（`psql`、migration，直接跟 Postgres 要一條全新連線）在 backend pool 已佔滿 `max_connections`（預設 100）額度時才會被**直接拒絕**，不是排隊。連線閒置要等 `Connection Idle Lifetime`（預設 300 秒）才會被 `Connection Pruning Interval`（預設 10 秒一次）收回，測試輪次間隔遠不到 5 分鐘，上一輪的連線就還占著額度——這就是為什麼每輪查 DB 前都要先重啟 backend。真正的風險不是「玩家請求會失敗」，是**同一時間需要對 Postgres 開新連線的其他操作**（migration job、DBA 手動查資料庫、多服務共享同一個 Postgres）會連不上，且這個風險不需要 app 本身出問題就會發生。
  **建議**：正式環境該給 Postgres `max_connections` 留 headroom（backend pool size + migrate + 其他服務 + 保留量 < max_connections），或明確設定 Npgsql `Maximum Pool Size` 上限／縮短 `Connection Idle Lifetime`。

**Phase 2**（2026-07-29，`k6/teamslot-edit-load.js` + `db/seed-load-teamslot.sql`；每輪每個 VUS 跑序：重啟+reseed → 跑 k6 → 重啟 → 查 DB）
- [x] 兩環境各 5 輪 × 3 個 VUS，共 30 次 k6 執行，**checks_failed 全部 0.00%、0 個誤觸發衝突**（每次 `✓ no conflict` 皆通過）。
- [x] **正確性**：每次查 DB 皆 `filled=VUS dup=0`，兩環境、所有輪次、所有 VUS 無例外。

  **環境 A：本機 Windows**

  | VUS | http_req p95（5 輪範圍） | iteration p95（5 輪範圍） | lock_timeout 誤觸發？ |
  |---|---|---|---|
  | 60 | 1.76s–2.10s | 3.35s–3.97s | 無（0/240 × 5 輪） |
  | 250 | 5.00s–5.41s | 9.08s–9.63s | 無（0/1000 × 5 輪） |
  | 500 | 9.39s–9.65s | 15.48s–16.28s | 無（0/2000 × 5 輪） |

  **環境 B：AWS Lightsail/EC2**

  | VUS | http_req p95（5 輪範圍） | iteration p95（5 輪範圍） | lock_timeout 誤觸發？ |
  |---|---|---|---|
  | 60 | 1.24s–1.32s | 2.76s–2.88s | 無（0/240 × 5 輪） |
  | 250 | 5.49s–5.95s | 9.41s–9.93s | 無（0/1000 × 5 輪） |
  | 500 | 9.86s–10.47s | 16.03s–16.90s | 無（0/2000 × 5 輪） |

  **觀察（跟 Phase 1 的模式不同，值得注意）**：VUS=60 時 EC2 依然明顯較快，跟 Phase 1 一致；但 VUS=250 起兩環境開始拉近，**VUS=500 時本機甚至略快於 EC2**（本機 15.48s–16.28s vs EC2 16.03s–16.90s，5 輪範圍幾乎不重疊）。這跟「專用機器全面比較快」的直覺相反——推測 Lightsail 入門規格的 4 vCPU 可能是共享／有節流的虛擬核心，在這種大量小交易搶同一把鎖的 CPU 密集情境下，不一定贏過桌機的實體核心；換句話說，「環境 B 比較快」只在 Phase 1 的連線數/序列化排隊這種瓶頸類型下成立，到了 Phase 2 高 VUS 的 CPU 排隊瓶頸，優勢就消失甚至反轉。
  **結論**：`lock_timeout=5s` 在兩環境、5 輪、3 個 VUS 等級（合計 30 次執行、約 12,150 次請求）裡**從未誤觸發**，即使 VUS=500 時 iteration 總延遲最高到過 16.9 秒。**原因**：`SET LOCAL lock_timeout` 只計算「已進入交易、卡在 `pg_advisory_xact_lock` 本身」的等待時間；client 端觀察到的巨大延遲，大部分花在進交易之前——Kestrel request 排隊、等 Npgsql 連線池給連線——這些不算進 `lock_timeout` 的計時範圍。安全邊際穩定存在，不受環境或測試輪次影響；真正該擔心的瓶頸是 Phase 1 找到的連線池斷點，不是這個 timeout 本身太短。

**不進正式 phase（有空再做）**
- [ ] idempotency／限流在併發下行為正確（409／429）。

## 工時估
- **Phase 1**：k6 腳本 + seed + compose load profile ≈ 一天；跑 + 分析 + 記斷點 ≈ 半天。
- **Phase 2**：重用 Phase 1 環境，加 `teamslot_edit_contention` 情境 + 跑 + 分析 ≈ 半天。
