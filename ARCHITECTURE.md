# DTCE API - System Architecture & Technical Documentation

This document provides a comprehensive technical overview of the DTCE (Document Template & Context Extractor) system, including service architecture, data flow, method descriptions, and implementation details.

## Table of Contents

1. [System Overview](#system-overview)
2. [Architecture Diagram](#architecture-diagram)
3. [Service Descriptions](#service-descriptions)
4. [Data Flow & Sequencing](#data-flow--sequencing)
5. [Core Interfaces & Abstractions](#core-interfaces--abstractions)
6. [Message Queue System](#message-queue-system)
7. [Storage System](#storage-system)
8. [Document Processing Pipeline](#document-processing-pipeline)
9. [Key Methods & Responsibilities](#key-methods--responsibilities)
10. [Error Handling & Status Management](#error-handling--status-management)

---

## System Overview

The DTCE API is a distributed, asynchronous document processing system that extracts templates and context from documents (Word, PDF, Google Docs). The system follows a microservices architecture with clear separation of concerns:

- **API Gateway**: Entry point for external requests
- **Ingestion Service**: Validates and prepares documents
- **Parsing Engine**: Extracts document structure and content
- **Analysis Engine**: Performs NLP and computer vision analysis
- **Web Client**: User interface for job submission and monitoring

### Key Design Patterns

- **Message Queue Pattern**: Asynchronous job processing via queues
- **Repository Pattern**: Abstraction over storage implementations
- **Strategy Pattern**: Document handlers for different file types
- **Dependency Injection**: Loose coupling between components
- **Factory Pattern**: Service selection based on platform mode (Dev/Prod)

---

## Architecture Diagram

```
┌─────────────────┐
│   Web Client    │  (ASP.NET Core MVC)
│  (Port 5091)    │
└────────┬────────┘
         │ HTTP
         ▼
┌─────────────────┐
│  API Gateway     │  (ASP.NET Core Web API)
│  (Port 5017)     │
└────────┬────────┘
         │
         ├──► IMessageProducer.PublishAsync()
         │    Queue: "job-requests"
         │
         ├──► IObjectStorage.UploadFileAsync()
         │    Store: documents/{jobId}/{filename}
         │
         └──► IJobStatusRepository.CreateJobAsync()
              Status: Pending

         ▼
┌─────────────────────────────────────────────────┐
│           Message Queue System                  │
│  (Azure Service Bus / File-based Queues)       │
│                                                 │
│  ┌──────────────┐  ┌──────────────┐           │
│  │job-requests  │  │parsing-jobs   │           │
│  └──────────────┘  └──────────────┘           │
│                          │                     │
│                          ▼                     │
│                  ┌──────────────┐              │
│                  │analysis-jobs │              │
│                  └──────────────┘              │
└─────────────────────────────────────────────────┘
         │
         ├─────────────────┬─────────────────┐
         ▼                 ▼                 ▼
┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│  Ingestion   │  │   Parsing    │  │   Analysis   │
│   Service    │  │    Engine    │  │    Engine   │
│              │  │              │  │             │
│  Worker.cs   │  │  Worker.cs   │  │  Worker.cs  │
└──────────────┘  └──────────────┘  └──────────────┘
         │                 │                 │
         └─────────────────┴─────────────────┘
                           │
                           ▼
              ┌────────────────────────┐
              │   Object Storage        │
              │  (Azure Blob / Local)   │
              │                         │
              │  - documents/           │
              │  - parsed/              │
              │  - results/             │
              └────────────────────────┘
                           │
                           ▼
              ┌────────────────────────┐
              │  Job Status Repository  │
              │ (Azure Table / Local)   │
              │                         │
              │  - JobStatus table      │
              └────────────────────────┘
```

---

## Service Descriptions

### 1. API Gateway (`Dtce.ApiGateway`)

**Type**: ASP.NET Core Web API  
**Port**: 5017 (local), Auto (Azure)  
**Purpose**: Entry point for all external API requests

#### Key Responsibilities

1. **Request Validation**: Validates file types, sizes, and API keys
2. **Job Creation**: Creates job records and publishes to message queue
3. **Status Queries**: Retrieves job status from repository
4. **Result Delivery**: Generates pre-signed URLs or returns JSON content
5. **File Serving**: Serves files from object storage with proper headers

#### Main Components

- **`JobsController`**: REST API endpoints
  - `POST /api/v1/jobs/submit`: Submit a document for processing
  - `GET /api/v1/jobs/{jobId}/status`: Get job status
  - `GET /api/v1/jobs/{jobId}/results`: Get job results
  - `GET /api/v1/jobs/files/{fileKey}`: Download/view files

- **`ApiKeyAuthorizeAttribute`**: Custom authorization filter for API key validation

#### Key Methods

**`SubmitJob`** (`JobsController.cs:34`)
```csharp
public async Task<IActionResult> SubmitJob([FromForm] IFormFile? document, [FromForm] string? documentUrl)
```
- Validates input (file type, size, URL format)
- Generates unique job ID (GUID)
- Uploads document to object storage (if file provided)
- Creates job status record (Pending)
- Publishes `JobRequest` to "job-requests" queue
- Returns 202 Accepted with job ID and status URL

**`GetJobStatus`** (`JobsController.cs:142`)
```csharp
public async Task<IActionResult> GetJobStatus(string jobId)
```
- Retrieves job status from `IJobStatusRepository`
- Returns current status, message, and completion timestamp

**`GetJobResults`** (`JobsController.cs:165`)
```csharp
public async Task<IActionResult> GetJobResults(string jobId, [FromQuery] bool includeContent = false)
```
- Checks if job is complete
- Generates pre-signed URLs for template.json and context.json
- If `includeContent=true`, returns JSON content directly in response
- Returns 202 Accepted if job still processing

**`GetFile`** (`JobsController.cs:229`)
```csharp
public async Task<IActionResult> GetFile(string fileKey, [FromQuery] bool download = false)
```
- Downloads file from object storage
- Sets appropriate Content-Type based on file extension
- Sets Content-Disposition header (inline for viewing, attachment for download)
- For JSON files, omits Content-Disposition to allow browser viewing

---

### 2. Ingestion Service (`Dtce.IngestionService`)

**Type**: .NET Background Service (Hosted Service)  
**Purpose**: Validates documents and prepares them for parsing

#### Key Responsibilities

1. **Message Consumption**: Listens to "job-requests" queue
2. **Document Validation**: Verifies document exists and is accessible
3. **URL Fetching**: (Future) Fetches documents from Google Docs URLs
4. **Queue Forwarding**: Publishes validated jobs to "parsing-jobs" queue

#### Main Components

- **`Worker.cs`**: Background service that consumes messages
- **`IngestionServiceHost.cs`**: Service configuration and dependency injection

#### Key Methods

**`ExecuteAsync`** (`Worker.cs:29`)
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
```
- Starts consuming messages from "job-requests" queue
- For each message, calls the handler function

**Message Handler** (`Worker.cs:36`)
```csharp
async (jobRequest, ct) => { ... }
```
1. Updates job status to `Processing` with message "Document ingestion in progress"
2. If `DocumentUrl` provided: (Future) Fetches document from URL
3. If `FilePath` provided: Validates file exists in object storage
4. If file not found: Updates status to `Failed` with error message
5. If validation succeeds: Updates status to `ParsingInProgress`
6. Publishes `JobRequest` to "parsing-jobs" queue

#### Dependencies

- `IMessageConsumer`: Consumes from "job-requests" queue
- `IMessageProducer`: Publishes to "parsing-jobs" queue
- `IJobStatusRepository`: Updates job status
- `IObjectStorage`: Validates document existence

---

### 3. Parsing Engine (`Dtce.ParsingEngine`)

**Type**: .NET Background Service (Hosted Service)  
**Purpose**: Extracts document structure, content, and visual theme

#### Key Responsibilities

1. **Message Consumption**: Listens to "parsing-jobs" queue
2. **Document Parsing**: Delegates to appropriate handler (Docx, PDF, GoogleDocs)
3. **Structure Extraction**: Extracts sections, subsections, content blocks
4. **Visual Theme Extraction**: Extracts fonts, colors, styling
5. **Result Storage**: Stores parsed data as JSON
6. **Queue Forwarding**: Publishes to "analysis-jobs" queue

#### Main Components

- **`Worker.cs`**: Background service that consumes messages
- **`DocumentParser.cs`**: Orchestrates parsing by delegating to handlers
- **`IDocumentHandler`**: Interface for document-specific parsers
  - `DocxHandler`: Parses Word documents using OpenXML
  - `PdfHandler`: Parses PDF documents
  - `GoogleDocsHandler`: Parses Google Docs via API

#### Key Methods

**`ExecuteAsync`** (`Worker.cs:32`)
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
```
- Starts consuming messages from "parsing-jobs" queue

**Message Handler** (`Worker.cs:38`)
```csharp
async (jobRequest, ct) => { ... }
```
1. Updates job status to `ParsingInProgress`
2. Calls `IDocumentParser.ParseAsync(jobRequest, ct)`
3. Stores `ParseResult` as JSON at `parsed/{jobId}/parse-result.json`
4. Updates status to `AnalysisInProgress`
5. Creates `AnalysisJob` with job ID and parse result key
6. Publishes `AnalysisJob` to "analysis-jobs" queue

**`ParseAsync`** (`DocumentParser.cs`)
```csharp
public async Task<ParseResult> ParseAsync(JobRequest jobRequest, CancellationToken cancellationToken)
```
- Resolves appropriate `IDocumentHandler` based on `DocumentType`
- Calls handler's `ParseAsync` method
- Returns `ParseResult` containing:
  - `Sections`: Document structure with nested subsections
  - `ContentSections`: Text content with placeholders
  - `TemplateJson`: Document template with visual theme
  - `VisualTheme`: Fonts, colors, styling information

#### Document Handlers

**`DocxHandler.ParseAsync`**
- Uses OpenXML SDK to read Word document
- Extracts paragraphs, headings, tables
- Detects styles and formatting
- Extracts images and logos
- Builds hierarchical section structure

**`PdfHandler.ParseAsync`**
- Uses PDF parsing library
- Extracts text, structure, and metadata
- Handles PDF-specific formatting

**`GoogleDocsHandler.ParseAsync`**
- Uses Google Docs API
- Fetches document content
- Converts to structured format

---

### 4. Analysis Engine (`Dtce.AnalysisEngine`)

**Type**: .NET Background Service (Hosted Service)  
**Purpose**: Performs NLP and computer vision analysis, generates final JSON files

#### Key Responsibilities

1. **Message Consumption**: Listens to "analysis-jobs" queue
2. **NLP Analysis**: Analyzes text for formality, tone, writing style
3. **CV Analysis**: Detects logos and visual elements
4. **JSON Generation**: Creates final template.json and context.json
5. **Job Completion**: Updates job status to Complete

#### Main Components

- **`Worker.cs`**: Background service that consumes messages
- **`NlpAnalyzer`**: Implements `INlpAnalyzer` for text analysis
- **`ComputerVisionAnalyzer`**: Implements `IComputerVisionAnalyzer` for logo detection

#### Key Methods

**`ExecuteAsync`** (`Worker.cs:35`)
```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
```
- Starts consuming messages from "analysis-jobs" queue

**Message Handler** (`Worker.cs:41`)
```csharp
async (analysisJob, ct) => { ... }
```
1. Updates job status to `AnalysisInProgress`
2. Loads `ParseResult` from object storage using `ParseResultKey`
3. Performs NLP analysis on all text content
4. Performs CV analysis for logo detection
5. Builds final `TemplateJson`:
   - Merges parse result template with logo map from CV analysis
6. Builds final `ContextJson`:
   - Adds `LinguisticStyle` from NLP analysis
   - Adds `ContentBlocks` from parse result
7. Stores both JSON files:
   - `results/{jobId}/template.json`
   - `results/{jobId}/context.json`
8. Updates job status to `Complete` with result file keys

**`AnalyzeAsync`** (`INlpAnalyzer`)
```csharp
public async Task<NlpAnalysisResult> AnalyzeAsync(string text, CancellationToken cancellationToken)
```
- Analyzes text for:
  - Formality level (formal/informal)
  - Tone (professional, casual, etc.)
  - Writing style vector
- Returns confidence scores for each attribute

**`DetectLogosAsync`** (`IComputerVisionAnalyzer`)
```csharp
public async Task<List<LogoAsset>> DetectLogosAsync(ParseResult parseResult, CancellationToken cancellationToken)
```
- Processes images from parse result
- Detects logos using computer vision
- Returns list of `LogoAsset` objects with bounding boxes and storage keys

---

### 5. Web Client (`Dtce.WebClient`)

**Type**: ASP.NET Core MVC  
**Port**: 5091 (local), Auto (Azure)  
**Purpose**: User interface for job submission and monitoring

#### Key Responsibilities

1. **User Interface**: Provides web UI for document submission
2. **Job Submission**: Calls API Gateway to submit jobs
3. **Status Polling**: Polls job status and displays results
4. **History Management**: Tracks and displays job history
5. **API Key Management**: Allows users to create and manage API keys

#### Main Components

- **`HomeController`**: Main UI controller
  - `Index`: Job submission form
  - `SubmitJob`: Submits job via API
  - `GetJobStatus`: Polls job status
  - `History`: Displays job history
- **`ApiKeyController`**: API key management
- **`JobHistoryService`**: Persists and retrieves job history

#### Key Methods

**`SubmitJob`** (`HomeController.cs:39`)
```csharp
public async Task<IActionResult> SubmitJob(IFormFile? document, string? documentUrl, string inputType, string? apiKey)
```
- Validates input and API key
- Calls `DtceApiService.SubmitJobAsync`
- Saves job to history
- Returns JSON response with job ID

**`GetJobStatus`** (`HomeController.cs:82`)
```csharp
public async Task<IActionResult> GetJobStatus(string jobId, string? apiKey)
```
- Calls `DtceApiService.GetJobStatusAsync`
- If complete, retrieves results
- Updates job history
- Returns JSON with status and result URLs

---

## Data Flow & Sequencing

### Complete Job Processing Flow

```
1. USER SUBMITS DOCUMENT
   └─► Web Client or API Client
       └─► POST /api/v1/jobs/submit
           └─► API Gateway: JobsController.SubmitJob()
               ├─► Validates file/URL
               ├─► Generates JobId (GUID)
               ├─► IObjectStorage.UploadFileAsync()
               │   └─► Store: documents/{jobId}/{filename}
               ├─► IJobStatusRepository.CreateJobAsync()
               │   └─► Status: Pending, Message: "Job created and queued"
               └─► IMessageProducer.PublishAsync("job-requests", JobRequest)
                   └─► Queue: "job-requests"
                       └─► Returns 202 Accepted with JobId

2. INGESTION SERVICE PROCESSES
   └─► IngestionService.Worker consumes "job-requests"
       ├─► IJobStatusRepository.UpdateJobStatusAsync()
       │   └─► Status: Processing, Message: "Document ingestion in progress"
       ├─► IObjectStorage.DownloadFileAsync() [Validation]
       │   └─► Verify document exists
       ├─► IJobStatusRepository.UpdateJobStatusAsync()
       │   └─► Status: ParsingInProgress, Message: "Document validated"
       └─► IMessageProducer.PublishAsync("parsing-jobs", JobRequest)
           └─► Queue: "parsing-jobs"

3. PARSING ENGINE PROCESSES
   └─► ParsingEngine.Worker consumes "parsing-jobs"
       ├─► IJobStatusRepository.UpdateJobStatusAsync()
       │   └─► Status: ParsingInProgress, Message: "Parsing document structure"
       ├─► IDocumentParser.ParseAsync(JobRequest)
       │   ├─► Resolves IDocumentHandler (DocxHandler/PdfHandler/GoogleDocsHandler)
       │   ├─► Handler.ParseAsync() extracts:
       │   │   ├─► Sections (hierarchical structure)
       │   │   ├─► Content blocks with placeholders
       │   │   ├─► Visual theme (fonts, colors)
       │   │   └─► Images/logos
       │   └─► Returns ParseResult
       ├─► IObjectStorage.UploadFileAsync()
       │   └─► Store: parsed/{jobId}/parse-result.json
       ├─► IJobStatusRepository.UpdateJobStatusAsync()
       │   └─► Status: AnalysisInProgress, Message: "Document parsed"
       └─► IMessageProducer.PublishAsync("analysis-jobs", AnalysisJob)
           └─► Queue: "analysis-jobs"

4. ANALYSIS ENGINE PROCESSES
   └─► AnalysisEngine.Worker consumes "analysis-jobs"
       ├─► IJobStatusRepository.UpdateJobStatusAsync()
       │   └─► Status: AnalysisInProgress, Message: "Performing NLP and CV analysis"
       ├─► IObjectStorage.DownloadFileAsync()
       │   └─► Load: parsed/{jobId}/parse-result.json
       ├─► INlpAnalyzer.AnalyzeAsync()
       │   └─► Analyzes text for formality, tone, style
       ├─► IComputerVisionAnalyzer.DetectLogosAsync()
       │   └─► Detects logos in images
       ├─► Build TemplateJson
       │   ├─► Merge ParseResult.TemplateJson
       │   └─► Add LogoMap from CV analysis
       ├─► Build ContextJson
       │   ├─► Add LinguisticStyle from NLP analysis
       │   └─► Add ContentBlocks from ParseResult
       ├─► IObjectStorage.UploadFileAsync()
       │   ├─► Store: results/{jobId}/template.json
       │   └─► Store: results/{jobId}/context.json
       └─► IJobStatusRepository.UpdateJobCompletionAsync()
           └─► Status: Complete, Message: "Job completed successfully"

5. USER RETRIEVES RESULTS
   └─► GET /api/v1/jobs/{jobId}/results
       └─► API Gateway: JobsController.GetJobResults()
           ├─► IJobStatusRepository.GetJobStatusAsync()
           │   └─► Check Status == Complete
           ├─► IObjectStorage.GeneratePreSignedUrlAsync()
           │   ├─► template.json URL (valid 1 hour)
           │   └─► context.json URL (valid 1 hour)
           └─► Returns JSON with URLs (or content if includeContent=true)
```

### Status Transitions

```
Pending
  └─► Processing (Ingestion Service)
      └─► ParsingInProgress (Parsing Engine)
          └─► AnalysisInProgress (Analysis Engine)
              └─► Complete (Analysis Engine)

Any Status
  └─► Failed (on error in any service)
```

---

## Core Interfaces & Abstractions

### IMessageProducer

**Purpose**: Publishes messages to queues

**Interface** (`Dtce.JobQueue/IMessageProducer.cs`):
```csharp
Task PublishAsync<T>(string topic, T message, CancellationToken cancellationToken = default) where T : class;
```

**Implementations**:
- **`AzureServiceBusProducer`**: Publishes to Azure Service Bus queues
- **`FileQueueMessageProducer`**: Writes messages to local file system (Dev mode)

**Usage**:
```csharp
await _messageProducer.PublishAsync("job-requests", jobRequest, cancellationToken);
```

---

### IMessageConsumer

**Purpose**: Consumes messages from queues

**Interface** (`Dtce.JobQueue/IMessageConsumer.cs`):
```csharp
Task StartConsumingAsync<T>(string topic, Func<T, CancellationToken, Task> handler, CancellationToken cancellationToken = default) where T : class;
Task StopConsumingAsync(CancellationToken cancellationToken = default);
```

**Implementations**:
- **`AzureServiceBusConsumer`**: Consumes from Azure Service Bus queues
- **`FileQueueMessageConsumer`**: Reads messages from local file system (Dev mode)

**Usage**:
```csharp
await _messageConsumer.StartConsumingAsync<JobRequest>(
    "job-requests",
    async (jobRequest, ct) => {
        // Process message
    },
    stoppingToken);
```

---

### IObjectStorage

**Purpose**: Abstracts file storage (Azure Blob Storage or local file system)

**Interface** (`Dtce.Persistence/IObjectStorage.cs`):
```csharp
Task<string> UploadFileAsync(string fileName, Stream fileStream, string contentType, CancellationToken cancellationToken = default);
Task<string> GeneratePreSignedUrlAsync(string fileKey, TimeSpan expiration, CancellationToken cancellationToken = default);
Task<Stream> DownloadFileAsync(string fileKey, CancellationToken cancellationToken = default);
Task DeleteFileAsync(string fileKey, CancellationToken cancellationToken = default);
```

**Implementations**:
- **`AzureBlobStorage`**: Stores files in Azure Blob Storage
- **`FileSystemObjectStorage`**: Stores files in local file system (Dev mode)

**Key Methods**:

**`UploadFileAsync`**
- Uploads file stream to storage
- Returns file key/path for later retrieval
- In Dev mode: Returns relative path
- In Prod mode: Returns blob name

**`DownloadFileAsync`**
- Downloads file as stream
- Handles both file:// URIs and relative paths
- Throws `FileNotFoundException` if file doesn't exist

**`GeneratePreSignedUrlAsync`**
- Generates time-limited URL for file access
- In Dev mode: Returns HTTP URL pointing to API Gateway file endpoint
- In Prod mode: Returns Azure Blob SAS URL

---

### IJobStatusRepository

**Purpose**: Manages job status and metadata

**Interface** (`Dtce.Persistence/IJobStatusRepository.cs`):
```csharp
Task<JobStatusResponse?> GetJobStatusAsync(string jobId, CancellationToken cancellationToken = default);
Task CreateJobAsync(string jobId, JobStatus status, string statusMessage, CancellationToken cancellationToken = default);
Task UpdateJobStatusAsync(string jobId, JobStatus status, string statusMessage, CancellationToken cancellationToken = default);
Task UpdateJobCompletionAsync(string jobId, string templateJsonUrl, string contextJsonUrl, CancellationToken cancellationToken = default);
Task UpdateJobErrorAsync(string jobId, string errorMessage, CancellationToken cancellationToken = default);
```

**Implementations**:
- **`AzureTableStorageJobRepository`**: Stores in Azure Table Storage
- **`FileSystemJobStatusRepository`**: Stores in local JSON files (Dev mode)

**Key Methods**:

**`CreateJobAsync`**
- Creates initial job record with Pending status
- Called by API Gateway on job submission

**`UpdateJobStatusAsync`**
- Updates job status and message
- Used throughout pipeline to track progress

**`UpdateJobCompletionAsync`**
- Marks job as Complete
- Stores result file keys (template.json, context.json)
- Sets completion timestamp

**`UpdateJobErrorAsync`**
- Marks job as Failed
- Stores error message
- Called on exceptions in any service

---

### IDocumentParser

**Purpose**: Orchestrates document parsing

**Interface** (`Dtce.ParsingEngine/IDocumentParser.cs`):
```csharp
Task<ParseResult> ParseAsync(JobRequest jobRequest, CancellationToken cancellationToken);
```

**Implementation**: **`DocumentParser`**

**Key Method**:

**`ParseAsync`** (`DocumentParser.cs`)
```csharp
public async Task<ParseResult> ParseAsync(JobRequest jobRequest, CancellationToken cancellationToken)
```
1. Determines document type from `JobRequest.DocumentType`
2. Resolves appropriate `IDocumentHandler` from DI container
3. Calls handler's `ParseAsync` method
4. Returns `ParseResult` with sections, content, and visual theme

---

### IDocumentHandler

**Purpose**: Document-specific parsing logic

**Interface** (`Dtce.ParsingEngine/Handlers/IDocumentHandler.cs`):
```csharp
Task<ParseResult> ParseAsync(JobRequest jobRequest, IObjectStorage objectStorage, CancellationToken cancellationToken);
```

**Implementations**:
- **`DocxHandler`**: Parses Word documents using OpenXML SDK
- **`PdfHandler`**: Parses PDF documents
- **`GoogleDocsHandler`**: Parses Google Docs via API

**Key Methods** (DocxHandler example):

**`ParseAsync`**
- Downloads document from object storage
- Uses OpenXML to read document structure
- Extracts paragraphs, headings, tables
- Detects styles and formatting
- Extracts images
- Builds hierarchical section structure
- Returns `ParseResult`

---

## Message Queue System

### Queue Names

1. **`job-requests`**: Initial job submissions from API Gateway
2. **`parsing-jobs`**: Validated jobs ready for parsing
3. **`analysis-jobs`**: Parsed documents ready for analysis

### Message Types

**`JobRequest`** (`Dtce.Common/Class1.cs`):
```csharp
public class JobRequest
{
    public string JobId { get; set; }
    public DocumentType DocumentType { get; set; }
    public string? DocumentUrl { get; set; }
    public string? FilePath { get; set; }
    public string? FileName { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

**`AnalysisJob`** (`Dtce.Common/AnalysisJob.cs`):
```csharp
public class AnalysisJob
{
    public string JobId { get; set; }
    public string ParseResultKey { get; set; }
    public DocumentType DocumentType { get; set; }
}
```

### Queue Implementation

**Dev Mode** (`FileQueueMessageProducer` / `FileQueueMessageConsumer`):
- Messages stored as JSON files in local directory
- File naming: `{timestamp}-{guid}.json`
- Consumer polls directory for new files

**Prod Mode** (`AzureServiceBusProducer` / `AzureServiceBusConsumer`):
- Messages published to Azure Service Bus queues
- Automatic retry and dead-letter queue support
- Scalable and reliable message delivery

---

## Storage System

### Storage Paths

**Documents**:
- Path: `documents/{jobId}/{filename}`
- Stored by: API Gateway on submission
- Used by: All services to access original document

**Parsed Data**:
- Path: `parsed/{jobId}/parse-result.json`
- Stored by: Parsing Engine
- Contains: Full `ParseResult` object

**Results**:
- Path: `results/{jobId}/template.json`
- Path: `results/{jobId}/context.json`
- Stored by: Analysis Engine
- Final output files

### Storage Implementation

**Dev Mode** (`FileSystemObjectStorage`):
- Files stored in local directory structure
- Root path: `Storage:RootPath` from configuration
- Full paths: `{RootPath}/{fileKey}`

**Prod Mode** (`AzureBlobStorage`):
- Files stored in Azure Blob Storage container
- Container name: `Azure:Storage:ContainerName` (default: "dtce-documents")
- Blob names: `{fileKey}`

---

## Document Processing Pipeline

### ParseResult Structure

**`ParseResult`** (`Dtce.Common/ParseResult.cs`):
```csharp
public class ParseResult
{
    public List<Section> Sections { get; set; }
    public List<ContentSection> ContentSections { get; set; }
    public TemplateJson TemplateJson { get; set; }
    public VisualTheme VisualTheme { get; set; }
}
```

**`Section`**: Hierarchical document structure
- `Title`: Section heading
- `Level`: Nesting level (1, 2, 3, ...)
- `ContentBlocks`: Text content in section
- `Subsections`: Nested sections

**`ContentSection`**: Text content with placeholders
- `PlaceholderId`: Unique identifier
- `SampleText`: Original text content
- `WordCount`: Number of words

**`TemplateJson`**: Document template structure
- `Sections`: Document structure
- `VisualTheme`: Fonts, colors, styling

**`VisualTheme`**: Styling information
- `FontMap`: Font definitions by style name
- `ColorPalette`: List of colors used

### Template JSON Output

**`TemplateJson`** (`Dtce.Common/Models/TemplateJson.cs`):
```json
{
  "sections": [
    {
      "title": "Professional Summary",
      "level": 1,
      "contentBlocks": [...],
      "subsections": []
    }
  ],
  "visualTheme": {
    "fontMap": {
      "Normal": { "family": "Calibri", "size": 11, "color": "#000000" },
      "Heading1": { "family": "Calibri", "size": 16, "color": "#2E74B5", "bold": true }
    },
    "colorPalette": ["#2E74B5", "#70AD47"]
  },
  "logoMap": {
    "company-logo": {
      "url": "...",
      "type": "image/png"
    }
  }
}
```

### Context JSON Output

**`ContextJson`** (`Dtce.Common/Models/ContextJson.cs`):
```json
{
  "linguisticStyle": {
    "overallFormality": "formal",
    "formalityConfidenceScore": 0.85,
    "dominantTone": "professional",
    "toneConfidenceScore": 0.90,
    "writingStyleVector": [0.1, 0.2, ...]
  },
  "contentBlocks": [
    {
      "placeholderId": "summary-1",
      "sectionSampleText": "Experienced software engineer...",
      "wordCount": 45
    }
  ]
}
```

---

## Key Methods & Responsibilities

### API Gateway Methods

| Method | Class | Purpose |
|--------|-------|---------|
| `SubmitJob` | `JobsController` | Validates and submits job |
| `GetJobStatus` | `JobsController` | Retrieves job status |
| `GetJobResults` | `JobsController` | Returns result URLs or content |
| `GetFile` | `JobsController` | Serves files from storage |

### Ingestion Service Methods

| Method | Class | Purpose |
|--------|-------|---------|
| `ExecuteAsync` | `Worker` | Starts message consumption |
| Message Handler | `Worker` | Validates document and forwards to parsing |

### Parsing Engine Methods

| Method | Class | Purpose |
|--------|-------|---------|
| `ExecuteAsync` | `Worker` | Starts message consumption |
| `ParseAsync` | `DocumentParser` | Orchestrates document parsing |
| `ParseAsync` | `DocxHandler` | Parses Word documents |
| `ParseAsync` | `PdfHandler` | Parses PDF documents |
| `ParseAsync` | `GoogleDocsHandler` | Parses Google Docs |

### Analysis Engine Methods

| Method | Class | Purpose |
|--------|-------|---------|
| `ExecuteAsync` | `Worker` | Starts message consumption |
| `AnalyzeAsync` | `NlpAnalyzer` | Performs NLP analysis |
| `DetectLogosAsync` | `ComputerVisionAnalyzer` | Detects logos in images |

---

## Error Handling & Status Management

### Error Flow

1. **Exception in any service**:
   - Caught in message handler try-catch
   - Logged with full exception details
   - `IJobStatusRepository.UpdateJobErrorAsync()` called
   - Status set to `Failed`
   - Error message stored

2. **File not found**:
   - `FileNotFoundException` thrown
   - Caught in Ingestion Service
   - Status updated to `Failed`
   - Message: "Document file not found"

3. **Parsing errors**:
   - Caught in Parsing Engine
   - Status updated to `Failed`
   - Message: "Parsing error: {exception message}"

4. **Analysis errors**:
   - Caught in Analysis Engine
   - Status updated to `Failed`
   - Message: "Analysis error: {exception message}"

### Status Management

**Status Enum** (`Dtce.Common/Class1.cs`):
```csharp
public enum JobStatus
{
    Pending,              // Initial state
    Processing,           // Ingestion in progress
    LayoutDetectionInProgress,  // (Future)
    ParsingInProgress,    // Parsing in progress
    AnalysisInProgress,   // Analysis in progress
    Complete,             // Job completed successfully
    Failed                // Job failed with error
}
```

**Status Updates**:
- Each service updates status at key points
- Status messages provide human-readable progress
- Completion timestamp set on success
- Error message set on failure

---

## Platform Modes

### Dev Mode

**Configuration**: `Platform:Mode = "Dev"`

**Characteristics**:
- Uses local file system for storage
- Uses file-based queues
- All services must use same storage/queue paths
- No Azure dependencies
- Suitable for local development

**Services Used**:
- `FileSystemObjectStorage`
- `FileSystemJobStatusRepository`
- `FileQueueMessageProducer`
- `FileQueueMessageConsumer`

### Prod Mode

**Configuration**: `Platform:Mode = "Prod"`

**Characteristics**:
- Uses Azure Blob Storage
- Uses Azure Service Bus
- Uses Azure Table Storage
- Scalable and reliable
- Requires Azure connection strings

**Services Used**:
- `AzureBlobStorage`
- `AzureTableStorageJobRepository`
- `AzureServiceBusProducer`
- `AzureServiceBusConsumer`

---

## Summary

The DTCE API system follows a clean, modular architecture with:

1. **Clear Separation of Concerns**: Each service has a single responsibility
2. **Asynchronous Processing**: Message queues enable scalable, non-blocking processing
3. **Abstraction Layers**: Interfaces allow switching between Dev and Prod implementations
4. **Error Resilience**: Comprehensive error handling and status tracking
5. **Extensibility**: Easy to add new document types or analysis capabilities

The system processes documents through a well-defined pipeline:
**Submission → Ingestion → Parsing → Analysis → Completion**

Each stage updates job status, enabling real-time progress tracking and reliable error handling.

