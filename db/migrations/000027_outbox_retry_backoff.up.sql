-- 000027 Outbox 重試退避 + 有效期限（見 plans/2026-09-22-outbox-retry-backoff.md）。
--   NextRetryAt   — 失敗後下次可派發時間（指數退避）；NULL＝立即可派發。
--   DeliverBefore — 有效期限，過了就不送（dispatcher 標 expired）；NULL＝預設建立後 24h，一律不超過建立後 24h。
ALTER TABLE "OutboxMessage"
    ADD COLUMN "NextRetryAt"   timestamptz NULL,
    ADD COLUMN "DeliverBefore" timestamptz NULL;

-- 撈取條件多了 NextRetryAt 到期過濾 → 索引改含 NextRetryAt，退避中的未來列不必反覆掃過
DROP INDEX "ix_outbox_unprocessed";
CREATE INDEX "ix_outbox_unprocessed" ON "OutboxMessage" ("NextRetryAt" NULLS FIRST, "Id")
    WHERE "ProcessedAt" IS NULL;
