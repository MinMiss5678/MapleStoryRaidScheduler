// apiClient 的認證錯誤處理：401 導回首頁登入（/login 不存在，導過去是 404）、403 只報錯不導頁。
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { apiClient, ApiError } from '@/services/apiClient'

describe('apiClient — 認證錯誤處理', () => {
    const originalLocation = window.location

    beforeEach(() => {
        // 攔截導頁：以可寫的假 location 取代，斷言 href 被設成什麼
        Object.defineProperty(window, 'location', { value: { href: 'http://localhost/teams' }, writable: true, configurable: true })
    })

    afterEach(() => {
        Object.defineProperty(window, 'location', { value: originalLocation, writable: true, configurable: true })
        vi.unstubAllGlobals()
    })

    it('401 → 導向首頁 /（登入入口）並丟出 ApiError(401)', async () => {
        vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 401 })))

        const err = await apiClient.get<never>('/api/Me/teams').catch((e: ApiError) => e)

        expect(window.location.href).toBe('/')
        expect(err).toBeInstanceOf(ApiError)
        expect(err.status).toBe(401)
        expect(err.message).toBe('未登入或登入已過期')
    })

    it('403 → 不導頁，丟出 ApiError(403, 權限不足)', async () => {
        vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 403 })))

        const err = await apiClient.get<never>('/api/Boss').catch((e: ApiError) => e)

        expect(window.location.href).toBe('http://localhost/teams')
        expect(err).toBeInstanceOf(ApiError)
        expect(err.status).toBe(403)
        expect(err.message).toBe('權限不足')
    })
})
