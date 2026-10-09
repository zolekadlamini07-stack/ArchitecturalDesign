using System.Security.Claims;
using FoodDelivery.BuildingBlocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FoodDelivery.Modules.Reporting;

// ============================================================================================
// Q2 CHANGE 3 - SEPARATE REPORTING FROM THE TRANSACTIONAL DATABASE (problem P4, D14)
//
//  1. READ REPLICA: every report query now uses "ConnectionStrings:ReportingReplica" - a live,
//     read-only copy of the database a second or two behind. Heavy SUM/COUNT queries no longer
//     compete with customers placing orders. The code change? One connection string.
//     (Locally it points at the same database; in production it is the managed replica.)
//
//  2. DAILY SUMMARY: a nightly job (in the WORKER only) pre-computes one row per restaurant per
//     day, so dashboards read a few hundred rows instead of scanning months of orders.
//     It is written on the PRIMARY (the replica is read-only) and read from the REPLICA.
//
// Reporting now owns exactly one small table: reporting.daily_summary.
// ============================================================================================
public sealed class ReportingModule : IModule
{
    public string Name => "reporting";

    public string SchemaSql => """
        CREATE SCHEMA IF NOT EXISTS reporting;
        CREATE TABLE IF NOT EXISTS reporting.daily_summary (
            day                   date NOT NULL,
            restaurant_id         uuid NOT NULL,
            orders                bigint NOT NULL,
            delivered_orders      bigint NOT NULL,
            revenue               numeric(12,2) NOT NULL,
            platform_fees         numeric(12,2) NOT NULL,
            computed_at           timestamptz NOT NULL,
            PRIMARY KEY (day, restaurant_id)
        );
        """;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        var replica = configuration.GetConnectionString("ReportingReplica") ?? configuration.ConnectionStringFor(Name);
        services.AddSingleton(new ReportingDatabase(NpgsqlDataSource.Create(replica)));
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        OrdersAndRevenue.Map(app);
        DeliveriesCompleted.Map(app);
        DailySummary.Map(app);
    }

    /// <summary>Q2: the nightly summary runs ONLY in the Worker process.</summary>
    public void AddWorkerServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddHostedService<DailySummaryJob>();
}

/// <summary>Read-only connection to the REPLICA.</summary>
internal sealed class ReportingDatabase(NpgsqlDataSource dataSource)
{
    public NpgsqlDataSource DataSource { get; } = dataSource;
}

internal static class ReportScope
{
    public static Guid? RestaurantFilter(ClaimsPrincipal user) => user.IsInRole(Roles.Admin) ? null : user.RestaurantId();

    public static NpgsqlParameter Restaurant(ClaimsPrincipal user) =>
        new("restaurant", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)RestaurantFilter(user) ?? DBNull.Value };
}

/// <summary>SLICE: "Orders and revenue" - live numbers, read from the REPLICA.</summary>
internal static class OrdersAndRevenue
{
    public sealed record Response(DateOnly From, DateOnly To, long Orders, long DeliveredOrders, decimal Revenue, decimal PlatformFees);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/reports/orders-revenue", Handle)
           .RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.RestaurantStaff)).WithTags("Reporting");

    private static async Task<IResult> Handle(DateOnly from, DateOnly to, ClaimsPrincipal user, ReportingDatabase db, CancellationToken ct)
    {
        const string sql = """
            SELECT count(*),
                   count(*) FILTER (WHERE status = 'Delivered'),
                   coalesce(sum(total)        FILTER (WHERE status NOT IN ('Placed','PaymentFailed','AwaitingAcceptance','Rejected')), 0),
                   coalesce(sum(platform_fee) FILTER (WHERE status NOT IN ('Placed','PaymentFailed','AwaitingAcceptance','Rejected')), 0)
            FROM ordering.orders
            WHERE created_at >= @from AND created_at < @to
              AND (@restaurant::uuid IS NULL OR restaurant_id = @restaurant)
            """;
        await using var cmd = db.DataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("from", from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        cmd.Parameters.AddWithValue("to", to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        cmd.Parameters.Add(ReportScope.Restaurant(user));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return Results.Ok(new Response(from, to, r.GetInt64(0), r.GetInt64(1), r.GetDecimal(2), r.GetDecimal(3)));
    }
}

/// <summary>SLICE: "Deliveries completed" - read from the REPLICA.</summary>
internal static class DeliveriesCompleted
{
    public sealed record Response(DateOnly From, DateOnly To, long Completed, double? AverageMinutesFromRequestToDoor);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/reports/deliveries", Handle)
           .RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.RestaurantStaff)).WithTags("Reporting");

    private static async Task<IResult> Handle(DateOnly from, DateOnly to, ClaimsPrincipal user, ReportingDatabase db, CancellationToken ct)
    {
        const string sql = """
            SELECT count(*), avg(extract(epoch FROM (delivered_at - requested_at)) / 60.0)
            FROM delivery.deliveries
            WHERE status = 'DELIVERED' AND delivered_at >= @from AND delivered_at < @to
              AND (@restaurant::uuid IS NULL OR restaurant_id = @restaurant)
            """;
        await using var cmd = db.DataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("from", from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        cmd.Parameters.AddWithValue("to", to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        cmd.Parameters.Add(ReportScope.Restaurant(user));
        await using var r = await cmd.ExecuteReaderAsync(ct);
        await r.ReadAsync(ct);
        return Results.Ok(new Response(from, to, r.GetInt64(0), r.IsDBNull(1) ? null : (double)r.GetDecimal(1)));
    }
}

/// <summary>Q2 SLICE: "Daily summary" - reads the small pre-computed table (cheap, even over months).</summary>
internal static class DailySummary
{
    public sealed record Row(DateOnly Day, Guid RestaurantId, long Orders, long DeliveredOrders, decimal Revenue, decimal PlatformFees);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/reports/daily-summary", Handle)
           .RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.RestaurantStaff)).WithTags("Reporting");

    private static async Task<IResult> Handle(DateOnly from, DateOnly to, ClaimsPrincipal user, ReportingDatabase db, CancellationToken ct)
    {
        await using var cmd = db.DataSource.CreateCommand("""
            SELECT day, restaurant_id, orders, delivered_orders, revenue, platform_fees
            FROM reporting.daily_summary
            WHERE day BETWEEN @from AND @to AND (@restaurant::uuid IS NULL OR restaurant_id = @restaurant)
            ORDER BY day, restaurant_id
            """);
        cmd.Parameters.AddWithValue("from", from);
        cmd.Parameters.AddWithValue("to", to);
        cmd.Parameters.Add(ReportScope.Restaurant(user));
        var rows = new List<Row>();
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
            rows.Add(new Row(r.GetFieldValue<DateOnly>(0), r.GetGuid(1), r.GetInt64(2), r.GetInt64(3), r.GetDecimal(4), r.GetDecimal(5)));
        return Results.Ok(rows);
    }
}

/// <summary>
/// WORKER-ONLY JOB: recompute yesterday's and today's summary rows. Runs at start-up and then every
/// hour here (in production: once a night at 03:00, when load is lowest). Writes to the PRIMARY.
/// Upsert = safe to run any number of times.
/// </summary>
internal sealed class DailySummaryJob(IConfiguration configuration, ILogger<DailySummaryJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await using var primary = NpgsqlDataSource.Create(configuration.ConnectionStringFor("Default"));
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await using var cmd = primary.CreateCommand("""
                    INSERT INTO reporting.daily_summary (day, restaurant_id, orders, delivered_orders, revenue, platform_fees, computed_at)
                    SELECT created_at::date, restaurant_id, count(*),
                           count(*) FILTER (WHERE status = 'Delivered'),
                           coalesce(sum(total)        FILTER (WHERE status NOT IN ('Placed','PaymentFailed','AwaitingAcceptance','Rejected')), 0),
                           coalesce(sum(platform_fee) FILTER (WHERE status NOT IN ('Placed','PaymentFailed','AwaitingAcceptance','Rejected')), 0),
                           now()
                    FROM ordering.orders
                    WHERE created_at >= current_date - 1
                    GROUP BY created_at::date, restaurant_id
                    ON CONFLICT (day, restaurant_id) DO UPDATE SET
                        orders = excluded.orders, delivered_orders = excluded.delivered_orders,
                        revenue = excluded.revenue, platform_fees = excluded.platform_fees, computed_at = excluded.computed_at
                    """);
                var rows = await cmd.ExecuteNonQueryAsync(stop);
                logger.LogInformation("Daily summary refreshed ({Rows} rows).", rows);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Daily summary failed; will try again next run.");
            }
            await Task.Delay(TimeSpan.FromHours(1), stop);
        }
    }
}
