# GoodMem Package (agent-framework-goodmem)

Integration with [GoodMem](https://goodmem.ai) for persistent semantic memory in agents.

## Main Classes

- **`GoodMemClient`** - Async HTTP client for the GoodMem REST API
- **`GoodMemContextProvider`** - Context provider that automatically retrieves/stores memories using `BaseContextProvider` hooks
- **`create_goodmem_tools`** - Factory that creates GoodMem function tools for agent-controlled memory

## Module Structure

```
agent_framework_goodmem/
├── __init__.py              # Public API exports
├── _client.py               # GoodMemClient (HTTP wrapper)
├── _context_provider.py     # GoodMemContextProvider (BaseContextProvider)
└── _tools.py                # create_goodmem_tools (FunctionTool wrappers)
```

## Integration Approaches

### 1. Context Provider (automatic)

```python
from agent_framework_goodmem import GoodMemClient, GoodMemContextProvider

client = GoodMemClient(base_url="https://api.goodmem.ai", api_key="key")
provider = GoodMemContextProvider(client=client, space_id="space-id")

agent = Agent(client=llm_client, context_providers=[provider])
```

### 2. Tools (agent-controlled)

```python
from agent_framework_goodmem import GoodMemClient, create_goodmem_tools

client = GoodMemClient(base_url="https://api.goodmem.ai", api_key="key")
tools = create_goodmem_tools(client)

agent = Agent(client=llm_client, tools=tools)
```

## Import Paths

```python
from agent_framework_goodmem import GoodMemClient, GoodMemContextProvider, create_goodmem_tools
```

## Notes

- `GoodMemClient` accepts `verify_ssl=False` for local development with self-signed certificates (defaults to `True`)
- `GoodMemContextProvider` follows the same `BaseContextProvider` hooks pattern as `Mem0ContextProvider`
- Tools are lazy-loaded via `__getattr__` to avoid importing `agent_framework.tool` at module level
