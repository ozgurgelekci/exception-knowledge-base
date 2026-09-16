CREATE EXTENSION IF NOT EXISTS vector;

-- Unified embeddings table per Section 32
CREATE TABLE IF NOT EXISTS embeddings (
    id UUID PRIMARY KEY,
    entity_id       VARCHAR(100) NOT NULL,
    entity_type     VARCHAR(50)  NOT NULL,     -- exception | knowledge | solution
    tenant_id       VARCHAR(100) NOT NULL DEFAULT 'default',
    content         TEXT         NOT NULL,
    embedding       VECTOR(1536) NOT NULL,
    model           VARCHAR(100) NOT NULL,
    model_version   VARCHAR(50)  NOT NULL DEFAULT '1',
    dimensions      INT          NOT NULL,
    metadata        JSONB        NOT NULL DEFAULT '{}'::jsonb,
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS embeddings_entity_uk
    ON embeddings (entity_type, entity_id, tenant_id, model, model_version);

CREATE INDEX IF NOT EXISTS embeddings_tenant_type_idx
    ON embeddings (tenant_id, entity_type);

CREATE INDEX IF NOT EXISTS embeddings_metadata_gin
    ON embeddings USING GIN (metadata);

-- HNSW cosine index per Section 15
CREATE INDEX IF NOT EXISTS embeddings_embedding_hnsw
    ON embeddings USING hnsw (embedding vector_cosine_ops);
