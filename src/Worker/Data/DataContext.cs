using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Worker.Data.Entities;

namespace Worker.Data
{
    public class DataContext : DbContext
    {
        public DbSet<MetadataJob> MetadataJobs => Set<MetadataJob>();
        public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();
        public DbSet<ProductionDocumentEntity> ProductionDocuments => Set<ProductionDocumentEntity>();
        public DbSet<ProductionDocumentTag> ProductionDocumentTags => Set<ProductionDocumentTag>();

        public DataContext(DbContextOptions<DataContext> options) : base(options) { }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MetadataJob>().HasIndex(x => x.CorrelationId).IsUnique();
            modelBuilder.Entity<DocumentEntity>().HasIndex(x => new { x.JobId, x.FileGuid }).IsUnique();
            modelBuilder.Entity<ProductionDocumentTag>().HasIndex(x => new { x.ProductionDocumentId, x.TagKey });
            modelBuilder.Entity<ProductionDocumentEntity>().HasMany<ProductionDocumentTag>().WithOne().HasForeignKey(t => t.ProductionDocumentId);
            base.OnModelCreating(modelBuilder);
        }
    }
}
