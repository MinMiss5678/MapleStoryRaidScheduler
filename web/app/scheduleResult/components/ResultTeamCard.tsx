"use client";

import { TeamSlot } from "@/types/raid";
import { formatDateTime } from "@/utils/dateTimeUtil";
import { Users, Calendar } from "lucide-react";

interface ResultTeamCardProps {
    teamSlot: TeamSlot;
    bossName: string;
    isMyTeam: boolean;
    requireMembers?: number;
}

export default function ResultTeamCard({ teamSlot, bossName, isMyTeam, requireMembers = 6 }: ResultTeamCardProps) {
    const memberCount = teamSlot.characters.filter(c => c.characterId !== null).length;
    
    return (
        <div className={`relative bg-card p-6 rounded-2xl shadow-sm border transition-all hover:shadow-md ${
            isMyTeam 
                ? "border-blue-500 ring-1 ring-blue-500/20 shadow-blue-500/10" 
                : "border-border"
        }`}>
            {isMyTeam && (
                <div className="absolute -top-3 -right-3 bg-blue-600 text-white text-xs font-bold px-3 py-1 rounded-full shadow-lg">
                    我的隊伍
                </div>
            )}

            <div className="flex justify-between items-start mb-4">
                <div className="space-y-1">
                    <div className="flex items-center gap-2">
                        <span className="text-sm font-semibold px-2 py-0.5 bg-muted rounded text-muted-foreground uppercase tracking-wider">
                            {bossName}
                        </span>
                    </div>
                    <div className="flex items-center gap-2">
                        <Calendar size={18} className="text-blue-600 dark:text-blue-400" />
                        <h3 className="text-lg font-bold">
                            {formatDateTime(teamSlot.slotDateTime)}
                        </h3>
                    </div>
                    <div className="flex items-center gap-2 text-sm text-muted-foreground">
                        <Users size={14} />
                        <span>成員: {memberCount} / {requireMembers}</span>
                    </div>
                </div>
            </div>

            <div className="overflow-hidden">
                <table className="w-full text-sm">
                    <thead>
                        <tr className="text-muted-foreground border-b border-border text-xs uppercase">
                            <th className="text-left font-medium py-2">玩家 / 角色</th>
                            <th className="text-left font-medium py-2 hidden sm:table-cell">職業</th>
                            <th className="text-right font-medium py-2">攻擊力</th>
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-border/50">
                        {teamSlot.characters.map((m, index) => (
                            <tr key={m.characterId || `empty-${index}`} className="group hover:bg-muted/30 transition-colors">
                                <td className="py-2.5">
                                    <div className="flex flex-col">
                                        {m.characterId ? (
                                            <>
                                                <span className="font-medium text-foreground">{m.characterName}</span>
                                                <span className="text-[10px] text-muted-foreground">{m.discordName}</span>
                                            </>
                                        ) : (
                                            <span className="text-muted-foreground/40 font-medium italic">
                                                (待安排)
                                            </span>
                                        )}
                                    </div>
                                </td>
                                <td className="py-2.5 hidden sm:table-cell">
                                    {m.characterId && (
                                        <span className="px-2 py-0.5 rounded text-[11px] bg-muted border border-border/50">
                                            {m.job}
                                        </span>
                                    )}
                                </td>
                                <td className="py-2.5 text-right font-mono text-xs">
                                    {m.characterId ? m.attackPower.toLocaleString() : "-"}
                                </td>
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </div>
    );
}
