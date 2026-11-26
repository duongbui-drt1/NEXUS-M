using Microsoft.EntityFrameworkCore;
using NexusM.Modules.Foundation.Domain;

namespace NexusM.Modules.Foundation.Infrastructure;

public sealed class FoundationDbContext(DbContextOptions<FoundationDbContext> options) : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("m_foundation");

        modelBuilder.Entity<Workspace>(e =>
        {
            e.ToTable("workspaces");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
            e.Property(x => x.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
            e.Property(x => x.DefaultLocale).HasColumnName("default_locale").HasMaxLength(20).IsRequired();
            e.Property(x => x.DefaultTimezone).HasColumnName("default_timezone").HasMaxLength(64).IsRequired();
            e.Property(x => x.DefaultCurrency).HasColumnName("default_currency").HasMaxLength(3).IsRequired();
            e.Property(x => x.OrgMode).HasColumnName("org_mode").IsRequired();
            e.Property(x => x.IsDefault).HasColumnName("is_default").IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
            e.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();
            e.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasDatabaseName("ux_m_workspaces_tenant_code");
        });

        modelBuilder.Entity<UserPreference>(e =>
        {
            e.ToTable("user_preferences");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
            e.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
            e.Property(x => x.Locale).HasColumnName("locale").HasMaxLength(20).IsRequired();
            e.Property(x => x.Timezone).HasColumnName("timezone").HasMaxLength(64).IsRequired();
            e.Property(x => x.Theme).HasColumnName("theme").HasMaxLength(16).IsRequired();
            e.Property(x => x.DefaultWorkspaceId).HasColumnName("default_workspace_id");
            e.Property(x => x.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
            e.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique().HasDatabaseName("ux_m_user_prefs_tenant_user");
        });
    }
}
