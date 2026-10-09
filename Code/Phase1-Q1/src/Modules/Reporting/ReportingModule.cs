using System.Security.Claims;
using FoodDelivery.BuildingBlocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FoodDelivery.Modules.Reporting;

// ============================================================================================
// REPORTING MODULE - the one deliberate exception to "only read your own schema".
//
// Reporting owns NO data. Reporting is cross-cutting by nature ("orders AND deliveries AND revenue"),
// so it may run READ-ONLY SQL across other modules' schemas. Building separate reporting copies of
// the data for ONE restaurant would be solving a problem we don't have.
//
// Q2 NOTE: these heavy SUM/COUNT queries start slowing down order taking (problem P4). The fix is to
// point THIS module's connection string at a read replica - which is why Reporting already asks for
// its own connection string ("ConnectionStrings:Reporting") today.
// ============================================================================================
public sealed class ReportingModule : IModule
{
    public string Name => "reporting";
    public string SchemaSql => ""; // owns no tables

    public void AddServices(IServiceCollection services, IConfiguration configuration) =>
        services.AddSingleton(new ReportingDatabase(NpgsqlDataSource.Create(configuration.ConnectionStringFor(Name))));

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        OrdersAndRevenue.Map(app);
        DeliveriesCompleted.Map(app);
    }
}

/// <summary>Read-only connection used only by Reporting.</summary>
internal sealed class ReportingDatabase(NpgsqlDataSource dataSource)
{
    public NpgsqlDataSource DataSource { get; } = dataSource;
}

/// <summary>
/// The admin sees the whole platform; restaurant staff see only their own restaurant.
/// </summary>
internal static class ReportScope
{
    public static Guid? RestaurantFilter(ClaimsPrincipal user) =>
        user.IsInRole(Roles.Admin) ? null : user.RestaurantId();
}

/// <summary>SLICE: "Orders and revenue" (brief: Business - "see orders and revenue").</summary>
internal static class OrdersAndRevenue
{
    public sealed record Response(DateOnly From, DateOnly To, long Orders, long DeliveredOrders, decimal Revenue, decimal PlatformFees);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/reports/orders-revenue", Handle)
           .RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.RestaurantStaff)).WithTags("Reporting");

    private static async Task<IResult> Handle(DateOnly from, DateOnly to, ClaimsPrincipal user, ReportingDatabase db, CancellationToken ct)
    {
        // Plain SQL across the ORDERING schema - read-only. Captured revenue = orders that were accepted.
        const string sql = """
            SELECT count(*)                                                        AS orders,
                   count(*) FILTER (WHERE status = 'Delivered')                    AS delivered,
                   coalesce(sum(total)        FILTER (WHERE status NOT IN ('Placed','PaymentFailed','AwaitingAcceptance','Rejected')), 0) AS revenue,
                   coalesce(sum(platform_fee) FILTER (WHERE status NOT IN ('Placed','PaymentFailed','AwaitingAcceptance','Rejected')), 0) AS fees
            FROM ordering.orders
            WHERE created_at >= @from AND created_at < @to
              AND (@restaurant::uuid IS NULL OR restaurant_id = @restaurant)
            """;
        await using var cmd = db.DataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("from", from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        cmd.Parameters.AddWithValue("to", to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        cmd.Parameters.Add(new NpgsqlParameter("restaurant", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)ReportScope.RestaurantFilter(user) ?? DBNull.Value });

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return Results.Ok(new Response(from, to, reader.GetInt64(0), reader.GetInt64(1), reader.GetDecimal(2), reader.GetDecimal(3)));
    }
}

/// <summary>SLICE: "How many deliveries are being completed" (brief: Business).</summary>
internal static class DeliveriesCompleted
{
    public sealed record Response(DateOnly From, DateOnly To, long Completed, double? AverageMinutesFromRequestToDoor);

    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/reports/deliveries", Handle)
           .RequireAuthorization(p => p.RequireRole(Roles.Admin, Roles.RestaurantStaff)).WithTags("Reporting");

    private static async Task<IResult> Handle(DateOnly from, DateOnly to, ClaimsPrincipal user, ReportingDatabase db, CancellationToken ct)
    {
        // Plain SQL across the DELIVERY schema - read-only.
        const string sql = """
            SELECT count(*),
                   avg(extract(epoch FROM (delivered_at - requested_at)) / 60.0)
            FROM delivery.deliveries
            WHERE status = 'DELIVERED' AND delivered_at >= @from AND delivered_at < @to
              AND (@restaurant::uuid IS NULL OR restaurant_id = @restaurant)
            """;
        await using var cmd = db.DataSource.CreateCommand(sql);
        cmd.Parameters.AddWithValue("from", from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        cmd.Parameters.AddWithValue("to", to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        cmd.Parameters.Add(new NpgsqlParameter("restaurant", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)ReportScope.RestaurantFilter(user) ?? DBNull.Value });

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return Results.Ok(new Response(from, to, reader.GetInt64(0), reader.IsDBNull(1) ? null : (double)reader.GetDecimal(1)));
    }
}
