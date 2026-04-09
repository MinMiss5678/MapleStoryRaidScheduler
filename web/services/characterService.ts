import { Character } from "@/types/character";

export const characterService = {
    async getCharacters(bossId?: number): Promise<Character[]> {
        const url = bossId ? `/api/character/GetWithDiscordName?bossId=${bossId}` : "/api/character/GetWithDiscordName";
        const res = await fetch(url);
        if (!res.ok) throw new Error("無法取得角色列表");
        return res.json();
    },

    async deleteCharacter(id: string): Promise<boolean> {
        const encodedId = encodeURIComponent(id);
        const res = await fetch(`/api/character/${encodedId}`, { method: "DELETE" });
        return res.ok;
    },

    async createCharacter(character: Omit<Character, 'id'>): Promise<Character> {
        const res = await fetch("/api/character", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(character),
        });
        if (!res.ok) throw new Error("新增失敗");
        return res.json();
    },

    async updateCharacter(character: Character): Promise<Character> {
        const res = await fetch(`/api/character/${character.id}`, {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(character),
        });
        if (!res.ok) throw new Error("更新失敗");
        return res.json();
    }
};
