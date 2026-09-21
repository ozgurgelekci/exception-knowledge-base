using ExceptionKnowledgeBase.Application.Analysis;
using ExceptionKnowledgeBase.Application.Normalization;
using ExceptionKnowledgeBase.Application.Options;
using ExceptionKnowledgeBase.Application.Security;
using ExceptionKnowledgeBase.Application.Services;
using ExceptionKnowledgeBase.Application.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExceptionKnowledgeBase.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddExceptionKnowledgeBaseApplication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OpenAiOptions>(configuration.GetSection("OpenAI"));
        services.Configure<AnalysisOptions>(configuration.GetSection("Analysis"));

        services.AddSingleton<IExceptionNormalizer, ExceptionNormalizer>();
        services.AddSingleton<IFingerprintGenerator, FingerprintGenerator>();
        services.AddSingleton<ISecretRedactor, SecretRedactor>();
        services.AddSingleton<IEmbeddingInputBuilder, EmbeddingInputBuilder>();
        services.AddSingleton<IPromptBuilder, PromptBuilder>();
        services.AddSingleton<IConfidenceCalculator, ConfidenceCalculator>();
        services.AddSingleton<IRootCauseClassifier, RootCauseClassifier>();
        services.AddSingleton<IReRanker, LexicalOverlapReRanker>();
        services.AddSingleton<IAnomalyAlertSink, InMemoryAnomalyAlertSink>();
        services.AddSingleton<IAnomalyDetector, InMemoryAnomalyDetector>();
        services.AddSingleton<AiMetrics>();

        services.AddScoped<IExceptionAnalysisService, ExceptionAnalysisService>();
        services.AddScoped<IKnowledgeService, KnowledgeService>();
        services.AddScoped<IFeedbackService, FeedbackService>();

        return services;
    }
}
