using Application.DTOs;

namespace Application.Interface;

/// <summary>即時找隊（period-less §8 Phase 3）：玩家發布/取消找隊意圖。</summary>
public interface ILfgService
{
    Task PostAsync(LfgIntentCreateCommand command);
    Task CancelAsync(ulong discordId, int id);
}
