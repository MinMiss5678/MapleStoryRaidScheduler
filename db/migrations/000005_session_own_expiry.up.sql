-- session 有自己的有效期（我的授權政策），與 Discord OAuth token TTL 解耦。
-- 原本 session 靠 AccessToken 過期 + 刷新續命 → 把第三方 token 壽命當成 session 政策；
-- 改成 SessionExpiry 自己管，session 驗證不再依賴 Discord 端點。

ALTER TABLE "Session" ADD COLUMN "SessionExpiry" timestamptz NOT NULL DEFAULT now();
-- 既有 session 給 30 天新鮮期（避免加欄位就全數失效）
UPDATE "Session" SET "SessionExpiry" = now() + interval '30 days';
-- 之後由 app（SessionRepository.CreateAsync）明確設定，移除 default
ALTER TABLE "Session" ALTER COLUMN "SessionExpiry" DROP DEFAULT;
