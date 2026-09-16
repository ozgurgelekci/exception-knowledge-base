using ExceptionKnowledgeBase.Application.Options;
using ExceptionKnowledgeBase.Domain.Analyses;
using ExceptionKnowledgeBase.Domain.Exceptions;
using ExceptionKnowledgeBase.Domain.Knowledge;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Driver;

namespace ExceptionKnowledgeBase.Infrastructure.Mongo;

public sealed class MongoContext
{
    static MongoContext()
    {
        var pack = new ConventionPack { new CamelCaseElementNameConvention(), new EnumRepresentationConvention(BsonType.String) };
        ConventionRegistry.Register("ekb-conventions", pack, _ => true);
    }

    public MongoContext(IOptions<MongoOptions> options)
    {
        var settings = MongoClientSettings.FromConnectionString(options.Value.ConnectionString);
        Client = new MongoClient(settings);
        Database = Client.GetDatabase(options.Value.Database);

        ExceptionDefinitions = Database.GetCollection<ExceptionDefinition>("exception_definitions");
        ExceptionOccurrences = Database.GetCollection<ExceptionOccurrence>("exception_occurrences");
        KnowledgeEntries = Database.GetCollection<KnowledgeEntry>("knowledge_entries");
        Solutions = Database.GetCollection<Solution>("solutions");
        AiAnalyses = Database.GetCollection<AiAnalysis>("ai_analyses");
        Feedbacks = Database.GetCollection<Feedback>("feedbacks");

        EnsureIndexes();
    }

    public IMongoClient Client { get; }
    public IMongoDatabase Database { get; }
    public IMongoCollection<ExceptionDefinition> ExceptionDefinitions { get; }
    public IMongoCollection<ExceptionOccurrence> ExceptionOccurrences { get; }
    public IMongoCollection<KnowledgeEntry> KnowledgeEntries { get; }
    public IMongoCollection<Solution> Solutions { get; }
    public IMongoCollection<AiAnalysis> AiAnalyses { get; }
    public IMongoCollection<Feedback> Feedbacks { get; }

    private void EnsureIndexes()
    {
        ExceptionDefinitions.Indexes.CreateOne(new CreateIndexModel<ExceptionDefinition>(
            Builders<ExceptionDefinition>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.Fingerprint),
            new CreateIndexOptions { Name = "tenant_fingerprint_uk", Unique = true }));

        ExceptionOccurrences.Indexes.CreateOne(new CreateIndexModel<ExceptionOccurrence>(
            Builders<ExceptionOccurrence>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.DefinitionId)
                .Descending(x => x.OccurredAt),
            new CreateIndexOptions { Name = "tenant_definition_occurred" }));

        KnowledgeEntries.Indexes.CreateOne(new CreateIndexModel<KnowledgeEntry>(
            Builders<KnowledgeEntry>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.Status),
            new CreateIndexOptions { Name = "tenant_status" }));

        Solutions.Indexes.CreateOne(new CreateIndexModel<Solution>(
            Builders<Solution>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.KnowledgeEntryId),
            new CreateIndexOptions { Name = "tenant_knowledge" }));

        AiAnalyses.Indexes.CreateOne(new CreateIndexModel<AiAnalysis>(
            Builders<AiAnalysis>.IndexKeys.Ascending(x => x.TenantId).Descending(x => x.CreatedAt),
            new CreateIndexOptions { Name = "tenant_created" }));

        Feedbacks.Indexes.CreateOne(new CreateIndexModel<Feedback>(
            Builders<Feedback>.IndexKeys.Ascending(x => x.TenantId).Ascending(x => x.AnalysisId),
            new CreateIndexOptions { Name = "tenant_analysis" }));
    }
}
