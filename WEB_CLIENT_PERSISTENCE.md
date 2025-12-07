# Web Client - Persistence Layer Documentation

This document describes the persistence mechanisms used by the Web Client for user registration, login, API keys, and job history.

## Table of Contents

1. [Overview](#overview)
2. [User Registration & Login](#user-registration--login)
3. [API Key Management](#api-key-management)
4. [Job History](#job-history)
5. [Storage Implementations](#storage-implementations)
6. [Configuration](#configuration)
7. [Current Limitations & Recommendations](#current-limitations--recommendations)

---

## Overview

The Web Client uses different persistence strategies depending on the environment:

| Data Type | Dev Mode | Production Mode |
|-----------|----------|-----------------|
| **User Accounts** | In-Memory (temporary) | Azure Table Storage |
| **API Keys** | In-Memory (temporary) | Azure Table Storage |
| **Job History** | Local File System | Local File System ⚠️ |

⚠️ **Note**: Job history is currently only stored in local file system, even in production mode.

---

## User Registration & Login

### Persistence Layer

**Interface**: `IUserStore` (`Dtce.Identity/Stores/IUserStore.cs`)

**Methods**:
```csharp
Task<UserRecord?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);
Task<UserRecord?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
Task CreateAsync(UserRecord user, CancellationToken cancellationToken = default);
Task UpdateAsync(UserRecord user, CancellationToken cancellationToken = default);
```

### Data Model

**`UserRecord`** (`Dtce.Identity/Models/UserRecord.cs`):
```csharp
public class UserRecord
{
    public Guid Id { get; set; }
    public string Email { get; set; }
    public string PasswordHash { get; set; }  // BCrypt hashed
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; }
}
```

### Storage Implementations

#### 1. InMemoryUserStore (Dev Mode)

**Location**: `Dtce.Identity/Stores/InMemoryUserStore.cs`

**Characteristics**:
- Stores data in `ConcurrentDictionary` in memory
- **Data is lost on application restart**
- Fast for development/testing
- No external dependencies

**Storage Structure**:
```csharp
private readonly ConcurrentDictionary<Guid, UserRecord> _users = new();
private readonly ConcurrentDictionary<string, Guid> _emailIndex = new();
```

**Usage**:
- Automatically selected when `Azure:Storage:ConnectionString` is not configured
- Suitable for local development only

#### 2. AzureTableUserStore (Production Mode)

**Location**: `Dtce.Identity/Stores/AzureTableUserStore.cs`

**Characteristics**:
- Stores data in Azure Table Storage
- **Persistent across restarts**
- Scalable and reliable
- Requires Azure Storage connection string

**Storage Structure**:
- **Table**: `Users`
- **PartitionKey**: `"USER"` (constant)
- **RowKey**: `{userId}` (GUID as string)
- **Properties**:
  - `Email`
  - `NormalizedEmail` (lowercase, for case-insensitive lookups)
  - `PasswordHash`
  - `CreatedAt`
  - `LastLoginAt`
  - `IsActive`

**Table Schema**:
```
PartitionKey: "USER"
RowKey: "550e8400-e29b-41d4-a716-446655440000"
Email: "user@example.com"
NormalizedEmail: "user@example.com"
PasswordHash: "$2a$11$..."
CreatedAt: 2024-01-15T10:00:00Z
LastLoginAt: 2024-01-15T11:30:00Z
IsActive: true
```

### Registration Flow

1. **User submits registration form** (`AccountController.Register`)
2. **`UserService.RegisterAsync`** called:
   - Checks if email already exists via `IUserStore.GetByEmailAsync`
   - Creates new `UserRecord` with:
     - Generated GUID for `Id`
     - BCrypt hashed password
     - `CreatedAt = DateTime.UtcNow`
     - `IsActive = true`
   - Saves via `IUserStore.CreateAsync`
3. **User signed in** via cookie authentication

**Code Path**:
```
AccountController.Register()
  └─► UserService.RegisterAsync()
      ├─► IUserStore.GetByEmailAsync() [Check exists]
      └─► IUserStore.CreateAsync() [Save user]
```

### Login Flow

1. **User submits login form** (`AccountController.Login`)
2. **`UserService.LoginAsync`** called:
   - Retrieves user via `IUserStore.GetByEmailAsync`
   - Verifies password using BCrypt
   - Updates `LastLoginAt = DateTime.UtcNow`
   - Saves via `IUserStore.UpdateAsync`
3. **User signed in** via cookie authentication

**Code Path**:
```
AccountController.Login()
  └─► UserService.LoginAsync()
      ├─► IUserStore.GetByEmailAsync() [Retrieve user]
      ├─► BCrypt.Verify() [Verify password]
      └─► IUserStore.UpdateAsync() [Update LastLoginAt]
```

### Configuration

**`WebApplicationHost.cs`**:
```csharp
var storageConnectionString = builder.Configuration["Azure:Storage:ConnectionString"];
if (!string.IsNullOrWhiteSpace(storageConnectionString))
{
    // Production: Use Azure Table Storage
    builder.Services.AddSingleton<IUserStore>(_ => 
        new AzureTableUserStore(storageConnectionString!));
}
else
{
    // Development: Use In-Memory (temporary)
    builder.Services.AddSingleton<IUserStore, InMemoryUserStore>();
}
```

**Configuration File** (`appsettings.json` or `appsettings.Production.json`):
```json
{
  "Azure": {
    "Storage": {
      "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net"
    }
  }
}
```

---

## API Key Management

### Persistence Layer

**Interface**: `IUserStore` (same interface, includes API key methods)

**Methods**:
```csharp
Task<ApiKeyRecord> CreateApiKeyAsync(ApiKeyRecord apiKey, CancellationToken cancellationToken = default);
Task<ApiKeyRecord?> GetApiKeyByIdAsync(Guid apiKeyId, CancellationToken cancellationToken = default);
Task<ApiKeyRecord?> GetApiKeyByKeyAsync(string apiKey, CancellationToken cancellationToken = default);
Task<IReadOnlyList<ApiKeyRecord>> GetApiKeysByUserAsync(Guid userId, CancellationToken cancellationToken = default);
Task UpdateApiKeyAsync(ApiKeyRecord apiKey, CancellationToken cancellationToken = default);
```

### Data Model

**`ApiKeyRecord`** (`Dtce.Identity/Models/ApiKeyRecord.cs`):
```csharp
public class ApiKeyRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Key { get; set; }  // The actual API key string
    public string Name { get; set; }  // User-friendly name
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool IsActive { get; set; }
}
```

### Storage Implementations

#### 1. InMemoryUserStore (Dev Mode)

**Storage Structure**:
```csharp
private readonly ConcurrentDictionary<Guid, ApiKeyRecord> _apiKeys = new();
private readonly ConcurrentDictionary<string, Guid> _apiKeyIndex = new();  // Key -> Id mapping
```

**Characteristics**:
- Data lost on restart
- Fast lookups via index

#### 2. AzureTableUserStore (Production Mode)

**Storage Structure**:
- **Table**: `ApiKeys`
- **PartitionKey**: `{userId}` (GUID as string)
- **RowKey**: `{apiKeyId}` (GUID as string)
- **Properties**:
  - `ApiKey` (the actual key string)
  - `Name`
  - `CreatedAt`
  - `RevokedAt`
  - `IsActive`

**Table Schema**:
```
PartitionKey: "550e8400-e29b-41d4-a716-446655440000"  (UserId)
RowKey: "660e8400-e29b-41d4-a716-446655440001"  (ApiKeyId)
ApiKey: "dtce_abc123xyz..."
Name: "Production API Key"
CreatedAt: 2024-01-15T10:00:00Z
RevokedAt: null
IsActive: true
```

### API Key Creation Flow

1. **User creates API key** (`ApiKeyController.Create`)
2. **`UserService.CreateApiKeyAsync`** called:
   - Generates unique API key string
   - Creates `ApiKeyRecord` with:
     - Generated GUID for `Id`
     - User's `UserId`
     - Generated key string
     - User-provided name
     - `CreatedAt = DateTime.UtcNow`
     - `IsActive = true`
   - Saves via `IUserStore.CreateApiKeyAsync`
3. **Key returned to user** (only shown once)

**Code Path**:
```
ApiKeyController.Create()
  └─► UserService.CreateApiKeyAsync()
      └─► IUserStore.CreateApiKeyAsync() [Save API key]
```

---

## Job History

### Persistence Layer

**Service**: `JobHistoryService` (`Dtce.WebClient/Services/JobHistoryService.cs`)

**Methods**:
```csharp
Task SaveJobHistoryAsync(JobHistory history, CancellationToken cancellationToken = default);
Task UpdateJobHistoryAsync(string jobId, string userId, Action<JobHistory> updateAction, CancellationToken cancellationToken = default);
Task<List<JobHistory>> GetUserJobHistoryAsync(string userId, int? limit = null, CancellationToken cancellationToken = default);
Task<JobHistory?> GetJobHistoryAsync(string jobId, string userId, CancellationToken cancellationToken = default);
```

### Data Model

**`JobHistory`** (`Dtce.WebClient/Models/JobHistory.cs`):
```csharp
public class JobHistory
{
    public string JobId { get; set; }
    public string UserId { get; set; }
    public string? FileName { get; set; }
    public string? DocumentUrl { get; set; }
    public string InputType { get; set; }  // "file" or "url"
    public string Status { get; set; }  // "Pending", "Complete", "Failed", etc.
    public string StatusMessage { get; set; }
    public string? TemplateJsonUrl { get; set; }
    public string? ContextJsonUrl { get; set; }
    public DateTime SubmittedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
```

### Storage Implementation

**Current Implementation**: **File System Only** (both Dev and Prod)

**Storage Location**:
- **Default**: `%LocalAppData%\DtceWebClient\history\{userId}\{jobId}.json`
- **Configurable**: `JobHistory:StoragePath` in `appsettings.json`

**File Structure**:
```
{StoragePath}/
  └─ {userId}/
      ├─ {jobId1}.json
      ├─ {jobId2}.json
      └─ {jobId3}.json
```

**File Format** (JSON):
```json
{
  "JobId": "550e8400-e29b-41d4-a716-446655440000",
  "UserId": "660e8400-e29b-41d4-a716-446655440001",
  "FileName": "document.docx",
  "InputType": "file",
  "Status": "Complete",
  "StatusMessage": "Job completed successfully",
  "TemplateJsonUrl": "https://api.example.com/api/v1/jobs/files/results/.../template.json",
  "ContextJsonUrl": "https://api.example.com/api/v1/jobs/files/results/.../context.json",
  "SubmittedAt": "2024-01-15T10:00:00Z",
  "CompletedAt": "2024-01-15T10:05:00Z"
}
```

### Job History Flow

1. **Job Submitted** (`HomeController.SubmitJob`):
   - Creates `JobHistory` object
   - Calls `JobHistoryService.SaveJobHistoryAsync`
   - Saves to: `{historyDir}/{userId}/{jobId}.json`

2. **Status Updated** (`HomeController.GetJobStatus`):
   - Calls `JobHistoryService.UpdateJobHistoryAsync`
   - Updates status, message, and result URLs
   - Updates same JSON file

3. **History Retrieved** (`HomeController.History`):
   - Calls `JobHistoryService.GetUserJobHistoryAsync`
   - Reads all JSON files in `{historyDir}/{userId}/`
   - Returns sorted list (newest first)

**Code Path**:
```
HomeController.SubmitJob()
  └─► JobHistoryService.SaveJobHistoryAsync()
      └─► File.WriteAllTextAsync() [Save JSON file]

HomeController.GetJobStatus()
  └─► JobHistoryService.UpdateJobHistoryAsync()
      ├─► File.ReadAllTextAsync() [Load JSON]
      ├─► Update properties
      └─► File.WriteAllTextAsync() [Save JSON]

HomeController.History()
  └─► JobHistoryService.GetUserJobHistoryAsync()
      ├─► Directory.GetFiles() [List all JSON files]
      ├─► File.ReadAllTextAsync() [Read each file]
      └─► Deserialize JSON
```

### Configuration

**`appsettings.json`**:
```json
{
  "JobHistory": {
    "StoragePath": "C:\\Path\\To\\History"  // Optional, defaults to %LocalAppData%
  }
}
```

---

## Storage Implementations Summary

### InMemoryUserStore

**Used For**: User accounts, API keys (Dev mode only)

**Characteristics**:
- ✅ Fast (in-memory)
- ✅ No external dependencies
- ❌ Data lost on restart
- ❌ Not suitable for production

**Storage**:
- `ConcurrentDictionary<Guid, UserRecord>` for users
- `ConcurrentDictionary<Guid, ApiKeyRecord>` for API keys
- Index dictionaries for fast lookups

### AzureTableUserStore

**Used For**: User accounts, API keys (Production mode)

**Characteristics**:
- ✅ Persistent
- ✅ Scalable
- ✅ Reliable
- ✅ Supports multiple instances
- ❌ Requires Azure Storage account

**Storage**:
- Azure Table Storage
- Table: `Users` (PartitionKey: "USER", RowKey: userId)
- Table: `ApiKeys` (PartitionKey: userId, RowKey: apiKeyId)

### JobHistoryService (File System)

**Used For**: Job history (Both Dev and Prod)

**Characteristics**:
- ✅ Simple implementation
- ✅ No external dependencies
- ❌ Not scalable (file system)
- ❌ Not shared across instances
- ❌ Not suitable for production multi-instance deployments

**Storage**:
- Local file system
- JSON files per job
- Organized by user ID

---

## Configuration

### Development Mode

**`appsettings.Development.json`** (or no Azure connection string):
```json
{
  "JobHistory": {
    "StoragePath": "C:\\Users\\<Username>\\AppData\\Local\\DtceWebClient\\history"
  }
}
```

**Result**:
- Users: `InMemoryUserStore` (temporary)
- API Keys: `InMemoryUserStore` (temporary)
- Job History: Local file system

### Production Mode

**`appsettings.Production.json`**:
```json
{
  "Azure": {
    "Storage": {
      "ConnectionString": "DefaultEndpointsProtocol=https;AccountName=...;AccountKey=..."
    }
  },
  "JobHistory": {
    "StoragePath": "/app/data/history"  // Or Azure File Share path
  }
}
```

**Result**:
- Users: `AzureTableUserStore` (persistent)
- API Keys: `AzureTableUserStore` (persistent)
- Job History: Local file system ⚠️

---

## Current Limitations & Recommendations

### ⚠️ Current Limitations

1. **Job History Not in Azure**:
   - Currently only stored in local file system
   - Not shared across multiple Web Client instances
   - Data lost if container/VM is recreated
   - Not suitable for production scaling

2. **In-Memory Storage in Dev Mode**:
   - User accounts and API keys lost on restart
   - Makes testing difficult
   - Consider using local SQLite or file-based storage for Dev

3. **No Job History Cleanup**:
   - History files accumulate indefinitely
   - No automatic cleanup of old records
   - Could fill disk space over time

### ✅ Recommendations

#### 1. Migrate Job History to Azure Table Storage

**Create `AzureTableJobHistoryService`**:
```csharp
public class AzureTableJobHistoryService : IJobHistoryService
{
    private readonly TableClient _historyTable;
    
    // Store in Azure Table Storage
    // PartitionKey: userId
    // RowKey: jobId
}
```

**Benefits**:
- Persistent across restarts
- Shared across multiple instances
- Scalable
- Can add TTL for automatic cleanup

#### 2. Add Local SQLite for Dev Mode

**Create `SqliteUserStore`**:
```csharp
public class SqliteUserStore : IUserStore
{
    // Use SQLite database for local development
    // Persistent across restarts
    // No Azure dependency
}
```

**Benefits**:
- Persistent in Dev mode
- Better testing experience
- No external dependencies

#### 3. Add Job History Cleanup

**Implement retention policy**:
```csharp
public async Task CleanupOldHistoryAsync(TimeSpan retentionPeriod)
{
    // Delete history older than retention period
    // Can be scheduled via background service
}
```

**Configuration**:
```json
{
  "JobHistory": {
    "RetentionDays": 90,
    "EnableAutoCleanup": true
  }
}
```

#### 4. Add Database Migrations

For production deployments, consider:
- Entity Framework Core with migrations
- Or manual table creation scripts
- Ensures schema consistency

---

## Summary

### Current State

| Feature | Dev Mode | Production Mode | Status |
|---------|----------|----------------|--------|
| **User Registration** | In-Memory | Azure Table Storage | ✅ Working |
| **User Login** | In-Memory | Azure Table Storage | ✅ Working |
| **API Keys** | In-Memory | Azure Table Storage | ✅ Working |
| **Job History** | File System | File System | ⚠️ Needs Improvement |

### Key Points

1. **User accounts and API keys** are properly persisted in production via Azure Table Storage
2. **Job history** is currently only file-based and should be migrated to Azure Table Storage for production
3. **Dev mode** uses in-memory storage for users/keys (data lost on restart)
4. All persistence uses **interface-based design** for easy swapping of implementations

### Next Steps

1. Implement `AzureTableJobHistoryService` for production
2. Consider `SqliteUserStore` for better Dev experience
3. Add job history cleanup/retention policy
4. Add database migration scripts for Azure Table Storage

