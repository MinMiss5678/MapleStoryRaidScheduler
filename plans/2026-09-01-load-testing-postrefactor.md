# 壓力測試（重構後重跑 · 加計時量測「等鎖」+「取得DB連線」· 2 台 EC2）

> 輕量 plan（動手前 spec）+ 結果。定位誠實：**readiness**、方向性數字（非 prod benchmark/SLA）。
> 承接 `plans/2026-07-28-load-testing.md`（原始方法論）；本份記「兩次產品轉向後、**在後端加計時量測「等鎖」與「取得DB連線」時間**、在 2 台 EC2（k6 與 app 隔離）重跑」。

## 這次的關鍵升級：把「client 延遲多出的秒數」量到底層

先前只看 k6 的 `http_req_duration`（client 觀察值）——那**包含進交易前的 Kestrel/連線池排隊**，分不出「真正等 advisory lock」有多久。用它推 `lock_timeout` 是**拿錯指標**（client 延遲 ≠ 等鎖）。
本次在後端兩個關鍵點各加一段 `Stopwatch` 計時，emit 到 log：

1. **等鎖**（`TeamSlotEditLock.AcquireAsync`）：`Stopwatch` 圈住 `pg_advisory_xact_lock`（發出取鎖 → 拿到鎖）→ `advisory_lock_wait_ms`。
2. **取得DB連線**（`DbContext.BeginAsync`）：`Stopwatch` 圈住 `OpenAsync`（Npgsql pool rent；pool 滿時在此等到有連線釋出）→ `connection_acquire_ms`。

→ 有了②，就能把「client 延遲多出的 ~2s 是連線池排隊」從**推論**變成**直接量到的數字**，而不是靠相減臆測。

## 目標

1. 確認 `lock_timeout=5s` 對現行 `ConfirmMemberAsync`（classId 1002 鎖）**安全邊際成立**（高併發不誤觸發）。
2. **量出「等鎖」p99/max**（非 client 延遲）→ 才有正確的指標定 `lock_timeout`。
3. **量出「取得DB連線」p99/max** → 坐實「client 延遲多出的秒數在等連線、非等鎖」，並看它是否才是隨負載爆的瓶頸。
4. 方向性延遲側寫（readiness，不當 prod SLA、不跨環境比倍數）。

## 方法（2 台 EC2，k6 與受測系統隔離）

- **硬體**：2 台 AWS EC2（ap-northeast-1，各 4 vCPU / 16GB）。
  - app（受測系統）：私有 `172.26.3.203`——backend（加計時版 `:locktest2`）+ Postgres 18 + Redis。
  - k6（負載產生器）：**專機跑 k6，不與 app 搶 CPU**（壓測基本原則，避免污染）。
- **Stack**：`compose.locktest.yaml`（backend inline env、Development 模式開 test-login；無 bot/cloudflared）。schema 套 `db/migrations/*.up.sql`（26 支）。
- **隔離 pool-rent**：PG `max_connections=200` > Npgsql `Maximum Pool Size=100` → 讓「取得DB連線」等待純粹是**等 pool 內連線釋出**，PG 不會先拒連線污染量測。
- **情境**：`k6/confirm-accept-load.js` + `db/seed-load-confirm.sql`（1 隊容量=600 + 600 筆 Invited，**全接受成功 → 純量鎖排隊、不摻「隊伍已滿」拒絕**）。每 VU 一輪：test-login → accept（兩次請求都經 UoW → 都量到 `connection_acquire_ms`；只有 accept 取鎖 → 量 `advisory_lock_wait_ms`）。
- **規模**：VUS **60 / 250 / 500** 三級；每級 **reseed（重置 600 Invited）+ 重建 backend（清連線池、隔離該輪 log）**。
- **量測**：後端 `advisory_lock_wait_ms`（等鎖）+ `connection_acquire_ms`（取得DB連線）+ k6 `http_req_duration`（client 延遲）+ `http_req_failed`（逾時→「隊伍忙碌中」非 2xx；0% = lock_timeout 從未觸發）。

## 結果（2026-09-10，同隊搶鎖，3 級 VUS）

**server 端 p99（ms）— 取得DB連線 vs 等鎖**：

| VUS | 取得DB連線 p95 | 取得DB連線 **p99** | 取得DB連線 max | 等鎖 p95 | 等鎖 **p99** | 等鎖 max | client accept p99 | `http_req_failed` |
|---|---|---|---|---|---|---|---|---|
| 60 | 481 | **493** | 568 | 528 | **541** | 547 | — | **0%** |
| 250 | 927 | **1010** | 1035 | 731 | **748** | 766 | 1.66s | **0%** |
| 500 | 2310 | **2502** | 2555 | 727 | **736** | 764 | 3.26s | **0%** |

> **兩條線走勢相反**：取得DB連線 p99 隨 VUS **一路爆**（493 → 1010 → 2502ms）；等鎖 p99 **持平**（~0.5–0.75s，不隨併發爆）。
> **閉環驗證**（VUS 500）：取得DB連線 p99 **2.50s** + 等鎖 p99 **0.74s** ≈ client accept p99 **3.26s** → client 延遲幾乎全被這兩段解釋掉，而**大宗是取得DB連線**。

## 結論

- **client 延遲多出的秒數 = 等連線，不是等鎖（直接量到，非相減臆測）**：VUS 500 時取得DB連線 p99 **2.50s**、等鎖 p99 只有 **0.74s** → client 3.26s 的絕大部分卡在**進交易前等 Npgsql 連線**。所以**不能拿 client p99 定 lock_timeout**。
- **連線池才是隨負載爆的臨界點（實測坐實）**：取得DB連線 p99 隨 VUS 493 → 1010 → 2502ms 成長（pool cap 100，VUS 500 時 ~400 請求在排隊）；等鎖持平。→ 要提升吞吐先動連線池/水平擴充，不是動鎖。
- **`lock_timeout=5s` 有巨大安全邊際、且不隨併發爆**：等鎖 p99 跨 3 級只在 0.5–0.75s、max 764ms → **5s ≈ 6.5× margin**；3 級全 **0% 誤觸發**、全數 Confirmed。真要縮到 2s 仍有餘。
- **lock_timeout 是「安全閥政策值」，壓測用來驗 margin、不是推導最佳值**：5s 遠高於實測等鎖（百 ms 級）、又在 UX 可忍範圍。
- **正確性守**：3 級 accept 全 200、`http_req_failed`=0% → 全數 Confirmed（超編/跨隊硬條件由 `Test.Integration` 的 deterministic 測守，容量=600≥VUS 故本壓測不觸發超編、不在此驗）。

## 誠實界線

- **我自訂的負載、單一 app 實例、compose Development + 合成 seed** → 方向性,**非 web-scale/prod benchmark**。
- `connection_acquire_ms` 混了「新實體連線建立（冷開）」與「等 pool 釋出」兩者：VUS 60（<pool cap）仍有 ~490ms 主要是**冷開**；真正隨 VUS 爆的增量（250/500 多出的部分）才是 **pool 排隊**。
- 為隔離 pool-rent 才把 PG `max_connections` 設高於 pool cap；正式環境兩者關係不同（先前壓測曾見 PG 端 `too many clients` 飽和）。
- 目的：找對指標（server 端等鎖 + 取得DB連線）並驗 5s margin，不宣稱高吞吐。

## 非範圍（YAGNI）

- 不對 prod 壓、不建常設 Grafana/InfluxDB、不做全端點覆蓋。
- 不改 `lock_timeout` / 連線池設定（只量測、不調參）。

## 附註

- 計時量測：`TeamSlotEditLock.cs`（`advisory_lock_wait_ms`）+ `DbContext.cs`（`connection_acquire_ms`），皆 optional `ILogger` + `Stopwatch`（對 prod 無害；可保留當可觀測性或壓測後還原）。
- 日誌走預設 MEL console → `docker logs` 抓 metric 行；backend 設 `Logging__LogLevel__Default=Warning` + `Infrastructure=Information` 只留 metric、砍框架噪音。
- 環境：兩台同 VPC（172.26.x），SG 允許 k6→app:5230；k6 以 `docker run --network host grafana/k6` 打 app 私有 IP。
