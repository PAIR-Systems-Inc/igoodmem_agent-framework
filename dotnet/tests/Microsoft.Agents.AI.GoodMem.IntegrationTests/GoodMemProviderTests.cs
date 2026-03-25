// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Shared.IntegrationTests;

namespace Microsoft.Agents.AI.GoodMem.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="GoodMemProvider"/> against a configured GoodMem service.
/// </summary>
public sealed class GoodMemProviderTests : IDisposable
{
    private const string SkipReason = null; // Set to a non-null string to skip all tests.

    private static readonly AIAgent s_mockAgent = new Moq.Mock<AIAgent>().Object;

    private readonly HttpClient _httpClient;
    private readonly string? _spaceId;
    private readonly bool _configured;

    public GoodMemProviderTests()
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile(path: "testsettings.development.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddUserSecrets<GoodMemProviderTests>(optional: true)
            .Build();

        var endpoint = configuration[TestSettings.GoodMemEndpoint];
        var apiKey = configuration[TestSettings.GoodMemApiKey];
        this._spaceId = configuration[TestSettings.GoodMemSpaceId];

        // Accept self-signed certs for local dev servers.
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        this._httpClient = new HttpClient(handler);

        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(apiKey))
        {
            this._httpClient.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
            this._httpClient.DefaultRequestHeaders.Add("X-API-Key", apiKey);
            this._configured = true;
        }
    }

    [Fact(Skip = SkipReason)]
    public async Task CanStoreAndRetrieveConversationMemoryAsync()
    {
        if (!this._configured || string.IsNullOrWhiteSpace(this._spaceId))
        {
            Assert.Skip("GoodMem service not configured. Set GOODMEM_ENDPOINT, GOODMEM_API_KEY, and GOODMEM_SPACE_ID.");
        }

        // Arrange
        var options = new GoodMemProviderOptions
        {
            SpaceId = this._spaceId!,
            MaxResults = 5,
            StoreConversations = true
        };
        var sut = new GoodMemProvider(this._httpClient, options);
        var mockSession = new TestAgentSession();

        // Store a unique marker so we can assert retrieval.
        var marker = Guid.NewGuid().ToString("N");
        var requestMessages = new List<ChatMessage>
        {
            new(ChatRole.User, "My favourite color is indigo. Marker: " + marker + ".")
        };
        var responseMessages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "Got it, I will remember that your favourite color is indigo.")
        };

        // Act — store
        await sut.InvokedAsync(new AIContextProvider.InvokedContext(s_mockAgent, mockSession, requestMessages, responseMessages));

        // Wait for indexing with retry.
        var question = new ChatMessage(ChatRole.User, "What is my favourite color?");
        var invokingContext = new MessageAIContextProvider.InvokingContext(s_mockAgent, mockSession, [question]);
        var result = await GetContextWithRetryAsync(sut, invokingContext);

        // Assert
        var memoryMessage = result.Skip(1).FirstOrDefault();
        Assert.NotNull(memoryMessage);
        Assert.Contains("indigo", memoryMessage!.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = SkipReason)]
    public async Task ReturnsEmptyContext_WhenNoRelevantMemoriesExistAsync()
    {
        if (!this._configured)
        {
            Assert.Skip("GoodMem service not configured.");
        }

        // Create a fresh, empty space for this test so there are no pre-existing memories.
        var shortId = Guid.NewGuid().ToString("N").AsSpan(0, 8).ToString();
        var emptySpaceId = await this.CreateTestSpaceAsync("dotnet-it-empty-" + shortId);

        // Arrange
        var options = new GoodMemProviderOptions
        {
            SpaceId = emptySpaceId,
            MaxResults = 5,
            StoreConversations = false
        };
        var sut = new GoodMemProvider(this._httpClient, options);
        var mockSession = new TestAgentSession();

        var question = new ChatMessage(ChatRole.User, "Any question — this space is empty so nothing should be retrieved.");
        var context = new MessageAIContextProvider.InvokingContext(s_mockAgent, mockSession, [question]);

        // Act
        var result = (await sut.InvokingAsync(context)).ToList();

        // Assert — only the original question, no memories appended.
        Assert.Single(result);
        Assert.Equal(question.Text, result[0].Text);
    }

    [Fact(Skip = SkipReason)]
    public async Task DoesNotStoreConversation_WhenStoreConversationsFalseAsync()
    {
        if (!this._configured || string.IsNullOrWhiteSpace(this._spaceId))
        {
            Assert.Skip("GoodMem service not configured.");
        }

        // Arrange — wrap the HttpClient to count POST requests.
        using var countingClient = new CountingHttpClient(this._httpClient.BaseAddress!, this._httpClient.DefaultRequestHeaders);

        var options = new GoodMemProviderOptions
        {
            SpaceId = this._spaceId!,
            StoreConversations = false
        };
        var sut = new GoodMemProvider(countingClient.Client, options);
        var mockSession = new TestAgentSession();

        // Act
        await sut.InvokedAsync(new AIContextProvider.InvokedContext(s_mockAgent, mockSession,
            [new ChatMessage(ChatRole.User, "Hello")],
            [new ChatMessage(ChatRole.Assistant, "Hi")]));

        // Assert — no POST to /v1/memories made.
        Assert.Equal(0, countingClient.PostCount);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static readonly string[] s_chunkSeparators = ["\n\n", "\n", ". ", " ", ""];

    private async Task<string> CreateTestSpaceAsync(string name)
    {
        // Get the first available embedder, then create the space.
        using var embeddersResp = await this._httpClient.GetAsync(new Uri("/v1/embedders", UriKind.Relative));
        embeddersResp.EnsureSuccessStatusCode();
        var embeddersJson = await embeddersResp.Content.ReadAsStringAsync();
        using var embeddersDoc = System.Text.Json.JsonDocument.Parse(embeddersJson);
        var embedderId = embeddersDoc.RootElement.GetProperty("embedders")[0].GetProperty("embedderId").GetString()!;

        var body = System.Text.Json.JsonSerializer.Serialize(new
        {
            name,
            spaceEmbedders = new[] { new { embedderId, defaultRetrievalWeight = 1.0 } },
            defaultChunkingConfig = new
            {
                recursive = new
                {
                    chunkSize = 256,
                    chunkOverlap = 25,
                    separators = s_chunkSeparators,
                    keepStrategy = "KEEP_END",
                    separatorIsRegex = false,
                    lengthMeasurement = "CHARACTER_COUNT"
                }
            }
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, new Uri("/v1/spaces", UriKind.Relative))
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        using var resp = await this._httpClient.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var respJson = await resp.Content.ReadAsStringAsync();
        using var doc = System.Text.Json.JsonDocument.Parse(respJson);
        return doc.RootElement.GetProperty("spaceId").GetString()!;
    }

    private static async Task<IEnumerable<ChatMessage>> GetContextWithRetryAsync(
        GoodMemProvider provider,
        MessageAIContextProvider.InvokingContext context,
        int attempts = 10,
        int delayMs = 2000)
    {
        IEnumerable<ChatMessage>? result = null;
        for (int i = 0; i < attempts; i++)
        {
            result = await provider.InvokingAsync(context);
            if (result.Count() > 1)
            {
                return result;
            }
            await Task.Delay(delayMs);
        }
        return result ?? [];
    }

    public void Dispose() => this._httpClient.Dispose();

    // -----------------------------------------------------------------
    // Helper types
    // -----------------------------------------------------------------

    private sealed class CountingHttpClient : IDisposable
    {
        private readonly RequestCountingHandler _handler;

        public HttpClient Client { get; }
        public int PostCount => this._handler.PostCount;

        public CountingHttpClient(Uri baseAddress, System.Net.Http.Headers.HttpRequestHeaders headers)
        {
            this._handler = new RequestCountingHandler(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            });
            this.Client = new HttpClient(this._handler) { BaseAddress = baseAddress };
            foreach (var header in headers)
            {
                this.Client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        public void Dispose()
        {
            this.Client.Dispose();
            this._handler.Dispose();
        }
    }

    private sealed class RequestCountingHandler : DelegatingHandler
    {
        public int PostCount { get; private set; }

        public RequestCountingHandler(HttpMessageHandler inner) : base(inner) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                this.PostCount++;
            }
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class TestAgentSession : AgentSession
    {
        public TestAgentSession()
        {
            this.StateBag = new AgentSessionStateBag();
        }
    }
}
