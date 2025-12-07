# DTCE API – High Level Requirements

This document summarises the core capabilities of the Document Template & Context Extraction (DTCE) platform. It acts as a quick reference for stakeholders and engineers.

## Purpose

DTCE ingests unstructured documents (DOCX, PDF, Google Docs) and produces two machine-consumable artefacts:

- **Template JSON** – a structural representation of the document suitable for document generation engines.
- **Context JSON** – contextual metadata and extracted content required to populate the template.

Third-party vendors integrate via a secure API to automate document understanding and generation workflows.

## Functional Requirements

1. **User Accounts & Authentication**
   - Users can self-register with an email address and password.
   - Authentication is cookie-based for the WebClient portal.
   - Credentials are stored with salted bcrypt hashes in Azure Table Storage.

2. **API Key Management**
   - Authenticated users can create, view, and revoke API keys via the WebClient portal.
   - API keys are persisted in Azure Table Storage and are validated for every API call.
   - Revoked keys become immediately unusable.

3. **Document Submission API**
   - Endpoint: `POST /api/v1/jobs/submit`
   - Accepts either a binary document upload (`multipart/form-data`) or a publicly accessible document URL.
   - Supported formats: DOCX, PDF, Google Docs (URL).
   - Maximum file size: 50 MB.
   - Requires `X-API-Key` header.
   - Returns `202 Accepted` and a job identifier.

4. **Job Status API**
   - Endpoint: `GET /api/v1/jobs/{jobId}/status`
   - Returns real-time processing status and descriptive messages.
   - Requires `X-API-Key` header.

5. **Job Results API**
   - Endpoint: `GET /api/v1/jobs/{jobId}/results`
   - Returns signed URLs for the generated Template JSON and Context JSON (valid for 1 hour).
   - Returns `202 Accepted` when processing is incomplete.
   - Requires `X-API-Key` header.

6. **Processing Pipeline**
   - Jobs are enqueued on Azure Service Bus (`job-requests` queue).
   - Worker services (Ingestion, Parsing, Analysis) consume messages, orchestrate extraction, and persist outputs to Azure Blob Storage and Table Storage.

7. **WebClient Portal**
   - Provides guided workflow to submit documents, monitor job status, and download results using the user’s API key.
   - Built with ASP.NET Core MVC targeting .NET 9.

## Non-Functional Requirements

- **Scalability**: Deployed on Azure App Service and Azure Functions with shared App Service Plan for WebClient, API Gateway, and Functions.
- **Security**: Enforces HTTPS, API key authentication, and uses Azure-managed identities (system-assigned) for App Services.
- **Persistence**:
  - Azure Blob Storage (`dtce-documents` container) for uploaded files and results.
  - Azure Table Storage (`Users`, `ApiKeys`, `JobStatus`) for identity, key management, and job tracking.
- **Messaging**: Azure Service Bus Standard namespace with partitioned queue for job orchestration.
- **Observability**: Logs emitted via ASP.NET Core logging stack; Application Insights enabled for Functions (extendable to other services).

## Deployment Overview

Infrastructure (defined in `infrastructure/main.bicep`):

- Storage Account (blob + table services).
- Service Bus namespace, shared access policy, and `job-requests` queue.
- Linux App Service Plan (Basic tier).
- Web Apps:
  - `Dtce.WebClient` (user portal).
  - `Dtce.ApiGateway` (REST API).
- Azure Function App (`Dtce.AzureFunctions`) for API endpoints and long-running orchestration.

Deployment script (`infrastructure/azure-deploy.ps1`):

- Supports interactive or service principal authentication.
- Deploys infrastructure, publishes .NET projects, and applies ZIP deployments to web/Function apps.
- Reads optional JSON configuration (`deploy.settings.json`) for credentials and environment overrides.

## Integration Checklist for Vendors

1. Obtain API key through the WebClient portal.
2. Submit document using `POST /api/v1/jobs/submit` with `X-API-Key`.
3. Poll `GET /api/v1/jobs/{jobId}/status` until status becomes `Complete`.
4. Retrieve `templateJsonUrl` and `contextJsonUrl` from `GET /api/v1/jobs/{jobId}/results`.
5. Download JSON artefacts (links valid for 60 minutes) and feed into downstream document generation tools.

## Outstanding Considerations

- Packaging and hosting the worker services (Ingestion, Parsing, Analysis) as Azure Container Apps or other long-running compute remains part of production rollout.
- Secrets such as Service Bus secondary keys should be rotated via Azure Key Vault and referenced using managed identities.
- Additional monitoring, alerting, and automated scaling policies should be configured per production SLA requirements.

