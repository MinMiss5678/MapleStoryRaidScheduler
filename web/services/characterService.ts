import { Character } from "@/types/character";
import { apiClient } from './apiClient';

// per 角色 per 王 的通關數（leader-led 候選 MinClearCount 過濾的資料來源）
export interface BossClear {
    bossId: number;
    clearCount: number;
}

export const characterService = {
    async getBossClears(characterId: string): Promise<BossClear[]> {
        return apiClient.get<BossClear[]>(`/api/character/${encodeURIComponent(characterId)}/BossClears`);
    },
    async saveBossClears(characterId: string, clears: BossClear[]): Promise<void> {
        await apiClient.post(`/api/character/${encodeURIComponent(characterId)}/BossClears`, clears);
    },
    async getCharacters(bossId?: number): Promise<Character[]> {
        const url = bossId ? `/api/character/GetWithDiscordName?bossId=${bossId}` : "/api/character/GetWithDiscordName";
        return apiClient.get<Character[]>(url);
    },
    async deleteCharacter(id: string): Promise<void> {
        const encodedId = encodeURIComponent(id);
        await apiClient.delete(`/api/character/${encodedId}`);
    },
    async createCharacter(character: Omit<Character, 'id'>, idempotencyKey?: string): Promise<Character> {
        return apiClient.post<Character>("/api/character", character, { idempotencyKey });
    },
    async updateCharacter(character: Character, idempotencyKey?: string): Promise<Character> {
        return apiClient.put<Character>(`/api/character/${character.id}`, character, { idempotencyKey });
    }
};
