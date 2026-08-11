export const ALLOWED_PATHS = new Set([
    'auth', 'character', 'boss', 'register', 'schedule',
    'teamslot', 'period', 'systemconfig', 'jobcategory',
    // leader-led 玩家/隊長自助讀 API：/api/Me/Invitations、/Me/Teams、/Me/LedTeams
    'me',
    // period-less Phase 2b-write：玩家自助管理可用時段例外 /api/AvailabilityOverride
    'availabilityoverride',
    // E2E test-login：只在非 production 開放（proxy 層 + 後端環境旗標雙重保險）
    ...(process.env.NODE_ENV !== 'production' ? ['test'] : []),
]);
