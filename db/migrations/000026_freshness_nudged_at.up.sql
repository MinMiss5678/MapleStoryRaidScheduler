-- 000026 常設時段新鮮度 nudge 追蹤（階段二；見 plans/2026-09-01-availability-freshness-decay.md）。
--   Player.FreshnessNudgedAt — 上次發「快過期」提醒 DM 的時戳。
--   去重：nudge 條件含 FreshnessNudgedAt IS NULL OR <= LastAffirmedAt（＝上次提醒後又有活動才會再提醒）。
ALTER TABLE "Player" ADD COLUMN "FreshnessNudgedAt" timestamptz NULL;
