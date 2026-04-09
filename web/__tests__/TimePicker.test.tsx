import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { TimePicker } from '../app/register/components/TimePicker';
import { RegisterFormState } from '../types/register';
import React from 'react';

const mockForm: RegisterFormState = {
    availabilities: [
        { weekday: 0, timeslot: "20:00" }
    ],
    characterRegisters: []
};

describe('TimePicker', () => {
    it('renders step title', () => {
        render(
            <TimePicker 
                form={mockForm} 
                quickFill={vi.fn()} 
                copyDay={vi.fn()} 
                toggleAvailability={vi.fn()} 
                handleWeekdayAllCheck={vi.fn()} 
                handleTimeslotAllCheck={vi.fn()} 
                onNext={vi.fn()} 
                hasId={false} 
            />
        );
        expect(screen.getByText('Step 1：選擇可出團時間')).toBeDefined();
    });

    it('highlights selected timeslots', () => {
        render(
            <TimePicker 
                form={mockForm} 
                quickFill={vi.fn()} 
                copyDay={vi.fn()} 
                toggleAvailability={vi.fn()} 
                handleWeekdayAllCheck={vi.fn()} 
                handleTimeslotAllCheck={vi.fn()} 
                onNext={vi.fn()} 
                hasId={false} 
            />
        );
        // 找出對應的 cell，它應該有 bg-blue-500 class
        const cells = document.querySelectorAll('td.bg-blue-500');
        expect(cells.length).toBeGreaterThan(0);
    });

    it('calls onNext when next button is clicked', () => {
        const onNext = vi.fn();
        render(
            <TimePicker 
                form={mockForm} 
                quickFill={vi.fn()} 
                copyDay={vi.fn()} 
                toggleAvailability={vi.fn()} 
                handleWeekdayAllCheck={vi.fn()} 
                handleTimeslotAllCheck={vi.fn()} 
                onNext={onNext} 
                hasId={false} 
            />
        );
        const nextBtn = screen.getByText('下一步');
        fireEvent.click(nextBtn);
        expect(onNext).toHaveBeenCalled();
    });

    it('disables next button when no availabilities', () => {
        const emptyForm: RegisterFormState = { availabilities: [], characterRegisters: [] };
        render(
            <TimePicker 
                form={emptyForm} 
                quickFill={vi.fn()} 
                copyDay={vi.fn()} 
                toggleAvailability={vi.fn()} 
                handleWeekdayAllCheck={vi.fn()} 
                handleTimeslotAllCheck={vi.fn()} 
                onNext={vi.fn()} 
                hasId={false} 
            />
        );
        const nextBtn = screen.getByText('下一步') as HTMLButtonElement;
        expect(nextBtn.disabled).toBe(true);
    });
});
