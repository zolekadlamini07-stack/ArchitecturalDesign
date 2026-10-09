namespace FoodDelivery.BuildingBlocks;

/// <summary>
/// VALUE OBJECT (DDD): defined only by its value. Any R45.00 is as good as any other R45.00.
/// Money always carries its currency, so we can never accidentally add rands to dollars.
/// Immutable: operations return a new Money instead of changing this one.
/// </summary>
public readonly record struct Money(decimal Amount, string Currency)
{
    public static Money Zero(string currency) => new(0m, currency);

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.Amount + b.Amount, a.Currency);
    }

    public Money Multiply(decimal factor) => new(decimal.Round(Amount * factor, 2, MidpointRounding.AwayFromZero), Currency);

    /// <summary>Card providers work in the smallest unit (cents), not decimals.</summary>
    public long ToMinorUnits() => (long)decimal.Round(Amount * 100m, 0, MidpointRounding.AwayFromZero);

    private static void EnsureSameCurrency(Money a, Money b)
    {
        if (!string.Equals(a.Currency, b.Currency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Cannot combine {a.Currency} with {b.Currency}.");
    }

    public override string ToString() => $"{Currency} {Amount:0.00}";
}
