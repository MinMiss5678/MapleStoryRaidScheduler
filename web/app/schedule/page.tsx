"use client";

import { useState, useCallback } from "react";
import PlayerRaidTeamCard from "./components/PlayerRaidTeamCard";
import { TeamSlot } from "@/types/raid";
import { useBosses } from "@/hooks/queries/useBosses";
import { useScheduleBossData, useTeamSlots, useJobMap } from "@/hooks/queries/useScheduleData";
import { useQueryClient } from "@tanstack/react-query";
import toast from "react-hot-toast";

export default function RaidJoinPage() {
    const { data: bosses = [], isError: bossesError } = useBosses();
    const [selectedBossId, setSelectedBossId] = useState<number | undefined>(undefined);

    // 初次載入 bosses 後自動選第一個
    const resolvedBossId = selectedBossId ?? bosses[0]?.id;

    const { data: bossData, isLoading: isLoadingCharacters } = useScheduleBossData(resolvedBossId);
    const { data: teamSlots = [], isLoading: isLoadingTeamSlots, error: teamSlotsError } = useTeamSlots(resolvedBossId);
    const { data: jobMap = {} } = useJobMap();
    const queryClient = useQueryClient();

    const onTeamSlotUpdate = useCallback((updatedTeamSlot: TeamSlot) => {
        // 直接更新 query cache，useTeamSlots 自動 re-render
        queryClient.setQueryData<TeamSlot[]>(
            ["schedule", "teamSlots", resolvedBossId],
            (prev) => prev?.map(t => t.id === updatedTeamSlot.id ? updatedTeamSlot : t) ?? []
        );
    }, [queryClient, resolvedBossId]);

    if (bossesError || teamSlotsError) {
        toast.error("載入資料失敗，請重新整理頁面。");
    }

    const selectedBoss = bosses.find(b => b.id === resolvedBossId);

    return (
        <div className="min-h-screen p-4 md:p-8 bg-background text-foreground transition-colors">
            <div className="max-w-7xl mx-auto">
                <div className="flex justify-between items-center mb-8">
                    <h1 className="text-3xl font-bold tracking-tight">補位系統</h1>
                </div>

                <div className="flex flex-col gap-6 mb-8">
                    <div className="flex items-center gap-2 overflow-x-auto pb-2 no-scrollbar">
                        {bosses.map((boss) => (
                            <button
                                key={boss.id}
                                onClick={() => setSelectedBossId(boss.id)}
                                className={`px-4 py-2 rounded-full whitespace-nowrap transition-all font-medium border-2 ${
                                    resolvedBossId === boss.id
                                        ? "bg-blue-600 border-blue-600 text-white shadow-md shadow-blue-500/20"
                                        : "bg-card border-border text-muted-foreground hover:border-blue-400 hover:text-blue-500 dark:bg-zinc-900"
                                }`}
                            >
                                {boss.name}
                            </button>
                        ))}
                    </div>
                </div>

                {selectedBoss && (
                    <>
                        <div className="flex items-center justify-between mb-6">
                            <h2 className="text-2xl font-bold">目前隊伍狀況</h2>
                            {!isLoadingTeamSlots && (
                                <span className="bg-blue-100 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400 px-3 py-1 rounded-full text-sm font-medium">
                                    共 {teamSlots.length} 個隊伍
                                </span>
                            )}
                        </div>

                        {!isLoadingTeamSlots && (
                            <div className="grid grid-cols-1 xl:grid-cols-2 2xl:grid-cols-3 gap-6 mb-8">
                                {teamSlots.map((teamSlot) => (
                                    <PlayerRaidTeamCard
                                        key={teamSlot.id}
                                        bossId={teamSlot.bossId}
                                        teamSlot={teamSlot}
                                        allTeamSlots={teamSlots}
                                        onTeamSlotUpdate={onTeamSlotUpdate}
                                        myCharacters={bossData?.characters ?? []}
                                        isLoadingCharacters={isLoadingCharacters}
                                        boss={selectedBoss}
                                        jobMap={jobMap}
                                        templates={bossData?.templates ?? []}
                                    />
                                ))}
                            </div>
                        )}
                    </>
                )}

                {!isLoadingTeamSlots && teamSlots.length === 0 && selectedBoss && (
                    <div className="text-center py-20 bg-card rounded-2xl border border-dashed border-border">
                        <p className="text-muted-foreground">目前此 Boss 尚無已排定的隊伍。</p>
                    </div>
                )}
            </div>
        </div>
    );
}
