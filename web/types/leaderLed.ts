// leader-led 前端型別（對齊後端 camelCase JSON）

// 本人自視（我的邀請 / 我的隊）；審核佇列另用 Applicant
export type Membership = {
    memberId: number;
    teamSlotId: number;
    bossName: string | null;
    slotDateTime: string; // ISO 字串
    characterId: string | null;
    characterName: string | null;
    job: string | null;
    attackPower: number;
    level: number;
    status: string; // Applied | Invited | Confirmed | Rejected
    requireMembers: number; // 隊伍容量
    confirmedCount: number; // 已入隊數（用來判斷是否已滿）
};

// 隊長審核佇列的一筆申請（Push）：認「人」+ 決策所需能力
export type Applicant = {
    memberId: number;
    characterId: string | null;
    characterName: string | null;
    discordName: string | null;
    job: string | null;
    attackPower: number;
    level: number;
    bossClearCount: number;
    mapleBlessingLevel: number;
};

export type OpenTeamRequirementJob = { job: string; minAttackPower: number };
export type OpenTeamRequirement = { count: number; minClearCount: number; minLevel: number; jobs: OpenTeamRequirementJob[] };
export type OpenTeam = {
    teamSlotId: number;
    bossId: number;
    bossName: string | null;
    slotDateTime: string;
    requireMembers: number;
    confirmedCount: number;
    description: string | null;
    requirements: OpenTeamRequirement[];
    confirmedMembers: OpenTeamMember[]; // 已確認成員能力（職業/攻擊/祝福，不露身分）——尋隊看組成+戰力
};

// 尋隊看得到的已確認成員能力（不含身分）
export type OpenTeamMember = { job: string | null; attackPower: number; level: number; mapleBlessingLevel: number };

export type InvitationAction = 'accept' | 'decline';

// 隊長轉讓
export type LeaderTransfer = { teamSlotId: number; bossName: string | null; slotDateTime: string };
export type RosterMember = { memberId: number; characterName: string | null; discordName: string | null };
export type ApplicationAction = 'approve' | 'reject';

// 招募缺口一列（隊長挑候選時看「還缺什麼職業」；jobs 空=不限職業）
export type RecruitmentGapRow = { jobs: string[]; required: number; remaining: number };

// 隊伍組成一列（已入隊成員看隊友；以 discordName 呈現「人」，characterName 僅 fallback）
export type TeamMember = { discordName: string | null; characterName: string | null; job: string | null; attackPower: number; level: number; mapleBlessingLevel: number; isLeader: boolean };

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
    discordName: string | null; // 顯示名（公會暱稱優先）——讓隊長認得出老班底
    job: string;
    attackPower: number;
    level: number;
    mapleBlessingLevel: number;
    bossClearCount: number;
    leaveRateWarn: boolean; // 退團率偏高警示（admin 開且達門檻才 true）
    prefersThisBoss: boolean; // 該角色偏好清單含本王 → 組內排前 + 標「偏好此王」（軟訊號）
};

// 開隊 command（對齊後端 CreateTeamCommand；LeaderDiscordId 由後端從登入身分注入）
export type CreateTeamRequirementJobInput = { job: string; minAttackPower: number };
export type CreateTeamRequirementInput = { count: number; minClearCount: number; minLevel: number; jobs: CreateTeamRequirementJobInput[] };
export type CreateTeamCommand = {
    bossId: number;
    slotDateTime: string;
    kind?: "Scheduled" | "Instant";   // period-less §8 Phase 3：即時團=Instant
    description?: string;
    leaderCharacterId?: string;        // 隊長帶自己下去打的角色（佔 1 位、自動 Confirmed）；不帶=只揪人
    requirements: CreateTeamRequirementInput[];
};
