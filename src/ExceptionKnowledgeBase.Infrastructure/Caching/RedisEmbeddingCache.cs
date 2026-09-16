using System.Buffers.Binary;
using ExceptionKnowledgeBase.Application.Abstractions;
using ExceptionKnowledgeBase.Application.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ExceptionKnowledgeBase.Infrastructure.Caching;

public sealed class RedisEmbeddingCache : IEmbeddingCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly RedisOptions _options;

    public RedisEmbeddingCache(IConnectionMultiplexer redis, IOptions<RedisOptions> options)
    {
        _redis = redis;
        _options = options.Value;
    }

    public async Task<float[]?> GetAsync(string key, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(key);
        return value.HasValue ? Decode(value!) : null;
    }

    public async Task SetAsync(string key, float[] vector, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(key, Encode(vector), TimeSpan.FromSeconds(_options.EmbeddingTtlSeconds));
    }

    private static byte[] Encode(float[] vector)
    {
        var buffer = new byte[vector.Length * sizeof(float)];
        var span = buffer.AsSpan();
        for (var i = 0; i < vector.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(span[(i * 4)..], vector[i]);
        return buffer;
    }

    private static float[] Decode(ReadOnlyMemory<byte> memory)
    {
        var span = memory.Span;
        var count = span.Length / sizeof(float);
        var result = new float[count];
        for (var i = 0; i < count; i++)
            result[i] = BinaryPrimitives.ReadSingleLittleEndian(span[(i * 4)..]);
        return result;
    }
}
