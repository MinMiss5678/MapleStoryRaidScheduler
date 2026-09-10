# 壓力測試（重構後重跑 · 埋點量「真正等鎖」· 2 台 EC2）

> 輕量 plan（動手前 spec）+ 結果。定位誠實：**readiness**、方向性數字（非 prod benchmark/SLA）。
> 承接 `plans/2026-07-28-load-testing.md`（原始方法論）；本份記「兩次產品轉向後、**加上後端埋點量真正等鎖時間**、在 2 台 EC2（k6 與 app 隔離）重跑」。

## 這次的關鍵升級：量「真正等鎖」，不只 client 延遲

先前只看 k6 的 `http_req_duration`（client 觀察值）——但那**包含進交易前的 Kestrel/連線池排隊**，分不出「真正等 advisory lock」有多久。用它推 `lock_timeout` 是**拿錯指標**（client 延遲 ≠ 等鎖）。
本次在 `TeamSlotEditLock.AcquireAsync` 用 **Stopwatch 圈住 `pg_advisory_xact_lock`**（從發出取鎖 → 拿到鎖），emit `advisory_lock_wait_ms` 到 Serilog → 讀出**真正等鎖分布**，跟 client 延遲並排比。

## 目標

1. 確認 `lock_timeout=5s` 對現行 `ConfirmMemberAsync`（classId 1002 鎖）**安全邊際成立**（高併發不誤觸發）。
2. **量出「真正等鎖」p99/max**（非 client 延遲）→ 才有正確的指標定 `lock_timeout`。
3. 方向性延遲側寫（readiness，不當 prod SLA、不跨環境比倍數）。

## 方法（2 台 EC2，k6 與受測系統隔離）

- **硬體**：2 台 AWS EC2（ap-northeast-1，各 4 vCPU / 16GB）。
  - app（受測系統）：`54.178.24.4` / 私有 `172.26.13.217`——backend（埋點版 `:locktest`）+ Postgres 18 + Redis + Seq。
  - k6（負載產生器）：`18.182.27.93`——**專機跑 k6，不與 app 搶 CPU**（壓測基本原則，避免污染）。
- **Stack**：`compose.locktest.yaml`（backend inline env、Development 模式開 test-login；無 bot/cloudflared）。schema 套 `db/migrations/*.up.sql`（26 支）。
- **情境**：`k6/confirm-accept-load.js` + `db/seed-load-confirm.sql`（1 隊容量=600 + 600 筆 Invited，**全接受成功 → 純量鎖排隊、不摻「隊伍已滿」拒絕**）。
- **規模**：VUS **60 / 250 / 500** 三級,同時 accept 同一 `teamSlotId`、全搶 classId 1002 這把鎖;每級 **reseed（重置 600 Invited）+ 重建 backend（清連線池、隔離該輪 log）**。
- **量測**：後端 `advisory_lock_wait_ms`（真等鎖）+ k6 `http_req_duration`（client 延遲）+ `http_req_failed`（逾時→「隊伍忙碌中」非 2xx；0% = lock_timeout 從未觸發）。

## 結果（2026-09-10，同隊搶鎖，3 級 VUS）

**真正等鎖 `advisory_lock_wait_ms`（後端埋點，ms）**：

| VUS | avg | p95 | p99 | max | `http_req_failed` | 正確性 confirmed / overcap / overlap_dup |
|---|---|---|---|---|---|---|
| 60 | 265 | 424 | 437 | 443 | **0%** | 60 / 0 / 0 |
| 250 | 468 | 617 | 636 | 653 | **0%** | 250 / 0 / 0 |
| 500 | 474 | 643 | 658 | 680 | **0%** | 500 / 0 / 0 |

**client 延遲對照（VUS=500）**：`http_req_duration` p99 = **2.69s**、max 2.70s —— 比同一輪真等鎖 p99（658ms）**多 ~2s**，那 ~2s 全是進交易前的連線池／Kestrel 排隊。

> **等鎖 p99 隨併發只從 437ms 緩升到 658ms（不隨 VUS 爆掉）**、max 680ms；而 client 延遲卻一路漲到 ~2.7s。→ 5s `lock_timeout` 在最大壓力仍有 ~7.4× margin、3 級全 **0% 誤觸發**、正確性硬條件全守。

## 結論

- **client 延遲 ≠ 等鎖（用數字坐實）**：client p99 **2.69s**、真正等鎖 p99 只有 **658ms** → 差的 ~2s **全是進交易前的連線池/Kestrel 排隊**，不是等鎖。所以**不能拿 client p99 定 lock_timeout**。
- **`lock_timeout=5s` 有巨大安全邊際、且不隨併發爆**：真等鎖 p99 跨 3 級只從 437ms（VUS 60）緩升到 658ms（VUS 500）、max 680ms → **5s ≈ 7.4× margin**;3 級全 **0% 誤觸發**、全數 Confirmed。對比 client 延遲一路漲到 ~2.7s → 瓶頸在連線池排隊、不是鎖。
- **lock_timeout 是「安全閥政策值」，壓測用來驗 margin、不是推導最佳值**：5s 遠高於正常等鎖（實測百 ms 級）、又在 UX 可忍範圍。真要縮到 2s 仍有餘（max 680ms）。
- **正確性守**：`overcap=0`、`overlap_dup=0` → period-less 重構 + 新鮮度 bump 後,超編/跨隊硬條件無破。

## 誠實界線

- **我自訂的負載、單一 app 實例、compose Development + 合成 seed** → 方向性,**非 web-scale/prod benchmark**。
- 埋點量的是「等鎖」本身;連線池斷點性質不變（Npgsql 100 cap）,本次未當獨立斷點量。
- 目的：找對指標（server 端等鎖）並驗 5s margin,不宣稱高吞吐。

## 非範圍（YAGNI）

- 不對 prod 壓、不建常設 Grafana/InfluxDB、不做全端點覆蓋。
- 不改 `lock_timeout` / 連線池設定（只量測、不調參）。

## 附註

- 埋點：`TeamSlotEditLock.cs` 的 optional `ILogger` + Stopwatch（對 prod 無害;可保留當可觀測性或壓測後還原）。
- 環境：兩台同 VPC（172.26.x），SG 允許 k6→app:5230;k6 以 `docker run --network host grafana/k6` 打 app 私有 IP。
