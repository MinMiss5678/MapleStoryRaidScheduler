"use client";

import {useState, useEffect} from "react";
import PlayerRaidTeamCard from "./components/PlayerRaidTeamCard";
import {TeamSlot, Boss, BossTemplate} from "@/types/raid";
import {Character} from "@/types/character";
import {characterService} from "@/services/characterService";
import {bossService, jobCategoryService} from "@/services/bossService";
import {scheduleService} from "@/services/scheduleService";
import toast from "react-hot-toast";

export default function RaidJoinPage() {
    const [bosses, setBosses] = useState<Boss[]>([]);
    const [selectedBoss, setSelectedBoss] = useState<Boss>();
    const [teamSlots, setTeamSlots] = useState<TeamSlot[]>([]);
    const [myCharacters, setMyCharacters] = useState<Character[]>([]);
    const [templates, setTemplates] = useState<BossTemplate[]>([]);
    const [jobMap, setJobMap] = useState<Record<string, string>>({});
    const [isLoadingCharacters, setIsLoadingCharacters] = useState(true);
    const [isLoadingTeamSlots, setIsLoadingTeamSlots] = useState(false);

    useEffect(() => {
        if (!selectedBoss) return;
        const fetchBossData = async () => {
            try {
                const [charactersData, templatesData] = await Promise.all([
                    characterService.getCharacters(selectedBoss.id),
                    bossService.getTemplates(selectedBoss.id)
                ]);
                setMyCharacters(charactersData);
                setTemplates(templatesData);
            } catch (error) {
                toast.error(error instanceof Error ? error.message : "載入 Boss 資料失敗");
            } finally {
                setIsLoadingCharacters(false);
            }
        };
        fetchBossData();
    }, [selectedBoss]);

    useEffect(() => {
        async function loadInitialData() {
            try {
                const [bossData, jobMapData] = await Promise.all([
                    bossService.getAllBosses(),
                    jobCategoryService.getJobMap()
                ]);
                setBosses(bossData);
                setJobMap(jobMapData);
                if (bossData.length > 0) {
                    setSelectedBoss(bossData[0]);
                } else {
                    // 如果沒有 Boss，則停止載入角色
                    setIsLoadingCharacters(false);
                }
            } catch (error) {
                toast.error(error instanceof Error ? error.message : "載入初始資料失敗");
                setIsLoadingCharacters(false);
            }
        }

        loadInitialData();
    }, []);

    useEffect(() => {
        if (!selectedBoss) return;
        async function loadTeamSlots() {
            setIsLoadingTeamSlots(true);
            try {
                const data = await scheduleService.getTeamSlots(selectedBoss!.id);
                setTeamSlots(data);
            } catch (error) {
                toast.error(error instanceof Error ? error.message : "載入隊伍資料失敗");
            } finally {
                setIsLoadingTeamSlots(false);
            }
        }

        loadTeamSlots();
    }, [selectedBoss]);

    const onTeamSlotUpdate = (updatedTeamSlot: TeamSlot) => {
        setTeamSlots(prev => prev.map(t => t.id === updatedTeamSlot.id ? { ...updatedTeamSlot } : t));
    };

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
                                onClick={() => {
                                    setSelectedBoss(boss);
                                }}
                                className={`px-4 py-2 rounded-full whitespace-nowrap transition-all font-medium border-2 ${
                                    selectedBoss?.id === boss.id
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
                        
                        {isLoadingTeamSlots ? (
                            null
                        ) : (
                            <div className="grid grid-cols-1 xl:grid-cols-2 2xl:grid-cols-3 gap-6 mb-8">
                                {teamSlots.map((teamSlot) => (
                                    <PlayerRaidTeamCard
                                        key={teamSlot.id}
                                        bossId={teamSlot.bossId}
                                        teamSlot={teamSlot}
                                        allTeamSlots={teamSlots}
                                        onTeamSlotUpdate={onTeamSlotUpdate}
                                        myCharacters={myCharacters}
                                        isLoadingCharacters={isLoadingCharacters}
                                        boss={selectedBoss}
                                        jobMap={jobMap}
                                        templates={templates}
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
