# Message Queue 計畫

> **狀態（2026-07-25）：Phase 1 已實作**。`OutboxRelay`（outbox → `XADD` stream）+ `OutboxStreamConsumer`（consumer group + `XACK` + `XAUTOCLAIM` 重投）取代原 `OutboxDispatcher`；broker = Redis Streams。整合測（relay 發布/標 processed、SKIP LOCKED、consumer ACK、PEL 重投、無 handler 丟棄）齊。
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
- 另一角度：報名的 `AutoAssignAsync` 目前同步在請求內跑 + advisory lock 防併發；改成「報名 → enqueue assign 工作 → 單一 consumer 序列處理」可用**佇列天然序列化**取代 advisory lock（但改 UX 語意成 async，需另評估）。

### 非範圍（YAGNI）
- 不引入 event sourcing / saga orchestration。
- 不做多 broker 抽象層（直接用選定 broker 的 client；避免過早抽象）。
- runtime 不碰 Dapper。

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

## 驗收

- [ ] outbox 列 → relay 發布到 stream → consumer 收到並處理 → ack（Testcontainers Redis 整合測）。
- [ ] consumer 崩在 ack 前 → 訊息留在 PEL → 重投（at-least-once）。
- [ ] 多 consumer（競爭）→ 各分不相交訊息（consumer group）。
- [ ] 處理失敗達上限 → 進 DLQ。
- [ ] relay 發布後、標 processed 前崩 → outbox 未標 processed → 重發（consumer 冪等吸收，不重複生效）。

## 工時估
- Phase 1（relay 改造 + consumer + consumer group + 整合測）≈ 1~1.5 天。
- Phase 2 依搬幾個工作流而定。
