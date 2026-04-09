"use client";

import { useState, useEffect, useMemo } from "react";
import { TeamSlot, Boss } from "@/types/raid";
import { Character } from "@/types/character";
import ResultTeamCard from "./components/ResultTeamCard";
import { Filter, Users, Calendar as CalendarIcon, CheckCircle2 } from "lucide-react";
import { bossService } from "@/services/bossService";
import { characterService } from "@/services/characterService";
import { scheduleService } from "@/services/scheduleService";


export default function ScheduleResultPage() {
    const [bosses, setBosses] = useState<Boss[]>([]);
    const [teamSlots, setTeamSlots] = useState<TeamSlot[]>([]);
    const [myCharacters, setMyCharacters] = useState<Character[]>([]);
    const [selectedBossId, setSelectedBossId] = useState<number | "all">("all");
    const [showOnlyMine, setShowOnlyMine] = useState(true);

    // 1. 基礎數據加載 (Boss 列表, 個人角色)
    useEffect(() => {
        async function fetchBaseData() {
            try {
                const [bossData, charData] = await Promise.all([
                    bossService.getAllBosses(),
                    characterService.getCharacters()
                ]);

                setBosses(bossData);
                setMyCharacters(charData);
            } catch (error) {
                console.error("Failed to fetch base data:", error);
            }
        }

        fetchBaseData();
    }, []);

    const isMyTeam = useMemo(() => (team: TeamSlot) => {
        return team.characters.some(c => 
            myCharacters.some(myChar => myChar.id === c.characterId)
        );
    }, [myCharacters]);

    // 2. 根據過濾條件加載團隊數據
    useEffect(() => {
        if (bosses.length === 0) return;

        async function fetchFilteredData() {
            try {
                let data: TeamSlot[] = [];
                if (showOnlyMine) {
                    data = await scheduleService.getByDiscordId();
                } else {
                    const bossIdToFetch = selectedBossId === "all" ? bosses[0].id : selectedBossId;
                    data = await scheduleService.getTeamSlots(bossIdToFetch);
                }
                setTeamSlots(data);
            } catch (error) {
                console.error("Failed to fetch filtered data:", error);
            }
        }

        fetchFilteredData();
    }, [selectedBossId, showOnlyMine, bosses]);

    const myTeamsCount = useMemo(() => {
        return teamSlots.filter(t => isMyTeam(t)).length;
    }, [teamSlots, isMyTeam]);

    const filteredTeams = useMemo(() => {
        return teamSlots
            .filter(t => {
                const bossMatch = selectedBossId === "all" || t.bossId === selectedBossId;
                const mineMatch = !showOnlyMine || isMyTeam(t);
                return bossMatch && mineMatch;
            })
            .sort((a, b) => new Date(a.slotDateTime).getTime() - new Date(b.slotDateTime).getTime());
    }, [teamSlots, selectedBossId, showOnlyMine, isMyTeam]);

    return (
        <div className="min-h-screen p-4 md:p-8 bg-background text-foreground transition-colors">
            <div className="max-w-7xl mx-auto">
                <div className="flex flex-col md:flex-row justify-between items-start md:items-center mb-8 gap-4">
                    <div>
                        <h1 className="text-3xl font-bold tracking-tight">排團結果</h1>
                        <p className="text-muted-foreground mt-1">查看本週已排定的所有 Boss 團隊資訊。</p>
                    </div>
                    
                    <div className="flex flex-wrap items-center gap-3">
                        <div className="flex bg-card border border-border rounded-xl p-1 shadow-sm">
                            <button
                                onClick={() => setShowOnlyMine(true)}
                                className={`px-4 py-1.5 rounded-lg text-sm font-medium transition-all flex items-center gap-2 ${
                                    showOnlyMine 
                                        ? "bg-blue-600 text-white shadow-md" 
                                        : "text-muted-foreground hover:text-foreground"
                                }`}
                            >
                                <CheckCircle2 size={16} />
                                我的場次 ({myTeamsCount})
                            </button>
                            <button
                                onClick={() => setShowOnlyMine(false)}
                                className={`px-4 py-1.5 rounded-lg text-sm font-medium transition-all flex items-center gap-2 ${
                                    !showOnlyMine 
                                        ? "bg-blue-600 text-white shadow-md" 
                                        : "text-muted-foreground hover:text-foreground"
                                }`}
                            >
                                <Users size={16} />
                                所有場次
                            </button>
                        </div>
                    </div>
                </div>

                {/* Filters */}
                <div className="bg-card border border-border rounded-2xl p-4 mb-8 flex flex-wrap items-center gap-4 shadow-sm">
                    <div className="flex items-center gap-2 text-sm font-medium text-muted-foreground mr-2">
                        <Filter size={16} />
                        篩選:
                    </div>
                    <div className="flex items-center gap-2 overflow-x-auto pb-1 no-scrollbar">
                        <button
                            onClick={() => setSelectedBossId("all")}
                            className={`px-3 py-1.5 rounded-full text-xs font-semibold whitespace-nowrap transition-all border ${
                                selectedBossId === "all"
                                    ? "bg-blue-100 border-blue-200 text-blue-700 dark:bg-blue-900/40 dark:border-blue-800 dark:text-blue-300"
                                    : "bg-background border-border text-muted-foreground hover:border-blue-400 hover:text-blue-500"
                            }`}
                        >
                            全部 Boss
                        </button>
                        {bosses.map((boss) => (
                            <button
                                key={boss.id}
                                onClick={() => setSelectedBossId(boss.id)}
                                className={`px-3 py-1.5 rounded-full text-xs font-semibold whitespace-nowrap transition-all border ${
                                    selectedBossId === boss.id
                                        ? "bg-blue-100 border-blue-200 text-blue-700 dark:bg-blue-900/40 dark:border-blue-800 dark:text-blue-300"
                                        : "bg-background border-border text-muted-foreground hover:border-blue-400 hover:text-blue-500"
                                }`}
                            >
                                {boss.name}
                            </button>
                        ))}
                    </div>
                </div>

                {/* Results Grid */}
                {filteredTeams.length > 0 ? (
                    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-6">
                        {filteredTeams.map((team) => (
                            <ResultTeamCard
                                key={team.id}
                                teamSlot={team}
                                bossName={bosses.find(b => b.id === team.bossId)?.name || "Unknown"}
                                isMyTeam={isMyTeam(team)}
                            />
                        ))}
                    </div>
                ) : (
                    <div className="text-center py-20 bg-card rounded-2xl border border-dashed border-border flex flex-col items-center justify-center">
                        <CalendarIcon size={48} className="text-muted-foreground mb-4 opacity-20" />
                        <h3 className="text-lg font-medium text-muted-foreground">
                            {showOnlyMine ? "您本週尚未被排入任何團隊。" : "目前尚無排定的團隊資訊。"}
                        </h3>
                        {showOnlyMine && teamSlots.length > myTeamsCount && (
                            <button 
                                onClick={() => setShowOnlyMine(false)}
                                className="mt-4 text-blue-600 hover:underline text-sm font-medium"
                            >
                                查看所有場次
                            </button>
                        )}
                    </div>
                )}
            </div>
        </div>
    );
}
