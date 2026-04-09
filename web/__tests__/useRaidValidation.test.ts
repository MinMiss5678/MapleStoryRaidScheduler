import { describe, it, expect, vi, beforeEach } from 'vitest';
import { useRaidValidation } from '../hooks/useRaidValidation';
import { TeamSlot, TeamSlotCharacter } from '../types/raid';
import toast from 'react-hot-toast';

// 模擬 react-hot-toast
vi.mock('react-hot-toast', () => ({
    default: {
        error: vi.fn(),
        success: vi.fn(),
    },
}));

describe('useRaidValidation', () => {
    const { validateAddCharacter } = useRaidValidation();

    beforeEach(() => {
        vi.clearAllMocks();
    });

    const mockCharacter: TeamSlotCharacter = {
        characterId: 'char1',
        characterName: 'Hero1',
        discordId: 12345,
        discordName: 'User1',
        job: 'Hero',
        attackPower: 50000,
        rounds: 1
    };

    const mockTeamSlot: TeamSlot = {
        id: 1,
        bossId: 1,
        slotDateTime: new Date('2024-01-01T20:00:00Z'),
        characters: [],
        deleteTeamSlotCharacterIds: []
    };

    it('should return true when all validations pass', () => {
        const result = validateAddCharacter(mockTeamSlot, mockCharacter, [mockTeamSlot]);
        expect(result).toBe(true);
        expect(toast.error).not.toHaveBeenCalled();
    });

    it('should fail when team is full (6 members)', () => {
        const fullTeamSlot: TeamSlot = {
            ...mockTeamSlot,
            characters: Array(6).fill({ ...mockCharacter, characterId: 'other' })
        };

        const result = validateAddCharacter(fullTeamSlot, mockCharacter, [fullTeamSlot]);
        expect(result).toBe(false);
        expect(toast.error).toHaveBeenCalledWith('隊伍人數已達上限 (6人)。');
    });

    it('should fail when character is already in the team', () => {
        const teamWithChar: TeamSlot = {
            ...mockTeamSlot,
            characters: [mockCharacter]
        };

        const result = validateAddCharacter(teamWithChar, mockCharacter, [teamWithChar]);
        expect(result).toBe(false);
        expect(toast.error).toHaveBeenCalledWith(`角色「${mockCharacter.characterName}」已在該隊伍中。`);
    });

    it('should fail when Discord ID is already in another team at the same time', () => {
        const sameTimeSlot: TeamSlot = {
            id: 2,
            bossId: 2,
            slotDateTime: new Date('2024-01-01T20:00:00Z'),
            characters: [{ ...mockCharacter, characterName: 'AnotherChar', characterId: 'char2' }],
            deleteTeamSlotCharacterIds: []
        };

        const result = validateAddCharacter(mockTeamSlot, mockCharacter, [mockTeamSlot, sameTimeSlot]);
        expect(result).toBe(false);
        expect(toast.error).toHaveBeenCalledWith(`您在該時段已使用角色「AnotherChar」參加了其他隊伍。`);
    });

    it('should fail when character exceeds its total rounds limit', () => {
        const otherSlot: TeamSlot = {
            id: 2,
            bossId: 2,
            slotDateTime: new Date('2024-01-02T20:00:00Z'),
            characters: [mockCharacter],
            deleteTeamSlotCharacterIds: []
        };

        const result = validateAddCharacter(mockTeamSlot, mockCharacter, [mockTeamSlot, otherSlot]);
        expect(result).toBe(false);
        expect(toast.error).toHaveBeenCalledWith(`角色「${mockCharacter.characterName}」的場數已達上限 (1 場)。`);
    });

    it('should allow adding character if total rounds is within limit', () => {
        const charWithMoreRounds = { ...mockCharacter, rounds: 2 };
        const otherSlot: TeamSlot = {
            id: 2,
            bossId: 2,
            slotDateTime: new Date('2024-01-02T20:00:00Z'),
            characters: [charWithMoreRounds],
            deleteTeamSlotCharacterIds: []
        };

        const result = validateAddCharacter(mockTeamSlot, charWithMoreRounds, [mockTeamSlot, otherSlot]);
        expect(result).toBe(true);
        expect(toast.error).not.toHaveBeenCalled();
    });
});
