using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FileUploader.Data;

public class User
{
    public int UserId { get; set; }

    public required string Sub { get; set; }

    public List<Upload> Uploads { get; set; } = [];
}

public class Upload
{
    public int UploadId { get; set; }

    public int UserId { get; set; }

    public required string FileName { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    public DateTimeOffset? VirusDetected { get; set; }

    public string? ScanReportRaw { get; set; }

    public User User { get; set; } = default!;

    public required string FileKey { get; set; }
}

public class Job
{
    public long Id { get; set; }

    public string Type { get; set; } = default!;

    public JsonDocument Payload { get; set; } = default!;

    public string Status { get; set; } = "pending";

    public int Attempts { get; set; } = 0;
    public int MaxAttempts { get; set; } = 5;

    public DateTime? LockedAt { get; set; }
    public string? LockedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}


public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    public DbSet<Upload> Uploads { get; set; }
    public DbSet<Job> Jobs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.Property(e => e.Sub)
                .HasMaxLength(128)
                .IsRequired();
            entity.HasIndex(e => e.Sub)
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
            entity.Property(e => e.FileKey)
                .HasMaxLength(1024)
                .IsRequired();
        });

        modelBuilder.Entity<Job>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type)
                .HasMaxLength(100)
                .IsRequired();
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(e => e.Payload)
                .HasColumnType("jsonb")
                .IsRequired();
        });
    }
}