# DTCE API

**Document Template & Context Extractor API**

A comprehensive .NET-based platform for extracting structured templates and contextual metadata from unstructured documents (DOCX, PDF, Google Docs).

## Overview

DTCE ingests unstructured documents and produces two machine-consumable artifacts:

- **Template JSON** – a structural representation of the document suitable for document generation engines
- **Context JSON** – contextual metadata and extracted content required to populate the template

Third-party vendors integrate via a secure API to automate document understanding and generation workflows.

## Architecture

This is a microservices-based architecture with the following components:

- **Dtce.ApiGateway** - REST API gateway for third-party integrations
- **Dtce.WebClient** - Web portal for user management and job monitoring
- **Dtce.IngestionService** - Document ingestion worker service
- **Dtce.ParsingEngine** - Document parsing and structure extraction
- **Dtce.AnalysisEngine** - NLP and computer vision analysis
- **Dtce.AzureFunctions** - Serverless functions for job orchestration
- **Dtce.Common** - Shared models and contracts
- **Dtce.Persistence** - Storage abstractions (Azure Blob Storage, Table Storage)
- **Dtce.JobQueue** - Message queue abstractions (Azure Service Bus)
- **Dtce.Identity** - User authentication and API key management
- **Dtce.DocumentRendering** - Template-based document generation

## Prerequisites

- .NET 9.0 SDK
- PowerShell 5.1+ (Windows)
- Azure CLI (for Azure deployment)
- Git (for version control)

## Quick Start

### Local Development

1. **Clone the repository**
   ```powershell
   git clone <repository-url>
   cd "DTCE API"
   ```

2. **Restore dependencies**
   ```powershell
   dotnet restore
   ```

3. **Build the solution**
   ```powershell
   dotnet build
   ```

4. **Run services locally**
   ```powershell
   # Start API Gateway
   .\start-api-gateway.ps1
   
   # Start Web Client
   .\start-web-client.ps1
   
   # Start Worker Services
   .\start-workers.ps1
   ```

### Configuration

The system supports two modes:
- **Dev Mode**: Uses local file system for storage and file-based queues
- **Azure Mode**: Uses Azure Blob Storage, Table Storage, and Service Bus

Configure mode in `appsettings.Development.json` files for each service.

## Documentation

- [API Documentation](API_DOCUMENTATION.md) - Complete API reference for third-party integrations
- [Architecture](ARCHITECTURE.md) - System architecture and design decisions
- [Deployment Guide](DEPLOYMENT.md) - Local, production, and Azure deployment instructions
- [Requirements](REQUIREMENTS.md) - Functional and non-functional requirements

## Features

- ✅ Multi-format document support (DOCX, PDF, Google Docs)
- ✅ Asynchronous job processing
- ✅ RESTful API with API key authentication
- ✅ Web portal for user management
- ✅ Template and context JSON extraction
- ✅ Azure cloud deployment ready
- ✅ Local development mode support

## Project Structure

```
DTCE API/
├── Dtce.ApiGateway/          # REST API Gateway
├── Dtce.WebClient/            # Web portal (MVC)
├── Dtce.IngestionService/     # Document ingestion worker
├── Dtce.ParsingEngine/        # Document parsing engine
├── Dtce.AnalysisEngine/       # NLP/CV analysis engine
├── Dtce.AzureFunctions/       # Azure Functions
├── Dtce.Common/               # Shared models
├── Dtce.Persistence/          # Storage abstractions
├── Dtce.JobQueue/             # Message queue abstractions
├── Dtce.Identity/             # Authentication & authorization
├── Dtce.DocumentRendering/    # Document generation
├── Dtce.Infrastructure/       # Infrastructure abstractions
├── Dtce.Tests/                # Unit and integration tests
├── infrastructure/             # Azure IaC (Bicep)
└── SampleDocs/                 # Sample documents for testing
```

## Testing

Run the test suite:

```powershell
dotnet test
```

## Deployment

See [DEPLOYMENT.md](DEPLOYMENT.md) for detailed deployment instructions.

### Azure Deployment

```powershell
.\infrastructure\azure-deploy.ps1
```

## License

[Specify your license here]

## Contributing

[Contributing guidelines]

## Support

[Support information]

