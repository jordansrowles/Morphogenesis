using Microsoft.AspNetCore.Http.Features;
using Microsoft.FluentUI.AspNetCore.Components;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Server;
using Rowles.Morphogenesis.Server.Api;
using Rowles.Morphogenesis.Server.Components;
using Rowles.Morphogenesis.Server.Configuration;
using Rowles.Morphogenesis.Server.Diagnostics;
using Rowles.Morphogenesis.Server.Experiments;
using Rowles.Morphogenesis.Server.Persistence;
using Rowles.Morphogenesis.Server.Services;
using Rowles.Morphogenesis.Server.Sessions;
using Serilog;
using Serilog.Context;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
LaboratoryServerOptions serverOptions = LaboratoryServerOptions.Load(builder.Configuration);
builder.WebHost.UseUrls(serverOptions.BindUrl);
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = serverOptions.ResourceLimits.MaxRequestBodyBytes);
builder.Services.AddSingleton(serverOptions);
builder.Services.AddSingleton(sp => sp.GetRequiredService<LaboratoryServerOptions>().ResourceLimits);
builder.Services.AddSingleton<LaboratoryDiagnostics>();
builder.Services.AddSingleton<SessionCapacityService>();
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(serverOptions.LogDirectory, "morphogenesis-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true));
builder.Services.AddOpenApi();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddFluentUIComponents();
builder.Services.AddSingleton<ExperimentCatalog>();
builder.Services.AddSingleton<SqliteWriteQueue>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SqliteWriteQueue>());
builder.Services.AddSingleton<LaboratoryDatabase>();
builder.Services.AddSingleton<SqliteRecordingStore>();
builder.Services.AddSingleton<IRecordingStore>(sp => sp.GetRequiredService<SqliteRecordingStore>());
builder.Services.AddSingleton<SqliteRecordingReader>();
builder.Services.AddSingleton<IRecordingReader>(sp => sp.GetRequiredService<SqliteRecordingReader>());
builder.Services.AddHostedService<DatabaseStartupService>();
builder.Services.AddHostedService<ExperimentCatalogStartupService>();
builder.Services.AddSingleton<SimulationSessionRegistry>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SimulationSessionRegistry>());
builder.Services.AddSingleton<SessionRetentionService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SessionRetentionService>());
builder.Services.AddProblemDetails();

WebApplication app = builder.Build();
if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();
else
    app.UseExceptionHandler();
app.UseRouting();
app.Use(async (context, next) =>
{
    using IDisposable connection = LogContext.PushProperty("ConnectionId", context.Connection.Id);
    PathString path = context.Request.Path;
    if (path.StartsWithSegments("/api/experiments") &&
        context.Request.RouteValues.TryGetValue("id", out object? experimentRouteId))
    {
        using IDisposable experiment = LogContext.PushProperty("ExperimentId", experimentRouteId);
        await next(context).ConfigureAwait(false);
        return;
    }

    if (path.StartsWithSegments("/api/sessions") &&
        context.Request.RouteValues.TryGetValue("id", out object? sessionRouteId) &&
        Guid.TryParse(Convert.ToString(sessionRouteId, System.Globalization.CultureInfo.InvariantCulture), out Guid sessionId))
    {
        using IDisposable session = LogContext.PushProperty("SessionId", sessionId.ToString("D"));
        await next(context).ConfigureAwait(false);
        return;
    }

    await next(context).ConfigureAwait(false);
});
app.UseSerilogRequestLogging();
app.Use(async (context, next) =>
{
    IHttpMaxRequestBodySizeFeature? bodySize = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (bodySize is { IsReadOnly: false })
        bodySize.MaxRequestBodySize = serverOptions.ResourceLimits.MaxRequestBodyBytes;
    if (context.Request.Path.StartsWithSegments("/api") &&
        context.Request.ContentLength is long contentLength &&
        contentLength > serverOptions.ResourceLimits.MaxRequestBodyBytes)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return;
    }

    await next(context).ConfigureAwait(false);
});
app.UseStaticFiles();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapOpenApi();
app.MapDiagnosticsEndpoints();
app.MapExperimentEndpoints();
app.MapSessionEndpoints();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
Microsoft.Extensions.Logging.ILogger lifecycleLogger = app.Services.GetRequiredService<ILoggerFactory>()
    .CreateLogger("Rowles.Morphogenesis.Server.Lifecycle");
app.Lifetime.ApplicationStarted.Register(() => lifecycleLogger.LogInformation(
    "Morphogenesis laboratory server started at {BindUrl}", serverOptions.BindUrl));
app.Lifetime.ApplicationStopping.Register(() => lifecycleLogger.LogInformation(
    "Morphogenesis laboratory server is stopping"));
app.Run();

public partial class Program;
