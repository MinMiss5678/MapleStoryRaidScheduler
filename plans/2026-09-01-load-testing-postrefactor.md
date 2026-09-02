# 壓力測試（重構後重跑 · 4 vCPU EC2）

> 輕量 plan（動手前 spec）。定位誠實：**readiness**，方向性數字（非可信 benchmark）。
> 承接 `plans/2026-07-28-load-testing.md`（原始方法論 + 歷史 baseline）；本份記「兩次產品轉向後、在獨立 4 核硬體重跑」。

## 目標

專案自原始壓測後經歷兩次改動，於**獨立 AWS EC2 4 vCPU**重跑 confirm-accept 壓測，確認：
1. **`lock_timeout=5s` 安全邊際**對現行 `ConfirmMemberAsync`（classId 1002 鎖）**依然成立**（高併發下不誤觸發）。
2. **新鮮度衰退（本次新功能）沒把熱路徑推壞**——accept 路徑現在多一筆 `BumpLastAffirmedAsync`（節流單列 `Player` UPDATE），要看它有沒有顯著劣化延遲/提前連線池斷點。
3. 在**專用 4 核硬體**取一組方向性延遲側寫（定位 readiness；不當 prod benchmark/SLA、不跨環境比倍數）。

## 背景（相對原始 baseline 變了什麼）

- **period-less 重指**（2026-08-17 已確認）：舊 `POST /api/register`（classId 1001）退場，classId 1002 鎖搬到 `ConfirmMemberAsync`（accept/approve 定案）。壓測早已重指到此熱路徑。
- **本次新增（要觀察的變數）**：新鮮度衰退把 `BumpLastAffirmedAsync` 加進 accept 路徑 → **每次 accept 多一筆 throttled 單列 `Player` UPDATE**（壓測時玩家 `LastAffirmedAt` 從 NULL 起，首次必真寫）→ 多佔連線一丁點，理論上可能把延遲/連線池斷點**稍微**往前推。這正是要量的。
- **硬體**：獨立 EC2 4 vCPU（ap-northeast-1，`52.198.178.244`）、無其他負載。

## 方法（複用既有環境，零新腳本）

- Stack：`compose.e2e.yaml`（e2e-db + e2e-migrate〔套 migration 至 026〕+ e2e-backend，Development 模式）。
- 自動化：`loadtest-multiround.sh`（每級 **restart e2e-backend 清連線池 + reseed + 跑 k6 + 驗 DB**）。
- 情境：`k6/confirm-accept-load.js` + `db/seed-load-confirm.sql`（1 隊容量=N + N 筆 Invited，全接受成功→純量鎖排隊延遲）。
- 規模：**3 輪 × VUS 60 / 250 / 500**。
- Code：clone repo main（含 freshness bump `8215e3f`）→ 測的是**最新程式碼**。

## 量測

- `accept_invite` p95/p99（client 觀察值，含進交易前 Kestrel/連線池排隊）。
- `http_req_failed`（逾時→「隊伍忙碌中」非 2xx；0% 代表 lock_timeout 從未誤觸發）。
- 正確性硬條件：`confirmed=VUS`、`overcap=0`、`overlap_dup=0`。

## 驗收

- [x] `http_req_failed=0%` 且 `confirmed=VUS`（含 VUS=500）→ lock_timeout 5s 重構+新鮮度後仍無誤觸發。
- [x] 正確性硬條件全守（`overcap=0`、`overlap_dup=0`），3 輪 × 3 級無例外。
- [x] 延遲維持合理範圍、尾端無長尾（新鮮度 bump 未顯著劣化熱路徑）。

> 延遲數字為**方向性**（compose Development + 合成 seed，非 prod benchmark），不做跨環境絕對比較。有連續性、值得看的是**是非結論**：重構＋新鮮度後與先前一致——**0% 失敗、正確性守**（見下）。

## 結果（EC2 4 vCPU 專用，ap-northeast-1，2026-09-01，3 輪範圍）

> 環境坑（記錄）：Amazon Linux 2023 的 docker 內建 buildx **0.12.1 < compose v5.5.0 需要的 0.17** → `compose build` 失敗，手動覆蓋成 buildx v0.17.1 才過。首次兩支腳本因健康檢查競態**併發跑同一 stack 污染數字**，已丟棄、重跑單一乾淨 pass。

`accept_invite`（2 輪平均值；p99 以 `--summary-trend-stats` 補抓）：

| VUS | avg | p95 | p99 | http_req_failed | 正確性（confirmed / overcap / overlap_dup） |
|---|---|---|---|---|---|
| 60 | ~345ms | ~452ms | ~566ms | **0%**（0/120） | 60 / 0 / 0 |
| 250 | ~1.09s | ~1.44s | ~1.45s | **0%**（0/500） | 250 / 0 / 0 |
| 500 | ~1.85s | ~2.63s | ~2.68s | **0%**（0/1000） | 500 / 0 / 0 |

（p99 幾乎貼著 p95 → 尾端很緊、無長尾。lock_timeout 5s 有巨大安全邊際：最慢 p99 才 2.68s。）

### 對照與結論
- **驗收全過**：3 輪 × 3 級（9 次 k6、約 4,860 次 accept）**`http_req_failed`＝0%、`confirmed`＝VUS（含 500）、overcap=0、overlap_dup=0** → **`lock_timeout=5s` 在 period-less 重構 + 新鮮度 bump 之後依然從未誤觸發**，超編/跨隊硬條件全守。
- **新鮮度 bump 沒把熱路徑推壞**：accept 路徑多一筆 throttled 單列 `Player` UPDATE，但即便 VUS=500，p95 僅 ~2.6s、0% 失敗、全數 Confirmed → 二階效應如預期、不成問題。
- **延遲側寫（方向性、不跨環境比）**：專用 4 核上，最慢 VUS=500 的 p99 才 ~2.68s、尾端緊無長尾。這是「lock_timeout 5s 有一倍以上安全邊際」的直接佐證，不拿來當 prod SLA。
- 連線池斷點性質不變（Npgsql 100 cap；methodology 每級 restart 清池），本次未當獨立斷點量。

## 非範圍（YAGNI）

- 不對 prod 壓、不建常設 Grafana/InfluxDB、不做全端點覆蓋。
- 不改 `lock_timeout` / 連線池設定（本次只量測，不調參）。
