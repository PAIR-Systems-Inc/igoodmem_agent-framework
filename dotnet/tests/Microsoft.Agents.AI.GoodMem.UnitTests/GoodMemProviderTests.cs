// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;

namespace Microsoft.Agents.AI.GoodMem.UnitTests;

/// <summary>
/// Unit tests for <see cref="GoodMemProvider"/>.
/// </summary>
public sealed class GoodMemProviderTests : IDisposable
{
    private static readonly AIAgent s_mockAgent = new Mock<AIAgent>().Object;

    private readonly Mock<ILogger<GoodMemProvider>> _loggerMock;
    private readonly Mock<ILoggerFactory> _loggerFactoryMock;
    private readonly RecordingHandler _handler = new();
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public GoodMemProviderTests()
    {
        this._loggerMock = new();
        this._loggerFactoryMock = new();
        this._loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(this._loggerMock.Object);
        this._loggerFactoryMock
            .Setup(f => f.CreateLogger(typeof(GoodMemProvider).FullName!))
            .Returns(this._loggerMock.Object);

        this._loggerMock
            .Setup(f => f.IsEnabled(It.IsAny<LogLevel>()))
            .Returns(true);

        this._httpClient = new HttpClient(this._handler)
        {
            BaseAddress = new Uri("https://localhost/")
        };
    }

    // -----------------------------------------------------------------
    // Constructor validation
    // -----------------------------------------------------------------

    [Fact]
    public void Constructor_Throws_WhenHttpClientIsNull()
    {
        var options = new GoodMemProviderOptions { SpaceId = "space-1" };
        var ex = Assert.Throws<ArgumentNullException>(() => new GoodMemProvider(null!, options));
        Assert.Contains("httpClient", ex.Message);
    }

    [Fact]
    public void Constructor_Throws_WhenOptionsIsNull()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new GoodMemProvider(this._httpClient, null!));
        Assert.Contains("options", ex.Message);
    }

    [Fact]
    public void Constructor_Throws_WhenBaseAddressMissing()
    {
        using var client = new HttpClient();
        var options = new GoodMemProviderOptions { SpaceId = "space-1" };
        var ex = Assert.Throws<ArgumentException>(() => new GoodMemProvider(client, options));
        Assert.StartsWith("The HttpClient BaseAddress must be set for GoodMem operations.", ex.Message);
    }

    [Fact]
    public void Constructor_Throws_WhenSpaceIdIsEmpty()
    {
        var options = new GoodMemProviderOptions { SpaceId = "" };
        var ex = Assert.Throws<ArgumentException>(() => new GoodMemProvider(this._httpClient, options));
        Assert.StartsWith("SpaceId must be set in GoodMemProviderOptions.", ex.Message);
    }

    // -----------------------------------------------------------------
    // InvokingAsync (ProvideMessagesAsync)
    // -----------------------------------------------------------------

    [Fact]
    public async Task InvokingAsync_ReturnsContextMessage_WhenMemoriesFoundAsync()
    {
        // Arrange
        this._handler.EnqueueNdjsonResponse("{\"retrievedItem\":{\"chunk\":{\"chunk\":{\"chunkText\":\"Name is Caoimhe\"}}}}");
        var options = new GoodMemProviderOptions { SpaceId = "space-1", EnableSensitiveTelemetryData = true };
        var sut = new GoodMemProvider(this._httpClient, options, this._loggerFactoryMock.Object);
        var inputMsg = new ChatMessage(ChatRole.User, "What is my name?");
        var context = new MessageAIContextProvider.InvokingContext(s_mockAgent, null, [inputMsg]);

        // Act
        var result = (await sut.InvokingAsync(context)).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal("What is my name?", result[0].Text);
        Assert.Contains("Name is Caoimhe", result[1].Text);
        Assert.Equal(AgentRequestMessageSourceType.AIContextProvider, result[1].GetAgentRequestMessageSourceType());

        var retrieveRequest = Assert.Single(this._handler.Requests, r => r.RequestMessage.RequestUri!.AbsoluteUri.EndsWith("/v1/memories:retrieve", StringComparison.Ordinal));
        using var doc = JsonDocument.Parse(retrieveRequest.RequestBody);
        Assert.Equal("What is my name?", doc.RootElement.GetProperty("message").GetString());
        Assert.Equal("space-1", doc.RootElement.GetProperty("spaceKeys")[0].GetProperty("spaceId").GetString());
    }

    [Fact]
    public async Task InvokingAsync_ReturnsOnlyInputMessages_WhenNoMemoriesFoundAsync()
    {
        // Arrange
        this._handler.EnqueueNdjsonResponse(string.Empty);
        var options = new GoodMemProviderOptions { SpaceId = "space-1" };
        var sut = new GoodMemProvider(this._httpClient, options);
        var inputMsg = new ChatMessage(ChatRole.User, "Hello");
        var context = new MessageAIContextProvider.InvokingContext(s_mockAgent, null, [inputMsg]);

        // Act
        var result = (await sut.InvokingAsync(context)).ToList();

        // Assert - only the original message, no memory context
        Assert.Single(result);
        Assert.Equal("Hello", result[0].Text);
    }

    [Fact]
    public async Task InvokingAsync_ShouldNotThrow_WhenRetrieveFailsAsync()
    {
        // Arrange
        this._handler.EnqueueEmptyInternalServerError();
        var options = new GoodMemProviderOptions { SpaceId = "space-1" };
        var sut = new GoodMemProvider(this._httpClient, options, this._loggerFactoryMock.Object);
        var context = new MessageAIContextProvider.InvokingContext(s_mockAgent, null, [new ChatMessage(ChatRole.User, "Q?")]);

        // Act
        var result = (await sut.InvokingAsync(context)).ToList();

        // Assert - still returns the original message, no exception
        Assert.Single(result);
        this._loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("GoodMemProvider: Failed to retrieve memories from GoodMem")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokingAsync_SkipsHttpCall_WhenQueryTextIsEmptyAsync()
    {
        // Arrange
        var options = new GoodMemProviderOptions { SpaceId = "space-1" };
        var sut = new GoodMemProvider(this._httpClient, options);
        // Only whitespace messages - no meaningful query
        var context = new MessageAIContextProvider.InvokingContext(s_mockAgent, null, [new ChatMessage(ChatRole.User, "   ")]);

        // Act
        var result = (await sut.InvokingAsync(context)).ToList();

        // Assert - no HTTP calls made
        Assert.Empty(this._handler.Requests);
        Assert.Single(result); // original whitespace message returned
    }

    [Fact]
    public async Task InvokingAsync_DefaultFilter_ExcludesNonExternalMessagesAsync()
    {
        // Arrange
        this._handler.EnqueueNdjsonResponse(string.Empty);
        var options = new GoodMemProviderOptions { SpaceId = "space-1" };
        var sut = new GoodMemProvider(this._httpClient, options);
        var externalMsg = new ChatMessage(ChatRole.User, "External question");
        var historyMsg = new ChatMessage(ChatRole.User, "History message")
            .WithAgentRequestMessageSource(AgentRequestMessageSourceType.ChatHistory, "src");
        var context = new MessageAIContextProvider.InvokingContext(s_mockAgent, null, [externalMsg, historyMsg]);

        // Act
        await sut.InvokingAsync(context);

        // Assert - only the external message is used in the search query
        var retrieveRequest = Assert.Single(this._handler.Requests);
        using var doc = JsonDocument.Parse(retrieveRequest.RequestBody);
        Assert.Equal("External question", doc.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task InvokingAsync_LogsRedactedData_WhenSensitiveTelemetryDisabledAsync()
    {
        // Arrange
        this._handler.EnqueueNdjsonResponse("{\"retrievedItem\":{\"chunk\":{\"chunk\":{\"chunkText\":\"Some memory\"}}}}");
        var options = new GoodMemProviderOptions { SpaceId = "space-1", EnableSensitiveTelemetryData = false };
        var sut = new GoodMemProvider(this._httpClient, options, this._loggerFactoryMock.Object);
        var context = new MessageAIContextProvider.InvokingContext(s_mockAgent, null, [new ChatMessage(ChatRole.User, "Hello?")]);

        // Act
        await sut.InvokingAsync(context);

        // Assert - logged at info level without sensitive data
        this._loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("GoodMemProvider: Retrieved 1 memory chunks")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // -----------------------------------------------------------------
    // InvokedAsync (StoreAIContextAsync)
    // -----------------------------------------------------------------

    [Fact]
    public async Task InvokedAsync_StoresConversationAsMemoryAsync()
    {
        // Arrange
        this._handler.EnqueueJsonResponse("{\"memoryId\":\"mem-1\",\"spaceId\":\"space-1\",\"processingStatus\":\"QUEUED\"}");
        var options = new GoodMemProviderOptions { SpaceId = "space-1" };
        var sut = new GoodMemProvider(this._httpClient, options);
        var mockSession = new TestAgentSession();

        var requestMessages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello, I am Caoimhe."),
            new(ChatRole.Tool, "Tool output should be ignored")
        };
        var responseMessages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "Nice to meet you, Caoimhe!")
        };

        // Act
        await sut.InvokedAsync(new AIContextProvider.InvokedContext(s_mockAgent, mockSession, requestMessages, responseMessages));

        // Assert
        var post = Assert.Single(this._handler.Requests, r => r.RequestMessage.Method == HttpMethod.Post && r.RequestMessage.RequestUri!.AbsoluteUri.EndsWith("/v1/memories", StringComparison.Ordinal));
        using var doc = JsonDocument.Parse(post.RequestBody);
        var content = doc.RootElement.GetProperty("originalContent").GetString();
        Assert.Contains("Caoimhe", content);
        Assert.Contains("Nice to meet you", content);
        Assert.DoesNotContain("Tool output", content);
    }

    [Fact]
    public async Task InvokedAsync_SkipsStorage_WhenStoreConversationsFalseAsync()
    {
        // Arrange
        var options = new GoodMemProviderOptions { SpaceId = "space-1", StoreConversations = false };
        var sut = new GoodMemProvider(this._httpClient, options);
        var mockSession = new TestAgentSession();

        // Act
        await sut.InvokedAsync(new AIContextProvider.InvokedContext(s_mockAgent, mockSession,
            [new ChatMessage(ChatRole.User, "Hello")],
            [new ChatMessage(ChatRole.Assistant, "Hi")]));

        // Assert - no HTTP calls
        Assert.Empty(this._handler.Requests);
    }

    [Fact]
    public async Task InvokedAsync_ShouldNotThrow_WhenStorageFailsAsync()
    {
        // Arrange
        this._handler.EnqueueEmptyInternalServerError();
        var options = new GoodMemProviderOptions { SpaceId = "space-1" };
        var sut = new GoodMemProvider(this._httpClient, options, this._loggerFactoryMock.Object);
        var mockSession = new TestAgentSession();

        // Act — must not throw
        await sut.InvokedAsync(new AIContextProvider.InvokedContext(s_mockAgent, mockSession,
            [new ChatMessage(ChatRole.User, "Hello")],
            [new ChatMessage(ChatRole.Assistant, "Hi")]));

        // Assert
        this._loggerMock.Verify(
            l => l.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("GoodMemProvider: Failed to store messages to GoodMem")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvokedAsync_SkipsStorage_WhenAllMessagesHaveEmptyTextAsync()
    {
        // Arrange
        var options = new GoodMemProviderOptions { SpaceId = "space-1" };
        var sut = new GoodMemProvider(this._httpClient, options);
        var mockSession = new TestAgentSession();

        // Act — messages with no text content
        await sut.InvokedAsync(new AIContextProvider.InvokedContext(s_mockAgent, mockSession,
            [new ChatMessage(ChatRole.User, "   ")],
            []));

        // Assert - no HTTP calls
        Assert.Empty(this._handler.Requests);
    }

    [Fact]
    public async Task InvokedAsync_DefaultFilter_ExcludesNonExternalRequestMessagesAsync()
    {
        // Arrange
        this._handler.EnqueueJsonResponse("{\"memoryId\":\"m\",\"spaceId\":\"s\",\"processingStatus\":\"QUEUED\"}");
        var options = new GoodMemProviderOptions { SpaceId = "space-1" };
        var sut = new GoodMemProvider(this._httpClient, options);
        var mockSession = new TestAgentSession();

        var externalMsg = new ChatMessage(ChatRole.User, "External message");
        var historyMsg = new ChatMessage(ChatRole.User, "History message")
            .WithAgentRequestMessageSource(AgentRequestMessageSourceType.ChatHistory, "src");

        // Act
        await sut.InvokedAsync(new AIContextProvider.InvokedContext(s_mockAgent, mockSession,
            [externalMsg, historyMsg], []));

        // Assert - only the external message stored
        var post = Assert.Single(this._handler.Requests, r => r.RequestMessage.Method == HttpMethod.Post);
        using var doc = JsonDocument.Parse(post.RequestBody);
        var content = doc.RootElement.GetProperty("originalContent").GetString();
        Assert.Contains("External message", content);
        Assert.DoesNotContain("History message", content);
    }

    // -----------------------------------------------------------------
    // NDJSON parsing edge cases
    // -----------------------------------------------------------------

    [Fact]
    public async Task InvokingAsync_ParsesMultipleNdjsonLinesAsync()
    {
        // Arrange
        const string ndjson = "{\"retrievedItem\":{\"chunk\":{\"chunk\":{\"chunkText\":\"Memory one\"}}}}\n" +
                              "{\"retrievedItem\":{\"chunk\":{\"chunk\":{\"chunkText\":\"Memory two\"}}}}\n";
        this._handler.EnqueueNdjsonResponse(ndjson);
        var options = new GoodMemProviderOptions { SpaceId = "space-1" };
        var sut = new GoodMemProvider(this._httpClient, options);
        var context = new MessageAIContextProvider.InvokingContext(s_mockAgent, null, [new ChatMessage(ChatRole.User, "What do I know?")]);

        // Act
        var result = (await sut.InvokingAsync(context)).ToList();

        // Assert - input + one context message containing both memories
        Assert.Equal(2, result.Count);
        Assert.Contains("Memory one", result[1].Text);
        Assert.Contains("Memory two", result[1].Text);
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    public void Dispose()
    {
        if (!this._disposed)
        {
            this._httpClient.Dispose();
            this._handler.Dispose();
            this._disposed = true;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        public List<(HttpRequestMessage RequestMessage, string RequestBody)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
#if NET
            var requestBody = await (request.Content?.ReadAsStringAsync(cancellationToken) ?? Task.FromResult(string.Empty));
#else
            var requestBody = await (request.Content?.ReadAsStringAsync() ?? Task.FromResult(string.Empty));
#endif
            this.Requests.Add((request, requestBody));
            return this._responses.Count > 0
                ? this._responses.Dequeue()
                : new HttpResponseMessage(HttpStatusCode.OK);
        }

        public void EnqueueNdjsonResponse(string ndjson)
        {
            this._responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ndjson, Encoding.UTF8, "application/x-ndjson")
            });
        }

        public void EnqueueJsonResponse(string json)
        {
            this._responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }

        public void EnqueueEmptyInternalServerError()
            => this._responses.Enqueue(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }

    private sealed class TestAgentSession : AgentSession
    {
        public TestAgentSession()
        {
            this.StateBag = new AgentSessionStateBag();
        }
    }
}
