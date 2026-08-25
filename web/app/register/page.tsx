"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { UserCog, Save, Swords, Zap, CalendarClock } from "lucide-react";
import toast from "react-hot-toast";
import { profileService, ProfileAvailability } from "@/services/profileService";
import { ApiError } from "@/services/apiClient";
import { AvailabilityStandingEditor } from "./components/AvailabilityStandingEditor";

export default function ProfilePage() {
    const qc = useQueryClient();
    const { data, isLoading } = useQuery({ queryKey: ["profile"], queryFn: () => profileService.getProfile() });

    const [availabilities, setAvailabilities] = useState<ProfileAvailability[]>([]);
    const [seeking, setSeeking] = useState<Set<string>>(new Set());

    // 載入後把伺服器狀態灌進本地編輯狀態
    useEffect(() => {
        if (!data) return;
        setAvailabilities(data.availabilities);
        setSeeking(new Set(data.characters.filter(c => c.isSeekingRaid).map(c => c.id)));
    }, [data]);

    const save = useMutation({
        mutationFn: () => profileService.saveProfile({ availabilities, seekingCharacterIds: [...seeking] }),
        onSuccess: () => {
            toast.success("已儲存");
            qc.invalidateQueries({ queryKey: ["profile"] });
        },
        onError: (e) => toast.error(e instanceof ApiError ? e.message : "儲存失敗，請稍後再試"),
    });

    const toggle = (id: string) => setSeeking(prev => {
        const next = new Set(prev);
        if (next.has(id)) next.delete(id); else next.add(id);
        return next;
    });

    return (
        <div className="max-w-2xl mx-auto px-4 py-8">
            <div className="flex items-center gap-3 mb-6">
                <div className="p-2 bg-amber-100 dark:bg-amber-900/30 text-amber-600 dark:text-amber-400 rounded-lg">
                    <UserCog size={24} />
                </div>
                <div>
                    <h1 className="text-2xl font-bold">我的資料</h1>
                    <p className="text-sm text-muted-foreground">設定你的常設可用時段、選哪些角色要被揪團。隊長會依此挑候選。</p>
                </div>
            </div>

            {isLoading ? (
                <p className="text-muted-foreground py-12 text-center">載入中…</p>
            ) : (
                <div className="flex flex-col gap-8">
                    {/* 常設可用時段 */}
                    <section>
                        <div className="flex items-center justify-between mb-3">
                            <h2 className="flex items-center gap-2 text-sm font-semibold text-muted-foreground">
                                <CalendarClock size={16} /> 常設可用時段
                            </h2>
                            <Link href="/me/availability" className="text-xs text-sky-600 dark:text-sky-400 hover:underline">
                                設定特定日期例外 →
                            </Link>
                        </div>
                        <AvailabilityStandingEditor availabilities={availabilities} onChange={setAvailabilities} />
                    </section>

                    {/* 角色參戰 opt-in */}
                    <section>
                        <h2 className="flex items-center gap-2 text-sm font-semibold text-muted-foreground mb-3">
                            <Swords size={16} /> 參戰角色
                        </h2>
                        {(!data || data.characters.length === 0) ? (
                            <div className="bg-card border border-border rounded-2xl p-6 text-center text-muted-foreground text-sm">
                                你還沒有角色。到「<Link href="/character" className="text-sky-600 dark:text-sky-400 hover:underline">角色管理</Link>」新增。
                            </div>
                        ) : (
                            <ul className="flex flex-col gap-2">
                                {data.characters.map(c => (
                                    <li key={c.id}
                                        className={`flex items-center gap-3 border rounded-xl px-4 py-3 cursor-pointer transition-colors ${seeking.has(c.id) ? "border-green-500 bg-green-50 dark:bg-green-900/20" : "border-border"}`}
                                        onClick={() => toggle(c.id)}>
                                        <input type="checkbox" checked={seeking.has(c.id)} readOnly className="w-4 h-4 accent-green-600" />
                                        <span className="font-medium">{c.name}</span>
                                        <span className="text-sm text-muted-foreground">{c.job}</span>
                                        <span className="ml-auto text-sm text-muted-foreground">Lv {c.level}</span>
                                        <span className="flex items-center gap-1 text-sm text-muted-foreground">
                                            <Zap size={14} /> {c.attackPower}
                                        </span>
                                    </li>
                                ))}
                            </ul>
                        )}
                    </section>

                    <button disabled={save.isPending} onClick={() => save.mutate()}
                        className="self-end px-5 py-2.5 bg-amber-600 text-white rounded-xl hover:bg-amber-700 disabled:opacity-50 flex items-center gap-1.5 font-medium">
                        <Save size={16} /> 儲存
                    </button>
                </div>
            )}
        </div>
    );
}
