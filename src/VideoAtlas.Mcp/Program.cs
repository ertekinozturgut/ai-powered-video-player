using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using VideoAtlas.Mcp;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(PathPolicy.FromEnvironment(TimeProvider.System));
builder.Services.AddSingleton<ExternalProcessRunner>();
builder.Services.AddSingleton<TopicSegmenter>();
builder.Services.AddSingleton<AudioAnalysisService>();
builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();
await builder.Build().RunAsync();
