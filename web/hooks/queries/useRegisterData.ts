import { useQuery } from "@tanstack/react-query";
import { registerService } from "@/services/registerService";

export function useRegisterData() {
  return useQuery({
    queryKey: ["register"],
    queryFn: () => registerService.getRegister(),
  });
}

export function useLastRegisterData() {
  return useQuery({
    queryKey: ["register", "last"],
    queryFn: () => registerService.getLastRegister(),
    enabled: false, // 只有在需要時才手動觸發獲取，或根據需求調整
  });
}
