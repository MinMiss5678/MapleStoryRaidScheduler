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
    deleteTeamSlotCharacterIds: number[];
    isTemporary?: boolean;
    isPublished?: boolean;
    templateId?: number;
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
