"use client";

import Link from "next/link";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import toast from "react-hot-toast";
import { Crown, Swords, Clock, Users, UserSearch, Inbox, Plus, Trash2 } from "lucide-react";
import { useLedTeams } from "@/hooks/queries/useLedTeams";
import { leaderService } from "@/services/leaderService";
import { invalidateTeamQueries } from "@/lib/invalidateTeamQueries";
import { ApiError } from "@/services/apiClient";
import { formatSlot } from "@/utils/dateTimeUtil";
import { TransferControl } from "./TransferControl";
import { TeamComposition } from "../TeamComposition";

export default function LedTeamsPage() {
    const { data: teams = [], isLoading } = useLedTeams();
    const qc = useQueryClient();
    const disband = useMutation({
        mutationFn: (teamSlotId: number) => leaderService.deleteTeam(teamSlotId),
        onSuccess: () => toast.success("已解散隊伍"),
        onSettled: () => invalidateTeamQueries(qc),
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "解散失敗，請稍後再試"),
    });

    return (
        <div className="max-w-2xl mx-auto px-4 py-8">
            <div className="flex items-center justify-between gap-3 mb-6">
                <div className="flex items-center gap-3">
                    <div className="p-2 bg-amber-100 dark:bg-amber-900/30 text-amber-600 dark:text-amber-400 rounded-lg">
                        <Crown size={24} />
                    </div>
                    <div>
                        <h1 className="text-2xl font-bold">我開的隊</h1>
                        <p className="text-sm text-muted-foreground">你當隊長的隊伍——挑候選發邀請、審核申請。</p>
                    </div>
                </div>
                <Link
                    href="/teams/new"
                    className="shrink-0 px-4 py-2 bg-amber-600 text-white rounded-xl hover:bg-amber-700 transition-colors flex items-center gap-1.5 font-medium"
                >
                    <Plus size={16} /> 開隊
                </Link>
            </div>

            {isLoading ? (
                <p className="text-muted-foreground py-12 text-center">載入中…</p>
            ) : teams.length === 0 ? (
                <div className="bg-card border border-border rounded-2xl p-10 text-center text-muted-foreground">
                    你本期還沒有開任何隊。點右上「開隊」開始揪團。
                </div>
            ) : (
                <ul className="space-y-4">
                    {teams.map((t) => (
                        <li
                            key={t.teamSlotId}
                            className="bg-card border border-border rounded-2xl p-5 shadow-sm flex flex-col gap-3"
                        >
                            <div className="flex items-center justify-between">
                                <div className="flex items-center gap-2 text-lg font-semibold">
                                    <Swords size={18} className="text-purple-500" />
                                    {t.bossName ?? "王"}
                                </div>
                                <span className="flex items-center gap-1 text-sm text-muted-foreground">
                                    <Users size={14} /> {t.confirmedCount}/{t.requireMembers}
                                </span>
                            </div>
                            <span className="flex items-center gap-1 text-sm text-muted-foreground">
                                <Clock size={14} /> {formatSlot(t.slotDateTime)}
                            </span>
                            {t.description && (
                                <p className="text-sm bg-muted/50 rounded-lg px-3 py-2 whitespace-pre-wrap">{t.description}</p>
                            )}
                            <TeamComposition teamSlotId={t.teamSlotId} />
                            <div className="flex gap-2 pt-1">
                                <Link
                                    href={`/teams/${t.teamSlotId}/candidates`}
                                    className="flex-1 px-4 py-2 bg-blue-600 text-white rounded-xl hover:bg-blue-700 transition-colors flex items-center justify-center gap-1.5 font-medium"
                                >
                                    <UserSearch size={16} /> 挑候選
                                </Link>
                                <Link
                                    href={`/teams/${t.teamSlotId}/applications`}
                                    className="flex-1 relative px-4 py-2 bg-muted text-foreground rounded-xl hover:bg-muted/70 transition-colors flex items-center justify-center gap-1.5 font-medium"
                                >
                                    <Inbox size={16} /> 審核申請
                                    {t.appliedCount > 0 && (
                                        <span className="absolute -top-2 -right-2 min-w-5 h-5 px-1.5 bg-red-500 text-white text-xs rounded-full flex items-center justify-center">
                                            {t.appliedCount}
                                        </span>
                                    )}
                                </Link>
                            </div>
                            {t.invitedCount > 0 && (
                                <p className="text-xs text-muted-foreground">已送出 {t.invitedCount} 則邀請，等待玩家回覆。</p>
                            )}
                            <TransferControl teamSlotId={t.teamSlotId} />
                            <button
                                onClick={() => {
                                    if (confirm(`確定要解散「${t.bossName ?? "王"}」的隊伍嗎？已入隊成員會收到通知。`))
                                        disband.mutate(t.teamSlotId);
                                }}
                                disabled={disband.isPending}
                                className="self-start flex items-center gap-1.5 text-sm text-red-500 hover:text-red-600 disabled:opacity-50 transition-colors"
                            >
                                <Trash2 size={14} /> 解散隊伍
                            </button>
                        </li>
                    ))}
                </ul>
            )}
        </div>
    );
}
