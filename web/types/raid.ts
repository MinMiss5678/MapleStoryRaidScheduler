export type TeamSlotCharacter = {
    id?: number;
    characterId: string | null;
    discordId: string;
    discordName: string;
    characterName: string | null;
    job: string;
    attackPower: number;
    level?: number;
    rounds: number;
    isManual?: boolean;
};

export type TeamSlot = {
    id: number;
    bossId: number;
    periodId?: number;
    slotDateTime: Date;
    characters: TeamSlotCharacter[];
    deleteTeamSlotCharacterIds?: number[]; // 前端暫存，不在 API 回應中
    source?: string;   // "auto" | "admin"，見後端 TeamSlotSource
    templateId?: number;
    // 註：尚未存檔的新隊以 id < 0 標記（存檔時走 CREATE）
};

// Character 已移至 character.ts

export type Boss = {
    id: number;
    name: string;
    requireMembers: number;
    roundConsumption: number;
};

export type BossTemplateRequirement = {
    id?: number;
    bossTemplateId: number;
    jobCategory: string;
    count: number;
    priority: number;
    minLevel?: number;
    minAttribute?: number;
    isOptional?: boolean;
    description?: string;
};

export type BossTemplate = {
    id: number;
    bossId: number;
    name: string;
    requirements: BossTemplateRequirement[];
};
