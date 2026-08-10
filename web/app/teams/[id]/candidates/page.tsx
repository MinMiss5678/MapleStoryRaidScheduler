"use client";

import { useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, UserSearch, Zap, Trophy, Sparkles, UserPlus, Check } from "lucide-react";
import toast from "react-hot-toast";
import { useCandidates } from "@/hooks/queries/useCandidates";
import { useLedTeams } from "@/hooks/queries/useLedTeams";
import { leaderService } from "@/services/leaderService";
import { ApiError } from "@/services/apiClient";
import { formatSlot } from "@/utils/dateTimeUtil";

export default function CandidatesPage() {
    const params = useParams();
    const teamSlotId = Number(params.id);
    const qc = useQueryClient();

    const { data: candidates = [], isLoading } = useCandidates(teamSlotId);
    const { data: ledTeams = [] } = useLedTeams();
    const team = ledTeams.find((t) => t.teamSlotId === teamSlotId);

    const [invited, setInvited] = useState<Set<string>>(new Set());

    const invite = useMutation({
        mutationFn: (characterId: string) => leaderService.invite(teamSlotId, characterId),
        onSuccess: (_data, characterId) => {
            setInvited((prev) => new Set(prev).add(characterId));
            toast.success("已送出邀請");
        },
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "邀請失敗，請稍後再試"),
        onSettled: () => qc.invalidateQueries({ queryKey: ["ledTeams"] }),
    });

    return (
        <div className="max-w-2xl mx-auto px-4 py-8">
            <Link href="/me/led-teams" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground mb-4">
                <ArrowLeft size={16} /> 我開的隊
            </Link>

            <div className="flex items-center gap-3 mb-6">
                <div className="p-2 bg-blue-100 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400 rounded-lg">
                    <UserSearch size={24} />
                </div>
                <div>
                    <h1 className="text-2xl font-bold">候選清單</h1>
                    <p className="text-sm text-muted-foreground">
                        {team ? `${team.bossName ?? "王"}・${formatSlot(team.slotDateTime)}` : "符合本隊條件的玩家，可直接邀請。"}
                    </p>
                </div>
            </div>

            {isLoading ? (
                <p className="text-muted-foreground py-12 text-center">載入中…</p>
            ) : candidates.length === 0 ? (
                <div className="bg-card border border-border rounded-2xl p-10 text-center text-muted-foreground">
                    沒有符合條件的候選。請確認已設定隊伍條件，或放寬職業/攻擊/通關數門檻。
                </div>
            ) : (
                <ul className="space-y-3">
                    {candidates.map((c) => {
                        const done = invited.has(c.characterId);
                        return (
                            <li
                                key={c.characterId}
                                className="bg-card border border-border rounded-2xl p-4 shadow-sm flex items-center justify-between gap-3"
                            >
                                <div className="flex flex-col gap-1 min-w-0">
                                    <span className="font-semibold truncate">
                                        {c.characterName} <span className="text-muted-foreground font-normal">（{c.job}）</span>
                                    </span>
                                    {c.discordName && (
                                        <span className="text-xs text-muted-foreground truncate">@{c.discordName}</span>
                                    )}
                                    <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
                                        <span className="flex items-center gap-1"><Zap size={12} /> {c.attackPower}</span>
                                        <span className="flex items-center gap-1"><Trophy size={12} /> 通關 {c.bossClearCount}</span>
                                        <span className="flex items-center gap-1"><Sparkles size={12} /> 祝福 {c.mapleBlessingLevel}</span>
                                    </div>
                                </div>
                                <button
                                    disabled={done || invite.isPending}
                                    onClick={() => invite.mutate(c.characterId)}
                                    className={`shrink-0 px-4 py-2 rounded-xl transition-colors flex items-center gap-1.5 font-medium disabled:opacity-60 ${
                                        done
                                            ? "bg-green-100 dark:bg-green-900/30 text-green-700 dark:text-green-400"
                                            : "bg-blue-600 text-white hover:bg-blue-700"
                                    }`}
                                >
                                    {done ? <><Check size={16} /> 已邀請</> : <><UserPlus size={16} /> 邀請</>}
                                </button>
                            </li>
                        );
                    })}
                </ul>
            )}
        </div>
    );
}
