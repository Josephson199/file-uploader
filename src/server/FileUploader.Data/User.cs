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

    public User User { get; set; } = null!;
}

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    public DbSet<Upload> Uploads { get; set; }
}