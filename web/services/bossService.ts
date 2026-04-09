import { Boss, BossTemplate } from "@/types/raid";

export const bossService = {
    async getAllBosses(): Promise<Boss[]> {
        const res = await fetch("/api/boss/GetAll");
        if (!res.ok) throw new Error("無法取得 Boss 列表");
        return res.json();
    },

    async getTemplates(bossId: number): Promise<BossTemplate[]> {
        const res = await fetch(`/api/boss/${bossId}/Templates`);
        if (!res.ok) throw new Error("無法取得範本列表");
        return res.json();
    },

    async saveTemplate(template: BossTemplate): Promise<boolean> {
        const method = template.id === 0 ? "POST" : "PUT";
        const url = template.id === 0 
            ? "/api/boss/Templates" 
            : `/api/boss/Templates/${template.id}`;

        const res = await fetch(url, {
            method,
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(template)
        });
        return res.ok;
    },

    async deleteTemplate(templateId: number): Promise<boolean> {
        const res = await fetch(`/api/boss/Templates/${templateId}`, {
            method: "DELETE"
        });
        return res.ok;
    },

    async saveBoss(boss: Boss): Promise<boolean> {
        const method = boss.id === 0 ? "POST" : "PUT";
        const url = boss.id === 0 ? "/api/boss" : `/api/boss/${boss.id}`;
        const res = await fetch(url, {
            method,
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(boss)
        });
        return res.ok;
    },

    async deleteBoss(bossId: number): Promise<boolean> {
        const res = await fetch(`/api/boss/${bossId}`, {
            method: "DELETE"
        });
        return res.ok;
    }
};

export const jobCategoryService = {
    async getJobMap(): Promise<Record<string, string>> {
        const res = await fetch("/api/JobCategory/GetJobMap");
        if (!res.ok) throw new Error("無法取得職業對照表");
        return res.json();
    }
};
