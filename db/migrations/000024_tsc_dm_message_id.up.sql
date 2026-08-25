-- 000024 邀請 DM 的 message id（dm-revoke-cleanup）：自動撤邀時據此編輯被邀者 DM（消死按鈕）。
-- 只有「帶按鈕的邀請 DM」會回寫此欄（bot 送出後）；純通知 DM 不動。NULL = 尚未送出/非邀請。
ALTER TABLE "TeamSlotCharacter" ADD COLUMN "DmMessageId" bigint NULL;
