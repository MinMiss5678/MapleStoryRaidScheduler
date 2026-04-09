import { RegisterFormState, CharacterRegister } from "@/types/register";
import { Boss } from "@/types/raid";
import { Character } from "@/types/character";
import { MAX_ROUNDS } from "@/constants/register";

interface UseBossAssignmentProps {
  form: RegisterFormState;
  updateForm: <K extends keyof RegisterFormState>(key: K, value: RegisterFormState[K]) => void;
  setForm: React.Dispatch<React.SetStateAction<RegisterFormState>>;
  bosses: Boss[];
  characters: Character[];
}

export function useBossAssignment({ form, updateForm, setForm, bosses, characters }: UseBossAssignmentProps) {
  const getTotalRounds = (characterId: string | undefined) => {
    return form.characterRegisters
      .filter((s) => s.characterId === characterId)
      .reduce((sum, s) => {
        const boss = bosses.find(b => b.id === s.bossId);
        const consumption = boss?.roundConsumption || 1;
        return sum + (s.rounds || 0) * consumption;
      }, 0);
  };

  const getAvailableRounds = (characterId: string | undefined, currentRounds = 0, currentBossId?: number) => {
    if (!characterId) return 0;

    const boss = bosses.find(b => b.id === currentBossId);
    const consumption = boss?.roundConsumption || 1;
    const currentTotalContribution = currentRounds * consumption;

    const used = getTotalRounds(characterId) - currentTotalContribution;
    const remaining = MAX_ROUNDS - used;

    if (remaining <= 0) return 0;

    return Math.floor(remaining / consumption);
  };

  const addCharacterRegister = () => {
    setForm(prev => ({
      ...prev,
      characterRegisters: [
        ...prev.characterRegisters,
        { characterId: "", bossId: 0, rounds: 1 }
      ]
    }));
  };

  const copyCharacterRegister = (characterId: string) => {
    const source = form.characterRegisters.find(c => c.characterId === characterId);
    if (!source) return;

    const available = getAvailableRounds(characterId);
    if (available <= 0) return;

    setForm(prev => ({
      ...prev,
      characterRegisters: [
        ...prev.characterRegisters,
        { ...source, id: undefined, rounds: Math.min(source.rounds, available) }
      ]
    }));
  };

  const removeCharacterRegister = (id: number | undefined, index: number) => {
    if (id) {
      setForm(prev => ({
        ...prev,
        DeleteCharacterRegisterIds: [...(prev.DeleteCharacterRegisterIds || []), id],
        characterRegisters: prev.characterRegisters.filter((_, i) => i !== index)
      }));
    } else {
      setForm(prev => ({
        ...prev,
        characterRegisters: prev.characterRegisters.filter((_, i) => i !== index)
      }));
    }
  };

  const handleCharacterChange = (index: number, characterId: string) => {
    setForm(prev => ({
      ...prev,
      characterRegisters: prev.characterRegisters.map((c, i) =>
        i === index ? { ...c, characterId } : c
      )
    }));
  };

  const handleBossChange = (index: number, bossId: number) => {
    setForm(prev => ({
      ...prev,
      characterRegisters: prev.characterRegisters.map((c, i) => {
        if (i === index) {
          const available = getAvailableRounds(c.characterId, c.rounds, bossId);
          return { ...c, bossId, rounds: Math.min(c.rounds, available || 1) };
        }
        return c;
      })
    }));
  };

  const handleRoundsChange = (index: number, rounds: number) => {
    setForm(prev => ({
      ...prev,
      characterRegisters: prev.characterRegisters.map((c, i) =>
        i === index ? { ...c, rounds } : c
      )
    }));
  };

  const upsertCharacterRegister = (characterId: string, bossId: number, rounds: number) => {
    const char = characters.find(c => c.id === characterId);
    if (!char) return;

    setForm(prev => {
      const index = prev.characterRegisters.findIndex(r => r.characterId === characterId && r.bossId === bossId);
      if (index !== -1) {
        return {
          ...prev,
          characterRegisters: prev.characterRegisters.map((c, i) =>
            i === index ? { ...c, rounds } : c
          )
        };
      } else {
        return {
          ...prev,
          characterRegisters: [
            ...prev.characterRegisters,
            { characterId, bossId, rounds }
          ]
        };
      }
    });
  };

  const quickFillBoss = (bossId: number, rounds: number) => {
    setForm(prev => ({
      ...prev,
      characterRegisters: prev.characterRegisters.map(char => {
        const available = getAvailableRounds(char.characterId, char.rounds, bossId);
        if (available >= rounds) {
          return { ...char, bossId, rounds };
        }
        return char;
      })
    }));
  };

  return {
    getTotalRounds,
    getAvailableRounds,
    addCharacterRegister,
    copyCharacterRegister,
    removeCharacterRegister,
    handleCharacterChange,
    handleBossChange,
    handleRoundsChange,
    upsertCharacterRegister,
    quickFillBoss
  };
}
