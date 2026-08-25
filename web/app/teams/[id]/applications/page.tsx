"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Inbox, Zap, Trophy, Sparkles, User, Check, X } from "lucide-react";
import toast from "react-hot-toast";
import { useApplications } from "@/hooks/queries/useApplications";
import { useRecruitmentGap } from "@/hooks/queries/useRecruitmentGap";
import { useLedTeams } from "@/hooks/queries/useLedTeams";
import { leaderService } from "@/services/leaderService";
import { invalidateTeamQueries } from "@/lib/invalidateTeamQueries";
import { ApplicationAction } from "@/types/leaderLed";
import { ApiError } from "@/services/apiClient";
import { formatSlot } from "@/utils/dateTimeUtil";

export default function ApplicationsPage() {
    const params = useParams();
    const teamSlotId = Number(params.id);
    const qc = useQueryClient();

    const { data: applications = [], isLoading } = useApplications(teamSlotId);
    const { data: gap = [] } = useRecruitmentGap(teamSlotId);
    const { data: ledTeams = [] } = useLedTeams();
    const team = ledTeams.find((t) => t.teamSlotId === teamSlotId);

    const missing = gap.filter((g) => g.remaining > 0);
    const hasRequirements = gap.length > 0;

    const respond = useMutation({
        mutationFn: (v: { memberId: number; action: ApplicationAction }) =>
            leaderService.respondApplication(teamSlotId, v.memberId, v.action),
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "操作失敗，請稍後再試"),
        // 成功/失敗都刷新：失敗多半是隊伍已滿（併發搶最後一位）→ 佇列/計數/缺口/組成與伺服器對齊
        onSettled: () => invalidateTeamQueries(qc),
    });

    return (
        <div className="max-w-2xl mx-auto px-4 py-8">
            <Link href="/me/led-teams" className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground mb-4">
                <ArrowLeft size={16} /> 我開的隊
            </Link>

            <div className="flex items-center gap-3 mb-6">
                <div className="p-2 bg-indigo-100 dark:bg-indigo-900/30 text-indigo-600 dark:text-indigo-400 rounded-lg">
                    <Inbox size={24} />
                </div>
                <div>
                    <h1 className="text-2xl font-bold">申請審核</h1>
                    <p className="text-sm text-muted-foreground">
                        {team ? `${team.bossName ?? "王"}・${formatSlot(team.slotDateTime)}` : "玩家申請加入本隊，核准後才占容量。"}
                    </p>
                </div>
            </div>

            {hasRequirements && (
                missing.length > 0 ? (
                    <div className="mb-5 rounded-xl border border-amber-300 dark:border-amber-800 bg-amber-50 dark:bg-amber-950/40 px-4 py-3 text-sm">
                        <span className="font-medium text-amber-800 dark:text-amber-300">還缺：</span>
                        <span className="text-amber-700 dark:text-amber-400">
                            {missing
                                .map((g) => `${g.jobs.length > 0 ? g.jobs.join("/") : "不限職業"} ×${g.remaining}`)
                                .join("、")}
                        </span>
                    </div>
                ) : (
                    <div className="mb-5 rounded-xl border border-green-300 dark:border-green-800 bg-green-50 dark:bg-green-950/40 px-4 py-3 text-sm text-green-700 dark:text-green-400 flex items-center gap-1.5">
                        <Check size={15} /> 職業需求已滿足
                    </div>
                )
            )}

            {isLoading ? (
                <p className="text-muted-foreground py-12 text-center">載入中…</p>
            ) : applications.length === 0 ? (
                <div className="bg-card border border-border rounded-2xl p-10 text-center text-muted-foreground">
                    目前沒有待審核的申請。
                </div>
            ) : (
                <ul className="space-y-3">
                    {applications.map((a) => (
                        <li
                            key={a.memberId}
                            className="bg-card border border-border rounded-2xl p-4 shadow-sm flex items-center justify-between gap-3"
                        >
                            <div className="flex flex-col gap-1 min-w-0">
                                <span className="font-semibold truncate flex items-center gap-1.5">
                                    <User size={15} /> {a.discordName || a.characterName || a.characterId}
                                    <span className="text-muted-foreground font-normal">（{a.job}）</span>
                                </span>
                                <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
                                    <span className="flex items-center gap-1">Lv {a.level}</span>
                                    <span className="flex items-center gap-1"><Zap size={12} /> {a.attackPower}</span>
                                    <span className="flex items-center gap-1"><Trophy size={12} /> 通關 {a.bossClearCount}</span>
                                    <span className="flex items-center gap-1"><Sparkles size={12} /> 祝福 {a.mapleBlessingLevel}</span>
                                </div>
                            </div>
                            <div className="flex gap-2 shrink-0">
                                <button
                                    disabled={respond.isPending}
                                    onClick={() => respond.mutate({ memberId: a.memberId, action: "approve" })}
                                    className="px-4 py-2 bg-green-600 text-white rounded-xl hover:bg-green-700 disabled:opacity-50 transition-colors flex items-center gap-1.5 font-medium"
                                >
                                    <Check size={16} /> 核准
                                </button>
                                <button
                                    disabled={respond.isPending}
                                    onClick={() => respond.mutate({ memberId: a.memberId, action: "reject" })}
                                    className="px-4 py-2 bg-muted text-foreground rounded-xl hover:bg-muted/70 disabled:opacity-50 transition-colors flex items-center gap-1.5 font-medium"
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
