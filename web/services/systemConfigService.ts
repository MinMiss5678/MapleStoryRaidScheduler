import { SystemConfig } from "@/types/system";

export const systemConfigService = {
    async getConfig(): Promise<SystemConfig> {
        const res = await fetch("/api/SystemConfig");
        if (!res.ok) throw new Error("取得系統設定失敗");
        return res.json();
    },

    async saveConfig(config: SystemConfig): Promise<boolean> {
        const res = await fetch("/api/SystemConfig", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(config),
        });
        return res.ok;
    }
};
