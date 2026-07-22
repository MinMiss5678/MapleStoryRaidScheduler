export class ApiError extends Error {
    constructor(public status: number, message: string) {
        super(message);
        this.name = 'ApiError';
    }
}

async function handleResponse<T>(res: Response): Promise<T> {
    if (res.status === 401) {
        if (typeof window !== 'undefined') {
            window.location.href = '/login';
        }
        throw new ApiError(401, '未登入或登入已過期');
    }
    if (res.status === 403) {
        throw new ApiError(403, '權限不足');
    }
    if (!res.ok) {
        // 嘗試解析後端統一的 ProblemDetails 格式 { detail: string }
        const body = await res.json().catch(() => null);
        const message = body?.detail || body?.title || `請求失敗 (${res.status})`;
        throw new ApiError(res.status, message);
    }
    const contentType = res.headers.get('content-type');
    if (contentType?.includes('application/json')) {
        return res.json();
    }
    return undefined as T;
}

/** 寫入操作選項。idempotencyKey：同一邏輯操作重送沿用同一把，後端才 de-dup 得掉；省略則現產一把（等同「不特別去重」）。 */
export interface MutationOptions {
    idempotencyKey?: string;
}

function idempotencyHeader(key?: string): Record<string, string> {
    return { 'X-Idempotency-Key': key ?? crypto.randomUUID() };
}

export const apiClient = {
    async get<T = unknown>(url: string): Promise<T> {
        const res = await fetch(url);
        return handleResponse<T>(res);
    },

    /** 404 / 204 視為「查無資料」，回傳 null；其他錯誤照常拋出 */
    async getNullable<T = unknown>(url: string): Promise<T | null> {
        const res = await fetch(url);
        if (res.status === 404 || res.status === 204) return null;
        return handleResponse<T>(res);
    },

    async post<T = unknown>(url: string, body?: unknown, opts?: MutationOptions): Promise<T> {
        const res = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', ...idempotencyHeader(opts?.idempotencyKey) },
            body: body !== undefined ? JSON.stringify(body) : undefined,
        });
        return handleResponse<T>(res);
    },

    async put<T = unknown>(url: string, body?: unknown, opts?: MutationOptions): Promise<T> {
        const res = await fetch(url, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json', ...idempotencyHeader(opts?.idempotencyKey) },
            body: body !== undefined ? JSON.stringify(body) : undefined,
        });
        return handleResponse<T>(res);
    },

    async delete<T = unknown>(url: string, opts?: MutationOptions): Promise<T> {
        const res = await fetch(url, { method: 'DELETE', headers: idempotencyHeader(opts?.idempotencyKey) });
        return handleResponse<T>(res);
    },
};
