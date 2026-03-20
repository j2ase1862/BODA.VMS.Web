using BODA.VMS.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BODA.VMS.Web.Data;

public class BodaVmsDbContext : DbContext
{
    public BodaVmsDbContext(DbContextOptions<BodaVmsDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<VisionClient> Clients => Set<VisionClient>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<Step> Steps => Set<Step>();
    public DbSet<InspectionTool> InspectionTools => Set<InspectionTool>();
    public DbSet<InspectionHistory> InspectionHistories => Set<InspectionHistory>();
    public DbSet<RecipeParameter> RecipeParameters => Set<RecipeParameter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        // VisionServer가 AUTOINCREMENT로 Id 부여 — Web은 읽기 전용으로 사용
        modelBuilder.Entity<VisionClient>()
            .Property(c => c.Id)
            .ValueGeneratedNever();

        modelBuilder.Entity<VisionClient>()
            .HasIndex(c => c.ClientIndex)
            .IsUnique();

        // Recipe도 VisionServer가 ID 관리
        modelBuilder.Entity<Recipe>()
            .Property(r => r.Id)
            .ValueGeneratedNever();

        modelBuilder.Entity<InspectionHistory>()
            .HasIndex(h => h.InspectedAt);

        modelBuilder.Entity<InspectionHistory>()
            .HasIndex(h => new { h.ClientId, h.InspectedAt });

        modelBuilder.Entity<RecipeParameter>()
            .HasIndex(p => new { p.RecipeId, p.ParamCode })
            .IsUnique();
    }
}
