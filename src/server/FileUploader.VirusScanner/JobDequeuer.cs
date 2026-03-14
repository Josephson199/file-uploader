using FileUploader.Data;
using Microsoft.EntityFrameworkCore;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace FileUploader.VirusScanner;



internal sealed class JobDequeuer
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly TimeSpan _pollInterval;

    public JobDequeuer(
        IDbContextFactory<AppDbContext> dbFactory,
        TimeSpan? pollInterval = null)
    {
        _dbFactory = dbFactory;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
    }

    private async Task<Job?> TryDequeueAsync(string appId, string jobType, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var job = await db.Jobs
            .FromSqlInterpolated($@"
                SELECT *
                FROM ""Jobs""
                WHERE ""Type"" = {jobType}
                  AND (
                        (""Status"" = {JobStatus.Pending})
                        OR
                        (""Status"" = {JobStatus.Failed} AND ""Attempts"" < ""MaxAttempts"")
                      )
                ORDER BY ""JobId""
                FOR UPDATE SKIP LOCKED
                LIMIT 1
            ")
            .FirstOrDefaultAsync(ct);

        if (job == null)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        job.Status = JobStatus.Processing;
        job.LockedAt = DateTimeOffset.UtcNow;
        job.LockedBy = appId;
        job.Attempts += 1;
        job.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return job;
    }

    public async IAsyncEnumerable<Job> DequeueJobsAsync(
        string appId,
        string jobType,
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var job = await TryDequeueAsync(appId, jobType, ct);

            if (job != null)
            {
                yield return job;
                continue;
            }

            // No job available — wait before polling again
            await Task.Delay(_pollInterval, ct);
        }
    }

}
