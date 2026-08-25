"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Users, Mail, Check, X, Swords, Clock, User, Zap, LogOut, UserCog } from "lucide-react";
import toast from "react-hot-toast";
import { useMyInvitations } from "@/hooks/queries/useMyInvitations";
import { useMyTeams } from "@/hooks/queries/useMyTeams";
import { TeamComposition } from "./TeamComposition";
import { leaderService } from "@/services/leaderService";
import { invalidateTeamQueries } from "@/lib/invalidateTeamQueries";
import { InvitationAction } from "@/types/leaderLed";
import { ApiError } from "@/services/apiClient";
import { formatSlot } from "@/utils/dateTimeUtil";

export default function MyTeamsPage() {
    const { data: invitations = [], isLoading: invLoading } = useMyInvitations();
    const { data: teams = [], isLoading: teamsLoading } = useMyTeams();
    const qc = useQueryClient();

    const respond = useMutation({
        mutationFn: (v: { teamSlotId: number; memberId: number; action: InvitationAction }) =>
            leaderService.respondInvitation(v.teamSlotId, v.memberId, v.action),
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "操作失敗，請稍後再試"),
        // 成功/失敗都刷新：接受失敗多半是隊伍已滿 → 邀請/已入隊清單與伺服器對齊
        onSettled: () => invalidateTeamQueries(qc),
    });

    const leave = useMutation({
        mutationFn: (teamSlotId: number) => leaderService.leaveTeam(teamSlotId),
        onSuccess: () => toast.success("已退出隊伍"),
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "退隊失敗，請稍後再試"),
        onSettled: () => invalidateTeamQueries(qc),
    });

    const { data: transfers = [] } = useQuery({
        queryKey: ["leaderTransfers"],
        queryFn: () => leaderService.getMyLeaderTransfers(),
    });

    const respondTransfer = useMutation({
        mutationFn: (v: { teamSlotId: number; action: "accept" | "decline" }) =>
            leaderService.respondTransfer(v.teamSlotId, v.action),
        onSuccess: (_d, v) => toast.success(v.action === "accept" ? "你已成為新隊長" : "已拒絕轉讓"),
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "操作失敗，請稍後再試"),
        onSettled: () => invalidateTeamQueries(qc),
    });

    const isLoading = invLoading || teamsLoading;

    return (
        <div className="max-w-2xl mx-auto px-4 py-8">
            <div className="flex items-center gap-3 mb-6">
                <div className="p-2 bg-green-100 dark:bg-green-900/30 text-green-600 dark:text-green-400 rounded-lg">
                    <Users size={24} />
                </div>
                <div>
                    <h1 className="text-2xl font-bold">我的隊伍</h1>
                    <p className="text-sm text-muted-foreground">你收到的邀請與已入隊的隊伍——用來排自己的跨隊行程。</p>
                </div>
            </div>

            {isLoading ? (
                <p className="text-muted-foreground py-12 text-center">載入中…</p>
            ) : (
                <div className="space-y-8">
                    {/* 待處理隊長轉讓（有才顯示） */}
                    {transfers.length > 0 && (
                        <section>
                            <h2 className="flex items-center gap-2 text-sm font-semibold text-muted-foreground mb-3">
                                <UserCog size={16} /> 隊長轉讓（{transfers.length}）
                            </h2>
                            <ul className="space-y-4">
                                {transfers.map((t) => (
                                    <li
                                        key={t.teamSlotId}
                                        className="bg-card border border-border rounded-2xl p-5 shadow-sm flex flex-col gap-3"
                                    >
                                        <div className="flex items-center gap-2 text-lg font-semibold">
                                            <Swords size={18} className="text-purple-500" />
                                            {t.bossName ?? "王"}
                                        </div>
                                        <span className="flex items-center gap-1 text-sm text-muted-foreground">
                                            <Clock size={14} /> {formatSlot(t.slotDateTime)}
                                        </span>
                                        <p className="text-sm text-muted-foreground">隊長想把這隊的隊長轉給你。</p>
                                        <TeamComposition teamSlotId={t.teamSlotId} />
                                        <div className="flex gap-2 pt-1">
                                            <button
                                                disabled={respondTransfer.isPending}
                                                onClick={() => respondTransfer.mutate({ teamSlotId: t.teamSlotId, action: "accept" })}
                                                className="flex-1 px-4 py-2 bg-amber-600 text-white rounded-xl hover:bg-amber-700 disabled:opacity-50 transition-colors flex items-center justify-center gap-1.5 font-medium"
                                            >
                                                <Check size={16} /> 接受當隊長
                                            </button>
                                            <button
                                                disabled={respondTransfer.isPending}
                                                onClick={() => respondTransfer.mutate({ teamSlotId: t.teamSlotId, action: "decline" })}
                                                className="px-4 py-2 bg-muted text-foreground rounded-xl hover:bg-muted/70 disabled:opacity-50 transition-colors flex items-center justify-center gap-1.5 font-medium"
                                            >
                                                <X size={16} /> 拒絕
                                            </button>
                                        </div>
                                    </li>
                                ))}
                            </ul>
                        </section>
                    )}

                    {/* 待處理邀請（有才顯示，當作可動作的 inbox） */}
                    {invitations.length > 0 && (
                        <section>
                            <h2 className="flex items-center gap-2 text-sm font-semibold text-muted-foreground mb-3">
                                <Mail size={16} /> 待處理邀請（{invitations.length}）
                            </h2>
                            <ul className="space-y-4">
                                {invitations.map((inv) => (
                                    <li
                                        key={inv.memberId}
                                        className="bg-card border border-border rounded-2xl p-5 shadow-sm flex flex-col gap-3"
                                    >
                                        <div className="flex items-center gap-2 text-lg font-semibold">
                                            <Swords size={18} className="text-purple-500" />
                                            {inv.bossName ?? "王"}
                                        </div>
                                        <div className="flex flex-wrap gap-x-5 gap-y-1 text-sm text-muted-foreground">
                                            <span className="flex items-center gap-1">
                                                <Clock size={14} /> {formatSlot(inv.slotDateTime)}
                                            </span>
                                            <span className="flex items-center gap-1">
                                                <User size={14} /> {inv.characterName ?? inv.characterId}（{inv.job}）
                                            </span>
                                            <span className="flex items-center gap-1">
                                                <Users size={14} /> {inv.confirmedCount}/{inv.requireMembers}
                                            </span>
                                        </div>
                                        <div className="flex gap-2 pt-1">
                                            <button
                                                disabled={respond.isPending}
                                                onClick={() => respond.mutate({ teamSlotId: inv.teamSlotId, memberId: inv.memberId, action: "accept" })}
                                                className="flex-1 px-4 py-2 bg-green-600 text-white rounded-xl hover:bg-green-700 disabled:opacity-50 transition-colors flex items-center justify-center gap-1.5 font-medium"
                                            >
                                                <Check size={16} /> 接受
                                            </button>
                                            <button
                                                disabled={respond.isPending}
                                                onClick={() => respond.mutate({ teamSlotId: inv.teamSlotId, memberId: inv.memberId, action: "decline" })}
                                                className="px-4 py-2 bg-muted text-foreground rounded-xl hover:bg-muted/70 disabled:opacity-50 transition-colors flex items-center justify-center gap-1.5 font-medium"
                                            >
                                                <X size={16} /> 拒絕
                                            </button>
                                        </div>
                                    </li>
                                ))}
                            </ul>
                        </section>
                    )}

                    {/* 已加入的隊 */}
                    <section>
                        <h2 className="flex items-center gap-2 text-sm font-semibold text-muted-foreground mb-3">
                            <Users size={16} /> 已加入
                        </h2>
                        {teams.length === 0 ? (
                            <div className="bg-card border border-border rounded-2xl p-10 text-center text-muted-foreground">
                                你目前還沒有已確認的隊伍。到「開放隊」找一個申請加入吧。
                            </div>
                        ) : (
                            <ul className="space-y-4">
                                {teams.map((t) => (
                                    <li
                                        key={t.memberId}
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
                                        <div className="flex flex-wrap gap-x-5 gap-y-1 text-sm text-muted-foreground">
                                            <span className="flex items-center gap-1">
                                                <Clock size={14} /> {formatSlot(t.slotDateTime)}
                                            </span>
                                            <span className="flex items-center gap-1">
                                                <User size={14} /> {t.characterName ?? t.characterId}（{t.job}）
                                            </span>
                                            <span className="flex items-center gap-1">Lv {t.level}</span>
                                            <span className="flex items-center gap-1">
                                                <Zap size={14} /> {t.attackPower}
                                            </span>
                                        </div>
                                        <TeamComposition teamSlotId={t.teamSlotId} />
                                        <div className="flex pt-1">
                                            <button
                                                disabled={leave.isPending}
                                                onClick={() => {
                                                    if (window.confirm(`確定退出「${t.bossName ?? "王"}」這隊？位子會重開。`)) leave.mutate(t.teamSlotId);
                                                }}
                                                className="ml-auto px-3 py-1.5 text-sm text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-lg disabled:opacity-50 transition-colors flex items-center gap-1.5"
                                            >
                                                <LogOut size={15} /> 退隊
                                            </button>
                                        </div>
                                    </li>
                                ))}
                            </ul>
                        )}
                    </section>
                </div>
            )}
        </div>
    );
}
