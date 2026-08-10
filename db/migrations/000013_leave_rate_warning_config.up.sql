-- 000013 leader-led：候選「退團率」可靠度信號的 admin 設定（見 plans/2026-08-07-leave-team-and-candidate-dedup.md Feature 1b）。
-- 預設關（Enabled=false）——名譽信號敏感、公會自決。三旋鈕：時間窗(月) / 門檻率(%) / 最小樣本數。
ALTER TABLE "SystemConfig"
    ADD COLUMN "LeaveRateWarnEnabled" boolean NOT NULL DEFAULT false,
    ADD COLUMN "LeaveRateWindowMonths" integer NOT NULL DEFAULT 3,
    ADD COLUMN "LeaveRateThreshold"    integer NOT NULL DEFAULT 30,  -- 百分比：退團率 ≥ 此值才示警
    ADD COLUMN "LeaveRateMinSample"    integer NOT NULL DEFAULT 5;   -- 參加 < 此數不算率（避免小樣本誤判）
