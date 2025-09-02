using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Worker.Data.DbContext
{
    using Microsoft.EntityFrameworkCore;
    using Worker.Data.Entities.staging;

    public class StagingDbContext : DbContext
    {
        public StagingDbContext(DbContextOptions<StagingDbContext> options) : base(options) { }

        public DbSet<MetadataJob> MetadataJobs => Set<MetadataJob>();
        public DbSet<DocumentStagingRaw> DocumentStagingRaws => Set<DocumentStagingRaw>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MetadataJob>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.CorrelationId).IsUnique();
                e.Property(x => x.Status).HasMaxLength(32);
            });

            modelBuilder.Entity<DocumentStagingRaw>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => new { x.JobId, x.Status });
                e.Property(x => x.RawData).IsRequired();
            });

        }
    }
}
