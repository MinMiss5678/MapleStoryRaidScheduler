-- 角色偏好王（複選）：候選匹配的軟訊號來源。純多對多，無額外欄位 → 複合主鍵。
-- 用途：隊長看候選時，偏好本隊王的角色排前 + 標記；不做硬篩（守 boss-agnostic）。
CREATE TABLE "CharacterPreferredBoss" (
    "CharacterId" text    NOT NULL REFERENCES "Character"("Id") ON DELETE CASCADE,
    "BossId"      integer NOT NULL REFERENCES "Boss"("Id")      ON DELETE CASCADE,
    PRIMARY KEY ("CharacterId", "BossId")
);
