using MigrateSQLServerDatabases.Components;
using MigrateSQLServerDatabases.Localization;
using MigrateSQLServerDatabases.Services;
using MigrateSQLServerDatabases.Desktop;
using MigrateSQLServerDatabases.Services.Manager;

// The published app opens in its own window; `dotnet run` (Development) keeps the browser workflow.
// --desktop forces the window, --server forces a plain web server.
var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");
var isDevelopment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);
var desktop = !args.Contains("--server") && (args.Contains("--desktop") || !isDevelopment);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args.Where(a => a is not ("--desktop" or "--server")).ToArray(),
    // Started from a shortcut or Explorer, the working folder may not be the app folder
    ContentRootPath = desktop && !isDevelopment ? AppContext.BaseDirectory : null
});

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    // Query scripts and opened .sql files are sent from the browser; the 32 KB default is too small
    .AddHubOptions(options => options.MaximumReceiveMessageSize = 32 * 1024 * 1024);

builder.Services.AddSingleton<SqlServerService>();
builder.Services.AddScoped<MigrationSession>();
builder.Services.AddScoped<Localizer>();
builder.Services.AddScoped<TableCopyJob>();

// Database manager
builder.Services.AddSingleton<ExplorerService>();
builder.Services.AddSingleton<SchemaService>();
builder.Services.AddSingleton<TableDataService>();
builder.Services.AddSingleton<AdminService>();
builder.Services.AddSingleton<ResultEditService>();
builder.Services.AddScoped<ManagerSession>();
builder.Services.AddScoped<ManagerActions>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
if (!desktop)
    app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (!desktop)
{
    app.Run();
    return;
}

// Desktop: a free port on this machine only, then the window; closing it stops the server
try
{
    app.Urls.Clear();
    app.Urls.Add("http://127.0.0.1:0");
    await app.StartAsync();
    DesktopWindow.Run(app.Urls.First().TrimEnd('/') + "/manager");
}
catch (Exception ex)
{
    DesktopWindow.ShowError("SQL Server Manager could not start:\n\n" + ex.Message);
}
finally
{
    await app.StopAsync();
}
