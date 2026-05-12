using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MEL.Spectre;
using MEL.Spectre.Samples.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSpectreConsole();
builder.Services.AddHostedService<HeartbeatWorker>();

await builder.Build().RunAsync();
