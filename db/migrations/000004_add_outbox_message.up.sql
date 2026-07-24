-- Transactional Outbox：post-commit 副作用改「意圖寫進同一筆交易」→ dispatcher 讀已提交列去投遞。
-- 解兩個 gap：(1) in-process AfterCommit 在 commit 後 crash 會遺失；(2) 寫在 API、消費在 bot，
-- in-process 事件跨不了行程。outbox 靠共享 DB 的已提交列，跨行程 + crash-safe。

CREATE TABLE "OutboxMessage" (
    "Id"           bigserial   PRIMARY KEY,
    "Type"         text        NOT NULL,          -- 事件類型（如 ConfigChanged）→ 對應 handler
    "Payload"      jsonb       NOT NULL,           -- 事件內容（自描述、可稽核；handler 可忽略）
    "OccurredAt"   timestamptz NOT NULL DEFAULT now(),
    "ProcessedAt"  timestamptz,                    -- 投遞成功才填；NULL = 待處理
    "AttemptCount" integer     NOT NULL DEFAULT 0, -- 重試次數（毒訊息判斷）
    "LastError"    text                            -- 最後一次失敗訊息（觀測用）
);

-- partial index：只索引「待處理」列 → dispatcher 撈取快，即使歷史列堆積也不拖
CREATE INDEX "ix_outbox_unprocessed" ON "OutboxMessage" ("Id") WHERE "ProcessedAt" IS NULL;
