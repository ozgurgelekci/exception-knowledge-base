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
        // §16: hybrid = vector top-N ∪ tsvector top-N merged via Reciprocal Rank Fusion.
        if (query.UseHybrid && !string.IsNullOrWhiteSpace(query.QueryText))
            return await HybridSearchAsync(query, ct);

        return await VectorOnlySearchAsync(query, ct);
    }

    private async Task<IReadOnlyList<VectorSearchHit>> VectorOnlySearchAsync(VectorSearchQuery query, CancellationToken ct)
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
            var similarity = 1d - distance;
            hits.Add(new VectorSearchHit(entityId, entityType, tenantId, content, distance, similarity));
        }

        _logger.LogDebug("pgvector search returned {Count} hits", hits.Count);
        return hits;
    }

    // §16: single CTE query — one round-trip, DB-side RRF fusion.
    // vec  = top-N by cosine distance
    // bm   = top-N by ts_rank against plainto_tsquery
    // RRF score = w_v/(k + rank_vec) + w_k/(k + rank_bm)
    // fused_distance = 1 - fused_similarity (so downstream still reads distance).
    private async Task<IReadOnlyList<VectorSearchHit>> HybridSearchAsync(VectorSearchQuery query, CancellationToken ct)
    {
        var pool = Math.Max(query.Limit * 4, 20);

        var entityFilter = string.IsNullOrWhiteSpace(query.EntityType)
            ? string.Empty
            : " AND entity_type = @entity_type";
        var metaFilter = query.MetadataFilters is { Count: > 0 }
            ? " AND metadata @> @metadata::jsonb"
            : string.Empty;

        var sql = $"""
WITH vec AS (
    SELECT id, entity_id, entity_type, tenant_id, content,
           embedding <=> @vector AS distance,
           ROW_NUMBER() OVER (ORDER BY embedding <=> @vector) AS rk
    FROM embeddings
    WHERE tenant_id = @tenant_id{entityFilter}{metaFilter}
    ORDER BY embedding <=> @vector
    LIMIT @pool
),
bm AS (
    SELECT id, entity_id, entity_type, tenant_id, content,
           ts_rank(content_tsv, plainto_tsquery('simple', @q)) AS score,
           ROW_NUMBER() OVER (ORDER BY ts_rank(content_tsv, plainto_tsquery('simple', @q)) DESC) AS rk
    FROM embeddings
    WHERE tenant_id = @tenant_id{entityFilter}{metaFilter}
      AND content_tsv @@ plainto_tsquery('simple', @q)
    ORDER BY score DESC
    LIMIT @pool
),
fused AS (
    SELECT COALESCE(vec.entity_id, bm.entity_id)       AS entity_id,
           COALESCE(vec.entity_type, bm.entity_type)   AS entity_type,
           COALESCE(vec.tenant_id, bm.tenant_id)       AS tenant_id,
           COALESCE(vec.content, bm.content)           AS content,
           COALESCE(vec.distance, 1.0)                 AS vec_distance,
           (@wv / (@k + COALESCE(vec.rk, 10000))
          + @wk / (@k + COALESCE(bm.rk, 10000)))       AS fused_score
    FROM vec
    FULL OUTER JOIN bm ON vec.id = bm.id
)
SELECT entity_id, entity_type, tenant_id, content, vec_distance, fused_score
FROM fused
ORDER BY fused_score DESC
LIMIT @limit;
""";

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("vector", new Vector(query.Vector));
        cmd.Parameters.AddWithValue("tenant_id", query.TenantId);
        cmd.Parameters.AddWithValue("q", query.QueryText ?? string.Empty);
        cmd.Parameters.AddWithValue("k", query.RrfK);
        cmd.Parameters.AddWithValue("wv", query.HybridVectorWeight);
        cmd.Parameters.AddWithValue("wk", query.HybridKeywordWeight);
        cmd.Parameters.AddWithValue("pool", pool);
        cmd.Parameters.AddWithValue("limit", query.Limit);
        if (!string.IsNullOrWhiteSpace(query.EntityType))
            cmd.Parameters.AddWithValue("entity_type", query.EntityType);
        if (query.MetadataFilters is { Count: > 0 })
            cmd.Parameters.AddWithValue("metadata", JsonSerializer.Serialize(query.MetadataFilters));

        var hits = new List<VectorSearchHit>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var entityId = reader.GetString(0);
            var entityType = reader.GetString(1);
            var tenantId = reader.GetString(2);
            var content = reader.GetString(3);
            var vecDistance = reader.GetDouble(4);
            // SQL already orders by RRF score; expose the vector similarity so
            // downstream MinSimilarity thresholds keep the same semantics.
            var similarity = 1d - vecDistance;
            hits.Add(new VectorSearchHit(entityId, entityType, tenantId, content, vecDistance, similarity));
        }

        _logger.LogDebug("hybrid search returned {Count} hits (pool={Pool})", hits.Count, pool);
        return hits;
    }
}
