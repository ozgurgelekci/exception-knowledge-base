# AI Exception Knowledge Base

`ai_exception_knowledge_base_teknik_analiz.md` (bölüm 59 + 72) tasarımının MVP uygulamasıdır:
exception'ları alan, OpenAI ile embed eden, pgvector üzerinden benzer bilgi
kayıtlarını getiren ve toplanan kanıtlar üzerinden LLM'e akıl yürüten bir .NET 8 backend'i.

## Yapı

```
ExceptionKnowledgeBase.slnx
src/
  ExceptionKnowledgeBase.Api            REST API, Swagger, health check'ler
  ExceptionKnowledgeBase.Application    Orkestrasyon, normalizasyon, promptlar, redaksiyon
  ExceptionKnowledgeBase.Domain         Entity'ler: definition, occurrence, knowledge, solution, analysis, feedback
  ExceptionKnowledgeBase.Infrastructure MongoDB repo'ları, pgvector arama, OpenAI istemcileri, Redis embedding cache
  ExceptionKnowledgeBase.Contracts      İstek/yanıt DTO'ları
  ExceptionKnowledgeBase.Worker         Arka plan embedding pipeline'ı (retry, dead-letter)
deploy/
  postgres/init/001-schema.sql          `CREATE EXTENSION vector` + embeddings tablosu + HNSW index
  api/Dockerfile, worker/Dockerfile
docker-compose.yml                      mongo + pgvector + redis + api + worker
```

## Endpoint'ler

| Method | Route                                      | Amaç                                |
|--------|--------------------------------------------|-------------------------------------|
| POST   | `/api/exceptions/analyze`                  | Alım + AI analizi (Bölüm 37)        |
| POST   | `/api/exceptions/search`                   | Semantik arama (Bölüm 38)           |
| POST   | `/api/knowledge`                           | Bilgi kaydı oluştur (Bölüm 39)      |
| GET    | `/api/knowledge/{id}`                      | Oku                                 |
| PUT    | `/api/knowledge/{id}`                      | Güncelle (yeniden embed eder)       |
| DELETE | `/api/knowledge/{id}`                      | Sil                                 |
| POST   | `/api/analyses/{id}/feedback`              | Geri bildirim gönder (Bölüm 40)     |
| GET    | `/health`, `/health/ready`, `/health/live` | Health check'ler (Bölüm 68)         |

Tenant, `X-Tenant-Id` header'ından çözümlenir; verilmezse `default` kullanılır.

## Yerel çalıştırma

Docker ve bir OpenAI API anahtarı gerekir.

```bash
export OPENAI_API_KEY=sk-...
docker compose up --build
```

- API:      http://localhost:8080/swagger
- MongoDB:  localhost:27017 (ekb/ekb)
- Postgres: localhost:5432 (ekb/ekb, veritabanı `exception_kb`)
- Redis:    localhost:6379

## Tasarım notları (analiz dokümanına bakınız)

- **Bölüm 9/10** — her alımda değişken alanlar (IP, GUID, süre, path) normalize edilir,
  ardından sonuç SHA256'lanır. Fingerprint tenant başına dedupe anahtarıdır.
- **Bölüm 12** — embed edilen içerik `tip + normalize mesaj + veritabanı + tag'ler`'dir;
  ham operasyonel gürültü asla dahil edilmez.
- **Bölüm 15** — pgvector tablosu HNSW cosine index kullanır.
- **Bölüm 22 + 51** — LLM promptu `SYSTEM > TRUSTED KB > UNTRUSTED EXCEPTION`
  ayrımını katı tutar ve JSON şemasını zorlar.
- **Bölüm 24 + 25** — AI yanıtı known/likely/unknown ifadelerini ve uygulama düzeyinde
  bir güven skorunu (`ConfidenceCalculator`) taşır; benzerlik, destek sayısı, çözüm
  başarı oranı ve LLM'in kendi iddiasını birleştirir.
- **Bölüm 34** — OpenAI embedding istemcisi opsiyonel Redis cache'ini
  (`embedding:{sha256}:{model}:{version}` anahtarları) kullanır.
- **Bölüm 42** — `ExceptionEmbeddingWorker` / `KnowledgeEmbeddingWorker` bekleyen
  ya da başarısız embedding'leri retry durumu ile birlikte tekrar işler.
- **Bölüm 47** — tenant, Mongo index'lerinde ve pgvector sorgularında birinci sınıf filtredir.
- **Bölüm 49/50** — `SecretRedactor` embed etmeden, saklamadan veya LLM'e göndermeden
  önce şifre, token, JWT, e-posta ve connection string'leri temizler.
- **Bölüm 52 + 53** — her analiz prompt/model versiyonları, kanıt ID'leri, benzerlik
  skorları, token sayıları ve aşama başına latency ile birlikte saklanır.
