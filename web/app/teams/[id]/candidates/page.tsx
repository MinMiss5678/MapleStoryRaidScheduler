"use client";

import { useState } from "react";
import Link from "next/link";
import { useParams } from "next/navigation";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, UserSearch, Zap, Trophy, Sparkles, UserPlus, Check, AlertTriangle, ArrowDown, ArrowUp, ChevronDown, ChevronRight, Star } from "lucide-react";
import toast from "react-hot-toast";
import { useCandidates } from "@/hooks/queries/useCandidates";
import { useRecruitmentGap } from "@/hooks/queries/useRecruitmentGap";
import { useLedTeams } from "@/hooks/queries/useLedTeams";
import { leaderService } from "@/services/leaderService";
import { ApiError } from "@/services/apiClient";
import { formatSlot } from "@/utils/dateTimeUtil";
import { TeamCandidate } from "@/types/leaderLed";
import { invalidateTeamQueries } from "@/lib/invalidateTeamQueries";

export default function CandidatesPage() {
    const params = useParams();
    const teamSlotId = Number(params.id);
    const qc = useQueryClient();

    const { data: candidates = [], isLoading } = useCandidates(teamSlotId);
    const { data: gap = [] } = useRecruitmentGap(teamSlotId);
    const { data: ledTeams = [] } = useLedTeams();
    const team = ledTeams.find((t) => t.teamSlotId === teamSlotId);

    const missing = gap.filter((g) => g.remaining > 0);
    const hasRequirements = gap.length > 0;

    // 組內排序可疊加：攻擊/通關都生效，sorts[0]=主排、sorts[1]=次排（主排同分再比次排）。
    // 點主排鈕→切該欄高↔低；點次排鈕→升為主排（兩者對調優先序、各自方向保留）。
    type SortKey = "attack" | "clears";
    const valueOf = (c: TeamCandidate, k: SortKey) => (k === "clears" ? c.bossClearCount : c.attackPower);
    const [sorts, setSorts] = useState<{ key: SortKey; dir: "desc" | "asc" }[]>([
        { key: "attack", dir: "desc" },
        { key: "clears", dir: "desc" },
    ]);
    const clickSort = (key: SortKey) =>
        setSorts((prev) =>
            prev[0].key === key
                ? [{ key, dir: prev[0].dir === "desc" ? "asc" : "desc" }, prev[1]]
                : [prev[1], prev[0]],
        );
    const cmp = (a: TeamCandidate, b: TeamCandidate) => {
        // 偏好本王優先（軟訊號）：組內排最前，其餘再按攻擊/通關
        if (a.prefersThisBoss !== b.prefersThisBoss) return a.prefersThisBoss ? -1 : 1;
        for (const s of sorts) {
            const d = s.dir === "asc" ? valueOf(a, s.key) - valueOf(b, s.key) : valueOf(b, s.key) - valueOf(a, s.key);
            if (d !== 0) return d;
        }
        return 0;
    };

    // 每職業還缺幾位：多職業共用的需求列，其缺口分攤給列中每個職業（取最大，保守視為仍缺）。
    const remainingByJob = new Map<string, number>();
    for (const g of gap)
        for (const j of g.jobs) remainingByJob.set(j, Math.max(remainingByJob.get(j) ?? 0, g.remaining));

    // 候選依職業分組、組內攻擊降冪；缺的職業排前（缺越多越前），已滿足的排後 → 「每個缺的職業各挑最強的前幾位」。
    const byJob = new Map<string, TeamCandidate[]>();
    for (const c of candidates) {
        const arr = byJob.get(c.job);
        if (arr) arr.push(c);
        else byJob.set(c.job, [c]);
    }
    const groups = Array.from(byJob, ([job, items]) => ({
        job,
        remaining: remainingByJob.get(job) ?? 0,
        items: [...items].sort(cmp),
    })).sort(
        (a, b) =>
            Number(b.remaining > 0) - Number(a.remaining > 0) ||
            b.remaining - a.remaining ||
            a.job.localeCompare(b.job),
    );

    const [invited, setInvited] = useState<Set<string>>(new Set());

    // 每組預設只顯示前幾位，避免某職業人多把其他職業擠到看不到；超過可展開。
    const PREVIEW_PER_JOB = 5;
    const [expandedJobs, setExpandedJobs] = useState<Set<string>>(new Set());
    const toggleExpand = (job: string) =>
        setExpandedJobs((prev) => {
            const next = new Set(prev);
            if (next.has(job)) next.delete(job);
            else next.add(job);
            return next;
        });

    // 整個職業組可收合成一行（只留標題）——快速略過不看的職業。
    const [collapsedJobs, setCollapsedJobs] = useState<Set<string>>(new Set());
    const toggleCollapse = (job: string) =>
        setCollapsedJobs((prev) => {
            const next = new Set(prev);
            if (next.has(job)) next.delete(job);
            else next.add(job);
            return next;
        });

    const invite = useMutation({
        mutationFn: (characterId: string) => leaderService.invite(teamSlotId, characterId),
        onSuccess: (_data, characterId) => {
            setInvited((prev) => new Set(prev).add(characterId));
            toast.success("已送出邀請");
        },
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "邀請失敗，請稍後再試"),
        onSettled: () => invalidateTeamQueries(qc),
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
            ) : candidates.length === 0 ? (
                <div className="bg-card border border-border rounded-2xl p-10 text-center text-muted-foreground">
                    沒有符合條件的候選。請確認已設定隊伍條件，或放寬職業/攻擊/通關數門檻。
                </div>
            ) : (
                <>
                    <div className="flex flex-wrap items-center gap-2 mb-4 text-sm">
                        <span className="text-muted-foreground">組內排序：</span>
                        {([["attack", "攻擊力"], ["clears", "通關數"]] as const).map(([key, label]) => {
                            const idx = sorts.findIndex((s) => s.key === key);
                            const primary = idx === 0;
                            return (
                                <button
                                    key={key}
                                    onClick={() => clickSort(key)}
                                    title={primary ? "點擊切換高↔低" : "點擊升為主排序"}
                                    className={`px-3 py-1 rounded-lg border flex items-center gap-1 transition-colors ${
                                        primary
                                            ? "border-blue-500 text-blue-600 dark:text-blue-400 bg-blue-50 dark:bg-blue-950/40"
                                            : "border-border text-muted-foreground hover:text-foreground"
                                    }`}
                                >
                                    <span className={`text-xs ${primary ? "font-bold" : "opacity-60"}`}>{idx + 1}</span>
                                    {label}
                                    {sorts[idx].dir === "desc" ? <ArrowDown size={14} /> : <ArrowUp size={14} />}
                                </button>
                            );
                        })}
                        <span className="text-xs text-muted-foreground">（1=主排、2=次排；主排同分再比次排）</span>
                    </div>
                    <div className="space-y-6">
                        {groups.map((grp) => {
                            const isExpanded = expandedJobs.has(grp.job);
                            const shown = isExpanded ? grp.items : grp.items.slice(0, PREVIEW_PER_JOB);
                            const collapsed = collapsedJobs.has(grp.job);
                            return (
                        <section key={grp.job} className={grp.remaining === 0 ? "opacity-60" : ""}>
                            <button
                                onClick={() => toggleCollapse(grp.job)}
                                className="w-full flex items-center gap-2 text-sm font-semibold mb-2 px-1 hover:text-foreground"
                            >
                                {collapsed ? <ChevronRight size={16} /> : <ChevronDown size={16} />}
                                <span>{grp.job}</span>
                                <span className="text-muted-foreground font-normal">（{grp.items.length}）</span>
                                {grp.remaining > 0 ? (
                                    <span className="text-amber-600 dark:text-amber-400">還缺 {grp.remaining}</span>
                                ) : (
                                    <span className="text-green-600 dark:text-green-400 font-normal flex items-center gap-0.5">
                                        <Check size={13} /> 已滿足
                                    </span>
                                )}
                            </button>
                            {!collapsed && (
                            <>
                            <ul className="space-y-3">
                                {shown.map((c) => {
                                    const done = invited.has(c.characterId);
                                    return (
                                        <li
                                            key={c.characterId}
                                            className="bg-card border border-border rounded-2xl p-4 shadow-sm flex items-center justify-between gap-3"
                                        >
                                            <div className="flex flex-col gap-1 min-w-0">
                                                <span className="font-semibold truncate">
                                                    {c.discordName || c.characterName} <span className="text-muted-foreground font-normal">（{c.job}）</span>
                                                </span>
                                                <div className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
                                                    <span className="flex items-center gap-1"><Zap size={12} /> {c.attackPower}</span>
                                                    <span className="flex items-center gap-1"><Trophy size={12} /> 通關 {c.bossClearCount}</span>
                                                    <span className="flex items-center gap-1"><Sparkles size={12} /> 祝福 {c.mapleBlessingLevel}</span>
                                                    {c.leaveRateWarn && (
                                                        <span className="flex items-center gap-1 text-red-600 dark:text-red-400 font-medium">
                                                            <AlertTriangle size={12} /> 退團率高
                                                        </span>
                                                    )}
                                                    {c.prefersThisBoss && (
                                                        <span className="flex items-center gap-1 text-blue-600 dark:text-blue-400 font-medium">
                                                            <Star size={12} /> 偏好此王
                                                        </span>
                                                    )}
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
                            {grp.items.length > PREVIEW_PER_JOB && (
                                <button
                                    onClick={() => toggleExpand(grp.job)}
                                    className="mt-2 px-1 text-sm text-blue-600 dark:text-blue-400 hover:underline"
                                >
                                    {isExpanded ? "收合" : `顯示更多（還有 ${grp.items.length - PREVIEW_PER_JOB} 位）`}
                                </button>
                            )}
                            </>
                            )}
                        </section>
                            );
                        })}
                    </div>
                </>
            )}
        </div>
    );
}
