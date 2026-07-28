import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import AdminRaidTeamCard from '../app/admin/schedule/components/AdminRaidTeamCard';
import { TeamSlot, TeamSlotCharacter } from '../types/raid';

const member = (overrides: Partial<TeamSlotCharacter> = {}): TeamSlotCharacter => ({
  id: 1,
  characterId: 'c1',
  discordId: 'd1',
  discordName: 'P1',
  characterName: 'C1',
  job: 'Hero',
  attackPower: 1000,
  rounds: 1,
  ...overrides,
});

const teamSlot = (characters: TeamSlotCharacter[]): TeamSlot => ({
  id: 1,
  bossId: 1,
  slotDateTime: new Date('2026-04-02T12:00:00Z'),
  characters,
  deleteTeamSlotCharacterIds: [],
  source: 'auto',
});

const noopProps = {
  bossId: 1,
  onTeamSlotUpdate: vi.fn(),
  onTeamSlotDelete: vi.fn(),
  onAddCharacter: vi.fn(),
};

describe('AdminRaidTeamCard', () => {
  it('顯示衝突提示與紅框，當 isConflicted 為 true', () => {
    const { container } = render(
      <AdminRaidTeamCard {...noopProps} teamSlot={teamSlot([member()])} requireMembers={6} isConflicted />
    );

    expect(screen.getByText(/此隊已被異動或消失/)).toBeDefined();
    expect(container.firstElementChild?.className).toContain('border-red-500/70');
  });

  it('不顯示衝突提示，當 isConflicted 為 false', () => {
    render(
      <AdminRaidTeamCard {...noopProps} teamSlot={teamSlot([member()])} requireMembers={6} isConflicted={false} />
    );

    expect(screen.queryByText(/此隊已被異動或消失/)).toBeNull();
  });

  it('隊伍已滿且未衝突時套用綠框，不顯示衝突提示', () => {
    const fullMembers = Array.from({ length: 6 }, (_, i) => member({ id: i + 1, characterId: `c${i + 1}` }));
    const { container } = render(
      <AdminRaidTeamCard {...noopProps} teamSlot={teamSlot(fullMembers)} requireMembers={6} isConflicted={false} />
    );

    expect(container.firstElementChild?.className).toContain('border-green-500/50');
    expect(screen.queryByText(/此隊已被異動或消失/)).toBeNull();
  });

  it('同時衝突且已滿時，衝突樣式優先於已滿樣式', () => {
    const fullMembers = Array.from({ length: 6 }, (_, i) => member({ id: i + 1, characterId: `c${i + 1}` }));
    const { container } = render(
      <AdminRaidTeamCard {...noopProps} teamSlot={teamSlot(fullMembers)} requireMembers={6} isConflicted />
    );

    expect(container.firstElementChild?.className).toContain('border-red-500/70');
    expect(container.firstElementChild?.className).not.toContain('border-green-500/50');
  });
});
