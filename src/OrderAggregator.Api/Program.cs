using OrderAggregator.Api.Authentication;
using OrderAggregator.Api.DependencyInjection;
using OrderAggregator.Api.Endpoints;
using OrderAggregator.Api.Health;
using OrderAggregator.Api.OpenApi;
using OrderAggregator.Api.Serialization;

var builder = WebApplication.CreateBuilder(args);

#region Services registration

// Put source-generated JSON metadata in front of the reflection resolver: the
// contract types (de)serialize via compile-time code on the hot path, everything
// else (ProblemDetails, …) still falls back to reflection later in the chain.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default));

builder.Services.AddAppOpenApi();
builder.Services.AddCommonServices();
builder.Services.AddAppObservability(builder.Configuration);
builder.Services.AddAppHealthChecks();

builder.Services.AddApiKeyAuthentication(builder.Configuration);
builder.Services.AddContractMapping();
builder.Services.AddAppLocalization();

builder.Services.AddOrderAggregation(builder.Configuration);
builder.Services.AddProductCatalog(builder.Configuration);
builder.Services.AddRequestLimits(builder.Configuration);

// Give the background flush loop room to drain + send its final batch on
// shutdown (including send retries) before the host force-stops it.
builder.Services.Configure<HostOptions>(options =>
    options.ShutdownTimeout = TimeSpan.FromSeconds(30));

const string CorsPolicyName = "BrowserClients";
builder.Services.AddAppCors(CorsPolicyName, builder.Configuration, builder.Environment);

builder.Services.AddProblemDetails();

// HSTS max-age for production responses (dev is skipped — see pipeline below).
builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));

#endregion

#region Middleware pipeline
var app = builder.Build();

app.EnsureProductCatalogLoaded();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsEnvironment("Testing"))
{
    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }
    app.UseHttpsRedirection();
}

app.UseRequestLocalization();

app.UseCors(CorsPolicyName);

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();

app.MapHealthChecks();

app.MapApiEndpoints();

#endregion

app.Run();


// Expose Program for WebApplicationFactory<Program> in the integration tests.
public partial class Program;

