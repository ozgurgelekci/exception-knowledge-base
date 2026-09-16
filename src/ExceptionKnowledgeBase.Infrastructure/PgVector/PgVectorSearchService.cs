using System.Text;
using System.Text.Json;
using ExceptionKnowledgeBase.Application.Abstractions;
using ExceptionKnowledgeBase.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector;

namespace ExceptionKnowledgeBase.Infrastructure.PgVector;

public sealed class PgVectorSearchService : IVectorSearchService
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PgVectorSearchService> _logger;

    public PgVectorSearchService(IOptions<PostgresOptions> options, ILogger<PgVectorSearchService> logger)
    {
        var builder = new NpgsqlDataSourceBuilder(options.Value.ConnectionString);
        builder.UseVector();
        _dataSource = builder.Build();
        _logger = logger;
    }

    public async Task UpsertAsync(EmbeddingUpsert upsert, CancellationToken ct)
    {
        const string sql = """
INSERT INTO embeddings
    (id, entity_id, entity_type, tenant_id, content, embedding, model, model_version, dimensions, metadata, created_at)
VALUES
    (@id, @entity_id, @entity_type, @tenant_id, @content, @embedding, @model, @model_version, @dimensions, @metadata::jsonb, now())
ON CONFLICT (entity_type, entity_id, tenant_id, model, model_version) DO UPDATE
    SET content    = EXCLUDED.content,
        embedding  = EXCLUDED.embedding,
        dimensions = EXCLUDED.dimensions,
        metadata   = EXCLUDED.metadata,
        created_at = now();
""";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("entity_id", upsert.EntityId);
        cmd.Parameters.AddWithValue("entity_type", upsert.EntityType);
        cmd.Parameters.AddWithValue("tenant_id", upsert.TenantId);
        cmd.Parameters.AddWithValue("content", upsert.Content);
        cmd.Parameters.AddWithValue("embedding", new Vector(upsert.Vector));
        cmd.Parameters.AddWithValue("model", upsert.Model);
        cmd.Parameters.AddWithValue("model_version", upsert.ModelVersion);
        cmd.Parameters.AddWithValue("dimensions", upsert.Dimensions);
        cmd.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(upsert.Metadata ?? new Dictionary<string, string>()));

        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<VectorSearchHit>> SearchAsync(VectorSearchQuery query, CancellationToken ct)
    {
        var sb = new StringBuilder("""
SELECT entity_id, entity_type, tenant_id, content,
       embedding <=> @vector AS distance
FROM embeddings
WHERE tenant_id = @tenant_id
""");

        if (!string.IsNullOrWhiteSpace(query.EntityType))
            sb.AppendLine(" AND entity_type = @entity_type");

        if (query.MetadataFilters is { Count: > 0 })
            sb.AppendLine(" AND metadata @> @metadata::jsonb");

        sb.AppendLine(" ORDER BY embedding <=> @vector");
        sb.AppendLine(" LIMIT @limit");

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sb.ToString(), conn);
        cmd.Parameters.AddWithValue("vector", new Vector(query.Vector));
        cmd.Parameters.AddWithValue("tenant_id", query.TenantId);
        if (!string.IsNullOrWhiteSpace(query.EntityType))
            cmd.Parameters.AddWithValue("entity_type", query.EntityType);
        if (query.MetadataFilters is { Count: > 0 })
            cmd.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(query.MetadataFilters));
        cmd.Parameters.AddWithValue("limit", query.Limit);

        var hits = new List<VectorSearchHit>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var entityId = reader.GetString(0);
            var entityType = reader.GetString(1);
            var tenantId = reader.GetString(2);
            var content = reader.GetString(3);
            var distance = reader.GetDouble(4);
            var similarity = 1d - distance;   // cosine distance -> similarity
            hits.Add(new VectorSearchHit(entityId, entityType, tenantId, content, distance, similarity));
        }

        _logger.LogDebug("pgvector search returned {Count} hits", hits.Count);
        return hits;
    }
}
