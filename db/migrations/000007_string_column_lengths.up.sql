-- 000007_string_column_lengths.up.sql
-- 把有輸入驗證（DTO MaxLength）的字串欄位從 text 收斂成 varchar(N)，讓「DB 欄位」與「request DTO」
-- 兩層長度一致（defense in depth）。長度對齊 Application/DTOs：
--   Character.Id/Name/Job ← CharacterRequest（5 / 20 / 20；Id＝遊戲內角色代碼，上限 5）
--   Boss.Name            ← BossRequest（50）
--   BossTemplate.Name    ← BossTemplateRequest（50）
-- 註：text→varchar(N) 是二進位相容轉型（只加長度檢查、不重寫資料表示），Character."Id" 雖為
-- PK 且被 CharacterRegister / TeamSlotCharacter 的 FK 參照，仍可安全 ALTER。
ALTER TABLE "Character"
    ALTER COLUMN "Id"   TYPE varchar(5),
    ALTER COLUMN "Name" TYPE varchar(20),
    ALTER COLUMN "Job"  TYPE varchar(20);

ALTER TABLE "Boss"
    ALTER COLUMN "Name" TYPE varchar(50);

ALTER TABLE "BossTemplate"
    ALTER COLUMN "Name" TYPE varchar(50);
