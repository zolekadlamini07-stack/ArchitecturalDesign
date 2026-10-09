using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDelivery.BuildingBlocks;

/// <summary>
/// The contract every module implements so a host can plug it in.
///
/// Q2 CHANGE: there are now TWO hosts built from the SAME modules (D13):
///   - FoodDelivery.Api    : serves HTTP. Calls AddServices + MapEndpoints.
///   - FoodDelivery.Worker : runs background jobs. Calls AddServices + AddWorkerServices.
/// Same code, same release, two start commands. The worker is NOT a separate service.
/// </summary>
public interface IModule
{
    string Name { get; }

    /// <summary>Services both hosts need: DbContext, public API implementation, event handlers.</summary>
    void AddServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>HTTP endpoints, one per vertical slice. Only the Api host calls this.</summary>
    void MapEndpoints(IEndpointRouteBuilder app);

    /// <summary>
    /// Q2: background work that must ONLY run in the Worker process (e.g. Reporting's nightly
    /// summary). Most modules have none - their event handlers are run by the shared JobRunner.
    /// </summary>
    void AddWorkerServices(IServiceCollection services, IConfiguration configuration) { }

    string SchemaSql { get; }
}
