using System.Reflection;
using Microsoft.AspNetCore.Mvc.Versioning;
using Serilog;
using Serilog.Filters;
using Wayroo.Payments.API;
using Wayroo.Payments.API.Extensions;
using Wayroo.Payments.DataAccess.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Serilog → JSON to stdout, excluding healthcheck noise.
builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .WriteTo.Console(formatter: new Serilog.Formatting.Json.JsonFormatter());

    configuration.Filter.ByExcluding(Matching.WithProperty("RequestPath", "/status"));
});

#pragma warning disable ASP0000 // BuildServiceProvider in ConfigureServices is needed to grab a logger before the host is built
var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
#pragma warning restore ASP0000

builder.Configuration.AddEnvironmentVariables();

// SSM Parameter Store is wired up for future config needs, but nothing under
// `/wayroo/api/payments/*` is required today — the deployed API gets everything it needs from
// container env vars set in the CDK PaymentsAPI construct (AwsRegion, PaymentConfigurationTableName,
// OpenTelemetry:*). Marked Optional = true so a missing/empty path doesn't crash startup;
// flip to `false` once at least one parameter is provisioned at that path.
if (!builder.Environment.IsDevelopment())
{
    builder.Configuration.AddSystemsManager(configureSource =>
    {
        configureSource.Path = "/wayroo/api/payments";
        configureSource.ReloadAfter = TimeSpan.FromMinutes(5);
        configureSource.Optional = true;
    });
}

var config = builder.Configuration;

var missingConfigurations = EnvironmentVariableKeys
    .Keys()
    .Where(configurationKey => string.IsNullOrWhiteSpace(config[configurationKey]))
    .ToList();

if (missingConfigurations.Any())
{
    logger.LogError(
        "Required configuration is missing! {@MissingConfiguration}",
        missingConfigurations
    );

    throw new Exception("Required configuration is missing!");
}

builder.Services.AddOpenTelemetryWithXRay(builder.Configuration);

builder.Services.AddPaymentsDataAccess(builder.Configuration);

builder.Services.AddApiVersioning(config =>
{
    config.DefaultApiVersion = Routes.SupportedVersions.First();
    config.AssumeDefaultVersionWhenUnspecified = true;
    config.ReportApiVersions = true;
    config.ApiVersionReader = new UrlSegmentApiVersionReader();
});
builder.Services.AddControllers();
builder.Services.AddVersionedApiExplorer(options =>
{
    options.GroupNameFormat = "'v'VVV";
    options.SubstituteApiVersionInUrl = true;
});
builder.Services.AddSwaggerGen(options =>
{
    // AppContext.BaseDirectory always carries a trailing platform separator; concatenation here is
    // intentional rather than Path.Combine, which is banned in this repo (see BannedSymbols.txt).
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments($"{AppContext.BaseDirectory}{xmlFilename}");
});

builder.Services.AddHealthChecks();

var app = builder.Build();

string[] swaggerEnvironments = ["dev", "qa"];

if (app.Environment.IsDevelopment()
    || swaggerEnvironments.Contains(app.Configuration[EnvironmentVariableKeys.Environment]))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// No UseAuthentication / UseAuthorization here: the micro trusts the calling composite
// to have enforced auth, matching the Wayroo.Notification.API convention.

app.MapControllers();
app.MapHealthChecks("/status");

app.Run();

/// <summary>
/// Exposed for WebApplicationFactory&lt;Program&gt; in integration tests.
/// </summary>
public partial class Program;
