DROP INDEX "ix_outbox_unprocessed";
CREATE INDEX "ix_outbox_unprocessed" ON "OutboxMessage" ("Id") WHERE "ProcessedAt" IS NULL;
ALTER TABLE "OutboxMessage"
    DROP COLUMN "DeliverBefore",
    DROP COLUMN "NextRetryAt";
