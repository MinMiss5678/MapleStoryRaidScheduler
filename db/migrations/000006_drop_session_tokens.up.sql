-- 移除 session 存的 Discord token：AccessToken/RefreshToken/Expiry 在 session 與 token TTL 解耦後
-- 已無任何消費者（登入用的是記憶體裡的 OAuth token、角色走 bot token）→ 存明文憑證只是安全負擔，移除。
ALTER TABLE "Session"
    DROP COLUMN "AccessToken",
    DROP COLUMN "RefreshToken",
    DROP COLUMN "Expiry";
