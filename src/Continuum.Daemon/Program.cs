using Continuum.Daemon;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<DaemonOptions>(builder.Configuration.GetSection("Daemon"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<DaemonOptions>>().Value);
builder.Services.AddSingleton<CursorStore>();
builder.Services.AddHttpClient<BackendClient>((sp, http) =>
    BackendClient.Configure(http, sp.GetRequiredService<DaemonOptions>()));
builder.Services.AddHostedService<TailWorker>();

var host = builder.Build();
host.Run();
