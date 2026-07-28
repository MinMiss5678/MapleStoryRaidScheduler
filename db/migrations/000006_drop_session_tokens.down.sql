-- 還原欄位（可逆用；資料無法復原，加 default 讓既有列可補）
ALTER TABLE "Session"
    ADD COLUMN "AccessToken"  text        NOT NULL DEFAULT '',
    ADD COLUMN "RefreshToken" text        NOT NULL DEFAULT '',
    ADD COLUMN "Expiry"       timestamptz NOT NULL DEFAULT now();
