using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace FileUploader.Data;

public record JobEvent(
    long JobId,
    string Type,
    int? UserId,
    JobStatus Status,
    int Attempts,
    int MaxAttempts,
    DateTimeOffset? LockedAt,
    string? LockedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    JsonDocument Payload);

public record Job<T>(long JobId, T Payload, JobStatus Status);

internal static class JobQueueOptions
{
    internal static readonly JsonSerializerOptions JsonSerializerOptions = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
}

public sealed class JobQueue<T>
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly TimeSpan _pollInterval;

    public JobQueue(
        IDbContextFactory<AppDbContext> dbFactory,
        TimeSpan? pollInterval = null)
    {
        _dbFactory = dbFactory;
        _pollInterval = pollInterval ?? TimeSpan.FromSeconds(2);
    }

    public async IAsyncEnumerable<Job<T>> Dequeue(
        string appId,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var job = await TryDequeue(appId, ct);

            if (job != null)
            {
                yield return job;
                continue;
            }

            // No job available — wait before polling again
            await Task.Delay(_pollInterval, ct);
        }
    }

    public async Task<long> Enqueue(T payload, int? userId = null, int maxAttempts = 5, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var entity = new Job
        {
            Type = typeof(T).Name,
            UserId = userId,
            Payload = JsonSerializer.SerializeToDocument(payload, JobQueueOptions.JsonSerializerOptions),
            Status = JobStatus.Pending,
            Attempts = 0,
            MaxAttempts = maxAttempts,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        db.Jobs.Add(entity);
        await db.SaveChangesAsync(ct);

        return entity.JobId;
    }

    public async Task CompleteJob(Job<T> job, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        await db.Jobs
            .Where(j => j.JobId == job.JobId)
            .ExecuteUpdateAsync(
            j => j
                .SetProperty(j => j.Status, JobStatus.Completed)
                .SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow),
            ct);
    }

    public async Task FailJob(Job<T> job, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        await db.Jobs
            .Where(j => j.JobId == job.JobId)
            .ExecuteUpdateAsync(
            j => j
                .SetProperty(j => j.Status, JobStatus.Failed)
                .SetProperty(j => j.UpdatedAt, DateTimeOffset.UtcNow),
            ct);
    }

    private async Task<Job<T>?> TryDequeue(string appId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var job = await db.Jobs
            .FromSqlInterpolated($@"
                SELECT *
                FROM ""Jobs""
                WHERE ""Type"" = {typeof(T).Name}
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

        return new Job<T>(job.JobId, job.Payload.Deserialize<T>(JobQueueOptions.JsonSerializerOptions)!, job.Status);
    }

    
}