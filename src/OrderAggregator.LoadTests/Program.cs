using System.Net.Http.Json;
using NBomber.CSharp;
using OrderAggregator.Contracts.Orders;

// Load test for POST /api/orders. Drives the "hundreds of small orders per
// second" requirement from the brief against a *running* API instance.
//
// Run:  dotnet run -c Release --project OrderAggregator.LoadTests
// Tune via env vars:
//   LOADTEST_URL       (default https://localhost:7282)
//   LOADTEST_API_KEY   (default dev-key-please-rotate-in-production-0001 — matches appsettings.json dev key)
//   LOADTEST_RPS       (default 300)  requests injected per second
//   LOADTEST_DURATION  (default 30)   seconds at that rate
//   LOADTEST_WARMUP    (default 5)    warm-up seconds
//   LOADTEST_BATCH     (default 0)    fixed items per request; 0 = random 1..5

internal class Program
{
    private static void Main(string[] args)
    {
        var baseUrl = Env("LOADTEST_URL", "https://localhost:7282");
        var apiKey = Env("LOADTEST_API_KEY", "dev-key-please-rotate-in-production-0001");
        var rps = EnvInt("LOADTEST_RPS", 300);
        var duration = TimeSpan.FromSeconds(EnvInt("LOADTEST_DURATION", 30));
        var warmup = TimeSpan.FromSeconds(EnvInt("LOADTEST_WARMUP", 5));
        var fixedBatch = EnvInt("LOADTEST_BATCH", 0);

        Console.WriteLine(baseUrl);

        // Product IDs that exist in the API's default products.json catalog — unknown
        // IDs would be rejected with 400 and skew the result toward failures. Covers the
        // full sample catalog so a large batch spreads across many aggregation keys.
        string[] knownProducts =
        [
            "456", "789", "p-001", "p-002", "p-003",
    .. Enumerable.Range(1, 95).Select(i => i.ToString()),
];

        using var httpClient = new HttpClient { BaseAddress = new Uri(baseUrl) };
        httpClient.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        var scenario = Scenario.Create("post_orders", async context =>
        {
            var batch = BuildRandomBatch(knownProducts, fixedBatch);

            var response = await httpClient.PostAsJsonAsync("/api/orders", batch, context.ScenarioCancellationToken);

            return response.IsSuccessStatusCode
                ? Response.Ok(statusCode: ((int)response.StatusCode).ToString())
                : Response.Fail(statusCode: ((int)response.StatusCode).ToString());
        })
        .WithWarmUpDuration(warmup)
        .WithLoadSimulations(
            // Inject a constant arrival rate (open workload) — models external clients
            // sending at a fixed RPS regardless of how fast the API responds, which is
            // exactly the scenario the aggregator must survive.
            Simulation.Inject(rate: rps, interval: TimeSpan.FromSeconds(1), during: duration));

        NBomberRunner
            .RegisterScenarios(scenario)
            .WithReportFileName("order-aggregator-loadtest")
            // Default report formats (HTML + CSV + MD + TXT) are written to ./reports.
            .Run();

        static OrderRequest[] BuildRandomBatch(string[] products, int fixedBatch)
        {
            var count = fixedBatch > 0 ? fixedBatch : Random.Shared.Next(1, 6); // fixed, or 1..5
            var batch = new OrderRequest[count];
            for (var i = 0; i < count; i++)
            {
                batch[i] = new OrderRequest
                {
                    ProductId = products[Random.Shared.Next(products.Length)],
                    Quantity = Random.Shared.Next(1, 50),
                };
            }
            return batch;
        }

        static string Env(string name, string fallback) =>
            Environment.GetEnvironmentVariable(name) is { Length: > 0 } v ? v : fallback;

        static int EnvInt(string name, int fallback) =>
            int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : fallback;
    }
}
