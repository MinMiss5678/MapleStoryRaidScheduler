import { useQuery } from "@tanstack/react-query";
import { periodService } from "@/services/periodService";

export function usePeriod() {
  return useQuery({
    queryKey: ["period", "current"],
    queryFn: () => periodService.getByNow(),
  });
}
