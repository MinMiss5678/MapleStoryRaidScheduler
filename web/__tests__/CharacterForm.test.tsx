import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import CharacterForm from '../app/character/components/CharacterForm';
import React from 'react';

// Mock fetch
global.fetch = vi.fn();

// 回傳一個「完整」的 Response：apiClient.handleResponse 會讀 res.headers.get('content-type')，
// 半殘的 { ok, json } 會讓它丟 TypeError（→ 進 catch → alert）。用真 Response 才忠實模擬後端回應。
const jsonResponse = (data: unknown, status = 200) =>
  new Response(JSON.stringify(data), {
    status,
    headers: { 'content-type': 'application/json' },
  });

describe('CharacterForm', () => {
  const mockOnSuccess = vi.fn();
  const mockOnReset = vi.fn();
  const mockSetLoading = vi.fn();
  const mockJobs = ['Hero', 'Bishop', 'Night Lord'];

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders correctly for creating a new character', () => {
    render(
      <CharacterForm
        editingCharacter={null}
        onSuccess={mockOnSuccess}
        onReset={mockOnReset}
        setLoading={mockSetLoading}
        jobs={mockJobs}
      />
    );

    expect(screen.getByText('建立角色')).toBeDefined();
    expect(screen.getByPlaceholderText('例如：艾莉絲')).toBeDefined();
    expect(screen.getByPlaceholderText('例如：ELISE')).toBeDefined();
  });

  it('fills form when editing a character', () => {
    const editingCharacter = {
      id: 'CHAR1',
      name: 'Alice',
      job: 'Bishop',
      attackPower: 100,
      level: 180
    };

    render(
      <CharacterForm
        editingCharacter={editingCharacter}
        onSuccess={mockOnSuccess}
        onReset={mockOnReset}
        setLoading={mockSetLoading}
        jobs={mockJobs}
      />
    );

    expect(screen.getByText('修改角色')).toBeDefined();
    expect(screen.getByDisplayValue('Alice')).toBeDefined();
    expect(screen.getByDisplayValue('CHAR1')).toBeDefined();
    expect(screen.getByDisplayValue('Bishop')).toBeDefined();
    expect(screen.getByDisplayValue('180')).toBeDefined(); // 人物等級帶入
  });

  it('shows error when level is out of 1–300 range', async () => {
    render(
      <CharacterForm
        editingCharacter={null}
        onSuccess={mockOnSuccess}
        onReset={mockOnReset}
        setLoading={mockSetLoading}
        jobs={mockJobs}
      />
    );

    fireEvent.change(screen.getByPlaceholderText('例如：艾莉絲'), { target: { value: 'X' } });
    fireEvent.change(screen.getByPlaceholderText('例如：ELISE'), { target: { value: 'X1' } });
    fireEvent.change(screen.getByDisplayValue('200'), { target: { value: '400' } }); // 等級預設 200 → 改成超界
    fireEvent.click(screen.getByText('提交'));

    expect(await screen.findByText('等級需在 1–200 之間')).toBeDefined();
    expect(global.fetch).not.toHaveBeenCalled();
  });

  it('shows error messages when required fields are empty', async () => {
    render(
      <CharacterForm
        editingCharacter={null}
        onSuccess={mockOnSuccess}
        onReset={mockOnReset}
        setLoading={mockSetLoading}
        jobs={mockJobs}
      />
    );

    const submitButton = screen.getByText('提交');
    fireEvent.click(submitButton);

    expect(await screen.findByText('請輸入名稱')).toBeDefined();
    expect(await screen.findByText('請輸入代碼')).toBeDefined();
    expect(mockSetLoading).toHaveBeenCalledWith(true);
    expect(mockSetLoading).toHaveBeenCalledWith(false);
  });

  it('calls API and onSuccess when creating a character', async () => {
    vi.mocked(global.fetch).mockResolvedValueOnce(
      jsonResponse({ id: 'NEW', name: 'Newbie', job: 'Hero', attackPower: 50, level: 200 })
    );

    render(
      <CharacterForm
        editingCharacter={null}
        onSuccess={mockOnSuccess}
        onReset={mockOnReset}
        setLoading={mockSetLoading}
        jobs={mockJobs}
      />
    );

    fireEvent.change(screen.getByPlaceholderText('例如：艾莉絲'), { target: { value: 'Newbie' } });
    fireEvent.change(screen.getByPlaceholderText('例如：ELISE'), { target: { value: 'NEW' } });
    
    fireEvent.click(screen.getByText('提交'));

    await waitFor(() => {
      expect(global.fetch).toHaveBeenCalledWith('/api/character', expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ name: 'Newbie', id: 'NEW', job: 'Hero', attackPower: 50, level: 200 })
      }));
      expect(mockOnSuccess).toHaveBeenCalledWith(expect.objectContaining({ name: 'Newbie' }), false);
    });
  });

  it('calls API and onSuccess when updating a character', async () => {
    const editingCharacter = {
      id: 'CHAR1',
      name: 'Alice',
      job: 'Bishop',
      attackPower: 100,
      level: 180
    };

    vi.mocked(global.fetch).mockResolvedValueOnce(
      jsonResponse({ ...editingCharacter, name: 'Alice Updated' })
    );

    render(
      <CharacterForm
        editingCharacter={editingCharacter}
        onSuccess={mockOnSuccess}
        onReset={mockOnReset}
        setLoading={mockSetLoading}
        jobs={mockJobs}
      />
    );

    fireEvent.change(screen.getByDisplayValue('Alice'), { target: { value: 'Alice Updated' } });
    
    fireEvent.click(screen.getByText('提交'));

    await waitFor(() => {
      expect(global.fetch).toHaveBeenCalledWith('/api/character/CHAR1', expect.objectContaining({
        method: 'PUT',
        body: JSON.stringify({ name: 'Alice Updated', id: 'CHAR1', job: 'Bishop', attackPower: 100, level: 180 })
      }));
      expect(mockOnSuccess).toHaveBeenCalledWith(expect.objectContaining({ name: 'Alice Updated' }), true);
    });
  });

  it('resets form when reset button is clicked', () => {
    render(
      <CharacterForm
        editingCharacter={null}
        onSuccess={mockOnSuccess}
        onReset={mockOnReset}
        setLoading={mockSetLoading}
        jobs={mockJobs}
      />
    );

    fireEvent.change(screen.getByPlaceholderText('例如：艾莉絲'), { target: { value: 'Temp' } });
    fireEvent.click(screen.getByText('重置'));

    expect(screen.getByPlaceholderText('例如：艾莉絲').getAttribute('value')).toBe('');
    expect(mockOnReset).toHaveBeenCalled();
  });
});
