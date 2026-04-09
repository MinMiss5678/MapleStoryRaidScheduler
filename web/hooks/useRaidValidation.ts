"use client";

import { TeamSlot, TeamSlotCharacter } from "@/types/raid";
import toast from "react-hot-toast";

export function useRaidValidation() {
    const validateAddCharacter = (
        teamSlot: TeamSlot,
        character: TeamSlotCharacter,
        allTeamSlots: TeamSlot[],
        requireMembers: number = 6
    ): boolean => {
        const errors: string[] = [];

        // 1. 檢查該隊伍是否已滿
        if (teamSlot.characters.filter(c => c.characterId !== null).length >= requireMembers) {
            errors.push(`隊伍人數已達上限 (${requireMembers}人)。`);
        }

        // 2. 檢查是否已在該隊伍中 (角色重複)
        if (teamSlot.characters.some(c => c.characterId === character.characterId)) {
            errors.push(`角色「${character.characterName}」已在該隊伍中。`);
        }

        // 3. 檢查在該時段是否已有其他角色 (同一個 Discord 帳號不能同時出兩團)
        const sameTimeSlots = allTeamSlots.filter(t => 
            new Date(t.slotDateTime).getTime() === new Date(teamSlot.slotDateTime).getTime()
        );
        
        let busyInfo: { characterName: string | null } | null = null;
        for (const t of sameTimeSlots) {
            const existingChar = t.characters.find(c => c.discordId === character.discordId);
            if (existingChar) {
                busyInfo = { characterName: existingChar.characterName };
                break;
            }
        }

        if (busyInfo) {
            errors.push(`您在該時段已使用角色「${busyInfo.characterName}」參加了其他隊伍。`);
        }

        // 4. 檢查該角色在所有隊伍中的總場數是否超過限制
        const characterTotalRounds = allTeamSlots.reduce((acc, t) => {
            return acc + t.characters.filter(c => c.characterId === character.characterId).length;
        }, 0);

        if (characterTotalRounds >= character.rounds) {
            errors.push(`角色「${character.characterName}」的場數已達上限 (${character.rounds} 場)。`);
        }

        // 5. 檢查場數是否與隊伍中已有的角色相同 (補位時第一個排進來的角色決定場數)
        const firstCharacter = teamSlot.characters.find(c => c.characterId !== null);
        if (firstCharacter && firstCharacter.rounds !== character.rounds) {
            errors.push(`該隊伍限制場數為 ${firstCharacter.rounds} 場，角色「${character.characterName}」的限制場數為 ${character.rounds} 場。`);
        }

        if (errors.length > 0) {
            errors.forEach(err => toast.error(err));
            return false;
        }

        return true;
    };

    return {
        validateAddCharacter
    };
}
