import type { NextRequest } from 'next/server';
import { NextResponse } from 'next/server';
import { ALLOWED_PATHS } from '@/constants/apiWhitelist';

async function handleProxy(req: NextRequest, { params }: { params: Promise<{ path: string[] }> }) {
    const {path} = await params;

    // 路徑白名單驗證
    if (!ALLOWED_PATHS.has(path[0]?.toLowerCase())) {
        return new NextResponse('Forbidden', { status: 403 });
    }

    const targetPath = path.join('/');
    const targetUrl = `${process.env.BACKEND_API_URL}/api/${targetPath}${req.nextUrl.search}`;

    // 取真實 client IP：來源是 Cloudflare 設的 cf-connecting-ip（可信；流量必經 Cloudflare）。
    // 退而求其次取 x-forwarded-for 的第一段（cloudflared 也會帶）。
    const realIp = req.headers.get('cf-connecting-ip')
        ?? req.headers.get('x-forwarded-for')?.split(',')[0]?.trim()
        ?? '';

    // 複製 headers（避免 Host / Connection 導致問題）
    const headers = new Headers(req.headers);
    headers.delete('host');
    headers.delete('connection');
    headers.delete('content-length');
    // 先刪掉 client 可偽造的 header，再由本 proxy 設「乾淨、單一、可信」的真 IP 給後端。
    // 後端只在叢集內部可達（非公開），且只信本 proxy 送來的 x-forwarded-for → 不會被偽造。
    headers.delete('cf-connecting-ip');
    if (realIp) headers.set('x-forwarded-for', realIp);
    else headers.delete('x-forwarded-for');

    // 複製 body（GET/HEAD/DELETE 不應該有 body）
    const hasBody = !['GET', 'HEAD', 'DELETE'].includes(req.method);
    const body = hasBody ? await req.text() : undefined;

    // 執行後端請求
    const response = await fetch(targetUrl, {
        method: req.method,
        headers,
        body,
        cache: 'no-store'
    });

    // 簡化回應處理：直接轉發原始 ArrayBuffer
    const result = await response.arrayBuffer();

    return new NextResponse(result, {
        status: response.status,
        headers: response.headers,
    });
}

// 將所有方法都導向同一個 handler
export const GET = handleProxy;
export const POST = handleProxy;
export const PUT = handleProxy;
export const DELETE = handleProxy;
export const PATCH = handleProxy;
export const OPTIONS = handleProxy;
