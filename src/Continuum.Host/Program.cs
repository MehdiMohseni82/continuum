using Continuum.Core.Data;
using Continuum.Host.Api;
using Continuum.Host.Components;
using Continuum.Host.Services;
using Continuum.Core.Embeddings;
using Continuum.Core.Generation;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Port=5432;Database=continuum;Username=continuum;Password=continuum";

builder.Services.AddDbContext<ContinuumDbContext>(o =>
    o.UseNpgsql(connectionString, npg => npg.UseVector()));

// Embeddings. Default is self-hosted Ollama (open-source model, nothing leaves the network).
// "local" is a no-dependency fallback; "openai-compatible" is an opt-in external service.
var embeddingOptions = new EmbeddingOptions();
builder.Configuration.GetSection("Embeddings").Bind(embeddingOptions);
builder.Services.AddSingleton(embeddingOptions);
switch (embeddingOptions.Provider.ToLowerInvariant())
{
    case "ollama":
        builder.Services.AddHttpClient<IEmbedder, OllamaEmbedder>(h => h.Timeout = TimeSpan.FromMinutes(2));
        break;
    case "openai-compatible" when !string.IsNullOrWhiteSpace(embeddingOptions.ApiKey):
        builder.Services.AddHttpClient<IEmbedder, OpenAiCompatibleEmbedder>();
        break;
    default:
        builder.Services.AddSingleton<IEmbedder, LocalHashEmbedder>();
        break;
}

// Generation (self-hosted LLM via Ollama) — powers auto-memory extraction (and RAG later).
var genOptions = new GenerationOptions();
builder.Configuration.GetSection("Generation").Bind(genOptions);
builder.Services.AddSingleton(genOptions);
builder.Services.AddHttpClient<IChatCompleter, OllamaChatCompleter>(h => h.Timeout = TimeSpan.FromMinutes(5));

builder.Services.Configure<ExtractionOptions>(builder.Configuration.GetSection("Extraction"));
builder.Services.AddScoped<MemoryExtractionService>();
builder.Services.AddHostedService<ExtractionWorker>();

builder.Services.AddScoped<IngestService>();
builder.Services.AddScoped<HistoryService>();
builder.Services.AddScoped<ResumeService>();
builder.Services.AddScoped<MemoryService>();
builder.Services.AddScoped<CheckpointService>();
builder.Services.AddScoped<HookContextService>();
builder.Services.AddSingleton<BusBroadcaster>();
builder.Services.AddScoped<BusService>();
builder.Services.AddScoped<NotificationsService>();

builder.Services.Configure<MaintenanceOptions>(builder.Configuration.GetSection("Maintenance"));
builder.Services.AddScoped<MemoryMaintenanceService>();
builder.Services.AddScoped<RetentionService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<RedactionReviewService>();
builder.Services.AddHostedService<MaintenanceWorker>();

// Accept + emit enums as strings (e.g. "Project") in the JSON API.
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Apply migrations at startup so a fresh container is ready to go.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ContinuumDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapContinuumApi();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
