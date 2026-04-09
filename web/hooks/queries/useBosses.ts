import { useQuery } from "@tanstack/react-query";
import { bossService } from "@/services/bossService";

export function useBosses() {
  return useQuery({
    queryKey: ["bosses"],
    queryFn: () => bossService.getAllBosses(),
  });
}
