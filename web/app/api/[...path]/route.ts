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

    // 取真實 client IP：只信 Cloudflare 設的 cf-connecting-ip（流量必經 Cloudflare tunnel，不可繞過偽造）。
    // 刻意不 fallback 到 client 的 x-forwarded-for——那可偽造。沒有 cf-connecting-ip（如本機/e2e 無 Cloudflare）
    // 就不帶真 IP，後端退回看到 proxy IP（可接受；那些環境不做 IP 安全決策）。
    const realIp = req.headers.get('cf-connecting-ip') ?? '';

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

    // 204/205/304 這類「null body status」依 Fetch 規格不可帶 body（連空的 ArrayBuffer 都不行），
    // 否則建構 NextResponse 會直接丟 TypeError。204 No Content 的端點（如補位）才會踩到。
    const nullBodyStatuses = [204, 205, 304];
    const responseBody = nullBodyStatuses.includes(response.status) ? null : result;

    return new NextResponse(responseBody, {
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
