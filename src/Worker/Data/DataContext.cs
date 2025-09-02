//using Microsoft.EntityFrameworkCore;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using Worker.Data.Entities;

//namespace Worker.Data
//{
//    public class DataContext : DbContext
//    {
//        public DbSet<MetadataJob> MetadataJobs => Set<MetadataJob>();
//        public DbSet<DocumentStagingRaw> DocumentStagingRaws => Set<DocumentStagingRaw>();
//        public DbSet<ProductionDocumentEntity> ProductionDocuments => Set<ProductionDocumentEntity>();
//        public DbSet<ProductionDocumentTag> ProductionDocumentTags => Set<ProductionDocumentTag>();

//        public DataContext(DbContextOptions<DataContext> options) : base(options) { }

//        protected override void OnModelCreating(ModelBuilder modelBuilder)
//        {
//            modelBuilder.Entity<MetadataJob>(e =>
//            {
//                e.HasKey(x => x.Id);
//                e.HasIndex(x => x.CorrelationId).IsUnique();
//                e.Property(x => x.Status).HasMaxLength(32);
//            });

//            modelBuilder.Entity<DocumentStagingRaw>(e =>
//            {
//                e.HasKey(x => x.Id);
//                e.HasIndex(x => new { x.JobId, x.Status });
//                e.Property(x => x.RawData).IsRequired();
//            });

//            modelBuilder.Entity<ProductionDocumentEntity>(e =>
//            {
//                e.HasKey(x => x.Id);
//                e.HasIndex(x => new { x.JobId, x.FileGuid }).IsUnique();
//                e.Property(x => x.FileGuid).HasMaxLength(64);
//                e.Property(x => x.FileName).HasMaxLength(256);
//                e.Property(x => x.Extension).HasMaxLength(32);
//            });

//            modelBuilder.Entity<ProductionDocumentTag>(e =>
//            {
//                e.HasKey(x => x.Id);
//                e.HasIndex(x => new { x.ProductionDocumentId, x.Key });
//                e.Property(x => x.Key).HasMaxLength(128);
//                e.Property(x => x.Value).HasMaxLength(512);
//            });
//        }
//    }
//}
