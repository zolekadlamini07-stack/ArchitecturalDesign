using FoodDelivery.Modules.Payments.Data;
using FoodDelivery.Modules.Payments.Provider;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FoodDelivery.Modules.Payments.Features.Reconciliation;

/// <summary>
/// Q3 - RECONCILIATION LAYER 3: DAILY SETTLEMENT CHECK ("compare our receipts with the bank statement").
///
/// Fetches the provider's report of everything it did, and compares it line by line with our
/// ledger. Anything that doesn't match goes to payments.settlement_exceptions for a human.
/// It catches bugs in the other two layers - the net under the net. Finance needs it anyway.
/// Runs hourly here for the demo; in production once a day after the provider publishes the report.
/// </summary>
internal sealed class DailySettlementCheck(IServiceProvider services, ILogger<DailySettlementCheck> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
                var provider = scope.ServiceProvider.GetRequiredService<IPaymentProvider>();
                var today = DateOnly.FromDateTime(DateTime.UtcNow);
                foreach (var day in new[] { today.AddDays(-1), today })
                    await CompareAsync(db, provider, day, stop);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Settlement check failed; will retry."); }
            await Task.Delay(TimeSpan.FromHours(1), stop);
        }
    }

    private async Task CompareAsync(PaymentsDbContext db, IPaymentProvider provider, DateOnly day, CancellationToken ct)
    {
        var report = await provider.GetSettlementReportAsync(day, ct);
        var problems = 0;
        foreach (var line in report)
        {
            var attempt = Guid.TryParse(line.Reference, out var id) ? await db.Attempts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id, ct) : null;
            var problem = (line.Status, attempt?.Status) switch
            {
                (_, null) => "provider has a payment we have NO attempt for",
                (ProviderStatus.Captured, not AttemptStatus.Captured) => "provider CAPTURED money we don't show as captured",
                (ProviderStatus.Authorised, AttemptStatus.Voided) => "we think the hold was released but provider still holds it",
                _ => null,
            };
            if (problem is null) continue;
            if (await db.SettlementExceptions.AnyAsync(x => x.Reference == line.Reference && x.Problem == problem, ct)) continue;
            db.SettlementExceptions.Add(new SettlementException { AttemptId = attempt?.Id, Reference = line.Reference, Problem = problem });
            problems++;
        }
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Settlement check {Day}: {Lines} provider lines, {Problems} new exception(s).", day, report.Count, problems);
    }
}
