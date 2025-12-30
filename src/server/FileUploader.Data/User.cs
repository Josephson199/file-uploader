using Microsoft.EntityFrameworkCore;

namespace FileUploader.Data;

public class User
{
    public int UserId { get; set; }

    public required string Sid { get; set; }

    public List<Upload> Uploads { get; set; } = [];
}

public class Upload
{
    public int UploadId { get; set; }

    public int UserId { get; set; }

    public required string FileName { get; set; }

    public long FileSizeBytes { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    public DateTimeOffset? VirusDetected { get; set; }

    public string? ScanReportRaw { get; set; }

    public required User User { get; set; }
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    public DbSet<Upload> Uploads { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.Sid)
                .HasMaxLength(128)
                .IsRequired();
            entity.HasIndex(e => e.Sid)
                .IsUnique();
            entity.HasMany(e => e.Uploads)
                  .WithOne(e => e.User)
                  .HasForeignKey(e => e.UserId);
        });

        modelBuilder.Entity<Upload>(entity =>
        {
            entity.HasKey(e => e.UploadId);
            entity.Property(e => e.FileName)
                .HasMaxLength(255)
                .IsRequired();
            entity.Property(e => e.ScanReportRaw)
                .HasColumnType("jsonb");
        });
    }
}