import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { CharacterBossList } from '../app/register/components/CharacterBossList';
import React from 'react';
import { RegisterFormState } from '@/types/register';
import { Character, Boss } from '@/types/raid';

describe('CharacterBossList', () => {
    const mockForm: RegisterFormState = {
        id: 1,
        periodId: 10,
        availabilities: [],
        characterRegisters: [
            { characterId: 'char1', bossId: 1, rounds: 1 }
        ],
        deleteCharacterRegisterIds: []
    };

    const mockCharacters: Character[] = [
        { id: 'char1', name: 'Hero', job: 'Warrior', attackPower: 50000, discordId: 123 }
    ];

    const mockBosses: Boss[] = [
        { id: 1, name: 'Zakum', requireMembers: 6, roundConsumption: 1 }
    ];

    const mockHandlers = {
        onBack: vi.fn(),
        onAdd: vi.fn(),
        onCopyCharacter: vi.fn(),
        onRemove: vi.fn(),
        onCharacterChange: vi.fn(),
        onBossChange: vi.fn(),
        onRoundsChange: vi.fn(),
        onSubmit: vi.fn(),
        getTotalRounds: vi.fn().mockReturnValue(1),
        getAvailableRounds: vi.fn().mockReturnValue(2),
        quickFillBoss: vi.fn()
    };

    it('renders character list view by default', () => {
        render(
            <CharacterBossList
                form={mockForm}
                characters={mockCharacters}
                bosses={mockBosses}
                {...mockHandlers}
            />
        );

        expect(screen.getByText(/Hero \(Warrior\)/)).toBeDefined();
        expect(screen.getByText(/Step 2：角色報名 Boss/)).toBeDefined();
    });

    it('switches to boss view mode', () => {
        render(
            <CharacterBossList
                form={mockForm}
                characters={mockCharacters}
                bosses={mockBosses}
                {...mockHandlers}
            />
        );

        const bossViewBtn = screen.getByText('快速分配 (依 Boss)');
        fireEvent.click(bossViewBtn);

        expect(screen.getByText('Zakum')).toBeDefined();
    });

    it('calls onCopyCharacter when add button is clicked', () => {
        render(
            <CharacterBossList
                form={mockForm}
                characters={mockCharacters}
                bosses={mockBosses}
                {...mockHandlers}
            />
        );

        const addBtn = screen.getByTitle('為此角色新增另一個 Boss');
        fireEvent.click(addBtn);

        expect(mockHandlers.onCopyCharacter).toHaveBeenCalledWith('char1');
    });

    it('calls quickFillBoss in boss view mode', () => {
        render(
            <CharacterBossList
                form={mockForm}
                characters={mockCharacters}
                bosses={mockBosses}
                {...mockHandlers}
            />
        );

        fireEvent.click(screen.getByText('快速分配 (依 Boss)'));
        
        // Find the auto-fill button for Zakum (e.g., '7場')
        const quickFillBtn = screen.getByText('7場');
        fireEvent.click(quickFillBtn);

        expect(mockHandlers.quickFillBoss).toHaveBeenCalledWith(1, 7);
    });
});
