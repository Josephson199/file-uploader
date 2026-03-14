using Microsoft.EntityFrameworkCore;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Text.Json;

namespace FileUploader.Data;

public enum JobStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
}

public record VirusScanPayload(int UploadId);

public class User
{
    public int UserId { get; set; }

    public required string Sub { get; set; }

    public List<Upload> Uploads { get; set; } = [];

    public List<UploadCandidate> UploadCandidates { get; set; } = [];

    public List<Job> Jobs { get; set; } = [];
}

public class Upload
{
    public int UploadId { get; set; }

    public int UserId { get; set; }

    public required string OrignalFileName { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public User User { get; set; } = default!;

    public required string TempObjectKey { get; set; }

    public string? PersistedObjectKey { get; set; }

    public required string FileId { get; set; }

    public List<UploadValidationError> UploadValidationErrors { get; set; } = [];

    public UploadVirusScanResult? UploadVirusScanResult { get; set; }
}

public class UploadValidationError
{
    public int UploadValidationErrorId { get; set; }
    public int UploadId { get; set; }
    public string? ValidationErrorMessage { get; set; }
    public string ValidationType { get; set; } = default!;
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}

public class UploadVirusScanResult
{
    public int UploadVirusScanResultId { get; set; }
    public int UploadId { get; set; }
    public ScanReult Result { get; set; }
    public string? RawResult { get; set; }
    public List<InfectedFile> InfectedFiles { get; set; } = [];
    public DateTimeOffset Created { get; set; } = DateTimeOffset.UtcNow;
}

public class InfectedFile
{
    public int InfectedFileId { get; set; }
    public UploadVirusScanResult UploadVirusScanResult { get; set; } = default!;
    public int UploadVirusScanResultId { get; set; }
    public string FileName { get; set; } = default!;
    public string VirusName { get; set; } = default!;
}

public enum ScanReult
{
    Unknown,
    Clean,
    VirusDetected,
    Error
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
    public long JobId { get; set; }

    public string Type { get; set; } = default!;

    public int? UserId { get; set; }

    public User? User { get; set; }

    public JsonDocument Payload { get; set; } = default!;

    public JobStatus Status { get; set; } = JobStatus.Pending;

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
    public DbSet<UploadCandidate> UploadCandidates { get; set; }
    public DbSet<UploadVirusScanResult> UploadVirusScanResults { get; set; }
    public DbSet<UploadValidationError> UploadValidationResults { get; set; }

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
            entity.HasMany(e => e.Jobs)
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
            entity.Property(e => e.TempObjectKey)
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

        modelBuilder.Entity<UploadVirusScanResult>(entity =>
        {
            entity.HasKey(e => e.UploadVirusScanResultId);
            entity.Property(e => e.RawResult)
                .HasMaxLength(2048);
        });

        modelBuilder.Entity<InfectedFile>(entity =>
        {
            entity.HasKey(e => e.InfectedFileId);
            entity.Property(e => e.FileName)
                .HasMaxLength(255)
                .IsRequired();
            entity.Property(e => e.VirusName)
                .HasMaxLength(1048)
                .IsRequired();
        });

        modelBuilder.Entity<UploadValidationError>(entity =>
        {
            entity.HasKey(e => e.UploadValidationErrorId);
            entity.Property(e => e.ValidationErrorMessage)
                .HasMaxLength(4000);
        });

        modelBuilder.Entity<Job>(entity =>
        {
            entity.HasKey(e => e.JobId);
            entity.Property(e => e.Type)
                .HasMaxLength(100)
                .IsRequired();
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .IsRequired();
            entity.Property(e => e.Payload)
                .HasColumnType("jsonb")
                .IsRequired();
            entity.HasOne(e => e.User)
                .WithMany(u => u.Jobs)
                .HasForeignKey(j => j.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}