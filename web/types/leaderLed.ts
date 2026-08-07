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
    requireMembers: number; // 隊伍容量
    confirmedCount: number; // 已入隊數（用來判斷是否已滿）
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
export type ApplicationAction = 'approve' | 'reject';

// 隊長「我開的隊」hub 一列（對齊後端 LedTeamDto）
export type LedTeam = {
    teamSlotId: number;
    bossId: number;
    bossName: string | null;
    slotDateTime: string;
    requireMembers: number;
    confirmedCount: number;
    appliedCount: number;
    invitedCount: number;
    description: string | null;
};

// 隊長挑人的候選（對齊後端 TeamCandidateDto，只回能力欄、無 discord 身分）
export type TeamCandidate = {
    characterId: string;
    characterName: string;
    job: string;
    attackPower: number;
    mapleBlessingLevel: number;
    bossClearCount: number;
};

// 開隊 command（對齊後端 CreateTeamCommand；LeaderDiscordId 由後端從登入身分注入）
export type CreateTeamRequirementJobInput = { job: string; minAttackPower: number };
export type CreateTeamRequirementInput = { count: number; minClearCount: number; jobs: CreateTeamRequirementJobInput[] };
export type CreateTeamCommand = {
    bossId: number;
    slotDateTime: string;
    description?: string;
    requirements: CreateTeamRequirementInput[];
};
