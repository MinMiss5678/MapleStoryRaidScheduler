using System.Reflection;
using Domain.Attributes;
using Infrastructure.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Migrations;

/// <summary>
/// 僅供 design-time 使用（dotnet ef migrations add）。
/// 不注冊到 DI，不影響 runtime，Dapper 效能不受影響。
/// </summary>
public class MigrationDbContext(DbContextOptions<MigrationDbContext> options) : DbContext(options)
{
    public DbSet<BossDbModel> Bosses { get; set; }
    public DbSet<BossTemplateDbModel> BossTemplates { get; set; }
    public DbSet<BossTemplateRequirementDbModel> BossTemplateRequirements { get; set; }
    public DbSet<CharacterDbModel> Characters { get; set; }
    public DbSet<CharacterRegisterDbModel> CharacterRegisters { get; set; }
    public DbSet<DiscordRoleMappingDbModel> DiscordRoleMappings { get; set; }
    public DbSet<JobCategoryDbModel> JobCategories { get; set; }
    public DbSet<PeriodDbModel> Periods { get; set; }
    public DbSet<PlayerAvailabilityDbModel> PlayerAvailabilities { get; set; }
    public DbSet<PlayerDbModel> Players { get; set; }
    public DbSet<PlayerRegisterDbModel> PlayerRegisters { get; set; }
    public DbSet<SessionDbModel> Sessions { get; set; }
    public DbSet<SystemConfigDbModel> SystemConfigs { get; set; }
    public DbSet<TeamSlotDbModel> TeamSlots { get; set; }
    public DbSet<TeamSlotCharacterDbModel> TeamSlotCharacters { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // [ExplicitKey] = PK 由使用者指定，非 DB 自增
        // EF Core 不認識此 attribute，需手動設定 HasKey + ValueGeneratedNever
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var explicitKeyProp = entityType.ClrType.GetProperties()
                .FirstOrDefault(p => p.GetCustomAttribute<ExplicitKeyAttribute>() != null);

            if (explicitKeyProp == null) continue;

            modelBuilder.Entity(entityType.ClrType)
                .HasKey(explicitKeyProp.Name);

            modelBuilder.Entity(entityType.ClrType)
                .Property(explicitKeyProp.Name)
                .ValueGeneratedNever();
        }
    }
}
