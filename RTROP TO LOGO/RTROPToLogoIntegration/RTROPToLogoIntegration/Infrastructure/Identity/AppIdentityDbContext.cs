using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RTROPToLogoIntegration.Domain.Entities;

namespace RTROPToLogoIntegration.Infrastructure.Identity
{
    /// <summary>
    /// Kimlik doğrulama işlemleri için DbContext.
    /// Sadece Identity tablolarını yönetir.
    /// </summary>
    public class AppIdentityDbContext : IdentityDbContext
    {
        public AppIdentityDbContext(DbContextOptions<AppIdentityDbContext> options) : base(options)
        {
        }

        public DbSet<RTROPToLogoIntegration.Domain.Entities.LogIncomingRequest> LogIncomingRequests { get; set; }
        public DbSet<RTROPToLogoIntegration.Domain.Entities.ApplicationLog> Logs { get; set; } // Serilog Logs table
        public DbSet<MrpItemParameter> MrpItemParameters { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<RTROPToLogoIntegration.Domain.Entities.LogIncomingRequest>()
                .HasIndex(l => l.TransactionId);

            builder.Entity<MrpItemParameter>(entity =>
            {
                entity.HasIndex(e => new { e.FirmNo, e.ItemID })
                      .IsUnique()
                      .HasDatabaseName("IX_MRP_ITEM_PARAMETERS_FirmNo_ItemID");

                entity.Property(e => e.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
                entity.Property(e => e.UpdatedAt).HasDefaultValueSql("GETUTCDATE()");
            });
        }
    }
}
