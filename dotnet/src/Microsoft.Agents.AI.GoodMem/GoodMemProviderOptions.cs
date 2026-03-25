// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.AI;

namespace Microsoft.Agents.AI.GoodMem;

/// <summary>
/// Options for configuring the <see cref="GoodMemProvider"/>.
/// </summary>
public sealed class GoodMemProviderOptions
{
    /// <summary>
    /// Gets or sets the GoodMem space ID to search and store memories in.
    /// </summary>
    /// <value>Required. The space ID must be set before the provider can operate.</value>
    public string SpaceId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the maximum number of memory chunks to retrieve per query.
    /// </summary>
    /// <value>Defaults to 5.</value>
    public int MaxResults { get; set; } = 5;

    /// <summary>
    /// Gets or sets the prompt prepended to retrieved memories when injecting them into the conversation context.
    /// </summary>
    /// <value>Defaults to "## Relevant Memories\nThe following memories were retrieved from long-term storage and may be relevant to the current conversation:".</value>
    public string? ContextPrompt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether conversations should be stored as memories after each invocation.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool StoreConversations { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether sensitive data such as user messages may appear in logs.
    /// </summary>
    /// <value>Defaults to <see langword="false"/>.</value>
    public bool EnableSensitiveTelemetryData { get; set; }

    /// <summary>
    /// Gets or sets the key used to store the provider state in the session's <see cref="AgentSessionStateBag"/>.
    /// </summary>
    /// <value>Defaults to the provider's type name.</value>
    public string? StateKey { get; set; }

    /// <summary>
    /// Gets or sets an optional filter function applied to request messages when building the search text
    /// during <see cref="AIContextProvider.InvokingAsync"/>.
    /// </summary>
    /// <value>
    /// When <see langword="null"/>, the provider defaults to including only
    /// <see cref="AgentRequestMessageSourceType.External"/> messages.
    /// </value>
    public Func<IEnumerable<ChatMessage>, IEnumerable<ChatMessage>>? SearchInputMessageFilter { get; set; }

    /// <summary>
    /// Gets or sets an optional filter function applied to request messages when determining which messages to
    /// store as memories during <see cref="AIContextProvider.InvokedAsync"/>.
    /// </summary>
    /// <value>
    /// When <see langword="null"/>, the provider defaults to including only
    /// <see cref="AgentRequestMessageSourceType.External"/> messages.
    /// </value>
    public Func<IEnumerable<ChatMessage>, IEnumerable<ChatMessage>>? StorageInputRequestMessageFilter { get; set; }

    /// <summary>
    /// Gets or sets an optional filter function applied to response messages when determining which messages to
    /// store as memories during <see cref="AIContextProvider.InvokedAsync"/>.
    /// </summary>
    /// <value>
    /// When <see langword="null"/>, the provider applies no filtering and includes all response messages.
    /// </value>
    public Func<IEnumerable<ChatMessage>, IEnumerable<ChatMessage>>? StorageInputResponseMessageFilter { get; set; }
}
