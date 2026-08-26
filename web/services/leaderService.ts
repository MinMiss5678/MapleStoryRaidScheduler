import { apiClient } from './apiClient';
import {
    Applicant,
    ApplicationAction,
    CreateTeamCommand,
    InvitationAction,
    LeaderTransfer,
    LedTeam,
    RecruitmentGapRow,
    RecruitmentHeatmap,
    RecruitmentHeatmapCommand,
    RosterMember,
    Membership,
    OpenTeam,
    TeamCandidate,
    TeamMember,
} from '@/types/leaderLed';

// leader-led（隊長主導組隊）前端 API：對齊後端 TeamSlotController 的 leader-led 端點。
export const leaderService = {
    // Pull 玩家端：我的邀請 / 我的隊
    getMyInvitations: () => apiClient.get<Membership[]>('/api/Me/Invitations'),
    getMyTeams: () => apiClient.get<Membership[]>('/api/Me/Teams'),
    leaveTeam: (teamSlotId: number) => apiClient.post(`/api/teamSlot/${teamSlotId}/Leave`),

    // 隊長轉讓
    getMyLeaderTransfers: () => apiClient.get<LeaderTransfer[]>('/api/Me/LeaderTransfers'),
    getTeamRoster: (teamSlotId: number) => apiClient.get<RosterMember[]>(`/api/teamSlot/${teamSlotId}/Roster`),
    getTeamMembers: (teamSlotId: number) => apiClient.get<TeamMember[]>(`/api/teamSlot/${teamSlotId}/Members`),
    proposeTransfer: (teamSlotId: number, memberId: number) =>
        apiClient.post(`/api/teamSlot/${teamSlotId}/TransferLeader`, { memberId }),
    respondTransfer: (teamSlotId: number, action: 'accept' | 'decline') =>
        apiClient.put(`/api/teamSlot/${teamSlotId}/TransferLeader`, { action }),
    respondInvitation: (teamSlotId: number, memberId: number, action: InvitationAction) =>
        apiClient.put(`/api/teamSlot/${teamSlotId}/Invitations/${memberId}`, { action }),

    // Push 玩家端：開放隊發現 / 申請
    getOpenTeams: () => apiClient.get<OpenTeam[]>('/api/teamSlot/Open'),
    apply: (teamSlotId: number, characterId: string) =>
        apiClient.post(`/api/teamSlot/${teamSlotId}/Applications`, { characterId }),

    // 隊長端：開隊 / 解散 / 我開的隊 hub / 候選挑人 / 申請審核
    createTeam: (command: CreateTeamCommand) =>
        apiClient.post<{ teamSlotId: number }>('/api/teamSlot', command),
    getRecruitmentHeatmap: (command: RecruitmentHeatmapCommand) =>
        apiClient.post<RecruitmentHeatmap>('/api/teamSlot/Heatmap', command),
    deleteTeam: (teamSlotId: number) => apiClient.delete(`/api/teamSlot/${teamSlotId}`),
    getLedTeams: () => apiClient.get<LedTeam[]>('/api/Me/LedTeams'),
    getCandidates: (teamSlotId: number) =>
        apiClient.get<TeamCandidate[]>(`/api/teamSlot/${teamSlotId}/Candidates`),
    getRecruitmentGap: (teamSlotId: number) =>
        apiClient.get<RecruitmentGapRow[]>(`/api/teamSlot/${teamSlotId}/RecruitmentGap`),
    invite: (teamSlotId: number, characterId: string) =>
        apiClient.post(`/api/teamSlot/${teamSlotId}/Invitations`, { characterId }),
    getApplications: (teamSlotId: number) =>
        apiClient.get<Applicant[]>(`/api/teamSlot/${teamSlotId}/Applications`),
    respondApplication: (teamSlotId: number, memberId: number, action: ApplicationAction) =>
        apiClient.put(`/api/teamSlot/${teamSlotId}/Applications/${memberId}`, { action }),
};
