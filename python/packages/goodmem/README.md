# Get Started with Microsoft Agent Framework GoodMem

GoodMem is memory layer for AI agents with support for semantic storage, retrieval, and summarization. This package exposes GoodMem operations as Agent Framework tools that can be used with any Agent Framework agent.

## Installation

```bash
pip install agent-framework-goodmem --pre
```

## Quick Start

```python
from agent_framework_goodmem import GoodMemClient, create_goodmem_tools

# Create a GoodMem client
client = GoodMemClient(
    base_url="https://api.goodmem.ai",
    api_key="your-api-key",
)

# Create tools bound to the client
tools = create_goodmem_tools(client)

# Pass tools to any Agent Framework agent
from agent_framework import Agent
from agent_framework.openai import OpenAIChatClient

agent = Agent(
    name="memory-agent",
    instructions="You are an agent with persistent memory capabilities.",
    model=OpenAIChatClient(model="gpt-4o"),
    tools=tools,
)
```

## Available Tools

| Tool | Description |
|------|-------------|
| `goodmem_create_space` | Create a new space or reuse an existing one. A space is a logical container for organizing related memories. |
| `goodmem_create_memory` | Store a document (text or file) as a new memory in a space. |
| `goodmem_retrieve_memories` | Perform similarity-based semantic retrieval across one or more spaces. |
| `goodmem_get_memory` | Fetch a specific memory record by ID, including metadata and content. |
| `goodmem_delete_memory` | Permanently delete a memory and its associated chunks and embeddings. |

## Configuration

The `GoodMemClient` requires:
- **base_url**: The base URL of your GoodMem API server (e.g., `https://api.goodmem.ai` or `http://localhost:8080`)
- **api_key**: Your GoodMem API key for authentication

## Usage Example

```python
import asyncio
from agent_framework_goodmem import GoodMemClient, create_goodmem_tools

async def main():
    client = GoodMemClient(base_url="https://api.goodmem.ai", api_key="your-key")
    tools = create_goodmem_tools(client)

    # Use tools directly for testing
    create_space_tool = tools[0]
    result = await create_space_tool.invoke(
        arguments={"name": "my-space", "embedder_id": "your-embedder-id"}
    )
    print(result)

    await client.close()

asyncio.run(main())
```
