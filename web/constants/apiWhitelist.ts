export const ALLOWED_PATHS = new Set([
    'auth', 'character', 'boss', 'register', 'schedule',
    'teamslot', 'period', 'systemconfig', 'jobcategory',
    // E2E test-login：只在非 production 開放（proxy 層 + 後端環境旗標雙重保險）
    ...(process.env.NODE_ENV !== 'production' ? ['test'] : []),
]);
