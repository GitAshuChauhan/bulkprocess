using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Worker.Configuration;
using Worker.Data;
using Worker.Data.Repositories;
using Worker.Resilience;
using Worker.Services;
using Worker.Pipeline;
using Worker.Workers;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddEnvironmentVariables();

// Options
builder.Services.Configure<DocumentProcessingOptions>(builder.Configuration.GetSection("Processing"));
builder.Services.Configure<ServiceBusOptions>(builder.Configuration.GetSection("ServiceBus"));
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));

// DbContext
var pgConn = builder.Configuration.GetConnectionString("Postgres") ?? builder.Configuration["POSTGRES_CONNECTIONSTRING"];
if (string.IsNullOrWhiteSpace(pgConn)) throw new InvalidOperationException("Postgres connection string not configured.");
builder.Services.AddDbContext<DataContext>(o => o.UseNpgsql(pgConn));

// Resilience
builder.Services.AddSingleton<ResiliencePolicyFactory>();

// Azure client factory
builder.Services.AddSingleton<IAzureClientFactory, AzureClientFactory>();

// BlobMover / Extractor / Processing pipeline
builder.Services.AddSingleton<IBlobMover, BlobMover>();
builder.Services.AddSingleton<IZipExtractor, ZipExtractor>();
builder.Services.AddScoped<IMetadataStager, MetadataStager>();
builder.Services.AddScoped<IDocumentProcessingService, DocumentProcessingService>();
builder.Services.AddScoped<ZipIngestPipeline>();

// Repositories, MFT client
builder.Services.AddScoped<IDocumentRepository, PostgresDocumentRepository>();
builder.Services.AddScoped<IMftClient, HttpMftClient>();
builder.Services.AddHttpClient();

// Service bus worker
builder.Services.AddHostedService<ServiceBusWorker>();

// OpenTelemetry minimal setup (App Insights via connection string)
var aiConn = builder.Configuration["ApplicationInsights:ConnectionString"];

builder.Services.AddOpenTelemetry()
    .WithTracing(tracerProviderBuilder =>
    {
        tracerProviderBuilder
            .AddSource("mft-zip-processor")
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName: "mft-zip-processor"))
            .AddHttpClientInstrumentation()
            .AddAzureMonitorTraceExporter(options =>
            {
                options.ConnectionString = aiConn; 
                // You can also set this using an environment variable named "APPLICATIONINSIGHTS_CONNECTION_STRING"
                // [TO: 1) Add pgclient telemetry 2) Add memory telemetry 3) Add CPU telemetry]
            });
    });

builder.Logging.AddConsole();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<DataContext>();
    ctx.Database.EnsureCreated();
}

await app.RunAsync();