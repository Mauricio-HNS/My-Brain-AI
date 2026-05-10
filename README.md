# NeuroDocs AI

NeuroDocs AI is a document assistant that lets users upload PDF or TXT files, process their content, and ask questions with source snippets returned from the document.

The repository is structured as a microservice-ready monorepo so the web app, backend services, infrastructure, and documentation stay separated.

## Repository Structure

```text
NeuroDocs-AI/
├── apps/
│   └── web/                  # Angular frontend
├── services/
│   └── document-api/         # ASP.NET Core document/RAG API
├── infra/
│   └── docker/               # Docker Compose variants
├── docs/                     # Architecture and project docs
├── docker-compose.yml        # Local full-stack orchestration
└── README.md
```

## Stack

- Frontend: Angular 18
- Backend: ASP.NET Core 8
- PDF extraction: UglyToad.PdfPig
- RAG MVP: local chunking plus keyword retrieval
- Runtime: Docker Compose or local Node/.NET processes

## Run With Docker

From the repository root:

```bash
docker compose up --build
```

Services:

- Web app: http://localhost:4200
- API: http://localhost:5000
- Swagger: http://localhost:5000/swagger

## Run Locally Without Docker

Start the API:

```bash
cd services/document-api
dotnet restore
dotnet run --urls http://localhost:5000
```

Start the frontend in another terminal:

```bash
cd apps/web
npm install
npm start
```

Then open http://localhost:4200.

## API Endpoints

| Method | Path | Description |
| --- | --- | --- |
| `GET` | `/` | API health/status response. |
| `POST` | `/api/documents/upload` | Upload and process a PDF or TXT document. |
| `GET` | `/api/documents` | List uploaded documents. |
| `GET` | `/api/documents/{id}` | Get document details and chunks. |
| `POST` | `/api/chat` | Ask a question about a document. |

## Architecture Notes

The current `document-api` owns ingestion, in-memory storage, chunking, retrieval, and chat response generation. This is good for an MVP, while the folder structure keeps the project ready to split responsibilities into dedicated services later.

Recommended next production services:

- `identity-api` for authentication and organizations.
- `ingestion-worker` for PDF/OCR processing in the background.
- `rag-api` for embeddings, vector search, and LLM calls.
- `storage-api` for file metadata and object storage.

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the suggested evolution path.

## Production Upgrade Checklist

- Replace in-memory storage with PostgreSQL.
- Store uploaded files in S3, Azure Blob Storage, Cloudflare R2, or equivalent.
- Add embeddings with OpenAI or Azure OpenAI.
- Add a vector database such as pgvector, Qdrant, Pinecone, or Azure AI Search.
- Add authentication and authorization.
- Move document processing to background jobs.
- Add CI checks for backend build and frontend build.
