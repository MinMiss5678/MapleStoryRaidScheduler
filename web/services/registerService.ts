import { RegisterFormState } from "@/types/register";
import { TeamSlotCharacter } from "@/types/raid";

export const registerService = {
    async getRegister(): Promise<RegisterFormState | null> {
        const res = await fetch("/api/register");
        if (res.status === 404 || res.status === 204) return null;
        if (!res.ok) throw new Error("無法取得報名資料");
        return res.json();
    },

    async getLastRegister(): Promise<RegisterFormState | null> {
        const res = await fetch("/api/register/GetLast");
        if (res.status === 404 || res.status === 204) return null;
        if (!res.ok) throw new Error("無法取得上週報名紀錄");
        return res.json();
    },

    async createRegister(form: RegisterFormState): Promise<void> {
        const res = await fetch("/api/register", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(form)
        });
        if (!res.ok) {
            const error = await res.json();
            throw new Error(error.message || "報名失敗");
        }
    },

    async updateRegister(form: RegisterFormState): Promise<void> {
        const res = await fetch("/api/register", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(form)
        });
        if (!res.ok) {
            const error = await res.json();
            throw new Error(error.message || "修改失敗");
        }
    },

    async deleteRegister(id: number): Promise<void> {
        const res = await fetch(`/api/register/${id}`, { method: "DELETE" });
        if (!res.ok) {
            const error = await res.json();
            throw new Error(error.message || "刪除失敗");
        }
    },

    async getByQuery(params: string): Promise<TeamSlotCharacter[] | null> {
        const res = await fetch(`/api/register/GetByQuery?${params}`);
        if (res.status === 404 || res.status === 204) return null;
        if (!res.ok) throw new Error("搜尋失敗");
        return res.json();
    }
};
