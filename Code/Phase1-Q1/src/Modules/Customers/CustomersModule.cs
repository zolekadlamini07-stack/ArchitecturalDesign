using FoodDelivery.BuildingBlocks;
using FoodDelivery.Modules.Customers.Data;
using FoodDelivery.Modules.Customers.PublicApi;
using FoodDelivery.Modules.Identity.PublicApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FoodDelivery.Modules.Customers;

public sealed class CustomersModule : IModule
{
    public string Name => "customers";
    public string SchemaSql => Persistence.ReadEmbeddedSql(typeof(CustomersModule).Assembly, "schema.sql");

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<CustomersDbContext>(o => o
            .UseNpgsql(configuration.ConnectionStringFor(Name))
            .UseSnakeCaseNamingConvention());

        // The PUBLIC interface is registered with an INTERNAL implementation.
        // Other modules get ICustomersApi from dependency injection; they can never see CustomersApi.
        services.AddScoped<ICustomersApi, CustomersApi>();

        // Subscribe to Identity's event.
        services.AddScoped<IDomainEventHandler<CustomerRegistered>, Features.Profile.CreateProfileWhenCustomerRegisters>();
    }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        Features.Profile.GetMyProfile.Map(app);
        Features.Profile.UpdateMyProfile.Map(app);
        Features.Addresses.AddAddress.Map(app);
        Features.Addresses.ListAddresses.Map(app);
    }
}

/// <summary>Internal implementation of the public API: translates our table rows into the public record.</summary>
internal sealed class CustomersApi(CustomersDbContext db) : ICustomersApi
{
    public async Task<DeliveryAddress?> GetDeliveryAddressAsync(Guid customerId, Guid addressId, CancellationToken ct) =>
        await db.Addresses.AsNoTracking()
            .Where(a => a.Id == addressId && a.CustomerId == customerId)
            .Select(a => new DeliveryAddress(a.Id, a.Line1, a.City, a.PostalCode))
            .SingleOrDefaultAsync(ct);
}
