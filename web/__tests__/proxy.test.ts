/**
 * @vitest-environment node
 */
// proxy 的真實 client IP 覆寫（安全）：證明 client 偽造的 x-forwarded-for 蓋不掉真 IP。
// 用 node 環境（NextRequest / Request / Response / fetch 走 node 內建）。
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { NextRequest } from 'next/server'
import { GET, POST } from '@/app/api/[...path]/route'

describe('API proxy — 真實 client IP 覆寫（安全）', () => {
    let fetchMock: ReturnType<typeof vi.fn>

    beforeEach(() => {
        process.env.BACKEND_API_URL = 'http://backend:5230'
        fetchMock = vi.fn().mockResolvedValue(new Response('ok', { status: 200 }))
        vi.stubGlobal('fetch', fetchMock)
    })

    afterEach(() => {
        vi.unstubAllGlobals()
    })

    // 打一個合法路徑（character 在白名單），回傳轉發給後端的 headers
    async function forwardedHeaders(headers: Record<string, string>): Promise<Headers> {
        const req = new NextRequest('http://localhost/api/character/GetWithDiscordName', { headers })
        await GET(req, { params: Promise.resolve({ path: ['character', 'GetWithDiscordName'] }) })
        expect(fetchMock).toHaveBeenCalledOnce()
        return fetchMock.mock.calls[0][1].headers as Headers
    }

    it('cf-connecting-ip 覆寫 client 偽造的 x-forwarded-for', async () => {
        const sent = await forwardedHeaders({
            'cf-connecting-ip': '1.2.3.4',
            'x-forwarded-for': '9.9.9.9', // client 偽造的
        })
        expect(sent.get('x-forwarded-for')).toBe('1.2.3.4') // 用真 IP，不是偽造的
        expect(sent.get('cf-connecting-ip')).toBeNull()      // 可偽造的 header 已刪，不轉給後端
    })

    it('沒有 cf-connecting-ip 時，client 的 x-forwarded-for 不被採用（不可注入）', async () => {
        const sent = await forwardedHeaders({
            'x-forwarded-for': '9.9.9.9', // 無 Cloudflare、只有 client 偽造
        })
        expect(sent.get('x-forwarded-for')).toBeNull() // 不帶 → 後端看 proxy IP，client 蓋不掉
    })
})

describe('API proxy — 204 No Content 轉發', () => {
    // 204/205/304 依 Fetch 規格不可帶 body（連空的都不行），否則 NextResponse 建構會丟 TypeError。
    // 補位端點回 204，是第一個踩到這個坑的端點——回歸測試守住，避免未來又有端點回 204 時炸掉。
    beforeEach(() => {
        process.env.BACKEND_API_URL = 'http://backend:5230'
        vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 204 })))
    })

    afterEach(() => {
        vi.unstubAllGlobals()
    })

    it('後端回 204 時，proxy 不帶 body 轉發，不拋例外', async () => {
        const req = new NextRequest('http://localhost/api/teamSlot/Fill', { method: 'POST' })
        const res = await POST(req, { params: Promise.resolve({ path: ['teamSlot', 'Fill'] }) })
        expect(res.status).toBe(204)
    })
})

describe('API proxy — 路徑白名單', () => {
    beforeEach(() => {
        process.env.BACKEND_API_URL = 'http://backend:5230'
        vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('ok', { status: 200 })))
    })
    afterEach(() => vi.unstubAllGlobals())

    // leader-led 玩家/隊長自助讀走 /api/Me/*：若 'me' 未加白名單，proxy 會直接 403（Invitations/Teams/LedTeams 全壞）。
    it('/api/Me/* 在白名單內 → 轉發給後端（非 403）', async () => {
        const req = new NextRequest('http://localhost/api/Me/LedTeams')
        const res = await GET(req, { params: Promise.resolve({ path: ['Me', 'LedTeams'] }) })
        expect(res.status).toBe(200)
    })

    it('不在白名單的路徑 → 403', async () => {
        const req = new NextRequest('http://localhost/api/secretadmin')
        const res = await GET(req, { params: Promise.resolve({ path: ['secretadmin'] }) })
        expect(res.status).toBe(403)
    })
})
