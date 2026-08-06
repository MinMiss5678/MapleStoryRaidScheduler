// leader-led 前端型別（對齊後端 camelCase JSON）

export type Membership = {
    memberId: number;
    teamSlotId: number;
    bossName: string | null;
    slotDateTime: string; // ISO 字串
    characterId: string | null;
    characterName: string | null;
    job: string | null;
    attackPower: number;
    status: string; // Applied | Invited | Confirmed | Rejected
};

export type OpenTeamRequirementJob = { job: string; minAttackPower: number };
export type OpenTeamRequirement = { count: number; minClearCount: number; jobs: OpenTeamRequirementJob[] };
export type OpenTeam = {
    teamSlotId: number;
    bossId: number;
    bossName: string | null;
    slotDateTime: string;
    requireMembers: number;
    confirmedCount: number;
    description: string | null;
    requirements: OpenTeamRequirement[];
};

export type InvitationAction = 'accept' | 'decline';
