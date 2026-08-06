"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Mail, Check, X, Swords, Clock, User } from "lucide-react";
import { useMyInvitations } from "@/hooks/queries/useMyInvitations";
import { leaderService } from "@/services/leaderService";
import { InvitationAction } from "@/types/leaderLed";
import { ApiError } from "@/services/apiClient";

function formatSlot(iso: string): string {
    return new Date(iso).toLocaleString("zh-TW", {
        timeZone: "Asia/Taipei",
        month: "numeric",
        day: "numeric",
        weekday: "short",
        hour: "2-digit",
        minute: "2-digit",
        hour12: false,
    });
}

export default function MyInvitationsPage() {
    const { data: invitations = [], isLoading } = useMyInvitations();
    const qc = useQueryClient();

    const respond = useMutation({
        mutationFn: (v: { teamSlotId: number; memberId: number; action: InvitationAction }) =>
            leaderService.respondInvitation(v.teamSlotId, v.memberId, v.action),
        onSuccess: () => qc.invalidateQueries({ queryKey: ["myInvitations"] }),
        onError: (e) => alert(e instanceof ApiError ? e.message : "操作失敗，請稍後再試"),
    });

    return (
        <div className="max-w-2xl mx-auto px-4 py-8">
            <div className="flex items-center gap-3 mb-6">
                <div className="p-2 bg-blue-100 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400 rounded-lg">
                    <Mail size={24} />
                </div>
                <div>
                    <h1 className="text-2xl font-bold">我的邀請</h1>
                    <p className="text-sm text-muted-foreground">隊長邀請你加入的隊伍，接受即入隊。</p>
                </div>
            </div>

            {isLoading ? (
                <p className="text-muted-foreground py-12 text-center">載入中…</p>
            ) : invitations.length === 0 ? (
                <div className="bg-card border border-border rounded-2xl p-10 text-center text-muted-foreground">
                    目前沒有待處理的邀請。
                </div>
            ) : (
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
            )}
        </div>
    );
}
