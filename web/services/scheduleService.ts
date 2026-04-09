import { TeamSlot } from "@/types/raid";

export const scheduleService = {
    async getTeamSlots(bossId: number): Promise<TeamSlot[]> {
        const res = await fetch(`/api/teamSlot?bossId=${bossId}`);
        if (!res.ok) throw new Error("無法取得隊伍列表");
        return res.json();
    },

    async autoSchedule(bossId: number, templateId: number): Promise<TeamSlot[]> {
        const res = await fetch("/api/schedule/AutoScheduleWithTemplate", {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ bossId, templateId }),
        });
        if (!res.ok) throw new Error("自動排團失敗");
        return res.json();
    },

    async saveSchedule(bossId: number, teamSlots: TeamSlot[], deleteTeamSlotIds: number[]): Promise<boolean> {
        const res = await fetch("/api/teamSlot", {
            method: "PUT",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify({ bossId, teamSlots, deleteTeamSlotIds }),
        });
        return res.ok;
    },

    async getByDiscordId(): Promise<TeamSlot[]> {
        const res = await fetch("/api/teamSlot/GetByDiscordId");
        if (!res.ok) throw new Error("無法取得個人場次");
        return res.json();
    }
};
