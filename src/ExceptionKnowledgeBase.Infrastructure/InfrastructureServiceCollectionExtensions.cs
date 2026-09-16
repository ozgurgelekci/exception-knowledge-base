using ExceptionKnowledgeBase.Application.Abstractions;
using ExceptionKnowledgeBase.Application.Options;
using ExceptionKnowledgeBase.Infrastructure.Caching;
using ExceptionKnowledgeBase.Infrastructure.Mongo;
using ExceptionKnowledgeBase.Infrastructure.OpenAI;
using ExceptionKnowledgeBase.Infrastructure.PgVector;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using StackExchange.Redis;

namespace ExceptionKnowledgeBase.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddExceptionKnowledgeBaseInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MongoOptions>(configuration.GetSection("Mongo"));
        services.Configure<PostgresOptions>(configuration.GetSection("Postgres"));
        services.Configure<RedisOptions>(configuration.GetSection("Redis"));

        services.AddSingleton<MongoContext>();
        services.AddScoped<IExceptionDefinitionRepository, MongoExceptionDefinitionRepository>();
        services.AddScoped<IExceptionOccurrenceRepository, MongoExceptionOccurrenceRepository>();
        services.AddScoped<IKnowledgeEntryRepository, MongoKnowledgeRepository>();
        services.AddScoped<ISolutionRepository, MongoSolutionRepository>();
        services.AddScoped<IAiAnalysisRepository, MongoAnalysisRepository>();
        services.AddScoped<IFeedbackRepository, MongoFeedbackRepository>();

        services.AddSingleton<IVectorSearchService, PgVectorSearchService>();

        services.AddHttpClient<IEmbeddingService, OpenAiEmbeddingService>()
            .AddStandardResilienceHandler(o =>
            {
                o.Retry.MaxRetryAttempts = 3;
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(120);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(120);
            });

        services.AddHttpClient<IChatCompletionService, OpenAiChatService>()
            .AddStandardResilienceHandler(o =>
            {
                o.Retry.MaxRetryAttempts = 2;
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(120);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(4);
                o.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(2);
            });

        var redis = configuration.GetSection("Redis").Get<RedisOptions>();
        if (!string.IsNullOrWhiteSpace(redis?.ConnectionString))
        {
            services.AddSingleton<IConnectionMultiplexer>(_ =>
                ConnectionMultiplexer.Connect(redis.ConnectionString));
            services.AddSingleton<IEmbeddingCache, RedisEmbeddingCache>();
        }

        return services;
    }
}
