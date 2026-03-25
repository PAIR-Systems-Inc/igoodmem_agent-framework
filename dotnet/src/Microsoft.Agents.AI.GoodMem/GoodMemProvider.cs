// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Shared.Diagnostics;

namespace Microsoft.Agents.AI.GoodMem;

/// <summary>
/// Provides a GoodMem backed <see cref="MessageAIContextProvider"/> that retrieves relevant memories
/// to augment the agent invocation context and persists conversation messages as memories.
/// </summary>
/// <remarks>
/// <para>
/// The provider searches for semantically relevant memory chunks before each invocation and injects them
/// as user messages. After invocation, request and response messages are stored as new memories in
/// the configured GoodMem space.
/// </para>
/// <para>
/// <strong>Security considerations:</strong>
/// <list type="bullet">
/// <item><description><strong>External service trust:</strong> This provider communicates with an external GoodMem service over HTTP.
/// Agent Framework does not manage authentication, encryption, or connection details for this service — these are the responsibility
/// of the <see cref="HttpClient"/> configuration. Ensure the HTTP client is configured with appropriate authentication
/// and uses HTTPS to protect data in transit.</description></item>
/// <item><description><strong>PII and sensitive data:</strong> Conversation messages (including user inputs, LLM responses, and system
/// instructions) are sent to the external GoodMem service for storage. These messages may contain PII or sensitive information.
/// Ensure the GoodMem service is configured with appropriate data retention policies and access controls.</description></item>
/// <item><description><strong>Indirect prompt injection:</strong> Memories retrieved from the GoodMem service are injected into the LLM
/// context as user messages. If the memory store is compromised, adversarial content could influence LLM behavior. The data
/// returned from the service is accepted as-is without validation or sanitization.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class GoodMemProvider : MessageAIContextProvider
{
    private const string DefaultContextPrompt = "## Relevant Memories\nThe following memories were retrieved from long-term storage and may be relevant to the current conversation:";

    private readonly GoodMemClient _client;
    private readonly ILogger<GoodMemProvider>? _logger;
    private readonly string _spaceId;
    private readonly int _maxResults;
    private readonly string _contextPrompt;
    private readonly bool _storeConversations;
    private readonly bool _enableSensitiveTelemetryData;

    /// <summary>
    /// Initializes a new instance of the <see cref="GoodMemProvider"/> class.
    /// </summary>
    /// <param name="httpClient">Configured <see cref="HttpClient"/> (base address + X-API-Key header).</param>
    /// <param name="options">Provider options including the space ID to use.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="httpClient"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Thrown when the HttpClient BaseAddress is not set or SpaceId is empty.</exception>
    /// <remarks>
    /// The base address and API key should be set on the <paramref name="httpClient"/> before passing it.
    /// <code>
    /// using var httpClient = new HttpClient();
    /// httpClient.BaseAddress = new Uri("https://api.goodmem.ai");
    /// httpClient.DefaultRequestHeaders.Add("X-API-Key", "&lt;Your APIKey&gt;");
    /// var options = new GoodMemProviderOptions { SpaceId = "your-space-id" };
    /// new GoodMemProvider(httpClient, options);
    /// </code>
    /// </remarks>
    public GoodMemProvider(HttpClient httpClient, GoodMemProviderOptions options, ILoggerFactory? loggerFactory = null)
        : base(options?.SearchInputMessageFilter, options?.StorageInputRequestMessageFilter, options?.StorageInputResponseMessageFilter)
    {
        Throw.IfNull(httpClient);
        Throw.IfNull(options);

        if (string.IsNullOrWhiteSpace(httpClient.BaseAddress?.AbsoluteUri))
        {
            throw new ArgumentException("The HttpClient BaseAddress must be set for GoodMem operations.", nameof(httpClient));
        }

        if (string.IsNullOrWhiteSpace(options.SpaceId))
        {
            throw new ArgumentException("SpaceId must be set in GoodMemProviderOptions.", nameof(options));
        }

        this._logger = loggerFactory?.CreateLogger<GoodMemProvider>();
        this._client = new GoodMemClient(httpClient);
        this._spaceId = options.SpaceId;
        this._maxResults = options.MaxResults;
        this._contextPrompt = options.ContextPrompt ?? DefaultContextPrompt;
        this._storeConversations = options.StoreConversations;
        this._enableSensitiveTelemetryData = options.EnableSensitiveTelemetryData;
    }

    /// <inheritdoc />
    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideMessagesAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        Throw.IfNull(context);

        string queryText = string.Join(
            Environment.NewLine,
            context.RequestMessages
                .Where(m => !string.IsNullOrWhiteSpace(m.Text))
                .Select(m => m.Text));

        if (string.IsNullOrWhiteSpace(queryText))
        {
            return [];
        }

        try
        {
            var chunks = (await this._client.RetrieveMemoriesAsync(
                queryText,
                [this._spaceId],
                this._maxResults,
                cancellationToken).ConfigureAwait(false)).ToList();

            var outputMessageText = chunks.Count == 0
                ? null
                : $"{this._contextPrompt}\n{string.Join(Environment.NewLine, chunks)}";

            if (this._logger?.IsEnabled(LogLevel.Information) is true)
            {
                this._logger.LogInformation(
                    "GoodMemProvider: Retrieved {Count} memory chunks. SpaceId: '{SpaceId}'.",
                    chunks.Count,
                    this._spaceId);

                if (outputMessageText is not null && this._logger.IsEnabled(LogLevel.Trace))
                {
                    this._logger.LogTrace(
                        "GoodMemProvider: Search Results\nInput:{Input}\nOutput:{MessageText}\nSpaceId: '{SpaceId}'.",
                        this.SanitizeLogData(queryText),
                        this.SanitizeLogData(outputMessageText),
                        this._spaceId);
                }
            }

            return outputMessageText is not null
                ? [new ChatMessage(ChatRole.User, outputMessageText)]
                : [];
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (this._logger?.IsEnabled(LogLevel.Error) is true)
            {
                this._logger.LogError(
                    ex,
                    "GoodMemProvider: Failed to retrieve memories from GoodMem. SpaceId: '{SpaceId}'.",
                    this._spaceId);
            }

            return [];
        }
    }

    /// <inheritdoc />
    protected override async ValueTask StoreAIContextAsync(InvokedContext context, CancellationToken cancellationToken = default)
    {
        if (!this._storeConversations)
        {
            return;
        }

        try
        {
            var messagesToStore = context.RequestMessages
                .Concat(context.ResponseMessages ?? [])
                .Where(m => (m.Role == ChatRole.User || m.Role == ChatRole.Assistant || m.Role == ChatRole.System)
                    && !string.IsNullOrWhiteSpace(m.Text))
                .ToList();

            if (messagesToStore.Count == 0)
            {
                return;
            }

            var conversationText = string.Join(
                Environment.NewLine,
                messagesToStore.Select(m => $"{m.Role}: {m.Text}"));

            var metadata = new Dictionary<string, string>
            {
                ["source"] = "agent-framework-context-provider"
            };

            await this._client.CreateMemoryAsync(
                this._spaceId,
                conversationText,
                metadata,
                cancellationToken).ConfigureAwait(false);

            if (this._logger?.IsEnabled(LogLevel.Information) is true)
            {
                this._logger.LogInformation(
                    "GoodMemProvider: Stored {Count} messages as memory. SpaceId: '{SpaceId}'.",
                    messagesToStore.Count,
                    this._spaceId);
            }
        }
        catch (Exception ex)
        {
            if (this._logger?.IsEnabled(LogLevel.Error) is true)
            {
                this._logger.LogError(
                    ex,
                    "GoodMemProvider: Failed to store messages to GoodMem. SpaceId: '{SpaceId}'.",
                    this._spaceId);
            }
        }
    }

    private string? SanitizeLogData(string? data) => this._enableSensitiveTelemetryData ? data : "<redacted>";
}
