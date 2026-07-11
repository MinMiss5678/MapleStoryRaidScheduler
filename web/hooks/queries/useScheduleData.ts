import { useQuery } from "@tanstack/react-query";
import { characterService } from "@/services/characterService";
import { bossService, jobCategoryService } from "@/services/bossService";
import { scheduleService } from "@/services/scheduleService";

export function useScheduleBossData(bossId: number | undefined) {
    return useQuery({
        queryKey: ["schedule", "bossData", bossId],
        queryFn: async () => {
            const [characters, templates] = await Promise.all([
                characterService.getCharacters(bossId!),
                bossService.getTemplates(bossId!),
            ]);
            return { characters, templates };
        },
        enabled: !!bossId,
    });
}

export function useTeamSlots(bossId: number | undefined) {
    return useQuery({
        queryKey: ["schedule", "teamSlots", bossId],
        queryFn: () => scheduleService.getTeamSlots(bossId!),
        enabled: !!bossId,
    });
}

export function useJobMap() {
    return useQuery({
        queryKey: ["jobMap"],
        queryFn: () => jobCategoryService.getJobMap(),
    });
}
