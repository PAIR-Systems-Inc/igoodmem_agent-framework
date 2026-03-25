# Microsoft Agent Framework - GoodMem Integration

[GoodMem](https://goodmem.ai) is a semantic memory layer for AI agents with support for storage, retrieval, and summarization. This package integrates GoodMem with the Microsoft Agent Framework for .NET.

## Installation

```bash
dotnet add package Microsoft.Agents.AI.GoodMem --prerelease
```

## Quick Start

### 1. Configure the HttpClient

```csharp
using var httpClient = new HttpClient();
httpClient.BaseAddress = new Uri("https://api.goodmem.ai");
httpClient.DefaultRequestHeaders.Add("X-API-Key", "<Your API Key>");
```

For local development with self-signed certificates, configure an `HttpClientHandler`:

```csharp
var handler = new HttpClientHandler
{
    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
};
using var httpClient = new HttpClient(handler)
{
    BaseAddress = new Uri("https://localhost:8080")
};
httpClient.DefaultRequestHeaders.Add("X-API-Key", "<Your API Key>");
```

### 2. Create the GoodMem Provider

```csharp
using Microsoft.Agents.AI.GoodMem;

var options = new GoodMemProviderOptions
{
    SpaceId = "your-space-id",
    MaxResults = 5,
    StoreConversations = true
};

var provider = new GoodMemProvider(httpClient, options);
```

### 3. Attach to an Agent

```csharp
using Microsoft.Agents.AI;

var agent = new ChatClientAgent(chatClient, new()
{
    Instructions = "You are a helpful assistant with persistent memory.",
    AIContextProviders = [provider]
});
```

Memories are automatically retrieved before each invocation and conversations are stored after.

## Configuration

### `GoodMemProviderOptions`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `SpaceId` | `string` | *required* | GoodMem space ID for search and storage |
| `MaxResults` | `int` | `5` | Max memory chunks to retrieve per query |
| `ContextPrompt` | `string?` | *(built-in)* | Prompt prepended to retrieved memories |
| `StoreConversations` | `bool` | `true` | Store conversations after each invocation |
| `EnableSensitiveTelemetryData` | `bool` | `false` | Allow PII in log output |
| `StateKey` | `string?` | type name | Key for session state storage |
| `SearchInputMessageFilter` | `Func<...>?` | external only | Filter for search input messages |
| `StorageInputRequestMessageFilter` | `Func<...>?` | external only | Filter for storage request messages |
| `StorageInputResponseMessageFilter` | `Func<...>?` | all | Filter for storage response messages |

## Security Considerations

- The `HttpClient` must be configured with appropriate authentication and HTTPS.
- Conversation messages (which may contain PII) are sent to the GoodMem service.
- Retrieved memories are injected as user messages — ensure the memory store is trusted.
- When `EnableSensitiveTelemetryData` is `true`, full memory content may appear in logs.
