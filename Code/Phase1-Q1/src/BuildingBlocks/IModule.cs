using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDelivery.BuildingBlocks;

/// <summary>
/// The contract every module implements so the host can plug it in.
///
/// MODULAR MONOLITH (D1): there is ONE deployable (the Api project), but it is assembled from
/// eight self-contained modules. Each module registers its own services, its own HTTP endpoints
/// (one per vertical slice) and its own database schema. The host never reaches inside a module;
/// it only calls these three members.
/// </summary>
public interface IModule
{
    /// <summary>Module name, also used as its PostgreSQL schema name (D4: one schema per module).</summary>
    string Name { get; }

    /// <summary>Register the module's internal services, DbContext and public API implementation.</summary>
    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Map one HTTP endpoint per vertical slice (D2).</summary>
    void MapEndpoints(IEndpointRouteBuilder app);

    /// <summary>
    /// The SQL that creates this module's schema and tables (IF NOT EXISTS, so it is safe on every start).
    /// Lives in the module's Data/schema.sql file. Only this module ever writes to these tables.
    /// </summary>
    string SchemaSql { get; }
}
