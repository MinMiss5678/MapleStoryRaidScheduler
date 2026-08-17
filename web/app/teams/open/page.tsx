"use client";

import { useState } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Compass, Swords, Clock, Users, Send, Zap, Sparkles } from "lucide-react";
import toast from "react-hot-toast";
import { useOpenTeams } from "@/hooks/queries/useOpenTeams";
import { useCharacters } from "@/hooks/queries/useCharacters";
import { leaderService } from "@/services/leaderService";
import { invalidateTeamQueries } from "@/lib/invalidateTeamQueries";
import { OpenTeam } from "@/types/leaderLed";
import { ApiError } from "@/services/apiClient";
import { formatSlot } from "@/utils/dateTimeUtil";

function OpenTeamCard({ team }: { team: OpenTeam }) {
    const qc = useQueryClient();
    const { data: characters = [] } = useCharacters();
    const [characterId, setCharacterId] = useState("");

    const apply = useMutation({
        mutationFn: (charId: string) => leaderService.apply(team.teamSlotId, charId),
        onSuccess: () => {
            toast.success("已送出申請，等待隊長審核。");
            setCharacterId("");
        },
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "申請失敗，請稍後再試"),
        // 成功/失敗都刷新：失敗多半是隊伍已滿 → 讓卡片人數/剩餘與伺服器對齊
        onSettled: () => invalidateTeamQueries(qc),
    });

    return (
        <li className="bg-card border border-border rounded-2xl p-5 shadow-sm flex flex-col gap-3">
            <div className="flex items-center justify-between">
                <div className="flex items-center gap-2 text-lg font-semibold">
                    <Swords size={18} className="text-purple-500" />
                    {team.bossName ?? "王"}
                </div>
                <span className="flex items-center gap-1 text-sm text-muted-foreground">
                    <Users size={14} /> {team.confirmedCount}/{team.requireMembers}
                </span>
            </div>

            <span className="flex items-center gap-1 text-sm text-muted-foreground">
                <Clock size={14} /> {formatSlot(team.slotDateTime)}
            </span>

            {team.description && (
                <p className="text-sm bg-muted/50 rounded-lg px-3 py-2 whitespace-pre-wrap">{team.description}</p>
            )}

            {team.confirmedMembers.length > 0 && (
                <div className="flex flex-col gap-1 border-t border-border pt-2">
                    {team.confirmedMembers.map((m, i) => (
                        <div key={i} className="text-xs text-muted-foreground flex flex-wrap items-center gap-x-3 gap-y-0.5">
                            <span className="text-foreground">{m.job}</span>
                            <span className="flex items-center gap-0.5"><Zap size={11} /> {m.attackPower}</span>
                            <span className="flex items-center gap-0.5"><Sparkles size={11} /> 祝福 {m.mapleBlessingLevel}</span>
                        </div>
                    ))}
                </div>
            )}

            <div className="flex gap-2 pt-1">
                <select
                    value={characterId}
                    onChange={(e) => setCharacterId(e.target.value)}
                    className="flex-1 px-3 py-2 bg-background border border-border rounded-xl text-sm"
                >
                    <option value="">選擇要申請的角色…</option>
                    {characters.map((c) => (
                        <option key={c.id} value={c.id}>
                            {c.name}（{c.job}・{c.attackPower}）
                        </option>
                    ))}
                </select>
                <button
                    disabled={!characterId || apply.isPending}
                    onClick={() => apply.mutate(characterId)}
                    className="px-4 py-2 bg-blue-600 text-white rounded-xl hover:bg-blue-700 disabled:opacity-50 transition-colors flex items-center gap-1.5 font-medium"
                >
                    <Send size={16} /> 申請
                </button>
            </div>
        </li>
    );
}

export default function OpenTeamsPage() {
    const { data: teams = [], isLoading } = useOpenTeams();

    return (
        <div className="max-w-2xl mx-auto px-4 py-8">
            <div className="flex items-center gap-3 mb-6">
                <div className="p-2 bg-blue-100 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400 rounded-lg">
                    <Compass size={24} />
                </div>
                <div>
                    <h1 className="text-2xl font-bold">開放隊伍</h1>
                    <p className="text-sm text-muted-foreground">本期尚有空位的隊伍，挑一個用自己的角色申請加入。</p>
                </div>
            </div>

            {isLoading ? (
                <p className="text-muted-foreground py-12 text-center">載入中…</p>
            ) : teams.length === 0 ? (
                <div className="bg-card border border-border rounded-2xl p-10 text-center text-muted-foreground">
                    目前沒有開放中的隊伍。
                </div>
            ) : (
                <ul className="space-y-4">
                    {teams.map((t) => (
                        <OpenTeamCard key={t.teamSlotId} team={t} />
                    ))}
                </ul>
            )}
        </div>
    );
}
