"use client";

import { useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { useMutation } from "@tanstack/react-query";
import { Crown, Plus, Trash2, Swords } from "lucide-react";
import { useBosses } from "@/hooks/queries/useBosses";
import { usePeriod } from "@/hooks/queries/usePeriod";
import { useJobMap } from "@/hooks/queries/useScheduleData";
import toast from "react-hot-toast";
import { leaderService } from "@/services/leaderService";
import { ApiError } from "@/services/apiClient";
import { JOBS } from "@/constants/jobs";
import { CreateTeamRequirementInput } from "@/types/leaderLed";

function toLocalInput(d: Date): string {
    const pad = (n: number) => String(n).padStart(2, "0");
    return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

const emptyRow = (): CreateTeamRequirementInput => ({ count: 1, minClearCount: 0, jobs: [] });

export default function NewTeamPage() {
    const router = useRouter();
    const { data: bosses = [] } = useBosses();
    const { data: period } = usePeriod();
    const { data: jobMap = {} } = useJobMap();

    // 反轉 job→category 成 category→jobs[]，供「一鍵勾滿整組」的分類鈕。
    const categories = useMemo(() => {
        const m: Record<string, string[]> = {};
        for (const [job, cat] of Object.entries(jobMap)) (m[cat] ??= []).push(job);
        return m;
    }, [jobMap]);

    const [bossId, setBossId] = useState(0);
    const [slotDateTime, setSlotDateTime] = useState("");
    const [isInstant, setIsInstant] = useState(false);   // period-less §8 Phase 3：即時團
    const [description, setDescription] = useState("");
    const [rows, setRows] = useState<CreateTeamRequirementInput[]>([emptyRow()]);

    const updateRow = (i: number, patch: Partial<CreateTeamRequirementInput>) =>
        setRows((rs) => rs.map((r, idx) => (idx === i ? { ...r, ...patch } : r)));

    const toggleJob = (i: number, job: string) =>
        setRows((rs) =>
            rs.map((r, idx) => {
                if (idx !== i) return r;
                const has = r.jobs.some((j) => j.job === job);
                return {
                    ...r,
                    jobs: has ? r.jobs.filter((j) => j.job !== job) : [...r.jobs, { job, minAttackPower: 0 }],
                };
            }),
        );

    const addCategory = (i: number, cat: string) =>
        setRows((rs) =>
            rs.map((r, idx) => {
                if (idx !== i) return r;
                const existing = new Set(r.jobs.map((j) => j.job));
                const added = (categories[cat] ?? [])
                    .filter((job) => !existing.has(job))
                    .map((job) => ({ job, minAttackPower: 0 }));
                return { ...r, jobs: [...r.jobs, ...added] };
            }),
        );

    const setJobAttack = (i: number, job: string, minAttackPower: number) =>
        setRows((rs) =>
            rs.map((r, idx) =>
                idx === i ? { ...r, jobs: r.jobs.map((j) => (j.job === job ? { ...j, minAttackPower } : j)) } : r,
            ),
        );

    const create = useMutation({
        mutationFn: () =>
            leaderService.createTeam({
                bossId,
                // 即時團：時間＝現在、不綁 period；排程團用選的時段
                slotDateTime: isInstant ? new Date().toISOString() : new Date(slotDateTime).toISOString(),
                kind: isInstant ? "Instant" : "Scheduled",
                description: description.trim() || undefined,
                requirements: rows,
            }),
        onSuccess: () => router.push(isInstant ? "/me/led-teams" : "/me/led-teams"),
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "開隊失敗，請稍後再試"),
    });

    const canSubmit = bossId > 0 && (isInstant || slotDateTime !== "") && rows.every((r) => r.count >= 1);

    return (
        <div className="max-w-2xl mx-auto px-4 py-8">
            <div className="flex items-center gap-3 mb-6">
                <div className="p-2 bg-amber-100 dark:bg-amber-900/30 text-amber-600 dark:text-amber-400 rounded-lg">
                    <Crown size={24} />
                </div>
                <div>
                    <h1 className="text-2xl font-bold">開隊</h1>
                    <p className="text-sm text-muted-foreground">選王、時段，並設定要哪些職業/攻擊/通關數。</p>
                </div>
            </div>

            <div className="flex flex-col gap-5">
                {/* 王 + 時段 */}
                <div className="bg-card border border-border rounded-2xl p-5 flex flex-col gap-4">
                    {/* 隊伍種類：排程 vs 即時 */}
                    <div className="flex gap-2">
                        <button type="button" onClick={() => setIsInstant(false)}
                            className={`flex-1 px-3 py-2 rounded-xl text-sm font-medium transition-colors ${!isInstant ? "bg-amber-600 text-white" : "bg-muted text-foreground"}`}>
                            排程團（約好時間）
                        </button>
                        <button type="button" onClick={() => setIsInstant(true)}
                            className={`flex-1 px-3 py-2 rounded-xl text-sm font-medium transition-colors ${isInstant ? "bg-rose-600 text-white" : "bg-muted text-foreground"}`}>
                            即時團（現在就打）
                        </button>
                    </div>

                    <label className="flex flex-col gap-1.5">
                        <span className="text-sm font-medium flex items-center gap-1.5"><Swords size={14} /> 王</span>
                        <select
                            value={bossId}
                            onChange={(e) => setBossId(Number(e.target.value))}
                            className="px-3 py-2 bg-background border border-border rounded-xl text-sm"
                        >
                            <option value={0}>選擇王…</option>
                            {bosses.map((b) => (
                                <option key={b.id} value={b.id}>{b.name}</option>
                            ))}
                        </select>
                    </label>

                    {isInstant ? (
                        <p className="text-sm text-muted-foreground">即時團：時間為「現在」，候選來自即時揪團看板。</p>
                    ) : (
                        <label className="flex flex-col gap-1.5">
                            <span className="text-sm font-medium">打王時段</span>
                            <input
                                type="datetime-local"
                                value={slotDateTime}
                                min={period ? toLocalInput(period.startDate) : undefined}
                                max={period ? toLocalInput(period.endDate) : undefined}
                                onChange={(e) => setSlotDateTime(e.target.value)}
                                className="px-3 py-2 bg-background border border-border rounded-xl text-sm"
                            />
                            {period && (
                                <span className="text-xs text-muted-foreground">
                                    須落在本期：{period.startDate.toLocaleDateString("zh-TW")} ~ {period.endDate.toLocaleDateString("zh-TW")}
                                </span>
                            )}
                        </label>
                    )}

                    <label className="flex flex-col gap-1.5">
                        <span className="text-sm font-medium">隊伍說明（選填）</span>
                        <textarea
                            value={description}
                            onChange={(e) => setDescription(e.target.value)}
                            rows={2}
                            maxLength={500}
                            placeholder="例：楓葉祝福9、需自備 buff…"
                            className="px-3 py-2 bg-background border border-border rounded-xl text-sm resize-none"
                        />
                    </label>
                </div>

                {/* 條件 builder */}
                <div className="flex flex-col gap-3">
                    <div className="flex items-center justify-between">
                        <h2 className="font-semibold">隊伍條件</h2>
                        <button
                            onClick={() => setRows((rs) => [...rs, emptyRow()])}
                            className="text-sm px-3 py-1.5 border border-border rounded-lg hover:bg-muted transition-colors flex items-center gap-1"
                        >
                            <Plus size={14} /> 新增需求列
                        </button>
                    </div>

                    {rows.map((row, i) => (
                        <div key={i} className="bg-card border border-border rounded-2xl p-4 flex flex-col gap-3">
                            <div className="flex items-center gap-3">
                                <label className="flex items-center gap-1.5 text-sm">
                                    需要
                                    <input
                                        type="number"
                                        min={1}
                                        value={row.count}
                                        onChange={(e) => updateRow(i, { count: Number(e.target.value) })}
                                        className="w-16 px-2 py-1 bg-background border border-border rounded-lg text-sm"
                                    />
                                    位
                                </label>
                                <label className="flex items-center gap-1.5 text-sm">
                                    通關≥
                                    <input
                                        type="number"
                                        min={0}
                                        value={row.minClearCount}
                                        onChange={(e) => updateRow(i, { minClearCount: Number(e.target.value) })}
                                        className="w-16 px-2 py-1 bg-background border border-border rounded-lg text-sm"
                                    />
                                </label>
                                {rows.length > 1 && (
                                    <button
                                        onClick={() => setRows((rs) => rs.filter((_, idx) => idx !== i))}
                                        className="ml-auto text-muted-foreground hover:text-red-500 transition-colors"
                                        aria-label="移除需求列"
                                    >
                                        <Trash2 size={16} />
                                    </button>
                                )}
                            </div>

                            {/* 分類一鍵勾滿 */}
                            {Object.keys(categories).length > 0 && (
                                <div className="flex flex-wrap gap-1.5">
                                    {Object.keys(categories).map((cat) => (
                                        <button
                                            key={cat}
                                            onClick={() => addCategory(i, cat)}
                                            className="text-xs px-2.5 py-1 bg-muted rounded-full hover:bg-muted/70 transition-colors"
                                        >
                                            + {cat}
                                        </button>
                                    ))}
                                </div>
                            )}

                            {/* 職業勾選 + 各自攻擊下限 */}
                            <div className="flex flex-wrap gap-2 border-t border-border pt-3">
                                {JOBS.map((job) => {
                                    const picked = row.jobs.find((j) => j.job === job);
                                    return (
                                        <div
                                            key={job}
                                            className={`flex items-center gap-1.5 px-2 py-1 rounded-lg border text-sm transition-colors ${
                                                picked ? "border-blue-500 bg-blue-50 dark:bg-blue-900/20" : "border-border"
                                            }`}
                                        >
                                            <label className="flex items-center gap-1 cursor-pointer">
                                                <input
                                                    type="checkbox"
                                                    checked={!!picked}
                                                    onChange={() => toggleJob(i, job)}
                                                />
                                                {job}
                                            </label>
                                            {picked && (
                                                <input
                                                    type="number"
                                                    min={0}
                                                    value={picked.minAttackPower}
                                                    onChange={(e) => setJobAttack(i, job, Number(e.target.value))}
                                                    placeholder="攻擊≥"
                                                    className="w-20 px-1.5 py-0.5 bg-background border border-border rounded text-xs"
                                                />
                                            )}
                                        </div>
                                    );
                                })}
                            </div>
                        </div>
                    ))}
                </div>

                <button
                    disabled={!canSubmit || create.isPending}
                    onClick={() => create.mutate()}
                    className="px-5 py-3 bg-amber-600 text-white rounded-xl hover:bg-amber-700 disabled:opacity-50 transition-colors font-medium flex items-center justify-center gap-2"
                >
                    <Crown size={18} /> {create.isPending ? "開隊中…" : "開隊"}
                </button>
            </div>
        </div>
    );
}
