using Continuum.Core.Data;
using Continuum.Host.Api;
using Continuum.Host.Auth;
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
builder.Services.AddScoped<RagService>();
builder.Services.AddHostedService<ExtractionWorker>();

builder.Services.AddScoped<IngestService>();
builder.Services.AddScoped<HistoryService>();
builder.Services.AddScoped<ResumeService>();
builder.Services.AddScoped<MemoryService>();
builder.Services.AddScoped<CheckpointService>();
builder.Services.AddScoped<HookContextService>();
builder.Services.AddSingleton<BusBroadcaster>();
builder.Services.AddScoped<BusService>();
builder.Services.AddScoped<RoomService>();
builder.Services.AddScoped<NotificationsService>();

builder.Services.Configure<MaintenanceOptions>(builder.Configuration.GetSection("Maintenance"));
builder.Services.AddScoped<MemoryMaintenanceService>();
builder.Services.AddScoped<RetentionService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<TokenAnalyticsService>();
builder.Services.AddScoped<RedactionReviewService>();
builder.Services.AddHostedService<MaintenanceWorker>();

// Ops: daily digest + backup reporting (Phase 6).
builder.Services.Configure<DigestOptions>(builder.Configuration.GetSection("Digest"));
builder.Services.AddScoped<DigestService>();
builder.Services.AddHostedService<DigestWorker>();
var backupOptions = new BackupOptions();
builder.Configuration.GetSection("Backups").Bind(backupOptions);
builder.Services.AddSingleton(backupOptions);
builder.Services.AddSingleton<BackupService>();

// Auth + accounts (Phase 7). Legacy shared token stays valid until AllowLegacyToken is turned off.
var authOptions = new AuthOptions();
builder.Configuration.GetSection("Auth").Bind(authOptions);
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton<Continuum.Host.Auth.TokenSigner>();
builder.Services.AddScoped<Continuum.Host.Auth.CurrentUserAccessor>();
builder.Services.AddScoped<Continuum.Host.Auth.ICurrentUser>(sp => sp.GetRequiredService<Continuum.Host.Auth.CurrentUserAccessor>());
builder.Services.AddScoped<Continuum.Host.Auth.AuthFilter>();
builder.Services.AddScoped<AuthService>();

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

    // Bootstrap the admin account (id = DefaultOwnerId, so it owns all pre-accounts data).
    // Idempotent: only creates it when no user with that id exists yet.
    var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
    if (await auth.FindByIdAsync(Continuum.Core.Domain.Defaults.DefaultOwnerId, default) is null)
    {
        var email = builder.Configuration["Auth:AdminEmail"];
        var password = builder.Configuration["Auth:AdminPassword"];
        if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password))
        {
            var admin = new Continuum.Core.Domain.User
            {
                Id = Continuum.Core.Domain.Defaults.DefaultOwnerId,
                Email = email.Trim().ToLowerInvariant(),
                DisplayName = builder.Configuration["Auth:AdminName"] ?? "Admin",
                PasswordHash = Continuum.Host.Auth.PasswordHasher.Hash(password),
                Role = Continuum.Core.Domain.UserRole.Admin,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Users.Add(admin);
            await db.SaveChangesAsync();
            app.Logger.LogInformation("Bootstrapped admin account {Email}.", admin.Email);
        }
        else
        {
            app.Logger.LogWarning("No admin account and Auth:AdminEmail/AdminPassword not set — set them to enable login.");
        }
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapContinuumApi(authOptions);
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
