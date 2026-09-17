# AI Destekli Exception Knowledge Base

Uygulamalarda oluşan exception'ları merkezi bir bilgi tabanına toplayan, benzer
hataları semantic/vector search ile bulan ve geçmişte çözülmüş problemlere
dayanarak geliştiriciye **kaynakları gösterilmiş** AI destekli açıklama ve çözüm
önerisi üreten .NET 8 backend'idir.

Bu depo, `ai_exception_knowledge_base_teknik_analiz.md` dokümanındaki 80 bölümlük
tasarımın **Section 72 MVP + Section 73 Phase 2** kapsamında implement edilmiş
halidir. README, hem kullanım kılavuzu hem de kısaltılmış tasarım referansı
olarak yazılmıştır — her mimari karar analizin ilgili bölüm numarasına referans
verir.

**Phase 2 (§73) eklemeleri:**
- Hybrid search: pgvector cosine + tsvector, Reciprocal Rank Fusion ile birleşik ranking (§16).
- Async analiz endpoint'i: `POST /api/exceptions/analyze/async` → 202 + `GET /api/analyses/{id}` polling (§64).
- Redis analiz + search response cache, `tenant + fingerprint + prompt/knowledge/model version` bileşik anahtar (§36).
- Knowledge lifecycle endpoint'leri: `POST /api/knowledge/{id}/verify | archive | reset` (§74 draft → verified → archived).
- Per-tenant rate limiting: analyze / search / knowledge policy'leri (§65).

---

## İçindekiler

1. [Neden?](#1-neden)
2. [Mimari genel bakış](#2-mimari-genel-bakış)
3. [Repo yapısı](#3-repo-yapısı)
4. [Yerel çalıştırma](#4-yerel-çalıştırma)
5. [Endpoint referansı](#5-endpoint-referansı)
6. [Veri modeli](#6-veri-modeli)
7. [Exception analiz pipeline'ı (RAG)](#7-exception-analiz-pipelineı-rag)
8. [Normalizasyon, fingerprint, dedupe](#8-normalizasyon-fingerprint-dedupe)
9. [Embedding stratejisi](#9-embedding-stratejisi)
10. [Vector search + threshold](#10-vector-search--threshold)
11. [LLM prompt tasarımı ve hallucination kontrolü](#11-llm-prompt-tasarımı-ve-hallucination-kontrolü)
12. [Confidence hesabı](#12-confidence-hesabı)
13. [Güvenlik: PII/secret redaction ve prompt injection](#13-güvenlik-piisecret-redaction-ve-prompt-injection)
14. [Multi-tenant izolasyon](#14-multi-tenant-izolasyon)
15. [Feedback ve solution success rate](#15-feedback-ve-solution-success-rate)
16. [Background worker'lar](#16-background-workerlar)
17. [Caching stratejisi](#17-caching-stratejisi)
18. [OpenAI hata yönetimi](#18-openai-hata-yönetimi)
19. [Observability, health, metrics](#19-observability-health-metrics)
20. [Konfigürasyon](#20-konfigürasyon)
21. [Uçtan uca örnek](#21-uçtan-uca-örnek)
22. [Yol haritası (Phase 2-4)](#22-yol-haritası-phase-2-4)
23. [Analiz bölüm → kod eşlemesi](#23-analiz-bölüm--kod-eşlemesi)

---

## 1. Neden?

Klasik text search sadece birebir eşleşmeyi bulur. Aynı problem farklı
sistemlerde farklı ifadelerle görünür (Section 2):

```
System.TimeoutException            → "The operation has timed out"
MongoDB timeout                    → "Timed out waiting for a server"
SQL timeout                        → "Execution Timeout Expired"
HTTP timeout                       → "The request was canceled due to timeout"
```

Bu sistem, exception'ı **anlamı üzerinden** ele alır: embedding vektörü çıkarır,
pgvector cosine mesafesi ile en yakın geçmiş vakaları bulur, bulunanları LLM'e
**kanıt** olarak verir ve LLM sadece bu kanıta dayanarak çözüm önerir. Yani
sorulan asıl soru şudur:

> *"Bu hataya semantik olarak en yakın geçmiş problemler hangileri ve
> bunlarda hangi çözümler uygulanmış?"*

En kritik mimari prensip (Section 79):

> **LLM'i knowledge base'in yerine koymamak; LLM'i knowledge base üzerinde
> çalışan bir reasoning katmanı olarak kullanmak.**

---

## 2. Mimari genel bakış

Analiz dokümanı Section 5 ve Section 76'daki tasarımın gerçeklenmiş hali:

```
        Client / Application
                │
                ▼
        ┌───────────────────┐
        │   .NET 8 Web API  │  ← Program.cs, Controllers, TenantAccessor
        └────────┬──────────┘
                 │
   ┌─────────────┼─────────────────┐
   │             │                 │
   ▼             ▼                 ▼
MongoDB      OpenAI            Redis (opsiyonel)
Source of    ┌─────┴────┐      Embedding cache
Truth        │ Embed +  │
             │  Chat    │
             └─────┬────┘
                   ▼
            PostgreSQL / pgvector
            (Semantic Search Index)
                   │
                   ▼
            Top-K similar entries
                   │
                   ▼
        ┌─── Prompt Builder ───┐
        │ SYSTEM (trusted)     │
        │ KB context (trusted) │
        │ EXCEPTION (untrust.) │
        └───────────┬──────────┘
                    ▼
                OpenAI Chat
                    │
                    ▼
        Structured JSON analysis
                    │
                    ▼
             MongoDB (audit)
```

**MongoDB + pgvector ayrımı (Section 4):** MongoDB *source of truth*
(exception'lar, bilgi kayıtları, çözümler, audit), pgvector ise *arama için
optimize edilmiş projeksiyon*. Aynı entity iki katmanda birden yaşar; embedding
lifecycle her iki tarafı senkron tutar.

---

## 3. Repo yapısı

Analiz Section 59'daki clean-architecture layering:

```
ExceptionKnowledgeBase.slnx
src/
  ExceptionKnowledgeBase.Api             REST API, Swagger, health check'ler, tenant resolver
  ExceptionKnowledgeBase.Application     Orkestrasyon, normalizasyon, prompt, redaksiyon, confidence
  ExceptionKnowledgeBase.Domain          Entity'ler: definition, occurrence, knowledge, solution, analysis, feedback
  ExceptionKnowledgeBase.Infrastructure  MongoDB repo'ları, pgvector servisi, OpenAI istemcileri, Redis cache
  ExceptionKnowledgeBase.Contracts       Request/response DTO'ları
  ExceptionKnowledgeBase.Worker          ExceptionEmbeddingWorker + KnowledgeEmbeddingWorker
deploy/
  postgres/init/001-schema.sql           CREATE EXTENSION vector + embeddings tablosu + HNSW index
  api/Dockerfile
  worker/Dockerfile
docker-compose.yml                       mongo + pgvector + redis + api + worker
```

Katmanlar arası bağımlılık: `Api → Application → Domain`,
`Api / Worker → Infrastructure → Application (abstractions) → Domain`.
Domain, MongoDB.Bson attribute'ları için MongoDB.Bson paketine bağlanır — MVP
için bilinçli kısayol.

---

## 4. Yerel çalıştırma

Gereksinim: Docker + geçerli bir OpenAI API anahtarı.

```bash
export OPENAI_API_KEY=sk-...
docker compose up --build
```

Compose ayakta olan servisler (Section 58):

| Servis        | Adres                   | Notlar                                |
|---------------|-------------------------|---------------------------------------|
| API + Swagger | http://localhost:8080/swagger | ASP.NET Core 8.0                |
| MongoDB       | localhost:27017         | user/pass `ekb/ekb`, db `exception_kb`|
| PostgreSQL    | localhost:5432          | pgvector/pgvector:pg16, `ekb/ekb`     |
| Redis         | localhost:6379          | opsiyonel — connection string boşsa DI atlanır |
| Worker        | (headless)              | 5–10 sn aralıkla embedding batch      |

Postgres init script'i (`deploy/postgres/init/001-schema.sql`) container ilk
ayağa kalkarken çalışır: `CREATE EXTENSION vector`, `embeddings` tablosu ve
HNSW cosine index'i kurar.

---

## 5. Endpoint referansı

| Method | Route                                  | Amaç                                | Analiz |
|--------|----------------------------------------|-------------------------------------|--------|
| POST   | `/api/exceptions/analyze`              | Exception alımı + AI analizi        | §37    |
| POST   | `/api/exceptions/search`               | Semantik arama                      | §38    |
| POST   | `/api/knowledge`                       | Knowledge entry oluştur (embed edilir) | §39 |
| GET    | `/api/knowledge/{id}`                  | Oku                                 | §39    |
| PUT    | `/api/knowledge/{id}`                  | Güncelle (yeniden embed)            | §39    |
| DELETE | `/api/knowledge/{id}`                  | Sil                                 | §39    |
| POST   | `/api/analyses/{id}/feedback`          | Analiz için geri bildirim           | §40    |
| GET    | `/health` / `/health/ready` / `/health/live` | Health check                  | §68    |

Tenant `X-Tenant-Id` header'ından çözümlenir. Header yoksa `Analysis:DefaultTenantId`
(default: `default`) kullanılır (Section 47). Vector search ve tüm Mongo
sorguları tenant filtresi ile çalışır.

### `/api/exceptions/analyze` — istek

```json
{
  "exceptionType": "MongoConnectionException",
  "message": "Timed out after 10000 ms while waiting for a server",
  "stackTrace": "...",
  "innerException": null,
  "service": "invoice-service",
  "application": "e-invoice",
  "module": null,
  "environment": "production",
  "database": "MongoDB",
  "host": "worker-42",
  "container": "invoice-svc-7f8b9",
  "endpoint": "POST /api/invoices",
  "httpMethod": "POST",
  "statusCode": 500,
  "tags": ["mongodb", "timeout"],
  "context": { "correlationId": "abc-123" }
}
```

### `/api/exceptions/analyze` — yanıt (Section 23, 56)

```json
{
  "analysisId": "0f7b...",
  "definitionId": "6f3c...",
  "occurrenceId": "9a11...",
  "summary": "MongoDB primary node erişilemiyor.",
  "rootCause": {
    "text": "Replica set primary erişim problemi olası.",
    "confidence": 0.91
  },
  "applicationConfidence": 0.87,
  "known":  ["Bu exception MongoDB timeout ile ilişkilendirilmiş."],
  "likely": ["Replica set primary erişim problemi olabilir."],
  "unknown":["Network kesin sebep olarak doğrulanmış değil."],
  "recommendedChecks": [
    "Replica set health kontrol edilmeli",
    "DNS resolution kontrol edilmeli"
  ],
  "recommendedSolutions": [
    { "text": "Replica set member durumlarını kontrol edin", "solutionId": "kb-001-sol-a" }
  ],
  "evidence": [
    { "knowledgeId": "kb-001", "similarity": 0.94, "title": "MongoDB Replica Set Primary Timeout" }
  ],
  "sources": ["kb-001", "kb-032"],
  "prompt": { "version": 1 },
  "model":  { "chat": "gpt-4o-mini", "embedding": "text-embedding-3-small" },
  "latencies": { "embeddingMs": 210, "vectorSearchMs": 38, "llmMs": 1820, "totalMs": 2130 },
  "tokens":    { "promptTokens": 812, "completionTokens": 264 }
}
```

---

## 6. Veri modeli

### 6.1 MongoDB collection'ları (Section 31)

| Collection                | İçerik                                                        |
|---------------------------|---------------------------------------------------------------|
| `exception_definitions`   | Normalize edilmiş, fingerprint'lenmiş exception tanımı (§45)  |
| `exception_occurrences`   | Her bireysel olay — timestamp, host, endpoint, context (§45)  |
| `knowledge_entries`       | İnsan doğrulamalı bilgi kartı (title, symptoms, rootCause…)   |
| `solutions`               | Çözüm kayıtları + `timesApplied/Successful/Failed` (§26)      |
| `ai_analyses`             | Her AI çağrısının audit kaydı (§52-54)                        |
| `feedbacks`               | Developer geri bildirimleri (§27, §40)                        |

Tüm collection'lar tenant öncelikli compound index'ler ile başlar
(`MongoContext.EnsureIndexes`):

- `exception_definitions`: `(tenantId, fingerprint)` **unique** — dedupe anahtarı
- `exception_occurrences`: `(tenantId, definitionId, occurredAt desc)`
- `knowledge_entries`: `(tenantId, status)`
- `solutions`: `(tenantId, knowledgeEntryId)`
- `ai_analyses`: `(tenantId, createdAt desc)`
- `feedbacks`: `(tenantId, analysisId)`

### 6.2 Exception definition vs occurrence ayrımı (Section 45, 46)

Aynı exception yüz binlerce kez tekrarlanabilir. Her occurrence için embedding
üretmek anlamsız ve maliyetli. Bu nedenle:

```
ExceptionDefinition (1)                ← tek embedding, tek pgvector kaydı
       │
       ├── occurrenceCount: 10 000
       ├── firstSeenAt / lastSeenAt
       └── occurrences (N) ────────── her olay: host, container, endpoint,
                                                statusCode, context, timestamp
```

Bir occurrence, definition'ın fingerprint'i üzerinden upsert edilir. 10.000
occurrence → 1 embedding + 1 pgvector satırı.

### 6.3 Embedding lifecycle

`ExceptionDefinition` ve `KnowledgeEntry` her ikisi de `EmbeddingState` alanı
taşır: `pending → processing → indexed | failed`. API tarafı entity'yi `pending`
oluşturur, worker `indexed`'e taşır. `failed` durumunda son hata mesajı ve retry
sayısı saklanır (Section 42).

### 6.4 PostgreSQL — birleşik `embeddings` tablosu (Section 32, 13, 15)

```sql
CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE embeddings (
    id UUID PRIMARY KEY,
    entity_id       VARCHAR(100) NOT NULL,
    entity_type     VARCHAR(50)  NOT NULL,     -- exception | knowledge | solution
    tenant_id       VARCHAR(100) NOT NULL DEFAULT 'default',
    content         TEXT         NOT NULL,     -- embed edilen kontrollü metin
    embedding       VECTOR(1536) NOT NULL,     -- text-embedding-3-small
    model           VARCHAR(100) NOT NULL,
    model_version   VARCHAR(50)  NOT NULL DEFAULT '1',
    dimensions      INT          NOT NULL,
    metadata        JSONB        NOT NULL DEFAULT '{}'::jsonb,
    created_at      TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX embeddings_entity_uk
    ON embeddings (entity_type, entity_id, tenant_id, model, model_version);

CREATE INDEX embeddings_metadata_gin
    ON embeddings USING GIN (metadata);

CREATE INDEX embeddings_embedding_hnsw
    ON embeddings USING hnsw (embedding vector_cosine_ops);
```

Notlar:

- Tek tabloda `entity_type` üzerinden ayrım (Section 32). Alternatif ayrı
  tablolar da tasarım açısından geçerli; MVP sadelik için birleşik.
- `metadata` JSONB'de `database`, `module`, `category`, `tags` gibi filtre
  değerleri tutulur; GIN index ile `@>` filtresi hızlı çalışır (Section 17).
- HNSW `vector_cosine_ops` — OpenAI vektörleri normalize olduğundan cosine
  distance doğal seçim (Section 14).
- Model/version tuple'ı embedding versiyonlamayı destekler (Section 33) —
  yeni bir modele geçildiğinde eski satırlar migration bitene kadar birlikte
  yaşayabilir.

---

## 7. Exception analiz pipeline'ı (RAG)

`ExceptionAnalysisService` orkestrasyonu (Section 62):

```
POST /api/exceptions/analyze
        │
        ▼
1. SecretRedactor.Redact(exception)                           ← §49, §50
        │
        ▼
2. ExceptionNormalizer.Normalize()                            ← §10
   → normalizedMessage
        │
        ▼
3. FingerprintGenerator.Compute(normalized, type, dbHint)     ← §9
   → SHA256 fingerprint
        │
        ▼
4. UpsertDefinition(tenantId, fingerprint)                    ← §45
   → occurrenceCount++, firstSeen/lastSeen güncellenir
        │
        ▼
5. InsertOccurrence(host, endpoint, statusCode, context)      ← §45
        │
        ▼
6. IEmbeddingService.EmbedAsync(embeddingContent)             ← §11, §12
   → Redis cache kontrolü → OpenAI POST /embeddings
        │
        ▼
7. IVectorSearchService.SearchAsync(vector,                   ← §14, §17, §18, §19
        tenantId, entityType=knowledge,
        topK, minSimilarity, metadataFilter)
   → KNN sorgusu: embedding <=> @vector
        │
        ▼
8. IKnowledgeService.LoadEntries(topIds)                      ← from MongoDB
        │
        ▼
9. PromptBuilder.Build(SYSTEM, TRUSTED_KB, UNTRUSTED_EX)      ← §22, §51
        │
        ▼
10. IChatCompletionService.CompleteAsync(prompt, jsonSchema)  ← §23
    → OpenAI POST /chat/completions (response_format:json_object)
        │
        ▼
11. ConfidenceCalculator.Compute(similarity, support,         ← §24, §25
                                 successRate, llmConfidence)
        │
        ▼
12. Save AiAnalysis (prompt/model version, evidence, tokens,  ← §52, §53, §54
                     per-stage latency)
        │
        ▼
Response
```

Her adım Section 62'deki `ExceptionAnalysisService` yöntemlerine bire bir
karşılık gelir.

---

## 8. Normalizasyon, fingerprint, dedupe

### Normalizasyon (Section 10) — `ExceptionNormalizer`

Volatil parçalar deterministik yer tutuculara çevrilir; anlam korunur, gürültü
kaldırılır:

| Regex                         | Yer tutucu       |
|-------------------------------|------------------|
| GUID                          | `{GUID}`         |
| IPv4                          | `{IP}`           |
| `:12345` gibi portlar         | `{PORT}`         |
| ISO 8601 zaman damgaları      | `{TIMESTAMP}`    |
| `10234ms`, `10s`              | `{DURATION}ms`   |
| 6+ haneli sayı                | `{LARGE_NUMBER}` |
| Windows/Unix mutlak yollar    | `{PATH}`         |
| `http(s)://...`               | `{URL}`          |

Örnek:

```
"Timeout connecting to MongoDB host gdeglbmgo01 after 10000 ms"
    ↓
"Timeout connecting to MongoDB host gdeglbmgo01 after {DURATION}ms"
```

Ana host adları normalize edilmez (semantik değer taşıyabilir); IP/GUID/port
gibi kesin olarak volatil olanlar normalize edilir. Orijinal mesaj *asla*
kaybedilmez — `originalMessage` ile `normalizedMessage` birlikte saklanır
(Section 10).

### Fingerprint (Section 9) — `FingerprintGenerator`

```
fingerprint = SHA256(exceptionType || '\n' || normalizedMessage || '\n' || dbHint)
```

`(tenantId, fingerprint)` unique index'i sayesinde aynı hata ne kadar tekrarlanırsa
tekrarlansın tek `ExceptionDefinition` satırı ve tek embedding üretilir; sadece
`occurrenceCount` ve zaman damgaları güncellenir (Section 44 — maliyet).

---

## 9. Embedding stratejisi

### 9.1 Ne embed ediyoruz? (Section 12) — `EmbeddingInputBuilder`

Sadece mesaj yetmez, gürültü de zararlı. Kontrollü input:

```
Exception exception:
Type: {exceptionType}
Message: {normalizedMessage}
Database: {database}
Module: {module}
Tags: {tag1, tag2, ...}
```

`Knowledge` için:

```
Knowledge entry:
Title: {title}
Exception Types: {types}
Symptoms: {symptoms}
Root Cause: {rootCause}
Tags: {tags}
```

**Dahil edilmeyenler:** hostname, requestId, timestamp, IP, GUID — bunlar
similarity'yi bozar (Section 12).

### 9.2 Embedding cache (Section 34) — `RedisEmbeddingCache`

```
key   = "embedding:" + SHA256(content) + ":" + model + ":" + version
value = binary little-endian float[]
TTL   = 7 gün (varsayılan, `Redis:EmbeddingTtlSeconds`)
```

Aynı normalize edilmiş metin farklı entity'lerde tekrar geçtiğinde OpenAI çağrısı
tekrar yapılmaz. Redis bağlantı string'i boş bırakılırsa cache DI'a hiç
kaydedilmez — sistem cache'siz de çalışır.

### 9.3 Versiyonlama (Section 33)

Embedding üretilirken `model`, `modelVersion`, `dimensions` her satıra yazılır.
`embeddings_entity_uk` unique index'i `(entity_type, entity_id, tenant_id, model,
model_version)` üzerinden — böylece yeni bir modele geçildiğinde eski satırlar
silinmeden yeni satırlar yan yana yaşar, migration deterministik olur.

---

## 10. Vector search + threshold

### 10.1 Sorgu — `PgVectorSearchService`

```sql
SELECT entity_id, content, metadata,
       1 - (embedding <=> @vector) AS similarity
FROM embeddings
WHERE tenant_id = @tenant
  AND entity_type = @type
  AND metadata @> @filterJson
ORDER BY embedding <=> @vector
LIMIT @candidatePoolSize;
```

Candidate pool (`Analysis:VectorCandidatePoolSize`, default 20) → post-filter →
similarity threshold → `TopK`.

### 10.2 Similarity threshold (Section 19)

`Analysis:MinSimilarity` (default `0.75`) altındaki adaylar elenir. `TopK` tek
başına yetmez; alakasız sonuçları elemek için minimum benzerlik zorunludur.
Değer sabit değildir — precision/recall ile tune edilmelidir.

### 10.3 Metadata filtering (Section 17)

Aynı sorgu ekseninde `database=mongodb`, `module=Invoicing` gibi filtreler JSONB
`@>` operatörüyle uygulanır. `embeddings_metadata_gin` index'i bu tür
predicate'leri hızlandırır.

### 10.4 Hybrid search (Section 16, Phase 2)

`ORA-00060`, `SqlException 2627` gibi exact hata kodları için vector search
tek başına ideal değildir. MVP'de sadece vector search var; hybrid search
(BM25/tsvector re-ranking) Phase 2 için planlanmıştır.

---

## 11. LLM prompt tasarımı ve hallucination kontrolü

### 11.1 Prompt katmanları (Section 22, 51) — `PromptBuilder`

Prompt injection saldırılarına karşı üç katman **kesinlikle ayrıştırılır**:

```
┌──────────────────────────────────────┐
│ SYSTEM (TRUSTED)                     │  → PromptBuilder.SystemPromptV1
│  - Kim olduğun, JSON şema, kurallar  │
├──────────────────────────────────────┤
│ TRUSTED KB CONTEXT                   │  → knowledge_entries'ten çekilen kayıtlar
│  - Her biri id + similarity + text   │
├──────────────────────────────────────┤
│ UNTRUSTED EXCEPTION                  │  → developer'dan gelen exception
│  - Ayrı ayrı fence'lenmiş bloklar    │
└──────────────────────────────────────┘
```

Exception metni **hiçbir zaman** system instruction gibi yorumlanmaz; her zaman
"aşağıdaki UNTRUSTED_EXCEPTION içeriğini incele" formatında verilir. Model
"ignore previous instructions..." tarzı içerik görürse bunu veri olarak
değerlendirir.

### 11.2 Zorunlu JSON şema (Section 23) — `OpenAiChatService`

`response_format: { "type": "json_object" }` ile modelin şu şemayı üretmesi
zorunlu kılınır:

```json
{
  "summary": "...",
  "rootCause": "...",
  "rootCauseConfidence": 0.0,
  "known":   [ "..." ],
  "likely":  [ "..." ],
  "unknown": [ "..." ],
  "recommendedChecks":   [ "..." ],
  "recommendedSolutions":[ { "text": "...", "solutionId": null } ],
  "sourceKnowledgeIds":  [ "kb-001" ]
}
```

Şemaya uymayan yanıt parse hatası verir; retry devreye girer.

### 11.3 Known / Likely / Unknown (Section 24)

Sistem prompt'unda model açıkça talimatlandırılır:

> Knowledge base'de kanıt yoksa çözüm uydurma. Bilgini üç kova halinde döndür:
> `known` (kanıtla desteklenen), `likely` (dolaylı çıkarım), `unknown`
> (kanıtsız). "Unknown" boş bırakılamaz.

Bu, tek satırlık bir kural gibi görünse de hallucination'a karşı en güçlü
korumadır.

---

## 12. Confidence hesabı

Section 25'te belirtildiği gibi **LLM'in kendi confidence'ı tek başına
güvenilir bir istatistik değildir.** `ConfidenceCalculator` uygulama düzeyinde
ağırlıklı bir skor hesaplar:

```
applicationConfidence =
    0.45 * topSimilarity              // en yakın kanıtın gücü
  + 0.15 * avgSimilarity              // kanıt kümesinin ortalaması
  + 0.15 * supportFactor              // desteğin kaç KB kaydı üzerinden geldiği
  + 0.10 * solutionSuccessBoost       // ilgili çözümlerin geçmiş başarı oranı
  + 0.15 * llmSelfConfidence          // modelin kendi rootCauseConfidence'ı
```

- `supportFactor = min(1, matchedKnowledgeCount / 3)` — 3+ kanıt tam puan.
- `solutionSuccessBoost` — matched knowledge'a bağlı solution'ların
  `SuccessRate` ortalaması, yoksa 0.5 nötr.

Sonuç `applicationConfidence` alanı olarak yanıta eklenir ve UI karar destek
için kullanır.

---

## 13. Güvenlik: PII/secret redaction ve prompt injection

### 13.1 `SecretRedactor` (Section 49, 50)

Embedding ve LLM çağrısından **önce** çalışan tek bir sanitization noktası:

| Örüntü                                    | Sonuç                                |
|-------------------------------------------|--------------------------------------|
| `Password=…`, `Pwd=…`, `Secret=…`         | `Password={REDACTED}`                |
| `ApiKey=…`, `Api-Key: …`                  | `ApiKey={REDACTED}`                  |
| `Bearer eyJ…` JWT                         | `Bearer {TOKEN_REDACTED}`            |
| E-posta                                   | `{EMAIL_REDACTED}`                   |
| `mongodb://user:pass@host/…` conn string  | `mongodb://{REDACTED}@host/…`        |
| `Server=…;User Id=…;Password=…`           | credentials `{REDACTED}`             |

Ham exception yine de MongoDB'de saklanır (erişim kontrollü). LLM'e ve
pgvector'a giden içerik daima sanitized (Section 50).

### 13.2 Prompt injection (Section 51)

Yukarıda §11.1'de açıklandığı gibi UNTRUSTED katmanı ayrıştırılır; buna ek
olarak exception message'ları LLM'e verilmeden önce control character'lardan
temizlenir ve fence'lenir.

---

## 14. Multi-tenant izolasyon

Section 47'ye göre tenant birinci sınıf filtre:

- Tüm Mongo collection index'lerinin ilk anahtarı `tenantId`.
- `embeddings` tablosunda `tenant_id` NOT NULL, HNSW sorguları tenant WHERE
  clause ile.
- API'de `TenantAccessor`, `X-Tenant-Id` header'ını okur → yoksa
  `Analysis:DefaultTenantId`.
- Fingerprint dedupe'u **tenant'a özel** — aynı hata farklı tenant'larda ayrı
  definition oluşturur.

Global bilgi + tenant-specific bilgi (Section 48) MVP'de yok; Phase 3'te
`tenantId = null` global scope + merge stratejisi eklenmesi planlı.

---

## 15. Feedback ve solution success rate

### Feedback (Section 27, 40)

`POST /api/analyses/{id}/feedback`:

```json
{
  "result": "Resolved",           // Unspecified | Resolved | PartiallyResolved | NotResolved
  "solutionId": "kb-001-sol-a",
  "comment": "Timeout değerini artırınca çözüldü."
}
```

`FeedbackService`:

1. `feedbacks` collection'ına insert eder.
2. `solutionId` verildiyse `Solution.RecordOutcome(result)` çağırır →
   `TimesApplied++`, `Resolved` ise `TimesSuccessful++`, `NotResolved` ise
   `TimesFailed++`.
3. `Solution.SuccessRate = TimesSuccessful / max(1, TimesApplied)`.

Bu skor bir sonraki analiz turunda `ConfidenceCalculator`'a beslenir ve LLM'e
"Solution X %92 vakada işe yaradı" formatında sunulur (Section 26).

---

## 16. Background worker'lar

Section 41-42'ye göre embedding üretimi **HTTP request path'inden ayrılır**:

- API entity'yi `EmbeddingState.Pending` olarak kaydeder, 201 döner.
- `ExceptionEmbeddingWorker` her 5 sn, `KnowledgeEmbeddingWorker` her 10 sn:

```
GetPendingEmbeddingsAsync(batch=20)
        │
        ▼
foreach entity:
   inputBuilder.ForException / ForKnowledge
        │
        ▼
   embedding.EmbedAsync (cache kontrolüyle)
        │
        ▼
   vectors.UpsertAsync(EmbeddingUpsert{ ... })
        │
        ▼
   repo.MarkEmbeddedAsync   veya   repo.MarkFailedAsync(exception.Message)
```

`MarkFailedAsync` retry sayısını artırır ve son hata mesajını saklar. Retry
policy `OpenAiEmbeddingService` içindeki `AddStandardResilienceHandler`
tarafından uygulanır.

`/analyze` endpoint'i **sync** modda çalışır (kullanıcı sonucu hemen alır);
worker'lar mevcut definition ve knowledge kayıtları için asenkron toplu
işlem yürütür. Full async model (Section 64) Phase 2.

---

## 17. Caching stratejisi

Section 57'deki katmanlardan MVP'de sadece **Embedding Cache** aktif:

| Katman            | Durum      | Neden                                                   |
|-------------------|------------|---------------------------------------------------------|
| Embedding cache   | ✅ aktif   | Aynı normalize metin → aynı vektör, çağrı gereksiz      |
| AI response cache | ❌ MVP dışı | Knowledge versiyonu değiştiğinde eski cevap zehirlenir (§36). Doğru key: `ai:{exceptionHash}:{knowledgeVersion}:{promptVersion}:{model}` |
| Search cache      | ❌ MVP dışı | Sık aranan sorgular için Phase 2                        |

Redis bağlantı string'i `Redis:ConnectionString` boş bırakılırsa DI'a hiç
kaydedilmez — sistem tamamen çalışır durumdadır, sadece her request için OpenAI
çağrısı yapılır.

---

## 18. OpenAI hata yönetimi

Section 43. `Microsoft.Extensions.Http.Resilience` üzerinden
`AddStandardResilienceHandler`:

- Retry: 3 deneme, exponential backoff (jitter'lı).
- Timeout: per-request 30 sn, toplam 60 sn.
- Circuit breaker: %50 hata oranı → 30 sn açık.
- Bulkhead: eşzamanlı istek limitleri.

`429 Too Many Requests` özel işlenir — `Retry-After` header'ı honore edilir.
5xx ve timeout'lar retry'lanır; 4xx (auth, invalid request) retry'lanmaz.

---

## 19. Observability, health, metrics

### Health (Section 68)

- `/health/live` — process ayakta mı?
- `/health/ready` — Mongo + Postgres + (Redis varsa) hazır mı? `AspNetCore.
  HealthChecks.NpgSql` + Mongo driver ping + Redis ping.
- `/health` — tüm check'ler, verbose response.

### Loglama (Section 66)

Her analize eklenen structured field'lar:

```
AnalysisId, TenantId, DefinitionId, Fingerprint,
EmbeddingMs, VectorSearchMs, LlmMs, TotalMs,
PromptTokens, CompletionTokens,
Model.Embedding, Model.Chat, PromptVersion, KnowledgeVersion
```

### Metrics (Section 67, planlı)

Prometheus için önerilen sayaçlar (MVP'de logdan türetilebilir, Phase 2'de
export edilecek): `exception_analysis_total`, `embedding_cache_hits_total`,
`vector_search_duration_seconds`, `llm_tokens_total`,
`ai_solution_helpful_total`.

---

## 20. Konfigürasyon

Örnek `appsettings.json`:

```json
{
  "Mongo":    { "ConnectionString": "mongodb://…", "Database": "exception_kb" },
  "Postgres": { "ConnectionString": "Host=…;Database=exception_kb;…" },
  "Redis":    { "ConnectionString": "",
                "EmbeddingTtlSeconds": 604800,
                "AnalysisTtlSeconds": 3600 },
  "OpenAI": {
    "ApiKey": "",
    "BaseUrl": "https://api.openai.com/v1",
    "EmbeddingModel": "text-embedding-3-small",
    "EmbeddingDimensions": 1536,
    "EmbeddingModelVersion": "1",
    "ChatModel": "gpt-4o-mini",
    "ChatMaxTokens": 1024,
    "ChatTemperature": 0.2,
    "PromptVersion": 1
  },
  "Analysis": {
    "TopK": 5,
    "MinSimilarity": 0.75,
    "KnowledgeVersion": 1,
    "DefaultTenantId": "default",
    "VectorCandidatePoolSize": 20
  }
}
```

Docker Compose ortamında `OpenAI__ApiKey` environment variable üzerinden
enjekte edilir. `.NET` config `__` ayracını `:` olarak yorumlar.

---

## 21. Uçtan uca örnek

Section 78'in tam gerçeklemesi:

```
1) POST /api/exceptions/analyze
   X-Tenant-Id: acme

{
  "exceptionType": "MongoConnectionException",
  "message": "Timed out after 10000 ms while waiting for a server that matches ReadPreferenceServerSelector{readPreference=primary}.",
  "service": "invoice-service",
  "environment": "production",
  "database": "MongoDB"
}

2) SecretRedactor      → değişiklik yok (secret bulunmadı)

3) Normalize           → "Timed out after {DURATION}ms while waiting for a
                          server that matches ReadPreferenceServerSelector
                          {readPreference=primary}."

4) Fingerprint         → SHA256(type + normalized + "MongoDB")
                       → 3f4a…c19

5) UpsertDefinition    → occurrenceCount 41 → 42, lastSeenAt = now

6) InsertOccurrence    → host=inv-api-3, endpoint=/api/invoices, status=500

7) EmbedAsync          → cache MISS → OpenAI → [0.021, -0.183, …] 1536-dim
                       → cache SET (TTL 7g)

8) VectorSearch(       → SELECT … WHERE tenant='acme' AND entity_type='knowledge'
       tenant='acme',      AND metadata @> '{"database":"MongoDB"}'
       type=knowledge,     ORDER BY embedding <=> @vec LIMIT 20
       filter={db:MongoDB},
       topK=5, min=0.75)
                       → Aday: KB-001 (0.94), KB-032 (0.88), KB-058 (0.71 ELE)
                       → Kalan: KB-001, KB-032

9) LoadEntries         → Mongo'dan KB-001 + KB-032 tam gövde

10) PromptBuilder      → SYSTEM (kurallar + JSON şema)
                       → TRUSTED KB (KB-001 + KB-032)
                       → UNTRUSTED EXCEPTION (redacted mesaj)

11) OpenAI Chat        → JSON yanıt:
                         summary, rootCause, rootCauseConfidence=0.90,
                         known/likely/unknown, recommendedChecks,
                         recommendedSolutions, sourceKnowledgeIds=[KB-001,KB-032]

12) ConfidenceCalculator
    top=0.94, avg=0.91, support=min(1,2/3)=0.67,
    successBoost=0.85 (KB-001-sol-a %85 başarı), llm=0.90
    → 0.45*0.94 + 0.15*0.91 + 0.15*0.67 + 0.10*0.85 + 0.15*0.90 = 0.855

13) Save AiAnalysis    → ai_analyses collection'a audit:
                         model=gpt-4o-mini, promptVersion=1,
                         knowledgeIds=[KB-001,KB-032],
                         similarities=[0.94,0.88], tokens, latencies

14) Response           → applicationConfidence=0.855, evidence, sources, …
```

---

## 22. Yol haritası (Phase 2-4)

### Phase 2 (Section 73)

- Redis'te AI response + search cache (proper composite key ile — §36).
- Async analiz endpoint'i (`202 + GET /analyses/{id}`) (§64).
- Hybrid search (pgvector + tsvector, re-ranking) (§16).
- Knowledge management UI için ek endpoint'ler (approve/verify flow).
- Rate limiting (§65).

### Phase 3 (Section 74)

- Solution success rate'in ranking'e daha güçlü katılımı.
- Automatic clustering + exception deduplication (fingerprint ötesi).
- Global knowledge + tenant knowledge merge (§48).
- Human approval workflow (AI → draft → reviewer → verified).
- Advanced re-ranking (cross-encoder).
- AI evaluation pipeline (golden dataset, precision/recall, MRR, NDCG) (§70, §71).

### Phase 4 (Section 75)

- Automatic root cause classification.
- Exception trend detection & incident correlation.
- Proactive error detection (streaming ingest → anomaly).
- Suggested monitoring/log queries.
- Runbook recommendation.

---

## 23. Analiz bölüm → kod eşlemesi

| Bölüm | Konu                                    | Nerede                                                   |
|-------|-----------------------------------------|----------------------------------------------------------|
| §4    | MongoDB + pgvector ayrımı               | `MongoContext.cs`, `PgVectorSearchService.cs`            |
| §9    | Fingerprint (SHA256)                    | `Application/Normalization/FingerprintGenerator.cs`      |
| §10   | Exception normalization                 | `Application/Normalization/ExceptionNormalizer.cs`       |
| §12   | Embedding input builder                 | `Application/Analysis/EmbeddingInputBuilder.cs`          |
| §13/15| pgvector şema + HNSW                    | `deploy/postgres/init/001-schema.sql`                    |
| §17   | Metadata filtering (JSONB @>)           | `PgVectorSearchService.cs`                               |
| §19   | Similarity threshold                    | `Analysis:MinSimilarity`, `ExceptionAnalysisService.cs`  |
| §22/51| Prompt katmanları (SYS/KB/UNTRUSTED)    | `Application/Analysis/PromptBuilder.cs`                  |
| §23   | JSON response format                    | `Infrastructure/OpenAI/OpenAiChatService.cs`             |
| §24/25| Known/Likely/Unknown + confidence       | `PromptBuilder.cs` + `ConfidenceCalculator.cs`           |
| §26/27| Solution success rate + feedback        | `Domain/Knowledge/Solution.cs`, `Services/FeedbackService.cs` |
| §32   | Birleşik embeddings tablosu             | `001-schema.sql`, `PgVectorSearchService.cs`             |
| §33   | Embedding model versioning              | `OpenAiEmbeddingService.cs` + `embeddings.model/model_version` |
| §34   | Redis embedding cache                   | `Infrastructure/Caching/RedisEmbeddingCache.cs`          |
| §37-40| API endpoint'leri                       | `Api/Controllers/*`                                      |
| §41/42| Background worker'lar                   | `Worker/ExceptionEmbeddingWorker.cs`, `KnowledgeEmbeddingWorker.cs` |
| §43   | OpenAI retry/circuit breaker            | `Infrastructure/InfrastructureServiceCollectionExtensions.cs` |
| §45/46| Definition vs Occurrence                | `Domain/Exceptions/ExceptionDefinition.cs`, `ExceptionOccurrence.cs` |
| §47   | Multi-tenant                            | `Api/Infrastructure/TenantAccessor.cs`, tüm repo sorguları |
| §49/50| Secret redaction                        | `Application/Security/SecretRedactor.cs`                 |
| §52-54| Audit + prompt/model versioning         | `Domain/Analyses/AiAnalysis.cs`, `PromptBuilder`, `OpenAI*` |
| §58   | Docker Compose                          | `docker-compose.yml`                                     |
| §59   | Solution layout                         | `ExceptionKnowledgeBase.slnx`, `src/`                    |
| §60/61| Application/Infrastructure servisleri   | `Application/Abstractions/*`, `Infrastructure/*`         |
| §62   | Orchestration flow                      | `Application/Services/ExceptionAnalysisService.cs`       |
| §68   | Health checks                           | `Api/Program.cs`                                         |
| §72   | MVP kapsamı                             | Bu depo                                                  |

---

## Lisans / durum

MVP — teknik analizin ilk faz gerçeklemesidir. Production kullanımından önce
Phase 2 (rate limiting, async model, hybrid search) ve Phase 3 (evaluation
pipeline, tenant merge, human approval) gereksinimlerinin gözden geçirilmesi
önerilir.
