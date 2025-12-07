# DTCE API - Third-Party API Documentation

This document provides comprehensive API documentation for third-party developers integrating with the DTCE (Document Template & Context Extractor) API.

## Table of Contents

1. [Overview](#overview)
2. [Authentication](#authentication)
3. [Base URL](#base-url)
4. [Endpoints](#endpoints)
5. [Request/Response Formats](#requestresponse-formats)
6. [Error Handling](#error-handling)
7. [Rate Limits](#rate-limits)
8. [Code Examples](#code-examples)

---

## Overview

The DTCE API is a RESTful service that extracts templates and context from documents (Word, PDF, Google Docs). The API follows an asynchronous job-based pattern:

1. **Submit** a document for processing
2. **Poll** for job status
3. **Retrieve** results when complete

### Supported Document Types

- **Word Documents** (`.docx`)
- **PDF Documents** (`.pdf`)
- **Google Docs** (via URL)

### Maximum File Size

- **50 MB** per document

---

## Authentication

All API requests require an API key in the request header.

### Header Format

```
X-API-Key: your-api-key-here
```

### Obtaining an API Key

1. Register an account via the Web Client UI
2. Navigate to the "API Keys" section
3. Create a new API key
4. Store the key securely (it cannot be retrieved after creation)

### Example

```bash
curl -H "X-API-Key: your-api-key-here" \
     https://api.example.com/api/v1/jobs/submit
```

---

## Base URL

### Production
```
https://your-api-gateway.azurewebsites.net
```

### Development
```
http://localhost:5017
```

All endpoints are prefixed with `/api/v1`.

---

## Endpoints

### 1. Submit Job

Submit a document for processing.

**Endpoint:** `POST /api/v1/jobs/submit`

**Headers:**
- `X-API-Key`: Your API key (required)
- `Content-Type`: `multipart/form-data` (for file upload) or `application/x-www-form-urlencoded` (for URL)

**Request Body (File Upload):**
```
document: [binary file]
```

**Request Body (Google Doc URL):**
```
document_url: https://docs.google.com/document/d/...
```

**Response:** `202 Accepted`

```json
{
  "jobId": "550e8400-e29b-41d4-a716-446655440000",
  "statusUrl": "/api/v1/jobs/550e8400-e29b-41d4-a716-446655440000/status"
}
```

**Error Responses:**

| Status Code | Description |
|-------------|-------------|
| 400 | Bad Request - Invalid file type, size, or missing document |
| 401 | Unauthorized - Invalid or missing API key |
| 500 | Internal Server Error |

**Example (cURL - File Upload):**
```bash
curl -X POST \
  -H "X-API-Key: your-api-key" \
  -F "document=@/path/to/document.docx" \
  https://api.example.com/api/v1/jobs/submit
```

**Example (cURL - Google Doc URL):**
```bash
curl -X POST \
  -H "X-API-Key: your-api-key" \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "document_url=https://docs.google.com/document/d/..." \
  https://api.example.com/api/v1/jobs/submit
```

---

### 2. Get Job Status

Check the status of a submitted job.

**Endpoint:** `GET /api/v1/jobs/{jobId}/status`

**Headers:**
- `X-API-Key`: Your API key (required)

**Path Parameters:**
- `jobId`: The job ID returned from the submit endpoint

**Response:** `200 OK`

```json
{
  "jobId": "550e8400-e29b-41d4-a716-446655440000",
  "status": "Complete",
  "statusMessage": "Job completed successfully",
  "completedAt": "2024-01-15T10:30:00Z",
  "errorMessage": null
}
```

**Status Values:**

| Status | Description |
|--------|-------------|
| `Pending` | Job is queued and waiting to be processed |
| `Processing` | Job is currently being processed |
| `Complete` | Job completed successfully |
| `Failed` | Job failed with an error |

**Example:**
```bash
curl -X GET \
  -H "X-API-Key: your-api-key" \
  https://api.example.com/api/v1/jobs/550e8400-e29b-41d4-a716-446655440000/status
```

---

### 3. Get Job Results

Retrieve the results of a completed job.

**Endpoint:** `GET /api/v1/jobs/{jobId}/results`

**Headers:**
- `X-API-Key`: Your API key (required)

**Path Parameters:**
- `jobId`: The job ID returned from the submit endpoint

**Query Parameters:**
- `includeContent` (optional, boolean): If `true`, returns JSON content directly in response. Default: `false` (returns URLs only).

**Response:** `200 OK` (when job is complete)

**With `includeContent=false` (default):**
```json
{
  "jobId": "550e8400-e29b-41d4-a716-446655440000",
  "templateJsonUrl": "https://api.example.com/api/v1/jobs/files/results/550e8400-e29b-41d4-a716-446655440000/template.json",
  "contextJsonUrl": "https://api.example.com/api/v1/jobs/files/results/550e8400-e29b-41d4-a716-446655440000/context.json"
}
```

**With `includeContent=true`:**
```json
{
  "jobId": "550e8400-e29b-41d4-a716-446655440000",
  "templateJsonUrl": "https://api.example.com/api/v1/jobs/files/results/550e8400-e29b-41d4-a716-446655440000/template.json",
  "contextJsonUrl": "https://api.example.com/api/v1/jobs/files/results/550e8400-e29b-41d4-a716-446655440000/context.json",
  "templateJson": {
    "sections": [...],
    "visualTheme": {...}
  },
  "contextJson": {
    "sections": [...],
    "assets": {...}
  }
}
```

**Response:** `202 Accepted` (when job is still processing)
```json
{
  "message": "Job is still processing",
  "status": "Processing"
}
```

**Response:** `404 Not Found` (when job doesn't exist)
```json
{
  "error": "Job not found"
}
```

**Example:**
```bash
# Get URLs only
curl -X GET \
  -H "X-API-Key: your-api-key" \
  https://api.example.com/api/v1/jobs/550e8400-e29b-41d4-a716-446655440000/results

# Get JSON content directly
curl -X GET \
  -H "X-API-Key: your-api-key" \
  "https://api.example.com/api/v1/jobs/550e8400-e29b-41d4-a716-446655440000/results?includeContent=true"
```

---

### 4. Download File

Download a file (template.json, context.json, or original document).

**Endpoint:** `GET /api/v1/jobs/files/{fileKey}`

**Headers:**
- `X-API-Key`: Your API key (required, optional in Dev mode)

**Path Parameters:**
- `fileKey`: The file path (e.g., `results/{jobId}/template.json`)

**Query Parameters:**
- `download` (optional, boolean): If `true`, forces file download. Default: `false` (displays in browser for JSON files).

**Response:** `200 OK`
- Content-Type: `application/json` (for .json files)
- Content-Type: `application/vnd.openxmlformats-officedocument.wordprocessingml.document` (for .docx files)
- Content-Type: `application/pdf` (for .pdf files)

**Example (View JSON in browser):**
```bash
curl -X GET \
  -H "X-API-Key: your-api-key" \
  https://api.example.com/api/v1/jobs/files/results/550e8400-e29b-41d4-a716-446655440000/template.json
```

**Example (Download file):**
```bash
curl -X GET \
  -H "X-API-Key: your-api-key" \
  "https://api.example.com/api/v1/jobs/files/results/550e8400-e29b-41d4-a716-446655440000/template.json?download=true" \
  -o template.json
```

---

## Request/Response Formats

### Template JSON Structure

The `template.json` file contains the document structure and styling information:

```json
{
  "sections": [
    {
      "title": "Professional Summary",
      "level": 1,
      "contentBlocks": [
        {
          "type": "paragraph",
          "content": "Experienced software engineer..."
        }
      ],
      "subsections": []
    }
  ],
  "visualTheme": {
    "fontMap": {
      "Normal": {
        "family": "Calibri",
        "size": 11,
        "color": "#000000"
      },
      "Heading1": {
        "family": "Calibri",
        "size": 16,
        "color": "#2E74B5",
        "bold": true
      }
    },
    "colorPalette": ["#2E74B5", "#70AD47", "#FFC000"]
  }
}
```

### Context JSON Structure

The `context.json` file contains the extracted content and placeholders:

```json
{
  "sections": [
    {
      "title": "Professional Summary",
      "content": "Experienced software engineer with 10+ years...",
      "placeholders": ["{{name}}", "{{years}}"]
    }
  ],
  "assets": {
    "logos": [
      {
        "key": "company-logo",
        "url": "https://...",
        "type": "image/png"
      }
    ]
  }
}
```

---

## Error Handling

### Standard Error Response Format

```json
{
  "error": "Error message describing what went wrong"
}
```

### HTTP Status Codes

| Status Code | Meaning | Description |
|-------------|---------|-------------|
| 200 | OK | Request successful |
| 202 | Accepted | Job submitted or still processing |
| 400 | Bad Request | Invalid request (missing/invalid parameters, file too large, etc.) |
| 401 | Unauthorized | Invalid or missing API key |
| 404 | Not Found | Job or resource not found |
| 500 | Internal Server Error | Server error occurred |

### Common Error Scenarios

**Invalid File Type:**
```json
{
  "error": "Invalid file type. Only .docx and .pdf are supported"
}
```

**File Too Large:**
```json
{
  "error": "File size exceeds 50MB limit"
}
```

**Missing Document:**
```json
{
  "error": "Either document file or document_url must be provided"
}
```

**Job Not Found:**
```json
{
  "error": "Job not found"
}
```

---

## Rate Limits

Currently, there are no rate limits enforced. However, we recommend:

- **Polling interval**: Wait at least 2-3 seconds between status checks
- **Concurrent jobs**: Limit to 10 concurrent job submissions per API key
- **Bulk processing**: For large batches, submit jobs sequentially with delays

---

## Code Examples

### Python

```python
import requests
import time

API_BASE_URL = "https://api.example.com/api/v1"
API_KEY = "your-api-key-here"

def submit_job(file_path=None, document_url=None):
    """Submit a document for processing."""
    url = f"{API_BASE_URL}/jobs/submit"
    headers = {"X-API-Key": API_KEY}
    
    if file_path:
        with open(file_path, 'rb') as f:
            files = {'document': f}
            response = requests.post(url, headers=headers, files=files)
    elif document_url:
        data = {'document_url': document_url}
        response = requests.post(url, headers=headers, data=data)
    else:
        raise ValueError("Either file_path or document_url must be provided")
    
    response.raise_for_status()
    return response.json()

def get_job_status(job_id):
    """Get the status of a job."""
    url = f"{API_BASE_URL}/jobs/{job_id}/status"
    headers = {"X-API-Key": API_KEY}
    response = requests.get(url, headers=headers)
    response.raise_for_status()
    return response.json()

def get_job_results(job_id, include_content=False):
    """Get the results of a completed job."""
    url = f"{API_BASE_URL}/jobs/{job_id}/results"
    headers = {"X-API-Key": API_KEY}
    params = {"includeContent": include_content}
    response = requests.get(url, headers=headers, params=params)
    response.raise_for_status()
    return response.json()

def wait_for_completion(job_id, poll_interval=3, max_wait=300):
    """Wait for a job to complete."""
    start_time = time.time()
    while True:
        status = get_job_status(job_id)
        if status['status'] == 'Complete':
            return status
        elif status['status'] == 'Failed':
            raise Exception(f"Job failed: {status.get('errorMessage', 'Unknown error')}")
        
        if time.time() - start_time > max_wait:
            raise TimeoutError("Job did not complete within the maximum wait time")
        
        time.sleep(poll_interval)

# Example usage
try:
    # Submit a job
    result = submit_job(file_path="document.docx")
    job_id = result['jobId']
    print(f"Job submitted: {job_id}")
    
    # Wait for completion
    status = wait_for_completion(job_id)
    print(f"Job completed: {status['statusMessage']}")
    
    # Get results with JSON content
    results = get_job_results(job_id, include_content=True)
    print(f"Template JSON: {results['templateJson']}")
    print(f"Context JSON: {results['contextJson']}")
    
except Exception as e:
    print(f"Error: {e}")
```

### JavaScript/Node.js

```javascript
const axios = require('axios');
const fs = require('fs').promises;

const API_BASE_URL = 'https://api.example.com/api/v1';
const API_KEY = 'your-api-key-here';

const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'X-API-Key': API_KEY
  }
});

async function submitJob(filePath = null, documentUrl = null) {
  const formData = new FormData();
  
  if (filePath) {
    const file = await fs.readFile(filePath);
    formData.append('document', file, { filename: path.basename(filePath) });
  } else if (documentUrl) {
    formData.append('document_url', documentUrl);
  } else {
    throw new Error('Either filePath or documentUrl must be provided');
  }
  
  const response = await apiClient.post('/jobs/submit', formData, {
    headers: {
      'Content-Type': 'multipart/form-data'
    }
  });
  
  return response.data;
}

async function getJobStatus(jobId) {
  const response = await apiClient.get(`/jobs/${jobId}/status`);
  return response.data;
}

async function getJobResults(jobId, includeContent = false) {
  const response = await apiClient.get(`/jobs/${jobId}/results`, {
    params: { includeContent }
  });
  return response.data;
}

async function waitForCompletion(jobId, pollInterval = 3000, maxWait = 300000) {
  const startTime = Date.now();
  
  while (true) {
    const status = await getJobStatus(jobId);
    
    if (status.status === 'Complete') {
      return status;
    } else if (status.status === 'Failed') {
      throw new Error(`Job failed: ${status.errorMessage || 'Unknown error'}`);
    }
    
    if (Date.now() - startTime > maxWait) {
      throw new Error('Job did not complete within the maximum wait time');
    }
    
    await new Promise(resolve => setTimeout(resolve, pollInterval));
  }
}

// Example usage
(async () => {
  try {
    // Submit a job
    const result = await submitJob('document.docx');
    const jobId = result.jobId;
    console.log(`Job submitted: ${jobId}`);
    
    // Wait for completion
    const status = await waitForCompletion(jobId);
    console.log(`Job completed: ${status.statusMessage}`);
    
    // Get results with JSON content
    const results = await getJobResults(jobId, true);
    console.log('Template JSON:', results.templateJson);
    console.log('Context JSON:', results.contextJson);
    
  } catch (error) {
    console.error('Error:', error.message);
  }
})();
```

### cURL (Bash)

```bash
#!/bin/bash

API_BASE_URL="https://api.example.com/api/v1"
API_KEY="your-api-key-here"
JOB_ID=""

# Submit a job
submit_job() {
    local file_path=$1
    local response=$(curl -s -X POST \
        -H "X-API-Key: $API_KEY" \
        -F "document=@$file_path" \
        "$API_BASE_URL/jobs/submit")
    
    JOB_ID=$(echo $response | jq -r '.jobId')
    echo "Job submitted: $JOB_ID"
}

# Get job status
get_status() {
    local job_id=$1
    curl -s -X GET \
        -H "X-API-Key: $API_KEY" \
        "$API_BASE_URL/jobs/$job_id/status" | jq
}

# Wait for completion
wait_for_completion() {
    local job_id=$1
    local max_wait=300
    local elapsed=0
    
    while [ $elapsed -lt $max_wait ]; do
        local status=$(get_status $job_id)
        local job_status=$(echo $status | jq -r '.status')
        
        if [ "$job_status" == "Complete" ]; then
            echo "Job completed!"
            return 0
        elif [ "$job_status" == "Failed" ]; then
            echo "Job failed!"
            return 1
        fi
        
        sleep 3
        elapsed=$((elapsed + 3))
    done
    
    echo "Timeout waiting for job completion"
    return 1
}

# Get results
get_results() {
    local job_id=$1
    local include_content=${2:-false}
    
    curl -s -X GET \
        -H "X-API-Key: $API_KEY" \
        "$API_BASE_URL/jobs/$job_id/results?includeContent=$include_content" | jq
}

# Example usage
submit_job "document.docx"
wait_for_completion $JOB_ID
get_results $JOB_ID true
```

### C# (.NET)

```csharp
using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

public class DtceApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    
    public DtceApiClient(string baseUrl, string apiKey)
    {
        _baseUrl = baseUrl;
        _httpClient = new HttpClient();
        _httpClient.BaseAddress = new Uri(baseUrl);
        _httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
    }
    
    public async Task<SubmitJobResponse> SubmitJobAsync(string filePath)
    {
        using var formData = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(await File.ReadAllBytesAsync(filePath));
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        formData.Add(fileContent, "document", Path.GetFileName(filePath));
        
        var response = await _httpClient.PostAsync("/api/v1/jobs/submit", formData);
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<SubmitJobResponse>(json);
    }
    
    public async Task<JobStatusResponse> GetJobStatusAsync(string jobId)
    {
        var response = await _httpClient.GetAsync($"/api/v1/jobs/{jobId}/status");
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JobStatusResponse>(json);
    }
    
    public async Task<JobResultsResponse> GetJobResultsAsync(string jobId, bool includeContent = false)
    {
        var response = await _httpClient.GetAsync($"/api/v1/jobs/{jobId}/results?includeContent={includeContent}");
        response.EnsureSuccessStatusCode();
        
        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JobResultsResponse>(json);
    }
    
    public async Task<JobStatusResponse> WaitForCompletionAsync(string jobId, int pollIntervalMs = 3000, int maxWaitMs = 300000)
    {
        var startTime = DateTime.UtcNow;
        
        while (true)
        {
            var status = await GetJobStatusAsync(jobId);
            
            if (status.Status == "Complete")
                return status;
            
            if (status.Status == "Failed")
                throw new Exception($"Job failed: {status.ErrorMessage ?? "Unknown error"}");
            
            if ((DateTime.UtcNow - startTime).TotalMilliseconds > maxWaitMs)
                throw new TimeoutException("Job did not complete within the maximum wait time");
            
            await Task.Delay(pollIntervalMs);
        }
    }
}

// Example usage
var client = new DtceApiClient("https://api.example.com", "your-api-key");
var submitResponse = await client.SubmitJobAsync("document.docx");
var status = await client.WaitForCompletionAsync(submitResponse.JobId);
var results = await client.GetJobResultsAsync(submitResponse.JobId, includeContent: true);
```

---

## Best Practices

1. **Error Handling**: Always check HTTP status codes and handle errors gracefully
2. **Polling**: Use exponential backoff for status polling (e.g., 2s, 4s, 8s intervals)
3. **Timeouts**: Set appropriate timeouts for long-running operations
4. **Retries**: Implement retry logic for transient failures (5xx errors)
5. **API Key Security**: Never commit API keys to version control
6. **File Validation**: Validate file types and sizes before submission
7. **Async Processing**: Don't block your application while waiting for job completion

---

## Support

For API support, issues, or feature requests:
- Check the deployment guide for troubleshooting
- Review application logs for detailed error messages
- Contact support with your API key and job ID for assistance

---

## Changelog

### Version 1.0.0
- Initial API release
- Support for .docx, .pdf, and Google Docs
- Asynchronous job processing
- Template and context JSON extraction

