"use client";

import { Crown, Zap, Sparkles } from "lucide-react";
import { useTeamMembers } from "@/hooks/queries/useTeamMembers";

// 已確認隊員組成（人・職業・攻擊・祝福，皇冠標隊長）——已入隊成員/轉讓對象看得到。
export function TeamComposition({ teamSlotId }: { teamSlotId: number }) {
    const { data: members = [] } = useTeamMembers(teamSlotId);
    if (members.length === 0) return null;
    return (
        <div className="flex flex-col gap-1 border-t border-border pt-2">
            {members.map((m, i) => (
                <div key={i} className="text-xs text-muted-foreground flex flex-wrap items-center gap-x-3 gap-y-0.5">
                    <span className="flex items-center gap-0.5 text-foreground">
                        {m.isLeader && <Crown size={12} className="text-amber-500" />}
                        {m.discordName || m.characterName}（{m.job}）
                    </span>
                    <span className="flex items-center gap-0.5"><Zap size={11} /> {m.attackPower}</span>
                    <span className="flex items-center gap-0.5"><Sparkles size={11} /> 祝福 {m.mapleBlessingLevel}</span>
                </div>
            ))}
        </div>
    );
}
