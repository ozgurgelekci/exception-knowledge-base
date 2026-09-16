# AI Exception Knowledge Base

MVP for the design in `ai_exception_knowledge_base_teknik_analiz.md` (sections 59 + 72):
a .NET 8 backend that ingests exceptions, embeds them with OpenAI, retrieves similar
knowledge entries from pgvector, and asks an LLM to reason over the retrieved evidence.

## Layout

```
ExceptionKnowledgeBase.slnx
src/
  ExceptionKnowledgeBase.Api            REST API, Swagger, health checks
  ExceptionKnowledgeBase.Application    Orchestration, normalization, prompts, redaction
  ExceptionKnowledgeBase.Domain         Entities: definitions, occurrences, knowledge, solutions, analyses, feedback
  ExceptionKnowledgeBase.Infrastructure MongoDB repos, pgvector search, OpenAI clients, Redis embedding cache
  ExceptionKnowledgeBase.Contracts      Request/response DTOs
  ExceptionKnowledgeBase.Worker         Background embedding pipeline (retries, dead-letter)
deploy/
  postgres/init/001-schema.sql          `CREATE EXTENSION vector` + embeddings table + HNSW index
  api/Dockerfile, worker/Dockerfile
docker-compose.yml                      mongo + pgvector + redis + api + worker
```

## Endpoints

| Method | Route                              | Purpose                          |
|--------|------------------------------------|----------------------------------|
| POST   | `/api/exceptions/analyze`          | Ingest + AI analysis (Section 37)|
| POST   | `/api/exceptions/search`           | Semantic search (Section 38)     |
| POST   | `/api/knowledge`                   | Create knowledge entry (Section 39)|
| GET    | `/api/knowledge/{id}`              | Read                              |
| PUT    | `/api/knowledge/{id}`              | Update (re-embeds)                |
| DELETE | `/api/knowledge/{id}`              | Delete                            |
| POST   | `/api/analyses/{id}/feedback`      | Submit feedback (Section 40)      |
| GET    | `/health`, `/health/ready`, `/health/live` | Health checks (Section 68) |

Tenant is resolved from the `X-Tenant-Id` header, defaulting to `default`.

## Running locally

Requires Docker + an OpenAI API key.

```bash
export OPENAI_API_KEY=sk-...
docker compose up --build
```

- API:      http://localhost:8080/swagger
- MongoDB:  localhost:27017 (ekb/ekb)
- Postgres: localhost:5432 (ekb/ekb, database `exception_kb`)
- Redis:    localhost:6379

## Design notes (see analysis doc)

- **Section 9/10** — every ingest normalizes volatile fields (IPs, GUIDs, timings, paths)
  then SHA256s the result. Fingerprint is the dedupe key per tenant.
- **Section 12** — embedded content is `type + normalized message + database + tags`,
  never raw operational noise.
- **Section 15** — pgvector table uses an HNSW cosine index.
- **Section 22 + 51** — the LLM prompt keeps `SYSTEM > TRUSTED KB > UNTRUSTED EXCEPTION`
  strictly separated and forces a JSON schema.
- **Section 24 + 25** — the AI response carries known/likely/unknown statements and an
  application-level confidence (`ConfidenceCalculator`) combining similarity, support
  count, solution success rate, and the LLM's own claim.
- **Section 34** — the OpenAI embedding client honours an optional Redis cache
  (`embedding:{sha256}:{model}:{version}` keys).
- **Section 42** — `ExceptionEmbeddingWorker` / `KnowledgeEmbeddingWorker` re-drive
  pending or failed embeddings out-of-band with retry state.
- **Section 47** — tenant is a first-class filter on Mongo indexes and pgvector queries.
- **Section 49/50** — `SecretRedactor` strips passwords, tokens, JWTs, emails, and
  connection strings before anything is embedded, stored, or sent to the LLM.
- **Section 52 + 53** — every analysis is persisted with prompt/model versions, evidence
  IDs, similarity scores, tokens, and per-stage latencies.
