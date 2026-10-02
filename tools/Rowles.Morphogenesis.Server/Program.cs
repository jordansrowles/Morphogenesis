using Microsoft.AspNetCore.Http.Features;
using Microsoft.FluentUI.AspNetCore.Components;
using Rowles.Morphogenesis.Laboratory.Recording;
using Rowles.Morphogenesis.Server;
using Rowles.Morphogenesis.Server.Api;
using Rowles.Morphogenesis.Server.Components;
using Rowles.Morphogenesis.Server.Configuration;
using Rowles.Morphogenesis.Server.Experiments;
using Rowles.Morphogenesis.Server.Persistence;
using Rowles.Morphogenesis.Server.Sessions;
using Serilog;
using Serilog.Context;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
LaboratoryServerOptions serverOptions = LaboratoryServerOptions.Load(builder.Configuration);
builder.WebHost.UseUrls(serverOptions.BindUrl);
builder.Services.AddSingleton(serverOptions);
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

WebApplication app = builder.Build();
if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();
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
        bodySize.MaxRequestBodySize = serverOptions.MaxRequestBodyBytes;
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
app.Run();

public partial class Program;
