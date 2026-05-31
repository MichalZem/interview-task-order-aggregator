namespace OrderAggregator.Api.DependencyInjection;

public static class CorsServiceCollectionExtensions
{
    public static string AddAppCors(
        this IServiceCollection services,
        string CorsPolicyName,
        IConfiguration configuration, 
        IWebHostEnvironment environment)
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicyName, policy =>
            {
                if (allowedOrigins.Length > 0)
                {
                    policy.WithOrigins(allowedOrigins);
                }
                else if (environment.IsDevelopment())
                {
                    policy.SetIsOriginAllowed(_ => true);
                }
                else
                {
                    policy.WithOrigins(); // explicit empty — production must opt in
                }

                policy.AllowAnyHeader().AllowAnyMethod();
            });
        });
        return CorsPolicyName;
    }
}
