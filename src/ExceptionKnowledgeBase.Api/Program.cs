using System.Threading.RateLimiting;
using ExceptionKnowledgeBase.Api.Infrastructure;
using ExceptionKnowledgeBase.Application;
using ExceptionKnowledgeBase.Application.Abstractions;
using ExceptionKnowledgeBase.Application.Options;
using ExceptionKnowledgeBase.Application.Telemetry;
using ExceptionKnowledgeBase.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddExceptionKnowledgeBaseApplication(builder.Configuration)
    .AddExceptionKnowledgeBaseInfrastructure(builder.Configuration);

builder.Services.Configure<RateLimitingOptions>(builder.Configuration.GetSection("RateLimiting"));

// Phase 2 (§64): async analyze — enqueue + background worker.
builder.Services.AddSingleton<IAnalysisJobQueue, ChannelAnalysisJobQueue>();
builder.Services.AddHostedService<AnalysisJobWorker>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

// §78: OpenTelemetry — subscribes to AiMetrics + AspNetCore/HttpClient sources.
// OTLP endpoint is opt-in via OTEL_EXPORTER_OTLP_ENDPOINT; without it the SDK collects
// but doesn't ship anywhere, so leaving the env var unset is a safe no-op.
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"]
                   ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
var serviceName = builder.Configuration["OpenTelemetry:ServiceName"] ?? "exception-knowledge-base";

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName))
    .WithTracing(t =>
    {
        t.AddAspNetCoreInstrumentation();
        t.AddHttpClientInstrumentation();
        t.AddSource("ExceptionKnowledgeBase");
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            t.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    })
    .WithMetrics(m =>
    {
        m.AddMeter(AiMetrics.MeterName);
        m.AddAspNetCoreInstrumentation();
        m.AddHttpClientInstrumentation();
        m.AddRuntimeInstrumentation();
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            m.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
    });

// Section 65: per-tenant fixed-window rate limits per policy.
var rateLimits = builder.Configuration.GetSection("RateLimiting").Get<RateLimitingOptions>() ?? new RateLimitingOptions();
if (rateLimits.Enabled)
{
    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = async (ctx, ct) =>
        {
            ctx.HttpContext.Response.Headers.RetryAfter = "60";
            await ctx.HttpContext.Response.WriteAsJsonAsync(new
            {
                title = "Too Many Requests",
                status = 429,
                detail = "Rate limit exceeded for tenant. Retry after 60 seconds."
            }, ct);
        };

        options.AddPolicy(RateLimitPolicies.Analyze, ctx => TenantWindow(ctx, rateLimits.AnalyzePermitPerMinute, rateLimits.QueueLimit));
        options.AddPolicy(RateLimitPolicies.Search, ctx => TenantWindow(ctx, rateLimits.SearchPermitPerMinute, rateLimits.QueueLimit));
        options.AddPolicy(RateLimitPolicies.Knowledge, ctx => TenantWindow(ctx, rateLimits.KnowledgePermitPerMinute, rateLimits.QueueLimit));
    });
}

static RateLimitPartition<string> TenantWindow(HttpContext ctx, int permit, int queueLimit)
    => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: ctx.ResolveTenantId(),
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = queueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true
        });

// Section 68: readiness = infra reachable, liveness = process up.
var healthChecks = builder.Services.AddHealthChecks();
var mongo = builder.Configuration.GetSection("Mongo").Get<MongoOptions>();
var pg = builder.Configuration.GetSection("Postgres").Get<PostgresOptions>();
var redis = builder.Configuration.GetSection("Redis").Get<RedisOptions>();

if (!string.IsNullOrWhiteSpace(mongo?.ConnectionString))
    healthChecks.AddMongoDb(sp => new MongoDB.Driver.MongoClient(mongo.ConnectionString), name: "mongodb", tags: new[] { "ready" });
if (!string.IsNullOrWhiteSpace(pg?.ConnectionString))
    healthChecks.AddNpgSql(pg.ConnectionString, name: "postgres", tags: new[] { "ready" });
if (!string.IsNullOrWhiteSpace(redis?.ConnectionString))
    healthChecks.AddRedis(redis.ConnectionString, name: "redis", tags: new[] { "ready" });

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (rateLimits.Enabled)
    app.UseRateLimiter();

app.MapControllers();
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready")
});
app.MapHealthChecks("/health");

app.Run();
