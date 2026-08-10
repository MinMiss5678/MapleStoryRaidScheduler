-- 000014 leader-led：隊長轉讓（需對方同意）。加 PendingLeaderDiscordId——提議轉讓的目標，等對方接受才搬進 LeaderDiscordId。
-- 一隊同時至多一個待處理轉讓（單欄表達，不用新表）。ON DELETE SET NULL：目標退公會 → 提議自動作廢。
ALTER TABLE "TeamSlot"
    ADD COLUMN "PendingLeaderDiscordId" bigint REFERENCES "Player"("DiscordId") ON DELETE SET NULL;
