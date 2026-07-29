import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import RaidSchedulerPage from '../app/admin/schedule/page';
import { LoadingProvider } from '../app/providers/LoadingContext';
import { TeamSlot, TeamSlotSaveResult } from '../types/raid';

const { boss } = vi.hoisted(() => ({
  boss: { id: 1, name: '王A', requireMembers: 6, roundConsumption: 1 },
}));

vi.mock('../hooks/queries/useBosses', () => ({
  useBosses: () => ({ data: [boss] }),
}));

vi.mock('../services/bossService', () => ({
  bossService: {
    getAllBosses: vi.fn().mockResolvedValue([boss]),
    getTemplates: vi.fn().mockResolvedValue([]),
  },
  jobCategoryService: {
    getJobMap: vi.fn().mockResolvedValue({}),
  },
}));

const mockGetTeamSlots = vi.fn();
const mockSaveSchedule = vi.fn();
vi.mock('../services/scheduleService', () => ({
  scheduleService: {
    getTeamSlots: (...args: unknown[]) => mockGetTeamSlots(...args),
    saveSchedule: (...args: unknown[]) => mockSaveSchedule(...args),
  },
}));

vi.mock('react-hot-toast', () => ({
  default: { error: vi.fn(), success: vi.fn() },
}));

// happy-dom 沒實作 scrollIntoView
Element.prototype.scrollIntoView = vi.fn();

const seedTeamSlots: TeamSlot[] = [
  { id: 10, bossId: 1, slotDateTime: new Date('2026-04-02T12:00:00Z'), characters: [], source: 'auto' },
  { id: 20, bossId: 1, slotDateTime: new Date('2026-04-03T12:00:00Z'), characters: [], source: 'auto' },
];

async function renderPageWithData() {
  mockGetTeamSlots.mockResolvedValue(seedTeamSlots);
  render(
    <LoadingProvider>
      <RaidSchedulerPage />
    </LoadingProvider>
  );
  await screen.findByText('共 2 個隊伍');
}

describe('RaidSchedulerPage 存檔衝突提示', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetTeamSlots.mockResolvedValue(seedTeamSlots);
  });

  it('存檔有衝突時，顯示衝突橫幅與筆數，衝突隊伍卡片標紅', async () => {
    const toast = (await import('react-hot-toast')).default;
    const savedResult: TeamSlotSaveResult = { conflictedTeamSlotIds: [10], teamSlots: seedTeamSlots };
    mockSaveSchedule.mockResolvedValue(savedResult);

    await renderPageWithData();

    // 存檔按鈕預設 disabled（沒有變更）；新增一隊觸發 hasChanges，才能點儲存
    fireEvent.click(screen.getByText('新增隊伍'));
    fireEvent.click(screen.getByText('儲存排團'));

    await waitFor(() => {
      expect(screen.getByText('1 隊有衝突，點此查看')).toBeDefined();
    });
    expect(toast.error).toHaveBeenCalledWith(expect.stringContaining('1 隊因被異動或消失而略過'));
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('點擊衝突橫幅會捲動到第一個衝突隊伍卡片', async () => {
    const savedResult: TeamSlotSaveResult = { conflictedTeamSlotIds: [10], teamSlots: seedTeamSlots };
    mockSaveSchedule.mockResolvedValue(savedResult);

    await renderPageWithData();
    fireEvent.click(screen.getByText('新增隊伍'));
    fireEvent.click(screen.getByText('儲存排團'));

    const banner = await screen.findByText('1 隊有衝突，點此查看');
    fireEvent.click(banner);

    expect(Element.prototype.scrollIntoView).toHaveBeenCalledWith({ behavior: 'smooth', block: 'center' });
  });

  it('存檔沒有衝突時，不顯示衝突橫幅，顯示成功訊息', async () => {
    const toast = (await import('react-hot-toast')).default;
    const savedResult: TeamSlotSaveResult = { conflictedTeamSlotIds: [], teamSlots: seedTeamSlots };
    mockSaveSchedule.mockResolvedValue(savedResult);

    await renderPageWithData();
    fireEvent.click(screen.getByText('新增隊伍'));
    fireEvent.click(screen.getByText('儲存排團'));

    await waitFor(() => {
      expect(toast.success).toHaveBeenCalledWith('排團已儲存！');
    });
    expect(screen.queryByText(/隊有衝突，點此查看/)).toBeNull();
    expect(toast.error).not.toHaveBeenCalled();
  });
});
