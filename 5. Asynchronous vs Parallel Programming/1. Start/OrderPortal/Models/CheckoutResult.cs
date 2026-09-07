namespace OrderPortal;

public record CheckoutResult(int OrdersPlaced, decimal Revenue, int TaxRateLookups, int TaxRateBuilds, TimeSpan Elapsed);