using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Data.DbContext
{
    using Microsoft.EntityFrameworkCore;
    using Worker.Data.Entities.production;

    public class ProductionDbContext : DbContext
    {
        public ProductionDbContext(DbContextOptions<ProductionDbContext> options) : base(options) { }

        public DbSet<ProductionDocumentEntity> ProductionDocuments => Set<ProductionDocumentEntity>();
        public DbSet<ProductionDocumentTag> ProductionDocumentTags => Set<ProductionDocumentTag>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ProductionDocumentEntity>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => new { x.JobId, x.FileGuid }).IsUnique();
                e.Property(x => x.FileGuid).HasMaxLength(64);
                e.Property(x => x.FileName).HasMaxLength(256);
                e.Property(x => x.Extension).HasMaxLength(32);
            });

            modelBuilder.Entity<ProductionDocumentTag>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => new { x.ProductionDocumentId, x.Key });
                e.Property(x => x.Key).HasMaxLength(128);
                e.Property(x => x.Value).HasMaxLength(512);
            });
        }
    }
}
