"use client";

import { useState } from "react";
import {formatDateTime} from "@/utils/dateTimeUtil";
import {Boss, TeamSlot, TeamSlotCharacter, BossTemplate} from "@/types/raid";
import { Character } from "@/types/character";
import {Users, ChevronDown, Plus} from "lucide-react";
import toast from "react-hot-toast";
import { useRaidValidation } from "@/hooks/useRaidValidation";
import { scheduleService } from "@/services/scheduleService";

interface PlayerRaidTeamCardProps {
    bossId: number;
    teamSlot: TeamSlot;
    allTeamSlots: TeamSlot[];
    onTeamSlotUpdate: (team: TeamSlot) => void;
    myCharacters: Character[];
    isLoadingCharacters: boolean;
    boss?: Boss;
    jobMap: Record<string, string>;
    templates: BossTemplate[];
}

export default function PlayerRaidTeamCard({ 
    bossId, 
    teamSlot, 
    allTeamSlots, 
    onTeamSlotUpdate,
    myCharacters,
    isLoadingCharacters,
    boss,
    jobMap,
    templates
}: PlayerRaidTeamCardProps) {
    const [showCharPicker, setShowCharPicker] = useState<string | null>(null);
    const { validateAddCharacter } = useRaidValidation();

    const requireMembers = boss?.requireMembers ?? 6;
    const filledCharacters = teamSlot.characters.filter(c => c.characterId !== null);
    const memberCount = filledCharacters.length;
    const isFull = memberCount >= requireMembers;
    
    // 由第一個排進來的角色決定該隊伍的限制場數
    const teamRounds = filledCharacters[0]?.rounds;

    // 檢查玩家是否已有任何角色在該隊伍中
    const myCharacterInTeam = !isLoadingCharacters && filledCharacters.some(c => 
        myCharacters.some(myChar => myChar.id === c.characterId)
    );

    // 計算缺少的範本職業
    const getMissingSlots = () => {
        if (isFull) return [];
        
        const template = templates.length > 0 ? templates[0] : null;
        const missing: { category: string, count: number }[] = [];
        
        if (template) {
            // 複製一份成員職業，用於比對
            const currentJobs = filledCharacters.map(c => c.job);
            
            // 建立職業與分類的對照 (Job -> Category)
            // jobMap: { "英雄": "打手", "聖騎士": "打手", ... }
            
            template.requirements.sort((a, b) => a.priority - b.priority).forEach(req => {
                let fulfilledCount = 0;
                // 找出目前成員中屬於此 category 的人
                for (let i = currentJobs.length - 1; i >= 0; i--) {
                    const job = currentJobs[i];
                    const category = jobMap[job] || job;
                    if (category === req.jobCategory) {
                        fulfilledCount++;
                        currentJobs.splice(i, 1); // 標記已佔用
                        if (fulfilledCount >= req.count) break;
                    }
                }
                
                if (fulfilledCount < req.count) {
                    missing.push({
                        category: req.jobCategory,
                        count: req.count - fulfilledCount
                    });
                }
            });
        }
        
        // 剩餘的人數（不限職業）
        const totalMissing = requireMembers - memberCount;
        const templateMissingCount = missing.reduce((sum, m) => sum + m.count, 0);
        
        if (totalMissing > templateMissingCount) {
            missing.push({
                category: "補位",
                count: totalMissing - templateMissingCount
            });
        }
        
        return missing;
    };

    const missingSlots = getMissingSlots();

    const handleJoinTeam = async (category: string, character: Character) => {
        // 檢查報名時段
        if (teamSlot.periodId && character.registeredPeriodIds && !character.registeredPeriodIds.includes(teamSlot.periodId)) {
            const confirmed = window.confirm("您並未報名此時段，確定要補位嗎？");
            if (!confirmed) return;
        }

        const teamSlotCharacter: TeamSlotCharacter = {
            characterId: character.id,
            characterName: character.name,
            discordId: character.discordId ?? "0",
            discordName: character.discordName ?? "",
            job: character.job,
            attackPower: character.attackPower,
            rounds: character.rounds ?? 0
        };

        const isValid = validateAddCharacter(teamSlot, teamSlotCharacter, allTeamSlots, requireMembers);
        if (!isValid) return;

        const updatedTeam = {
            ...teamSlot,
            characters: [...teamSlot.characters, teamSlotCharacter]
        };
        
        setShowCharPicker(null);

        try {
            const success = await scheduleService.saveSchedule(bossId, [updatedTeam], []);
            
            if (success) {
                // 資料庫會生成 ID 並在重新獲取時更新，此處先本地更新顯示
                onTeamSlotUpdate(updatedTeam);
                toast.success("補位成功！");
                // 建議在此重新拉取資料以獲取正確 ID，但父組件應該會處理
            } else {
                toast.error("補位失敗，請稍後再試");
            }
        } catch (error) {
            console.error("Failed to join team:", error);
            toast.error("補位發生錯誤");
        }
    };

    return (
        <div className={`relative bg-card p-6 rounded-2xl shadow-sm border transition-all hover:shadow-md ${
            isFull ? "border-green-500/50 dark:border-green-500/30 bg-green-50/10" : "border-border"
        }`}>
            {/* Header: DateTime */}
            <div className="flex justify-between items-start mb-4">
                <div className="space-y-1">
                    <div className="flex items-center gap-2">
                        <h3 className="text-lg font-bold text-blue-600 dark:text-blue-400">
                            {formatDateTime(teamSlot.slotDateTime)}
                        </h3>
                    </div>
                    <div className="flex items-center gap-2 text-sm text-muted-foreground">
                        <Users size={14} />
                        <span>成員: {memberCount} / {requireMembers}</span>
                        {teamRounds && (
                            <span className="ml-2 px-2 py-0.5 bg-amber-100 dark:bg-amber-900/30 text-amber-700 dark:text-amber-300 rounded-md font-bold border border-amber-200">
                                {teamRounds} 場
                            </span>
                        )}
                    </div>
                </div>
            </div>

            {/* Members List */}
            <div>
                <table className="w-full text-sm">
                    <thead>
                        <tr className="text-muted-foreground border-b border-border">
                            <th className="text-left font-medium py-2">玩家 / 角色</th>
                            <th className="text-left font-medium py-2 hidden sm:table-cell">職業</th>
                            <th className="text-right font-medium py-2">攻擊力</th>
                            <th className="text-right font-medium py-2 w-10"></th>
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-border/50">
                        {filledCharacters.map((m, index) => (
                            <tr key={m.characterId || `filled-${index}`} className="group hover:bg-muted/30 transition-colors">
                                <td className="py-2.5">
                                    <div className="flex flex-col">
                                        <span className="font-medium">{m.characterName}</span>
                                        <span className="text-xs text-muted-foreground">{m.discordName}</span>
                                    </div>
                                </td>
                                <td className="py-2.5 hidden sm:table-cell">
                                    <span className="px-2 py-0.5 rounded text-xs bg-muted">
                                        {m.job}
                                    </span>
                                </td>
                                <td className="py-2.5 text-right font-mono text-xs">
                                    {m.attackPower.toLocaleString()}
                                </td>
                                <td className="py-2.5 text-right"></td>
                            </tr>
                        ))}

                        {missingSlots.map((slot, index) => (
                            <tr key={`${slot.category}-${index}`} className="bg-amber-50/20 group hover:bg-muted/30 transition-colors">
                                <td className="py-2.5">
                                    <span className="text-amber-600 dark:text-amber-400 font-bold flex items-center gap-1">
                                        <Plus size={14} /> (待補位) {slot.count > 1 ? `x${slot.count}` : ""}
                                    </span>
                                </td>
                                <td className="py-2.5 hidden sm:table-cell">
                                    <span className="px-2 py-0.5 rounded text-xs bg-amber-100 dark:bg-amber-900/30 text-amber-700 dark:text-amber-300 border border-amber-200">
                                        {slot.category}
                                    </span>
                                </td>
                                <td className="py-2.5 text-right font-mono text-xs text-muted-foreground">-</td>
                                <td className="py-2.5 text-right">
                                    {!isLoadingCharacters && !myCharacterInTeam && (
                                        <div className="relative inline-block text-left">
                                            <button
                                                onClick={() => {
                                                    setShowCharPicker(showCharPicker === `${slot.category}-${index}` ? null : `${slot.category}-${index}`);
                                                }}
                                                className="bg-blue-600 text-white px-2 py-1 rounded text-xs hover:bg-blue-700 transition-colors flex items-center gap-1 ml-auto whitespace-nowrap"
                                            >
                                                補位 <ChevronDown size={12} />
                                            </button>
                                            {showCharPicker === `${slot.category}-${index}` && (
                                                <div className="absolute right-0 mt-2 w-64 rounded-md shadow-xl bg-white dark:bg-zinc-900 border border-border ring-1 ring-black ring-opacity-5 z-[100]">
                                                    <div className="py-1 max-h-48 overflow-auto">
                                                        <div className="px-3 py-1 text-xs font-semibold text-muted-foreground border-b border-border mb-1">
                                                            選擇您的角色 ({slot.category})
                                                        </div>
                                                        {myCharacters
                                                            .map((char) => (
                                                                <button
                                                                    key={char.id}
                                                                    onClick={() => {
                                                                        handleJoinTeam(slot.category, char);
                                                                    }}
                                                                    className="flex flex-col w-full px-4 py-2 text-sm text-left hover:bg-muted transition-colors border-b last:border-0 border-border/50"
                                                                >
                                                                    <div className="flex justify-between items-center w-full">
                                                                        <span className="font-medium text-foreground">{char.name}</span>
                                                                        <span className="text-[10px] px-1.5 py-0.5 bg-amber-100 dark:bg-amber-900/30 text-amber-700 dark:text-amber-300 rounded font-bold">
                                                                            {char.rounds} 場
                                                                        </span>
                                                                    </div>
                                                                    <span className="text-xs text-muted-foreground">{char.job} / {char.attackPower.toLocaleString()}</span>
                                                                </button>
                                                            ))}
                                                        {myCharacters.length === 0 && (
                                                            <div className="px-4 py-3 text-xs text-muted-foreground text-center">
                                                                尚未建立角色
                                                            </div>
                                                        )}
                                                    </div>
                                                </div>
                                            )}
                                        </div>
                                    )}
                                </td>
                            </tr>
                        ))}
                        {!isLoadingCharacters && teamSlot.characters.length === 0 && (
                            <tr>
                                <td colSpan={4} className="py-8 text-center text-muted-foreground italic">
                                    尚無成員。
                                </td>
                            </tr>
                        )}
                    </tbody>
                </table>
            </div>
        </div>
    );
}
