using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FileUploader.Data;

public static class JobStatus
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Completed = "completed";
    public const string Failed = "failed";
}
public record VirusScanPayload(int UploadId);

public class User
{
    public int UserId { get; set; }

    public required string Sub { get; set; }

    public List<Upload> Uploads { get; set; } = [];

    public List<UploadCandidate> UploadCandidates { get; set; } = [];
}

public class Upload
{
    public int UploadId { get; set; }

    public int UserId { get; set; }

    public required string OrignalFileName { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    public DateTimeOffset? VirusDetected { get; set; }

    public string? ScanReportRaw { get; set; }

    public User User { get; set; } = default!;

    public required string ObjectFileKey { get; set; }

    public required string FileId { get; set; }
}

public class UploadCandidate
{
    public int UploadCandidateId { get; set; }

    public required string FileId { get; set; }

    // Owner FK to User
    public int OwnerUserId { get; set; }

    // Navigation to the owning User
    public User OwnerUser { get; set; } = default!;

    // Optional key where the temporary object is stored by the tus S3 store
    public string? ObjectFileKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class Job
{
    public long Id { get; set; }

    public string Type { get; set; } = default!;

    public JsonDocument Payload { get; set; } = default!;

    public string Status { get; set; } = "pending";

    public int Attempts { get; set; } = 0;
    public int MaxAttempts { get; set; } = 5;

    public DateTimeOffset? LockedAt { get; set; }
    public string? LockedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}


public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    public DbSet<Upload> Uploads { get; set; }

    // New DbSet for upload candidates
    public DbSet<UploadCandidate> UploadCandidates { get; set; }

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

            entity.HasMany(e => e.UploadCandidates)
                  .WithOne(e => e.OwnerUser)
                  .HasForeignKey(e => e.OwnerUserId);
        });

        modelBuilder.Entity<Upload>(entity =>
        {
            entity.HasKey(e => e.UploadId);
            entity.Property(e => e.OrignalFileName)
                .HasMaxLength(255)
                .IsRequired();
            entity.Property(e => e.ScanReportRaw)
                .HasColumnType("jsonb");
            entity.Property(e => e.ObjectFileKey)
                .HasMaxLength(1024)
                .IsRequired();
            entity.Property(e => e.FileId)
                .HasMaxLength(128)
                .IsRequired();
            entity.HasIndex(e => e.FileId)
                .IsUnique();
        });

        modelBuilder.Entity<UploadCandidate>(entity =>
        {
            entity.HasKey(e => e.UploadCandidateId);
            entity.Property(e => e.FileId)
                .HasMaxLength(128)
                .IsRequired();
            entity.HasIndex(e => e.FileId)
                .IsUnique();
            entity.Property(e => e.OwnerUserId)
                .IsRequired();
            entity.Property(e => e.ObjectFileKey)
                .HasMaxLength(1024);
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