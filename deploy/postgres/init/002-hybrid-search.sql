-- Phase 2 (§16): hybrid search — pgvector + tsvector.
-- Add a stored, generated tsvector column over `content` plus a GIN index so
-- keyword-based retrieval can complement cosine-distance ranking.
ALTER TABLE embeddings
    ADD COLUMN IF NOT EXISTS content_tsv tsvector
    GENERATED ALWAYS AS (to_tsvector('simple', coalesce(content, ''))) STORED;

CREATE INDEX IF NOT EXISTS embeddings_content_tsv_gin
    ON embeddings USING GIN (content_tsv);
