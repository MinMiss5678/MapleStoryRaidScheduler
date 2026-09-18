# Message Queue 計畫

> **狀態（2026-07-25）：Phase 1 實作過後移除**。曾以 Redis Streams（`OutboxRelay` + `OutboxStreamConsumer`）做出 relay/consumer group/PEL 重投並整合測通過，但實測在現況（replicas=1、單 consumer、單事件）對截止日通知**無實質價值**——同結果、多一跳、仍輪詢，真正補洞的是 outbox 本身。依 YAGNI 移除，恢復 `OutboxDispatcher`（outbox → 直接呼叫 handler）。本 spec 保留供未來需要 fanout / 獨立擴 consumer / DLQ 時再上。
>
> 輕量 plan（動手前的 spec）：目標 / 範圍 / 決策 / 驗收 / 工時。做完可丟；穩定規則再收進 `docs/`。
> **定位誠實**：readiness。現況 replicas=1、outbox + polling 已滿足可靠投遞——MQ 是「要 push 低延遲 / fanout / 獨立擴 consumer / per-consumer DLQ」時的下一步，**不是現在的 bug**。

## 目標

把 outbox 的「投遞」從「dispatcher **直接 in-process 呼叫 handler**」升級成「**發布到 message queue、consumer 訂閱**」→ 生產者/消費者解耦、push（低延遲、免輪詢）、每 consumer 獨立重試/DLQ、可跨服務 fanout。

**★ 最關鍵的誠實點（多數人搞錯）**：**MQ 不取代 outbox，兩者組合。**
- outbox 解「**事件與資料原子**、避免 dual-write」（在交易內捕獲意圖）。
- MQ 解「**分發 / push / fanout / DLQ**」。
- 合起來就是教科書的 **Transactional Outbox + Message Relay** 架構。承 [[2026-07-24-transactional-outbox]]。

## 現況（已驗證，承 outbox PR #4）

`OutboxDispatcher` 輪詢 outbox 表（`FOR UPDATE SKIP LOCKED`，5s）→ **直接 in-process 呼叫** `IOutboxHandler`（都在 bot 行程）。
限制：consumer 綁死 bot 行程、無 fanout、無 per-consumer DLQ、polling 有 5s 延遲 + DB 負載。

## 範圍（分階段，右尺寸）

### Phase 1：outbox → MQ relay
- `OutboxDispatcher` 改成 **relay**：讀已提交 outbox 列 → **發布到 MQ** → **發布成功才標 `ProcessedAt`**。
- consumer 從 MQ 訂閱、處理、**ack**。
- **端到端 at-least-once**：outbox 保證「至少發布一次」到 MQ；MQ 的 consumer-group + ack 保證「至少處理一次」；handler 仍需**冪等**（承 outbox 決策）。

### Phase 2（選配）：更多 async 工作流上 MQ
- 例如把 Discord 通知 / 重排程從 in-process background job 改成 MQ consumer（獨立擴、重試、DLQ）。
- 另一角度（併發序列化）：現行 `ConfirmMemberAsync`（入隊定案，classId 1002）同步在請求內跑 + advisory lock 防超編；理論上可改「enqueue → 單一 per-team consumer 序列處理」用**佇列天然序列化**取代 advisory lock。但**吞吐天花板不變、且改成 async UX** → 詳見下節「MQ 對入隊定案的角色」。（註：舊 `AutoAssignAsync` 自動排團引擎已於 period-less 退役，此處改指現行熱路徑。）

### 非範圍（YAGNI）
- 不引入 event sourcing / saga orchestration。
- 不做多 broker 抽象層（直接用選定 broker 的 client；避免過早抽象）。
- runtime 不碰 Dapper。

## MQ 對「入隊定案 `ConfirmMemberAsync`」的角色 — 連線壓力 vs 吞吐（2026-09-11 壓測後補）

> 承 `plans/2026-09-01-load-testing-postrefactor.md`。這節釐清「把 confirm 改走 MQ」到底解什麼、不解什麼——結論仍 YAGNI，但用實測把理由釘死。

- **現況**：confirm 同步在請求內跑：取連線 → advisory lock（classId 1002 / teamSlotId）→ 重讀 count vs 容量 → xmin flip → commit。**等待期間連線一直被佔著**。
- **壓測實測（2 台 EC2，VUS 500 同隊）**：
  - 等鎖 p99 持平 **~0.7s**（鎖不是瓶頸、5s `lock_timeout` ~6.5× 餘裕、0% 誤觸發）。
  - client 延遲大宗在**取得DB連線**，但**不是 pool 槽位不足**：pool 100→600 取得DB連線 p99 幾乎沒降（2502→2261ms）、CPU 峰值仍 23% idle → 主因是「爆量現開連線的建立成本」。
  - **根本天花板是「序列化吞吐」**：pool 調大只是把等待從連線搬到鎖（等鎖 0.7s→3.8s、client 反而更慢）。
- **MQ 能解 / 不能解**：
  - ✅ **能**：confirm 改「enqueue → 秒回 → 單一（per-team）consumer 序列處理」→ **請求秒回、連線立刻釋放** → 這是**唯一能在等待期間放掉連線的做法**（換鎖形式如原子條件寫，等待時仍佔著連線）；附帶削峰、背壓、重試 / DLQ。
  - ❌ **不能**：**吞吐天花板不變**（consumer 仍序列化，佇列天然序列化 = 換載體、不是變快）；且把「按接受 → 當下知道進隊 / 隊滿」變**非同步** → 賠即時 UX（要補通知 / 輪詢 + 「意圖 vs 已確認」狀態機）。
- **連線壓力的第一手不是 MQ**：要緩解連線佔用 / `too many clients`，先動**連線層**——調 `max_connections` 留 headroom / 釘 pool 上限 / PgBouncer 收斂（backend + bot + dispatcher 多 pool 共用同一 PG 額度）。MQ 是「請求處理模型 sync→async」，降連線佔用只是副作用。
- **結論（YAGNI）**：真實幾十人、單團 ~6 位 → 序列化與連線壓力都碰不到；那個「爆」是壓測灌 500 人同團造的假象。**現況 advisory lock 最簡單好懂**；MQ 對 confirm 是「要 async / 削峰 / 獨立擴 consumer」時才上，不是現在的 bug。

## 關鍵決策（動手前拍板）

### ★ broker 選型：Redis Streams vs RabbitMQ vs Kafka

| broker | 語意 / 特性 | 這專案 |
|---|---|---|
| **Redis Streams（建議）** | 已在 stack；consumer group = 競爭消費 + ack + **PEL**（Pending Entries List）重投；輕量 | **選它**——零新基礎設施、複用 `IConnectionMultiplexer`、真 at-least-once 語意 |
| RabbitMQ | 正統 **AMQP broker**、exchange 路由、成熟 DLX/DLQ、per-message ack | 需新增 infra；要「正統 broker / 複雜路由」或 shop 已用才選 |
| Kafka | 高吞吐 **log**、partition/offset、可重播、保留期 | 這規模**過重**；Kafka 是 log 不是 queue，語意不同 |

> **誠實區分**：**queue（RabbitMQ）vs log（Kafka）vs Redis Streams（輕量混合）** 語意不同，不能混為一談。

### at-least-once + 冪等 + DLQ
- consumer group **ack**；處理失敗 → 重試 N 次（PEL 重投）→ 超上限進 **DLQ**（死信 stream）+ 告警。handler **冪等**吸收重複。

### ordering
- 單一 stream 內有序；競爭消費會打散跨 consumer 的順序。需 per-key 有序 → 用 key 分 stream 或單 consumer。本用例（喚醒 job）**不需嚴格序**。

### relay 的 at-least-once（同 dispatcher 現況形狀）
- relay「**發布 MQ 成功才標 outbox processed**」；發布後、標 processed 前崩 → 重啟重發（重複）→ 靠 consumer 冪等吸收。

## 基礎設施

- **Redis Streams**：複用現有 `redis`（compose / k8s 已有）——**零新增服務**。
- consumer 註冊為 `BackgroundService`，用 consumer group 做 competing consumer。
- DLQ = 另一個 stream + 監控（Seq 告警）。

## 驗收（2026-07-29 對照現有 code/測試核實）

> **這份驗收清單描述的是 Phase 1 實作當時（已 revert 前）驗證過的行為，不是目前 main 的現況。**
> 全庫搜尋 `OutboxRelay|OutboxStreamConsumer|Redis Streams|StreamAdd|StreamRead`，**main 上查無這些類別**——git log 確認 `10017fa` 做過、`4ecebc7` 明確 revert（YAGNI），恢復成 `OutboxDispatcher` 直接呼叫 handler。以下 5 項當初實作時**有整合測試跑過並通過**，但功能本身已撤銷，現在都**不成立於目前 main**：

- [ ] ~~outbox 列 → relay 發布到 stream → consumer 收到並處理 → ack~~（做過、測過、已 revert）
- [ ] ~~consumer 崩在 ack 前 → 訊息留在 PEL → 重投~~（做過、測過、已 revert）
- [ ] ~~多 consumer（競爭）→ 各分不相交訊息~~（做過、測過、已 revert）
- [ ] ~~處理失敗達上限 → 進 DLQ~~（做過、測過、已 revert）
- [ ] ~~relay 發布後、標 processed 前崩 → 重發~~（做過、測過、已 revert）

**這份計畫的正確狀態是「保留供未來需要時參考」，不是「待完成」——不要誤判成還沒做，也不要誤判成現在還在跑。**

## 工時估
- Phase 1（relay 改造 + consumer + consumer group + 整合測）≈ 1~1.5 天。
- Phase 2 依搬幾個工作流而定。
