import { defineConfig, devices } from '@playwright/test';

// E2E：對真前端（Phase 2 起加真後端/DB）跑。與 Vitest 單元測試分開（testDir: e2e）。
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  reporter: 'html',
  use: {
    baseURL: 'http://localhost:3000',
    trace: 'on-first-retry',   // 失敗重試才留 trace，方便查
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
  // 沒有既有 dev server 時自動啟動 next dev（Phase 2+ 的 auth 測試還需 compose.e2e 的 backend）
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:3000',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
    // 前端 proxy 轉發到 E2E backend（compose.e2e 對外 5230）
    env: { BACKEND_API_URL: 'http://localhost:5230' },
  },
});
