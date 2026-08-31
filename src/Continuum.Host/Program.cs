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

// Server-side room agent (optional; Claude API). Off by default; needs a key (config or ANTHROPIC_API_KEY).
// This is a separate, external completer — the global IChatCompleter above stays on self-hosted Ollama.
var serverAgentOptions = new ServerAgentOptions();
builder.Configuration.GetSection("ServerAgents").Bind(serverAgentOptions);
builder.Services.AddSingleton(serverAgentOptions);
if (serverAgentOptions.HasKey())
{
    builder.Services.AddSingleton(new AnthropicChatCompleter(
        serverAgentOptions.ResolveApiKey()!, serverAgentOptions.Model, serverAgentOptions.MaxTokens));
    builder.Services.AddScoped<ServerAgentDriver>();
    if (serverAgentOptions.Enabled)
        builder.Services.AddHostedService<ServerAgentWorker>();
}

// Room drafting picks the Claude completer when one is registered above, else the self-hosted one.
// Registered unconditionally so the feature exists on every deployment; only its quality varies.
builder.Services.AddScoped<RoomDraftCompleter>(sp =>
    new RoomDraftCompleter(sp.GetService<AnthropicChatCompleter>()));
builder.Services.AddScoped<RoomDraftService>();
// Singleton: it owns the in-flight job table, which must outlive the request that queued a draft.
builder.Services.AddSingleton<RoomDraftJobs>();

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
builder.Services.AddScoped<SharingService>();
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
// The single source of truth for data visibility. Every service asks this instead of restating the rule.
builder.Services.AddScoped<Continuum.Core.Access.IAccessPrincipal>(sp => sp.GetRequiredService<Continuum.Host.Auth.CurrentUserAccessor>());
builder.Services.AddScoped<Continuum.Core.Access.IGrantSource, Continuum.Core.Access.DbGrantSource>();
builder.Services.AddScoped<Continuum.Core.Access.IAccessPolicy, Continuum.Core.Access.AccessPolicy>();
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

    // Bootstrap the organization that holds all pre-tenancy data. The migration backfills existing
    // rows to this id, so it must exist before anything reads them. Idempotent.
    if (!await db.Organizations.AnyAsync(o => o.Id == Continuum.Core.Domain.Defaults.DefaultOrgId))
    {
        db.Organizations.Add(new Continuum.Core.Domain.Organization
        {
            Id = Continuum.Core.Domain.Defaults.DefaultOrgId,
            Name = builder.Configuration["Auth:DefaultOrgName"] ?? "Default",
            Slug = "default",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        app.Logger.LogInformation("Bootstrapped the default organization.");
    }

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

    // Enrol anyone not yet in an organization. Without a membership the access policy shows a user
    // nothing, so this runs every start and covers both the upgrade and any account created before
    // organizations existed. Idempotent.
    var unenrolled = await db.Users
        .Where(u => !db.OrgMemberships.Any(m => m.UserId == u.Id))
        .Select(u => new { u.Id, u.Role })
        .ToListAsync();

    if (unenrolled.Count > 0)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var u in unenrolled)
        {
            db.OrgMemberships.Add(new Continuum.Core.Domain.OrgMembership
            {
                Id = Guid.NewGuid(),
                OrgId = Continuum.Core.Domain.Defaults.DefaultOrgId,
                UserId = u.Id,
                // The bootstrap admin founded this organization; instance admins administer it.
                Role = u.Id == Continuum.Core.Domain.Defaults.DefaultOwnerId ? Continuum.Core.Domain.OrgRole.Owner
                     : u.Role == Continuum.Core.Domain.UserRole.Admin ? Continuum.Core.Domain.OrgRole.Admin
                     : Continuum.Core.Domain.OrgRole.Member,
                JoinedAt = now,
            });
        }
        await db.SaveChangesAsync();
        app.Logger.LogInformation("Enrolled {Count} user(s) into the default organization.", unenrolled.Count);
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
