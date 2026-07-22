/**
 * @vitest-environment node
 */
// proxy 的真實 client IP 覆寫（安全）：證明 client 偽造的 x-forwarded-for 蓋不掉真 IP。
// 用 node 環境（NextRequest / Request / Response / fetch 走 node 內建）。
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { NextRequest } from 'next/server'
import { GET } from '@/app/api/[...path]/route'

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

    // 打一個合法路徑（period 在白名單），回傳轉發給後端的 headers
    async function forwardedHeaders(headers: Record<string, string>): Promise<Headers> {
        const req = new NextRequest('http://localhost/api/period/GetByNow', { headers })
        await GET(req, { params: Promise.resolve({ path: ['period', 'GetByNow'] }) })
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
