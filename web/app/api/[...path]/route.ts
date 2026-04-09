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

    // 複製 headers（避免 Host / Connection 導致問題）
    const headers = new Headers(req.headers);
    headers.delete('host');
    headers.delete('connection');
    headers.delete('content-length');
    headers.delete('x-forwarded-for');
    headers.delete("cf-connecting-ip");

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
