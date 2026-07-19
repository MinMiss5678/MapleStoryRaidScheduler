import { defineConfig, devices } from '@playwright/test';

// 本機：baseURL=localhost:3000 + webServer 自動起 next dev（reuse 既有的）。
// CI（frontend 由 compose 提供、playwright 在另一容器跑）：設
//   PLAYWRIGHT_BASE_URL=http://<frontend-host>:3000 + PLAYWRIGHT_NO_WEBSERVER=1
const baseURL = process.env.PLAYWRIGHT_BASE_URL || 'http://localhost:3000';
const noWebServer = !!process.env.PLAYWRIGHT_NO_WEBSERVER;

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  reporter: 'html',
  use: {
    baseURL,
    trace: 'on-first-retry', // 失敗重試才留 trace，方便查
  },
  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
  // CI 由 compose 提供 frontend 時跳過自動起 dev server
  ...(noWebServer ? {} : {
    webServer: {
      command: 'npm run dev',
      url: 'http://localhost:3000',
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      env: { BACKEND_API_URL: 'http://localhost:5230' },
    },
  }),
});
