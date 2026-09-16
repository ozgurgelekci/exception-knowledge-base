using ExceptionKnowledgeBase.Api.Infrastructure;
using ExceptionKnowledgeBase.Application;
using ExceptionKnowledgeBase.Application.Options;
using ExceptionKnowledgeBase.Infrastructure;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddExceptionKnowledgeBaseApplication(builder.Configuration)
    .AddExceptionKnowledgeBaseInfrastructure(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();

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

app.MapControllers();
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = r => r.Tags.Contains("ready")
});
app.MapHealthChecks("/health");

app.Run();
