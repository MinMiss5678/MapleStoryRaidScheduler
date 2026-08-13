using Application.Events;
using Application.Interface;
using Application.Services;
using Domain.Repositories;
using Infrastructure.Dapper;
using Infrastructure.Repositories;
using Infrastructure.Services;
using Application.Queries;
using Infrastructure.Query;
using Presentation.WebApi.Middleware;

namespace Presentation.WebApi.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddScoped<IAuthAppService, AuthAppService>();
        services.AddSingleton<ConfigChangeNotifier>();
        return services;
    }

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services)
    {
        services.AddScoped<AuthenticationMiddleware>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<DbContext>();
        // outbox 寫入端：把設定變更事件寫進當前請求交易（與資料原子）。API 只寫、不派發。
        services.AddScoped<IOutbox, Outbox>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IPlayerService, PlayerService>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<ICharacterService, CharacterService>();
        services.AddScoped<IDiscordOAuthClient, DiscordOAuthClient>();
        services.AddScoped<IBossService, BossService>();
        services.AddScoped<IAvailabilityOverrideService, AvailabilityOverrideService>();
        services.AddScoped<IProfileService, ProfileService>();
        services.AddScoped<ILfgIntentRepository, LfgIntentRepository>();
        services.AddScoped<ILfgQuery, LfgQuery>();
        services.AddScoped<ILfgService, LfgService>();
        services.AddScoped<IPeriodService, PeriodService>();
        services.AddScoped<ISystemConfigService, SystemConfigService>();
        services.AddScoped<IMicrosoftMailService, MicrosoftMailService>();
        return services;
    }

    public static IServiceCollection AddRepositories(this IServiceCollection services)
    {
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<ISessionQuery, SessionQuery>();
        services.AddScoped<IPlayerRepository, PlayerRepository>();
        services.AddScoped<ICharacterQuery, CharacterQuery>();
        services.AddScoped<ICharacterRepository, CharacterRepository>();
        services.AddScoped<IBossRepository, BossRepository>();
        services.AddScoped<IPlayerAvailabilityStandingRepository, PlayerAvailabilityStandingRepository>();
        services.AddScoped<IPlayerAvailabilityOverrideRepository, PlayerAvailabilityOverrideRepository>();
        services.AddScoped<IPeriodRepository, PeriodRepository>();
        services.AddScoped<IPeriodQuery, PeriodQuery>();
        services.AddScoped<IDiscordRoleMappingRepository, DiscordRoleMappingRepository>();
        services.AddScoped<IJobCategoryRepository, JobCategoryRepository>();
        services.AddScoped<ITeamSlotRepository, TeamSlotRepository>();
        services.AddScoped<ITeamSlotQuery, TeamSlotQuery>();
        services.AddScoped<ITeamSlotCharacterRepository, TeamSlotCharacterRepository>();
        services.AddScoped<ITeamSlotRequirementRepository, TeamSlotRequirementRepository>();
        services.AddScoped<ICharacterBossClearRepository, CharacterBossClearRepository>();
        services.AddScoped<ITeamLeaderService, TeamLeaderService>();
        services.AddScoped<ITeamCandidateQuery, TeamCandidateQuery>();
        services.AddScoped<ITeamMembershipQuery, TeamMembershipQuery>();
        services.AddScoped<IRegistrationLock, RegistrationLock>();
        return services;
    }
}
