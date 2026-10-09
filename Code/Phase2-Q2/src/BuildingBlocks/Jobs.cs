using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FoodDelivery.BuildingBlocks;

// =============================================================================================
// POSTGRESQL-BACKED JOB QUEUE (Q2, D12) + WORKER (D13)
//
// Why a queue inside PostgreSQL instead of RabbitMQ?
//   - no new server to run (we still have no DevOps team)
//   - the outbox and the jobs live in the same database, so nothing can be lost between them
//   - PostgreSQL easily handles our volume (thousands of jobs per hour, not per second)
//
// A job is just a ROW. Workers take one with:
//     SELECT ... FOR UPDATE SKIP LOCKED LIMIT 1
// SKIP LOCKED lets several workers each grab a DIFFERENT job at the same time without blocking
// each other. Analogy: order slips on a kitchen ticket rail - each chef takes a different slip.
//
// Libraries that do this for you: Hangfire (.NET), pg-boss (Node). It's hand-written here so you
// can SEE how it works.
// =============================================================================================
public static class JobQueue
{
    public const string SchemaSql = """
        CREATE SCHEMA IF NOT EXISTS jobs;
        CREATE TABLE IF NOT EXISTS jobs.jobs (
            id            uuid PRIMARY KEY,
            queue         text NOT NULL DEFAULT 'default',
            kind          text NOT NULL,              -- e.g. the handler that must run
            payload       jsonb NOT NULL,
            dedupe_key    text NOT NULL UNIQUE,       -- same event + same handler = one job, ever
            status        text NOT NULL,              -- Pending | Running | Done | DeadLetter
            attempts      int NOT NULL DEFAULT 0,
            run_at        timestamptz NOT NULL,       -- not before this time (used for backoff)
            locked_until  timestamptz NULL,           -- crash recovery: a Running job past this time is retried
            last_error    text NULL,
            created_at    timestamptz NOT NULL,
            completed_at  timestamptz NULL
        );
        CREATE INDEX IF NOT EXISTS ix_jobs_ready ON jobs.jobs (queue, run_at) WHERE status = 'Pending';
        """;

    /// <summary>
    /// Exponential backoff with jitter: wait 10s, 30s, 90s, 4.5min... plus up to 20% randomness,
    /// so a thousand failing jobs don't all retry in the same second (a "thundering herd").
    /// </summary>
    public static TimeSpan Backoff(int attempt) =>
        TimeSpan.FromSeconds(10 * Math.Pow(3, attempt - 1) * (1 + Random.Shared.NextDouble() * 0.2));

    public const int MaxAttempts = 5;
}

/// <summary>What a job carries when it represents "run THIS handler for THIS event".</summary>
internal sealed record EventJobPayload(string EventType, string HandlerType, string EventJson);

/// <summary>
/// OUTBOX DISPATCHER (runs in the Worker): reads new rows from every module's outbox and creates
/// one job per subscribing handler. Both steps happen in ONE transaction, so an event is never
/// half-dispatched. ON CONFLICT on dedupe_key makes re-dispatching harmless.
/// </summary>
public sealed class OutboxDispatcher(
    OutboxRegistry outboxes, EventSubscriptions subscriptions, IConfiguration configuration, ILogger<OutboxDispatcher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var connectionString = configuration.ConnectionStringFor("Default");
        while (!stop.IsCancellationRequested)
        {
            var dispatched = 0;
            foreach (var schema in outboxes.Schemas)
            {
                try { dispatched += await DispatchAsync(connectionString, schema, stop); }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Outbox dispatch failed for {Schema}", schema); }
            }
            if (dispatched == 0) await Task.Delay(TimeSpan.FromMilliseconds(500), stop);
        }
    }

    private async Task<int> DispatchAsync(string connectionString, string schema, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);

        var rows = new List<(Guid Id, string Type, string Payload)>();
        await using (var select = new NpgsqlCommand(
            $"SELECT id, type, payload::text FROM {schema}.outbox WHERE processed_at IS NULL ORDER BY occurred_at LIMIT 50 FOR UPDATE SKIP LOCKED",
            connection, tx))
        await using (var reader = await select.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) rows.Add((reader.GetGuid(0), reader.GetString(1), reader.GetString(2)));

        foreach (var (id, type, payload) in rows)
        {
            var eventType = Type.GetType(type);
            foreach (var handler in eventType is null ? [] : subscriptions.HandlersFor(eventType))
            {
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO jobs.jobs (id, queue, kind, payload, dedupe_key, status, run_at, created_at)
                    VALUES (@id, @queue, @kind, @payload::jsonb, @dedupe, 'Pending', now(), now())
                    ON CONFLICT (dedupe_key) DO NOTHING
                    """, connection, tx);
                insert.Parameters.AddWithValue("id", Guid.NewGuid());
                insert.Parameters.AddWithValue("queue", JobQueues.QueueFor(handler));
                insert.Parameters.AddWithValue("kind", handler.Name);
                insert.Parameters.AddWithValue("payload", JsonSerializer.Serialize(new EventJobPayload(type, handler.AssemblyQualifiedName!, payload)));
                insert.Parameters.AddWithValue("dedupe", $"{id}:{handler.FullName}");
                await insert.ExecuteNonQueryAsync(ct);
            }
            await using var done = new NpgsqlCommand($"UPDATE {schema}.outbox SET processed_at = now() WHERE id = @id", connection, tx);
            done.Parameters.AddWithValue("id", id);
            await done.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        return rows.Count;
    }
}

/// <summary>Q2: every job goes to one queue. (Q3 splits queues so payments get their own lane.)</summary>
public static class JobQueues
{
    public const string Default = "default";
    public static string QueueFor(Type handlerType) => Default;
}

/// <summary>
/// JOB RUNNER (runs in the Worker): takes one job at a time, runs it, records the outcome.
///   success                  -> Done
///   failure, tries left      -> Pending again, later (backoff + jitter)
///   failure, no tries left   -> DeadLetter (a human looks at it; an alert would fire)
///   worker crashed mid-job   -> its lock expires and the job is picked up again
/// Because a job can run more than once, every handler is written to be idempotent.
/// </summary>
public sealed class JobRunner(IServiceProvider services, IConfiguration configuration, ILogger<JobRunner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var connectionString = configuration.ConnectionStringFor("Default");
        while (!stop.IsCancellationRequested)
        {
            bool ranOne;
            try { ranOne = await RunNextAsync(connectionString, JobQueues.Default, stop); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Job runner loop error"); ranOne = false; }
            if (!ranOne) await Task.Delay(TimeSpan.FromMilliseconds(500), stop);
        }
    }

    private async Task<bool> RunNextAsync(string connectionString, string queue, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        // Claim one job atomically. Also re-claims jobs whose worker died (lock expired).
        await using var claim = new NpgsqlCommand("""
            UPDATE jobs.jobs SET status = 'Running', attempts = attempts + 1, locked_until = now() + interval '5 minutes'
            WHERE id = (
                SELECT id FROM jobs.jobs
                WHERE queue = @queue
                  AND ((status = 'Pending' AND run_at <= now()) OR (status = 'Running' AND locked_until < now()))
                ORDER BY run_at
                FOR UPDATE SKIP LOCKED
                LIMIT 1)
            RETURNING id, kind, payload::text, attempts
            """, connection);
        claim.Parameters.AddWithValue("queue", queue);

        Guid id; string kind; string payload; int attempts;
        await using (var reader = await claim.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return false;
            (id, kind, payload, attempts) = (reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3));
        }

        try
        {
            await ExecuteEventJobAsync(payload, ct);
            await Complete(connection, id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var dead = attempts >= JobQueue.MaxAttempts;
            logger.LogWarning(ex, "Job {Kind} attempt {Attempt} failed{Dead}", kind, attempts, dead ? " - moved to DEAD LETTER" : ", will retry");
            await Fail(connection, id, ex.Message, dead, JobQueue.Backoff(attempts), ct);
        }
        return true;
    }

    private async Task ExecuteEventJobAsync(string payloadJson, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<EventJobPayload>(payloadJson)!;
        var eventType = Type.GetType(payload.EventType) ?? throw new InvalidOperationException($"Unknown event type {payload.EventType}");
        var handlerType = Type.GetType(payload.HandlerType) ?? throw new InvalidOperationException($"Unknown handler {payload.HandlerType}");
        var domainEvent = JsonSerializer.Deserialize(payload.EventJson, eventType)!;

        using var scope = services.CreateScope(); // fresh DbContexts per job
        var handler = scope.ServiceProvider.GetRequiredService(handlerType);
        var handle = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!;
        await (Task)handle.Invoke(handler, [domainEvent, ct])!;
    }

    private static async Task Complete(NpgsqlConnection c, Guid id, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("UPDATE jobs.jobs SET status='Done', completed_at=now(), locked_until=NULL WHERE id=@id", c);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task Fail(NpgsqlConnection c, Guid id, string error, bool dead, TimeSpan backoff, CancellationToken ct)
    {
        await using var cmd = new NpgsqlCommand("""
            UPDATE jobs.jobs SET status = @status, last_error = @error, locked_until = NULL, run_at = now() + @backoff
            WHERE id = @id
            """, c);
        cmd.Parameters.AddWithValue("status", dead ? "DeadLetter" : "Pending");
        cmd.Parameters.AddWithValue("error", error);
        cmd.Parameters.AddWithValue("backoff", backoff);
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
