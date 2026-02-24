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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<VisionClient>()
            .HasIndex(c => c.ClientIndex)
            .IsUnique();

        modelBuilder.Entity<InspectionHistory>()
            .HasIndex(h => h.InspectedAt);

        modelBuilder.Entity<InspectionHistory>()
            .HasIndex(h => new { h.ClientId, h.InspectedAt });

        // Seed default admin user (password: admin)
        modelBuilder.Entity<User>().HasData(new User
        {
            Id = 1,
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin"),
            DisplayName = "Administrator",
            Role = "Admin",
            IsApproved = true,
            CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        });
    }
}
