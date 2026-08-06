import { apiClient } from './apiClient';
import { InvitationAction, Membership, OpenTeam } from '@/types/leaderLed';

// leader-led（隊長主導組隊）前端 API：對齊後端 TeamSlotController 的 leader-led 端點。
export const leaderService = {
    // Pull 玩家端：我的邀請 / 我的隊
    getMyInvitations: () => apiClient.get<Membership[]>('/api/Me/Invitations'),
    getMyTeams: () => apiClient.get<Membership[]>('/api/Me/Teams'),
    respondInvitation: (teamSlotId: number, memberId: number, action: InvitationAction) =>
        apiClient.put(`/api/teamSlot/${teamSlotId}/Invitations/${memberId}`, { action }),

    // Push 玩家端：開放隊發現 / 申請
    getOpenTeams: () => apiClient.get<OpenTeam[]>('/api/teamSlot/Open'),
    apply: (teamSlotId: number, characterId: string) =>
        apiClient.post(`/api/teamSlot/${teamSlotId}/Applications`, { characterId }),
};
