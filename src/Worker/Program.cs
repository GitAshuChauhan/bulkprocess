using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Data.Common;
using Worker.Abstractions;
using Worker.Configuration;
using Worker.Data;
using Worker.Data.DbContext;
using Worker.Data.Repositories;
using Worker.Infrastructure;
using Worker.Mft;
using Worker.Services;
using Worker.Telemetry;
using Worker.Workers;

var builder = Host.CreateApplicationBuilder(args);
var config = builder.Configuration;

// service name/version
var serviceName = builder.Configuration["Service:Name"] ?? "blkupload-worker";
var serviceVersion = builder.Configuration["Service:Version"] ?? "1.0.0";
var envName = builder.Environment.EnvironmentName;

builder.Services.AddLogging();

// OpenTelemetry Resource
var resourceBuilder = ResourceBuilder.CreateDefault()
    .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
    .AddAttributes(new[]
    {
        new KeyValuePair<string, object>("deployment.environment", envName)
    });

//[TODO] to check AddOtlpExporter for vendor-agnostic or forward telemetry to multiple systems → use AddOtlpExporter and run an OTel Collector (sidecar or central).

// OpenTelemetry + AppInsights
var aiConn = config["ApplicationInsights:ConnectionString"];

builder.Services.AddOpenTelemetry()
    .WithTracing(t =>
    {
        t
        .SetResourceBuilder(resourceBuilder)
        .AddSource("IngestWorker.ZipUploader")
        .AddSource("IngestWorker.MetadataStager")
        .AddSource("IngestWorker.DocumentProcessor")
        .AddHttpClientInstrumentation()
        .AddAspNetCoreInstrumentation();
        //the below is optional, uncomment if you want to capture EF Core database calls. Make sure to add the NuGet package OpenTelemetry.Instrumentation.EntityFrameworkCore. This package is in beta as of Aud 2025.
        //.AddEntityFrameworkCoreInstrumentation(options =>
        //{
        //    options.SetDbStatementForText = true;
        //    options.SetDbStatementForStoredProcedure = true;
        //})        
        if (builder.Environment.IsDevelopment())
        {
            t.AddConsoleExporter();
            // Use 100% sampling in development
            t.SetSampler(new AlwaysOnSampler());
        }
        else
        {
            // In production, sample only 10% of requests to reduce overhead
            double samplingRatio = builder.Configuration.GetValue<double>("OpenTelemetry:SamplingRatio", 0.1);
            t.SetSampler(new TraceIdRatioBasedSampler(samplingRatio));
        }
        // Azure Monitor / Application Insights exporter (optional, requires connection string)
        if (!string.IsNullOrWhiteSpace(aiConn))
        {
            t.AddAzureMonitorTraceExporter(o => o.ConnectionString = aiConn);
        }
    })
    .UseAzureMonitor(options =>
    {
        // Configure the connection string from configuration or environment variable.
        // The connection string is automatically read from the "APPLICATIONINSIGHTS_CONNECTION_STRING"
        // environment variable if present.
        options.ConnectionString = aiConn;
    })
    .WithMetrics(m => //[TODO: add meter name and properties n meter.cs file and telemetry.cs file check bulkprocess code.]
    {
        m.SetResourceBuilder(resourceBuilder)
         .AddRuntimeInstrumentation()
         .AddAspNetCoreInstrumentation();
        if (builder.Environment.IsDevelopment())
        {
            m.AddConsoleExporter();
        }
        //.AddConsoleExporter();
        if (!string.IsNullOrWhiteSpace(aiConn))
        {
            //[TODO] for vendor-agnostic or forward telemetry to multiple systems → use AddOtlpExporter and run an OTel Collector (sidecar or central).
            m.AddAzureMonitorMetricExporter(o => o.ConnectionString = aiConn);
        }
    })
    .WithLogging(loggingBuilder =>
    {
        loggingBuilder
            .SetResourceBuilder(resourceBuilder);
        if (builder.Environment.IsDevelopment())
        {

            loggingBuilder.AddConsoleExporter();
        }
        if (!string.IsNullOrEmpty(aiConn))
        {
            // Export logs to Azure Monitor / App Insights
            loggingBuilder.AddAzureMonitorLogExporter(o => o.ConnectionString = aiConn);
        }
    });

// bind options
builder.Services.Configure<ServiceBusOptions>(config.GetSection("ServiceBus"));
builder.Services.Configure<ProcessingOptions>(config.GetSection("Processing"));
builder.Services.Configure<StorageOptions>(config.GetSection("Storage"));
builder.Services.Configure<DatabaseOptions>(config.GetSection("ConnectionStrings"));

// validators
builder.Services.AddSingleton<IValidateOptions<ServiceBusOptions>, ServiceBusOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<ProcessingOptions>, ProcessingOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();
builder.Services.AddHostedService<ValidateOptionsHostedService>();

// DbContext wiring (database-first friendly): obtain connection string from config
//builder.Services.AddDbContext<DataContext>((sp, options) =>
//{
//    var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString("Postgres");
//    options.UseNpgsql(cs);
//});

builder.Services.AddDbContext<StagingDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("StagingDb")));
builder.Services.AddDbContext<ProductionDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("ProductionDb")));

// Blob Service registration
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var conn = cfg["Storage:Connection"];
    if (!string.IsNullOrWhiteSpace(conn))
        return new Azure.Storage.Blobs.BlobServiceClient(conn);
    var accountUrl = cfg["Storage:AccountUrl"];
    if (!string.IsNullOrEmpty(accountUrl))
        return new Azure.Storage.Blobs.BlobServiceClient(new Uri(accountUrl), new DefaultAzureCredential());
    throw new InvalidOperationException("Storage:Connection or Storage:AccountUrl required");
});

// ServiceBus client & single processor (one message per pod)
builder.Services.AddSingleton(sp =>
{
    var sbOpts = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value;

    var options = new ServiceBusClientOptions
    {
        RetryOptions = new ServiceBusRetryOptions
        {
            Mode = ServiceBusRetryMode.Exponential,
            MaxRetries = sbOpts.MaxRetries,
            Delay = TimeSpan.FromSeconds(2),
            MaxDelay = TimeSpan.FromSeconds(60)
        }
    };

    ServiceBusClient? sbclient = null;

    if (sbOpts.IsManagedConnection)
        sbclient = new ServiceBusClient(sbOpts.NamespaceFqdn, new DefaultAzureCredential(),options);
    else
        sbclient = new ServiceBusClient(sbOpts.ConnectionString, options);

    return sbclient;
});
builder.Services.AddSingleton(sp =>
{
    var sbOpts = sp.GetRequiredService<IOptions<ServiceBusOptions>>().Value;
    var client = sp.GetRequiredService<ServiceBusClient>();
    var processorOptions = new ServiceBusProcessorOptions
    {
        AutoCompleteMessages = false,
        MaxConcurrentCalls = 1,
        MaxAutoLockRenewalDuration = TimeSpan.FromHours(sbOpts.MaxRenewHours),
        ReceiveMode = ServiceBusReceiveMode.PeekLock
    };
    return client.CreateProcessor(sbOpts.QueueName, processorOptions);
});

// resilience
builder.Services.AddSingleton<ResiliencePolicyFactory>();

// DI: abstractions & implementations
builder.Services.AddTransient<IMftClient, SftpMftClient>();
builder.Services.AddScoped<IStagingRepository, StagingRepository>();
builder.Services.AddScoped<IProductionRepository, ProductionRepository>();
//builder.Services.AddScoped<IDocumentRepository, PostgresDocumentRepository>();
builder.Services.AddScoped<IZipHandler, ZipHandlerSftp>();
builder.Services.AddScoped<ICsvStager, CsvStager>();
builder.Services.AddScoped<IDocumentProcessor, DocumentProcessor>();
builder.Services.AddScoped<IJobLogger, JobLogger>();
builder.Services.AddSingleton<IJobAlertService, JobAlertService>();

// hosted services
builder.Services.AddHostedService<ServiceBusWorker>();

//[TODO: Need to check if required to process the messages in Deadletter queue again as below.]

//builder.Services.AddHostedService<DlqProcessor>();

//[TODO: below healthservice service will not work untill resolve Healthcheck issues below code]
// health checks
builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString: builder.Configuration.GetConnectionString("Postgres")!,
        name: "postgresql",
        tags: ["db", "postgres"])
    .AddAzureBlobStorage(
         sp => new BlobServiceClient(
            builder.Configuration.GetConnectionString("BlobStorage")),
        name: "blobstorage",
        tags: ["storage", "azure"])
    .AddAzureServiceBusQueue(
        connectionString: builder.Configuration.GetConnectionString("ServiceBus")!,
        queueName: builder.Configuration["ServiceBus:QueueName"]!,
        name: "servicebus",
        tags: ["messaging", "azure" ]);

var app = builder.Build();

//[TODO: Fix the HealthCheck issue.]

// Health check endpoints (for liveness/readiness)
//app.MapHealthChecks("/health/live", new HealthCheckOptions
//{
//    Predicate = _ => false // just checks if app is alive
//});
//app.MapHealthChecks("/health/ready", new HealthCheckOptions
//{
//    Predicate = check => true, // run all checks
//    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
//});


await app.RunAsync();