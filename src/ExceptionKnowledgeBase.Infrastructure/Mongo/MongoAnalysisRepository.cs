using ExceptionKnowledgeBase.Application.Abstractions;
using ExceptionKnowledgeBase.Domain.Analyses;
using MongoDB.Driver;

namespace ExceptionKnowledgeBase.Infrastructure.Mongo;

public sealed class MongoAnalysisRepository : IAiAnalysisRepository
{
    private readonly MongoContext _ctx;
    public MongoAnalysisRepository(MongoContext ctx) => _ctx = ctx;

    public Task InsertAsync(AiAnalysis analysis, CancellationToken ct)
        => _ctx.AiAnalyses.InsertOneAsync(analysis, cancellationToken: ct);

    public Task<AiAnalysis?> GetByIdAsync(string tenantId, string id, CancellationToken ct)
        => _ctx.AiAnalyses.Find(x => x.TenantId == tenantId && x.Id == id).FirstOrDefaultAsync(ct)!;
}

public sealed class MongoFeedbackRepository : IFeedbackRepository
{
    private readonly MongoContext _ctx;
    public MongoFeedbackRepository(MongoContext ctx) => _ctx = ctx;

    public Task InsertAsync(Feedback feedback, CancellationToken ct)
        => _ctx.Feedbacks.InsertOneAsync(feedback, cancellationToken: ct);
}
