# Get Started with Microsoft Agent Framework GoodMem

[GoodMem](https://goodmem.ai) is a semantic memory layer for AI agents with support for storage, retrieval, and summarization. This package integrates GoodMem with the Microsoft Agent Framework, providing **two complementary approaches** for adding persistent memory to your agents.

## Installation

```bash
pip install agent-framework-goodmem --pre
```

## Quickstart

### 1. Set up the client

```python
from agent_framework_goodmem import GoodMemClient

client = GoodMemClient(
    base_url="https://api.goodmem.ai",  # or your self-hosted URL
    api_key="your-api-key",
)
```

For local development with self-signed certificates:

```python
client = GoodMemClient(
    base_url="https://localhost:8080",
    api_key="your-api-key",
    verify_ssl=False,
)
```

### 2. Create a space

A space is a logical container for organizing related memories, configured with an embedder for vector search.

```python
import asyncio

async def setup():
    # List available embedders
    embedders = await client.list_embedders()
    embedder_id = embedders[0]["embedderId"]

    # Create a space (or reuse existing by name)
    space = await client.create_space(
        name="my-agent-memory",
        embedder_id=embedder_id,
    )
    print(f"Space ID: {space['spaceId']}")
    return space["spaceId"]

space_id = asyncio.run(setup())
```

### 3. Choose your integration approach

## Approach A: Context Provider (Automatic)

The **recommended** approach for most use cases. `GoodMemContextProvider` extends `BaseContextProvider` and automatically:
- **Before each run** — searches for relevant memories and injects them into the conversation context
- **After each run** — stores the conversation as a new memory for future retrieval

```python
from agent_framework import Agent
from agent_framework.openai import OpenAIChatClient
from agent_framework_goodmem import GoodMemClient, GoodMemContextProvider

client = GoodMemClient(base_url="https://api.goodmem.ai", api_key="your-key")

provider = GoodMemContextProvider(
    client=client,
    space_id="your-space-id",
    max_results=5,                # chunks to retrieve per query
    store_conversations=True,     # persist conversations after each run
)

agent = Agent(
    client=OpenAIChatClient(model="gpt-4o"),
    name="memory-agent",
    instructions="You are a helpful assistant with persistent memory.",
    context_providers=[provider],
)

# Memories are retrieved and stored automatically
response = await agent.run("What did we discuss last time?")
```

## Approach B: Tools (Agent-Controlled)

For use cases where the **agent should decide** when to read/write memories. Exposes GoodMem operations as function tools the model can call.

```python
from agent_framework import Agent
from agent_framework.openai import OpenAIChatClient
from agent_framework_goodmem import GoodMemClient, create_goodmem_tools

client = GoodMemClient(base_url="https://api.goodmem.ai", api_key="your-key")
tools = create_goodmem_tools(client)

agent = Agent(
    client=OpenAIChatClient(model="gpt-4o"),
    name="memory-agent",
    instructions="You have access to persistent memory tools. Use them to remember important information.",
    tools=tools,
)

response = await agent.run("Remember that my favorite color is blue.")
```

### Available Tools

| Tool | Description |
|------|-------------|
| `goodmem_create_space` | Create a new space or reuse an existing one |
| `goodmem_create_memory` | Store text or a file as a new memory |
| `goodmem_retrieve_memories` | Semantic search across one or more spaces |
| `goodmem_get_memory` | Fetch a specific memory by ID |
| `goodmem_delete_memory` | Permanently delete a memory |

## Combining Both Approaches

You can use both approaches together — the context provider handles automatic retrieval while tools give the agent explicit control for writing:

```python
provider = GoodMemContextProvider(
    client=client,
    space_id=space_id,
    store_conversations=False,  # let the agent decide what to store via tools
)

agent = Agent(
    client=OpenAIChatClient(model="gpt-4o"),
    name="memory-agent",
    instructions="Relevant memories are provided automatically. Use the memory tools to store important new information.",
    context_providers=[provider],
    tools=create_goodmem_tools(client),
)
```

## Configuration Reference

### `GoodMemClient`

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `base_url` | `str` | *required* | GoodMem API server URL |
| `api_key` | `str` | *required* | API key for `X-API-Key` authentication |
| `verify_ssl` | `bool` | `True` | Enable/disable SSL certificate verification |

### `GoodMemContextProvider`

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `client` | `GoodMemClient` | *required* | Configured GoodMem client |
| `space_id` | `str` | *required* | Space to search and store memories in |
| `source_id` | `str` | `"goodmem"` | Unique identifier for this provider |
| `max_results` | `int` | `5` | Max memory chunks to retrieve per query |
| `context_prompt` | `str` | *(built-in)* | Prompt prepended to retrieved memories |
| `store_conversations` | `bool` | `True` | Store conversations after each run |
| `wait_for_indexing` | `bool` | `False` | Wait for indexing during retrieval |
