-- 000025 常設可用時段新鮮度衰退（anti-stale opt-in；見 plans/2026-09-01-availability-freshness-decay.md）。
--   Player.LastAffirmedAt                  — 玩家最後一次「組隊實質動作」時戳（心跳）。
--                                            NULL = 視為永久新鮮、不過濾（backfill 保守，避免上線瞬間誤砍既有玩家）。
--   SystemConfig.AvailabilityFreshnessDays — admin 可調的新鮮度門檻（天），預設 30（對齊 admin Session 30 天 sliding、Outbox 30 天保留）。
ALTER TABLE "Player"       ADD COLUMN "LastAffirmedAt"             timestamptz NULL;
ALTER TABLE "SystemConfig" ADD COLUMN "AvailabilityFreshnessDays" integer NOT NULL DEFAULT 30;
