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
        const text = await res.text().catch(() => '');
        throw new ApiError(res.status, text || `請求失敗 (${res.status})`);
    }
    const contentType = res.headers.get('content-type');
    if (contentType?.includes('application/json')) {
        return res.json();
    }
    return undefined as T;
}

export const apiClient = {
    async get<T = unknown>(url: string): Promise<T> {
        const res = await fetch(url);
        return handleResponse<T>(res);
    },

    async post<T = unknown>(url: string, body?: unknown): Promise<T> {
        const res = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: body !== undefined ? JSON.stringify(body) : undefined,
        });
        return handleResponse<T>(res);
    },

    async put<T = unknown>(url: string, body?: unknown): Promise<T> {
        const res = await fetch(url, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: body !== undefined ? JSON.stringify(body) : undefined,
        });
        return handleResponse<T>(res);
    },

    async delete<T = unknown>(url: string): Promise<T> {
        const res = await fetch(url, { method: 'DELETE' });
        return handleResponse<T>(res);
    },
};
