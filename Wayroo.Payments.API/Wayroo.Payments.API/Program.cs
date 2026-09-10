using System.Reflection;
using Microsoft.AspNetCore.Mvc.Versioning;
using Serilog;
using Serilog.Events;
using Wayroo.Payments.API;
using Wayroo.Payments.API.Extensions;
using Wayroo.Payments.API.Filters;
using Wayroo.Payments.API.Logging;
using Wayroo.Payments.BusinessLogic.Extensions;
using Wayroo.Payments.DataAccess.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Serilog → JSON to stdout. The JSON shape matters beyond readability: the CloudWatch metric filters
// in the PaymentsAPI construct match on `$.Properties.<name>`, which is where this formatter puts a
// message template property.
builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .WriteTo.Console(formatter: new Serilog.Formatting.Json.JsonFormatter())
        // Configuring Serilog in code means Serilog never reads the `Logging:LogLevel` section in
        // appsettings.json (that section governs only the pre-host logger built below, for the
        // missing-configuration check). So ASP.NET Core's own per-request chatter — "Request
        // starting", "Executing endpoint", "Request finished", several lines per call at Information
        // — has to be turned down here instead. UseSerilogRequestLogging below replaces all of it
        // with one line per request, which is also the line the probe filter can then drop.
        .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning);
});

#pragma warning disable ASP0000 // BuildServiceProvider in ConfigureServices is needed to grab a logger before the host is built
var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
#pragma warning restore ASP0000

builder.Configuration.AddEnvironmentVariables();

// Nothing is provisioned under `/wayroo/api/payments/*` today — the deployed API gets what it needs
// from container env vars set in the CDK PaymentsAPI construct (AwsRegion,
// PaymentConfigurationTableName, PropayApiBaseUrisOptions:*, OpenTelemetry:*). Optional = true so a
// missing/empty path doesn't crash startup; flip to `false` once something is provisioned there.
// Skipped locally: there is nothing to read, and it would cost an AWS round trip on every start.
if (!builder.Environment.IsDevelopment())
{
    builder.Configuration.AddSystemsManager(configureSource =>
    {
        configureSource.Path = "/wayroo/api/payments";
        configureSource.ReloadAfter = TimeSpan.FromMinutes(5);
        configureSource.Optional = true;
    });
}

// The per-tenant ProPay credentials, by contrast, exist ONLY in Parameter Store — there is nowhere
// else they could live, since they must never be committed. So this source loads in every environment
// **including Development**, where a developer reads them with their own AWS identity (which is what
// the AWSSDK.SSO/SSOOIDC references in the csproj make possible).
//
// Read from the shared vendor path rather than a copy under /wayroo/api/payments: these are the same
// secrets Luci.Orders uses, and duplicating a credential is how two services end up on different
// halves of a rotation. A tenant's optional per-tenant base-URL override arrives here too, and wins
// over the environment-wide PropayApiBaseUrisOptions defaults for that tenant.
var propaySecretsPath = builder.Configuration[EnvironmentVariableKeys.PropaySecretsPath];
if (!string.IsNullOrWhiteSpace(propaySecretsPath))
{
    builder.Configuration.AddSystemsManager(configureSource =>
    {
        configureSource.Path = propaySecretsPath;
        configureSource.ReloadAfter = TimeSpan.FromMinutes(5);
        // Optional locally, so a developer with no AWS credentials can still run the endpoints that
        // never touch a provider. Required everywhere else, where an unreadable path means a broken
        // deploy and failing at startup beats failing on a merchant's first request.
        configureSource.Optional = builder.Environment.IsDevelopment();
    });
}

// A blank path is left to the required-configuration check below, which names every missing key at
// once instead of dying inside the configuration provider on the first one.

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

builder.Services.AddPaymentsBusinessLogic(builder.Configuration);

builder.Services.AddApiVersioning(config =>
{
    config.DefaultApiVersion = Routes.SupportedVersions.First();
    config.AssumeDefaultVersionWhenUnspecified = true;
    config.ReportApiVersions = true;
    config.ApiVersionReader = new UrlSegmentApiVersionReader();
});
builder.Services.AddControllers(options =>
{
    // A provider refusing a well-formed request is a 400 carrying the provider's own words, not a 500.
    options.Filters.Add<PaymentProviderExceptionFilter>();
});
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

// One summary line per request, in place of the several ASP.NET Core emits (turned down to Warning
// above) — and the line the health/status probes are then dropped from, by RequestLogLevels.
// Registered first so it wraps everything below it and still reports a request that a later piece of
// middleware short-circuits.
app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, _, exception) => RequestLogLevels.For(
        httpContext.Request.Path,
        httpContext.Response.StatusCode,
        exception);
});

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
app.MapHealthChecks(Routes.StatusRoute);

app.Run();

/// <summary>
/// Exposed for WebApplicationFactory&lt;Program&gt; in integration tests.
/// </summary>
public partial class Program;
