// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.GoodMem;

/// <summary>
/// Client for the GoodMem semantic memory service.
/// </summary>
internal sealed class GoodMemClient
{
    private static readonly Uri s_memoriesUri = new("/v1/memories", UriKind.Relative);
    private static readonly Uri s_retrieveUri = new("/v1/memories:retrieve", UriKind.Relative);
    private static readonly Uri s_spacesUri = new("/v1/spaces", UriKind.Relative);
    private static readonly Uri s_embeddersUri = new("/v1/embedders", UriKind.Relative);

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="GoodMemClient"/> class.
    /// </summary>
    /// <param name="httpClient">Configured <see cref="HttpClient"/> pointing at the GoodMem service (base address + auth headers).</param>
    public GoodMemClient(HttpClient httpClient)
    {
        this._httpClient = Throw.IfNull(httpClient);
    }

    /// <summary>
    /// Creates a text memory in the specified space.
    /// </summary>
    /// <param name="spaceId">The space ID to store the memory in.</param>
    /// <param name="textContent">The text content to store.</param>
    /// <param name="metadata">Optional metadata to attach to the memory.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created memory response.</returns>
    public async Task<CreateMemoryResponse> CreateMemoryAsync(string spaceId, string textContent, Dictionary<string, string>? metadata = null, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhitespace(spaceId);
        Throw.IfNullOrWhitespace(textContent);

        var request = new CreateMemoryRequest
        {
            SpaceId = spaceId,
            ContentType = "text/plain",
            OriginalContent = textContent,
            Metadata = metadata
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(request, GoodMemSourceGenerationContext.Default.CreateMemoryRequest),
            Encoding.UTF8,
            "application/json");
        using var responseMessage = await this._httpClient.PostAsync(s_memoriesUri, content, cancellationToken).ConfigureAwait(false);
        responseMessage.EnsureSuccessStatusCode();

#if NET
        var response = await responseMessage.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        var response = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
        return JsonSerializer.Deserialize(response, GoodMemSourceGenerationContext.Default.CreateMemoryResponse)
            ?? throw new InvalidOperationException("Failed to deserialize create memory response.");
    }

    /// <summary>
    /// Retrieves memories via semantic search using NDJSON streaming response.
    /// </summary>
    /// <param name="query">The search query text.</param>
    /// <param name="spaceIds">The space IDs to search across.</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Enumerable of memory chunk text strings.</returns>
    public async Task<IEnumerable<string>> RetrieveMemoriesAsync(string query, IEnumerable<string> spaceIds, int maxResults = 5, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhitespace(query);

        var spaceKeys = spaceIds.Select(id => new SpaceKey { SpaceId = id }).ToArray();
        if (spaceKeys.Length == 0)
        {
            throw new ArgumentException("At least one space ID must be provided.", nameof(spaceIds));
        }

        var request = new RetrieveRequest
        {
            Message = query,
            SpaceKeys = spaceKeys,
            RequestedSize = maxResults,
            FetchMemory = true
        };

        using var httpContent = new StringContent(
            JsonSerializer.Serialize(request, GoodMemSourceGenerationContext.Default.RetrieveRequest),
            Encoding.UTF8,
            "application/json");

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, s_retrieveUri)
        {
            Content = httpContent
        };
        httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/x-ndjson"));

        using var responseMessage = await this._httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        responseMessage.EnsureSuccessStatusCode();

#if NET
        var responseText = await responseMessage.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        var responseText = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif

        return ParseNdjsonChunks(responseText);
    }

    /// <summary>
    /// Deletes a memory by ID.
    /// </summary>
    /// <param name="memoryId">The memory ID to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DeleteMemoryAsync(string memoryId, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhitespace(memoryId);

        var deleteUri = new Uri($"/v1/memories/{memoryId}", UriKind.Relative);
        using var responseMessage = await this._httpClient.DeleteAsync(deleteUri, cancellationToken).ConfigureAwait(false);
        responseMessage.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Lists available embedder models.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of embedder objects.</returns>
    public async Task<IEnumerable<EmbedderInfo>> ListEmbeddersAsync(CancellationToken cancellationToken = default)
    {
        using var responseMessage = await this._httpClient.GetAsync(s_embeddersUri, cancellationToken).ConfigureAwait(false);
        responseMessage.EnsureSuccessStatusCode();

#if NET
        var response = await responseMessage.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        var response = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif

        var result = JsonSerializer.Deserialize(response, GoodMemSourceGenerationContext.Default.EmbeddersResponse);
        return result?.Embedders ?? [];
    }

    /// <summary>
    /// Creates a space or returns an existing one with the same name.
    /// </summary>
    /// <param name="name">The space name.</param>
    /// <param name="embedderId">The embedder ID to use.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The space ID.</returns>
    public async Task<string> CreateSpaceAsync(string name, string embedderId, CancellationToken cancellationToken = default)
    {
        Throw.IfNullOrWhitespace(name);
        Throw.IfNullOrWhitespace(embedderId);

        var request = new CreateSpaceRequest
        {
            Name = name,
            SpaceEmbedders = [new SpaceEmbedder { EmbedderId = embedderId, DefaultRetrievalWeight = 1.0 }],
            DefaultChunkingConfig = new ChunkingConfig
            {
                Recursive = new RecursiveChunkingConfig
                {
                    ChunkSize = 256,
                    ChunkOverlap = 25,
                    Separators = ["\n\n", "\n", ". ", " ", ""],
                    KeepStrategy = "KEEP_END",
                    SeparatorIsRegex = false,
                    LengthMeasurement = "CHARACTER_COUNT"
                }
            }
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(request, GoodMemSourceGenerationContext.Default.CreateSpaceRequest),
            Encoding.UTF8,
            "application/json");
        using var responseMessage = await this._httpClient.PostAsync(s_spacesUri, content, cancellationToken).ConfigureAwait(false);
        responseMessage.EnsureSuccessStatusCode();

#if NET
        var response = await responseMessage.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
        var response = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif

        var spaceResponse = JsonSerializer.Deserialize(response, GoodMemSourceGenerationContext.Default.CreateSpaceResponse)
            ?? throw new InvalidOperationException("Failed to deserialize create space response.");
        return spaceResponse.SpaceId;
    }

    private static List<string> ParseNdjsonChunks(string ndjsonText)
    {
        var results = new List<string>();
        foreach (var line in ndjsonText.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            if (trimmed.StartsWith("data:", StringComparison.Ordinal))
            {
                trimmed = trimmed.Substring(5).Trim();
            }

            if (trimmed.StartsWith("event:", StringComparison.Ordinal) || string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;

                if (root.TryGetProperty("retrievedItem", out var retrievedItem)
                    && retrievedItem.TryGetProperty("chunk", out var outerChunk)
                    && outerChunk.TryGetProperty("chunk", out var innerChunk)
                    && innerChunk.TryGetProperty("chunkText", out var chunkText))
                {
                    var text = chunkText.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        results.Add(text);
                    }
                }
            }
            catch (JsonException)
            {
                // Skip malformed lines
            }
        }

        return results;
    }

    // -- Request/Response DTOs ------------------------------------------------

    internal sealed class CreateMemoryRequest
    {
        [JsonPropertyName("spaceId")] public string SpaceId { get; set; } = string.Empty;
        [JsonPropertyName("contentType")] public string ContentType { get; set; } = "text/plain";
        [JsonPropertyName("originalContent")] public string OriginalContent { get; set; } = string.Empty;
        [JsonPropertyName("metadata")] public Dictionary<string, string>? Metadata { get; set; }
    }

    internal sealed class CreateMemoryResponse
    {
        [JsonPropertyName("memoryId")] public string MemoryId { get; set; } = string.Empty;
        [JsonPropertyName("spaceId")] public string SpaceId { get; set; } = string.Empty;
        [JsonPropertyName("processingStatus")] public string ProcessingStatus { get; set; } = string.Empty;
    }

    internal sealed class RetrieveRequest
    {
        [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
        [JsonPropertyName("spaceKeys")] public SpaceKey[] SpaceKeys { get; set; } = [];
        [JsonPropertyName("requestedSize")] public int RequestedSize { get; set; } = 5;
        [JsonPropertyName("fetchMemory")] public bool FetchMemory { get; set; } = true;
    }

    internal sealed class SpaceKey
    {
        [JsonPropertyName("spaceId")] public string SpaceId { get; set; } = string.Empty;
    }

    internal sealed class EmbedderInfo
    {
        [JsonPropertyName("embedderId")] public string EmbedderId { get; set; } = string.Empty;
        [JsonPropertyName("displayName")] public string DisplayName { get; set; } = string.Empty;
        [JsonPropertyName("modelIdentifier")] public string ModelIdentifier { get; set; } = string.Empty;
    }

    internal sealed class EmbeddersResponse
    {
        [JsonPropertyName("embedders")] public EmbedderInfo[] Embedders { get; set; } = [];
    }

    internal sealed class CreateSpaceRequest
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("spaceEmbedders")] public SpaceEmbedder[] SpaceEmbedders { get; set; } = [];
        [JsonPropertyName("defaultChunkingConfig")] public ChunkingConfig? DefaultChunkingConfig { get; set; }
    }

    internal sealed class SpaceEmbedder
    {
        [JsonPropertyName("embedderId")] public string EmbedderId { get; set; } = string.Empty;
        [JsonPropertyName("defaultRetrievalWeight")] public double DefaultRetrievalWeight { get; set; } = 1.0;
    }

    internal sealed class ChunkingConfig
    {
        [JsonPropertyName("recursive")] public RecursiveChunkingConfig? Recursive { get; set; }
    }

    internal sealed class RecursiveChunkingConfig
    {
        [JsonPropertyName("chunkSize")] public int ChunkSize { get; set; } = 256;
        [JsonPropertyName("chunkOverlap")] public int ChunkOverlap { get; set; } = 25;
        [JsonPropertyName("separators")] public string[] Separators { get; set; } = [];
        [JsonPropertyName("keepStrategy")] public string KeepStrategy { get; set; } = "KEEP_END";
        [JsonPropertyName("separatorIsRegex")] public bool SeparatorIsRegex { get; set; }
        [JsonPropertyName("lengthMeasurement")] public string LengthMeasurement { get; set; } = "CHARACTER_COUNT";
    }

    internal sealed class CreateSpaceResponse
    {
        [JsonPropertyName("spaceId")] public string SpaceId { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.General,
    UseStringEnumConverter = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(GoodMemClient.CreateMemoryRequest))]
[JsonSerializable(typeof(GoodMemClient.CreateMemoryResponse))]
[JsonSerializable(typeof(GoodMemClient.RetrieveRequest))]
[JsonSerializable(typeof(GoodMemClient.EmbeddersResponse))]
[JsonSerializable(typeof(GoodMemClient.CreateSpaceRequest))]
[JsonSerializable(typeof(GoodMemClient.CreateSpaceResponse))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class GoodMemSourceGenerationContext : JsonSerializerContext;
