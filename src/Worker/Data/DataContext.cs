using Microsoft.EntityFrameworkCore;

namespace Worker.Data
{
    public class DataContext : DbContext
    {
        public DataContext(DbContextOptions<DataContext> opts) : base(opts) { }
        public DbSet<MetadataJob> MetadataJobs => Set<MetadataJob>();
        public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<MetadataJob>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Status).HasMaxLength(32).IsRequired();
                b.HasMany(x => x.Documents).WithOne(d => d.Job).HasForeignKey(d => d.JobId);
            });

            modelBuilder.Entity<DocumentEntity>(b =>
            {
                b.HasKey(x => x.Id);
                b.Property(x => x.Filepath).HasMaxLength(1024).IsRequired();
                b.Property(x => x.FileGuid).HasMaxLength(128);
                b.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
                b.HasIndex(x => new { x.JobId, x.Filepath }).IsUnique();
            });
        }
    }
}
