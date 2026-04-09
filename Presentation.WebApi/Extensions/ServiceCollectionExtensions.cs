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
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IPlayerService, PlayerService>();
        services.AddScoped<IJwtService, JwtService>();
        services.AddScoped<ICharacterService, CharacterService>();
        services.AddScoped<IDiscordOAuthClient, DiscordOAuthClient>();
        services.AddScoped<IBossService, BossService>();
        services.AddScoped<IRegisterService, RegisterService>();
        services.AddScoped<IPeriodService, PeriodService>();
        services.AddScoped<ISystemConfigService, SystemConfigService>();
        services.AddScoped<IScheduleService, ScheduleService>();
        services.AddScoped<ITeamSlotService, TeamSlotService>();
        services.AddScoped<ITeamSlotAutoAssignService, TeamSlotAutoAssignService>();
        services.AddScoped<ITeamSlotMergeService, TeamSlotMergeService>();
        services.AddScoped<ITeamSlotCharacterService, TeamSlotCharacterService>();
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
        services.AddScoped<IPlayerRegisterRepository, PlayerRegisterRepository>();
        services.AddScoped<IPlayerAvailabilityRepository, PlayerAvailabilityRepository>();
        services.AddScoped<ICharacterRegisterRepository, CharacterRegisterRepository>();
        services.AddScoped<IPeriodRepository, PeriodRepository>();
        services.AddScoped<IPeriodQuery, PeriodQuery>();
        services.AddScoped<IDiscordRoleMappingRepository, DiscordRoleMappingRepository>();
        services.AddScoped<IJobCategoryRepository, JobCategoryRepository>();
        services.AddScoped<IPlayerRegisterQuery, PlayerRegisterQuery>();
        services.AddScoped<ITeamSlotRepository, TeamSlotRepository>();
        services.AddScoped<ITeamSlotQuery, TeamSlotQuery>();
        services.AddScoped<ITeamSlotCharacterRepository, TeamSlotCharacterRepository>();
        return services;
    }
}
