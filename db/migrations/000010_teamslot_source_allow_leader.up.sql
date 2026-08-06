-- 000010 leader-led Phase 1b：放寬 chk_teamslot_source 允許 'leader'（隊長開隊）。
-- EXPAND：加值不減值——舊 'auto'/'admin' 仍合法；移除 'auto' + 收斂成 {leader,admin} 延後 contract（1c/3）。
ALTER TABLE "TeamSlot" DROP CONSTRAINT chk_teamslot_source;
ALTER TABLE "TeamSlot" ADD CONSTRAINT chk_teamslot_source CHECK ("Source" IN ('auto', 'admin', 'leader'));
