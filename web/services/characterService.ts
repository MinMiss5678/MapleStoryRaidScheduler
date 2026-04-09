import { Character } from "@/types/character";
import { apiClient } from './apiClient';

export const characterService = {
    async getCharacters(bossId?: number): Promise<Character[]> {
        const url = bossId ? `/api/character/GetWithDiscordName?bossId=${bossId}` : "/api/character/GetWithDiscordName";
        return apiClient.get<Character[]>(url);
    },
    async deleteCharacter(id: string): Promise<void> {
        const encodedId = encodeURIComponent(id);
        await apiClient.delete(`/api/character/${encodedId}`);
    },
    async createCharacter(character: Omit<Character, 'id'>): Promise<Character> {
        return apiClient.post<Character>("/api/character", character);
    },
    async updateCharacter(character: Character): Promise<Character> {
        return apiClient.put<Character>(`/api/character/${character.id}`, character);
    }
};
