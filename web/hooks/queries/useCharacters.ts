import { useQuery } from "@tanstack/react-query";
import { characterService } from "@/services/characterService";

export function useCharacters() {
  return useQuery({
    queryKey: ["characters"],
    queryFn: () => characterService.getCharacters(),
  });
}
