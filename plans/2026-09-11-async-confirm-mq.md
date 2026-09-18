# 非同步入隊定案（confirm async via MQ）— B 方案計畫

> **狀態誠實：YAGNI spec（設計底稿，非現在的 bug）**。現況「同步 confirm + advisory lock」對現規模綽綽有餘（見 `plans/2026-09-01-load-testing-postrefactor.md`）。本份是「**若哪天要削 backend 連線/序列化峰值、或走事件驅動**」時的動手前設計,先寫清楚**解什麼、不解什麼、代價、觸發條件**。
> 承 `plans/2026-07-25-message-queue.md`（broker 選型 + 「MQ 對入隊定案的角色」一節）。

## 要處理的問題

- **現況**：`ConfirmMemberAsync`（入隊定案）**同步在請求內**跑:取連線 → advisory lock（classId 1002 / teamSlotId）→ 重讀 confirmed 數 vs 容量 → xmin flip → commit。**等待期間請求一直佔著一條 DB 連線**。
- **壓測觀察**（承 load-testing plan）:VUS 500 同隊時 client 延遲大宗在**取得DB連線**、天花板是**序列化吞吐**;backend pool 撐大還會把外部連線（migrate/psql/bot）擠到 `too many clients`。
- **B 方案要解的**:把「同步等待佔連線」換成「**入佇列秒回、由有限 consumer 消化**」→ **降 backend 連線/執行緒峰值** + 背壓 + 可靠重試。
- **⚠️ B 方案「不」解的**:**吞吐天花板不變**（consumer 仍序列化);它只降**峰值連線佔用**,並把同步 UX 換成 async。

## 目標

1. confirm 請求改「**enqueue 意圖 → 秒回 202 → 消費者處理**」→ 請求不再持連線等鎖。
2. **正確性不變**:不超編、跨隊不重複（沿用 `uq_tsc_confirmed_overlap` 當最後防線）。
3. 玩家仍能知道結果（進隊 / 隊滿）——透過既有 outbox→DM 通知 + 前端輪詢/SSE。

## 架構 / 範圍

- **broker**:**Redis Streams**（複用現有 `redis`,零新基礎設施;承 MQ plan 選型)。
- **producer**（API 端點）:驗身分 + 基本檢查 → `XADD stream:confirm:{teamSlotId}` `{teamSlotId, memberId, action, idempotencyKey}` → 回 **202 Accepted**（「處理中」）。
- **consumer**（`BackgroundService`,同機/同 pod 起,未來要獨立擴才拆）:
  - **per-team 保序**:stream key 按 `teamSlotId` 分（或 consumer group 依 teamSlotId 綁定分區）→ 同團序列、跨團平行。
  - 每筆跑現有 `ConfirmMemberAsync` 核心:重讀 confirmed 數 vs 容量 → xmin flip / `uq` 擋重複。
  - **序列化來源改為「佇列 + 單一 partition consumer」**（佇列天然序列化）→ 可**省 advisory lock**(或保留當 belt-and-suspenders)。
- **結果回傳**:consumer 處理完 → 寫既有 **outbox（TeamNotification）** → bot 送 DM「你進隊了 / 隊伍已滿」;前端另用**輪詢/SSE**更新 UI（承 `plans/2026-09-02-leader-led-view-polling.md`)。
- **冪等**:`idempotencyKey`(已有 IdempotencyMiddleware 概念)→ consumer 冪等吸收重投（at-least-once)。

## 關鍵決策（動手前拍板）

- **ordering**:confirm 必須 **per-team 有序**（容量檢查是團層計數)→ 按 `teamSlotId` 分 stream 或分區綁 consumer;**不能用會打散序的競爭消費跨團混讀同一 key**。
- **容量檢查仍在 consumer 端**:enqueue **不代表**入隊成功 →「排到你時隊已滿」要能**回拒 + 通知**。
- **狀態機**:需表達「**意圖已受理 vs 真的確認**」——加中間態（如 `TeamSlotCharacter.Status = Queued/Pending`)或用 outbox intent;並給前端查詢「我的請求到哪了」。
- **durability**:Redis 記憶體優先 → 開 **AOF/RDB**,避免重啟掉佇列;或 intent **先落 DB 再 relay 到 stream**（但那又回到要寫 DB、部分抵銷「省連線」)。
- **失敗 / DLQ**:consumer 重試 N 次 → 進 DLQ（死信 stream）+ 告警;handler 冪等。

## 非範圍（YAGNI）

- 不改跨隊唯一索引（`uq_tsc_confirmed_overlap` 仍是最後防線)。
- 不引入 event sourcing / saga orchestration。
- 不為此上 Kafka/RabbitMQ（Redis Streams 足夠;承 MQ plan)。
- **不加機器**:consumer 是背景程序,同機/同 pod 甚至 in-process;broker 複用 Redis。

## 驗收

- [ ] 大量併發 accept → 請求**秒回 202**、backend **連線峰值明顯低於同步版**（量 `connection_acquire_ms` / `pg_stat_activity` 峰值對比)。
- [ ] **正確性**:不超編、跨隊不重複（沿用 `Test.Integration` 併發測 + 加 consumer 冪等/重投測)。
- [ ] 「**排到你時隊已滿**」→ 正確回拒 + 通知玩家。
- [ ] consumer **崩在 ack 前** → 訊息留 PEL → 重投、冪等吸收（不重複入隊)。
- [ ] 玩家從 DM/前端**確實收到最終結果**（進隊 / 隊滿)。

## 工時 / 風險

- 估 **~2–3 天**（producer 改造 + consumer + 分區保序 + 狀態機 + 結果通知 + 測)。
- **風險**:async UX 改動大（**前端要一起改**)、狀態機複雜度、Redis 持久性設定。

## 誠實界線 / 何時才做

- **現規模（幾十人、單團 ~6 位)碰不到峰值 → 純 YAGNI**;壓測那個「爆」是灌 500 人同團造的假象。
- **觸發條件**（滿足才做):真的出現「confirm 熱路徑把 backend pool 撐爆、擠掉外部連線」**或**「要事件驅動 / 獨立擴 consumer」。
- **先於 B 的更便宜手段**（先上這些):連線層——`max_connections` 留 headroom / 釘 pool 上限 / **PgBouncer**。**B 是「連線層擋不住 + 又要 async」才輪到**,不是第一手。
