import { describe, it, expect } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { RolesProvider, useRole } from '../app/providers/RolesProvider';
import React from 'react';

describe('RolesProvider', () => {
  it('should provide initial role', () => {
    const wrapper = ({ children }: { children: React.ReactNode }) => (
      <RolesProvider initialRole="admin">{children}</RolesProvider>
    );

    const { result } = renderHook(() => useRole(), { wrapper });

    expect(result.current.role).toBe('admin');
  });

  it('should update role via setRole', () => {
    const wrapper = ({ children }: { children: React.ReactNode }) => (
      <RolesProvider initialRole="">{children}</RolesProvider>
    );

    const { result } = renderHook(() => useRole(), { wrapper });

    act(() => {
      result.current.setRole('user');
    });

    expect(result.current.role).toBe('user');
  });

  it('should default to empty role if not provided via initialRole', () => {
      // 這裡雖然 RolesProvider 必填 initialRole，但我們可以測試 Provider 的預設行為
      const { result } = renderHook(() => useRole());
      expect(result.current.role).toBe("");
  });
});
