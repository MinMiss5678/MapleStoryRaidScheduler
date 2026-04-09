import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { Input, Select } from '../components/ui/FormControls';
import React from 'react';

describe('FormControls', () => {
  describe('Input', () => {
    it('renders correctly', () => {
      render(<Input placeholder="test placeholder" />);
      const input = screen.getByPlaceholderText('test placeholder');
      expect(input).toBeDefined();
      expect(input.className).toContain('border');
    });

    it('handles onChange', () => {
      const handleChange = vi.fn();
      render(<Input onChange={handleChange} />);
      const input = screen.getByRole('textbox');
      fireEvent.change(input, { target: { value: 'new value' } });
      expect(handleChange).toHaveBeenCalled();
    });
  });

  describe('Select', () => {
    it('renders with children', () => {
      render(
        <Select data-testid="test-select">
          <option value="1">Option 1</option>
          <option value="2">Option 2</option>
        </Select>
      );
      const select = screen.getByTestId('test-select');
      expect(select).toBeDefined();
      expect(screen.getByText('Option 1')).toBeDefined();
      expect(screen.getByText('Option 2')).toBeDefined();
    });

    it('handles value change', () => {
      const handleChange = vi.fn();
      render(
        <Select onChange={handleChange} data-testid="test-select">
          <option value="1">Option 1</option>
          <option value="2">Option 2</option>
        </Select>
      );
      const select = screen.getByTestId('test-select');
      fireEvent.change(select, { target: { value: '2' } });
      expect(handleChange).toHaveBeenCalled();
    });
  });
});
