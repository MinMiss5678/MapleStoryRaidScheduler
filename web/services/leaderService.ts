import { apiClient } from './apiClient';
import {
    ApplicationAction,
    CreateTeamCommand,
    InvitationAction,
    LedTeam,
    Membership,
    OpenTeam,
    TeamCandidate,
} from '@/types/leaderLed';

// leader-led（隊長主導組隊）前端 API：對齊後端 TeamSlotController 的 leader-led 端點。
export const leaderService = {
    // Pull 玩家端：我的邀請 / 我的隊
    getMyInvitations: () => apiClient.get<Membership[]>('/api/Me/Invitations'),
    getMyTeams: () => apiClient.get<Membership[]>('/api/Me/Teams'),
    leaveTeam: (teamSlotId: number) => apiClient.post(`/api/teamSlot/${teamSlotId}/Leave`),
    respondInvitation: (teamSlotId: number, memberId: number, action: InvitationAction) =>
        apiClient.put(`/api/teamSlot/${teamSlotId}/Invitations/${memberId}`, { action }),

    // Push 玩家端：開放隊發現 / 申請
    getOpenTeams: () => apiClient.get<OpenTeam[]>('/api/teamSlot/Open'),
    apply: (teamSlotId: number, characterId: string) =>
        apiClient.post(`/api/teamSlot/${teamSlotId}/Applications`, { characterId }),

    // 隊長端：開隊 / 我開的隊 hub / 候選挑人 / 申請審核
    createTeam: (command: CreateTeamCommand) =>
        apiClient.post<{ teamSlotId: number }>('/api/teamSlot', command),
    getLedTeams: () => apiClient.get<LedTeam[]>('/api/Me/LedTeams'),
    getCandidates: (teamSlotId: number) =>
        apiClient.get<TeamCandidate[]>(`/api/teamSlot/${teamSlotId}/Candidates`),
    invite: (teamSlotId: number, characterId: string) =>
        apiClient.post(`/api/teamSlot/${teamSlotId}/Invitations`, { characterId }),
    getApplications: (teamSlotId: number) =>
        apiClient.get<Membership[]>(`/api/teamSlot/${teamSlotId}/Applications`),
    respondApplication: (teamSlotId: number, memberId: number, action: ApplicationAction) =>
        apiClient.put(`/api/teamSlot/${teamSlotId}/Applications/${memberId}`, { action }),
};
