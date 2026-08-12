export const ALLOWED_PATHS = new Set([
    'auth', 'character', 'boss',
    'teamslot', 'systemconfig',
    // leader-led 玩家/隊長自助讀 API：/api/Me/Invitations、/Me/Teams、/Me/LedTeams
    'me',
    // period-less Phase 2b-write：玩家自助管理可用時段例外 /api/AvailabilityOverride
    'availabilityoverride',
    // period-less 報名 UX 大改：玩家 profile（常設時段 + 角色 opt-in）/api/Profile
    'profile',
    // period-less Phase 3：即時找隊看板 /api/LfgIntent
    'lfgintent',
    // E2E test-login：只在非 production 開放（proxy 層 + 後端環境旗標雙重保險）
    ...(process.env.NODE_ENV !== 'production' ? ['test'] : []),
]);
