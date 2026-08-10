-- 000012 leader-led：玩家自助退隊。
-- 加 'Left' 終態——語意上「自願退出」（別於 Rejected＝被拒/拒絕），行為同 Rejected：不占容量、可重邀。
-- 分出 Left 是為了未來「退團率可靠度信號」能區分自退 vs 被拒（見 plans/2026-08-07-leave-team-and-candidate-dedup.md）。
ALTER TABLE "TeamSlotCharacter" DROP CONSTRAINT chk_tsc_status;
ALTER TABLE "TeamSlotCharacter" ADD CONSTRAINT chk_tsc_status
    CHECK ("Status" IN ('Applied', 'Invited', 'Confirmed', 'Rejected', 'Left'));

-- 退隊時刻（只有 Left 列有值）：供退團率窗口分子 + 未來「離打王多近才烙跑」時機權重（LeftAt − SlotDateTime）。
ALTER TABLE "TeamSlotCharacter" ADD COLUMN "LeftAt" timestamptz;
