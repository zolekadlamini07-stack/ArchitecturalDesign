using System.Reflection;
using FoodDelivery.BuildingBlocks;

namespace FoodDelivery.Tests;

// =============================================================================================
// ARCHITECTURE TESTS (D3) - "a smoke alarm for messy code".
// They run in the build, so a boundary violation fails BEFORE it reaches code review.
// This is the mitigation for Q1 Risk 1 (module boundaries erode over time).
// =============================================================================================
public class ArchitectureTests
{
    private static readonly Assembly[] ModuleAssemblies =
    [
        typeof(Modules.Identity.IdentityModule).Assembly,
        typeof(Modules.Customers.CustomersModule).Assembly,
        typeof(Modules.Restaurants.RestaurantsModule).Assembly,
        typeof(Modules.Ordering.OrderingModule).Assembly,
        typeof(Modules.Payments.PaymentsModule).Assembly,
        typeof(Modules.Delivery.DeliveryModule).Assembly,
        typeof(Modules.Notifications.NotificationsModule).Assembly,
        typeof(Modules.Reporting.ReportingModule).Assembly,
    ];

    /// <summary>
    /// RULE 1: a module's only public types are its PublicApi folder and its Module class.
    /// If someone makes an internal class public "just to reuse it", this test fails.
    /// </summary>
    [Fact]
    public void Modules_expose_only_their_public_api()
    {
        var leaks = ModuleAssemblies
            .SelectMany(a => a.GetExportedTypes())
            .Where(t => !(t.Namespace?.EndsWith(".PublicApi") ?? false))
            .Where(t => !typeof(IModule).IsAssignableFrom(t))
            .Select(t => t.FullName)
            .ToList();

        Assert.True(leaks.Count == 0, "These types should be internal: " + string.Join(", ", leaks));
    }

    /// <summary>
    /// RULE 2: modules may only depend on the modules we agreed. Notably:
    ///   - nobody depends on Ordering (so there are no cycles; Ordering hears back via events)
    ///   - Reporting depends on no module at all (it reads the database, read-only)
    /// </summary>
    [Fact]
    public void Module_dependencies_match_the_agreed_design()
    {
        var allowed = new Dictionary<string, string[]>
        {
            ["Identity"] = [],
            ["Customers"] = ["Identity"],                 // subscribes to CustomerRegistered
            ["Restaurants"] = [],
            ["Payments"] = [],
            ["Notifications"] = [],
            ["Delivery"] = ["Identity"],                  // subscribes to DriverAccountCreated
            ["Ordering"] = ["Customers", "Restaurants", "Payments", "Delivery", "Notifications"],
            ["Reporting"] = [],
        };

        foreach (var assembly in ModuleAssemblies)
        {
            var name = ModuleName(assembly.GetName().Name!);
            var actual = assembly.GetReferencedAssemblies()
                .Select(r => r.Name!)
                .Where(n => n.StartsWith("FoodDelivery.Modules."))
                .Select(ModuleName)
                .ToList();

            var forbidden = actual.Except(allowed[name]).ToList();
            Assert.True(forbidden.Count == 0, $"{name} must not depend on: {string.Join(", ", forbidden)}");
        }
    }

    private static string ModuleName(string assemblyName) => assemblyName.Replace("FoodDelivery.Modules.", "");
}
