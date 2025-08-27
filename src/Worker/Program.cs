using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Worker.Abstractions;
using Worker.Configuration;
using Worker.Data;
using Worker.Data.Repositories;
using Worker.Infrastructure;
using Worker.Mft;
using Worker.Services;
using Worker.Telemetry;
using Worker.Workers;
using Azure.Monitor.OpenTelemetry.Exporter;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddLogging(o => o.AddConsole());

builder.Services.AddDbContext<DataContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.AddSingleton(_ => new BlobServiceClient(builder.Configuration["Storage:Account"]));
builder.Services.AddSingleton(_ => new ServiceBusClient(builder.Configuration["ServiceBus:ConnectionString"]));
builder.Services.AddSingleton<ResiliencePolicyFactory>();

// DI registrations
builder.Services.AddScoped<IMftClient, MftSftpClient>();
builder.Services.AddScoped<IZipUploader, ZipUploader>();
builder.Services.AddScoped<IZipExtractor, ZipExtractor>();
builder.Services.AddScoped<IMetadataStager, MetadataStager>();
builder.Services.AddScoped<IMetadataJobProcessor, MetadataJobProcessor>();
builder.Services.AddScoped<IDocumentRepository, PostgresDocumentRepository>();
builder.Services.AddScoped<IJobLogger, JobLogger>();

// Hosted worker
builder.Services.AddHostedService<ServiceBusWorker>();

builder.Services.AddOpenTelemetry()
  .ConfigureResource(r => r.AddService("BulkUploadProcessor"))
  .WithTracing(t => t
      .AddSource(Telemetry.SourceName)
      .AddAspNetCoreInstrumentation()
      .AddHttpClientInstrumentation()
      //.AddEntityFrameworkCoreInstrumentation()[TODO: Need to fix this issue]
      .AddAzureMonitorTraceExporter(o => o.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"]))
  .WithMetrics(m => m
      .AddMeter(Metrics.MeterName)
      .AddRuntimeInstrumentation()
      //.AddProcessInstrumentation()
      .AddAzureMonitorMetricExporter(o => o.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"]));

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<DataContext>();
    await ctx.Database.EnsureCreatedAsync();
}
await app.RunAsync();