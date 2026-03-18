# Copyright (c) Microsoft. All rights reserved.

"""Integration tests for the GoodMem Agent Framework package.

These tests hit a live GoodMem API instance and verify the full create-space,
create-memory (text + PDF), retrieve-memories, get-memory, and delete-memory
workflow end-to-end.

Run with:
    GOODMEM_API_KEY=<key> GOODMEM_BASE_URL=<url> python3 -m pytest tests/test_goodmem_integration.py -v -s -m integration
"""

from __future__ import annotations

import asyncio
import json
import os
import sys

import pytest

# Allow direct import without agent_framework installed
sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))
from agent_framework_goodmem._client import GoodMemClient  # noqa: E402

GOODMEM_API_KEY = os.environ.get("GOODMEM_API_KEY", "gm_rttn7pla4rm3ry6hqakfnnaal4")
GOODMEM_BASE_URL = os.environ.get("GOODMEM_BASE_URL", "https://localhost:8080")
PDF_FILE_PATH = os.environ.get(
    "GOODMEM_PDF_PATH",
    "/home/bashar/Downloads/New Quran.com Search Analysis (Nov 26, 2025)-1.pdf",
)


@pytest.fixture(scope="module")
def event_loop():
    loop = asyncio.new_event_loop()
    yield loop
    loop.close()


@pytest.fixture(scope="module")
async def client():
    c = GoodMemClient(base_url=GOODMEM_BASE_URL, api_key=GOODMEM_API_KEY)
    yield c
    await c.close()


# Store IDs across tests in module scope
_state: dict[str, str] = {}


@pytest.mark.integration
class TestGoodMemIntegration:
    """Full end-to-end integration test suite for GoodMem."""

    # -- 1. Create Space -------------------------------------------------------

    async def test_01_create_space(self, client: GoodMemClient) -> None:
        """Create a new space (or reuse existing) and store its ID for later tests."""
        embedders = await client.list_embedders()
        assert len(embedders) > 0, "No embedders available on the GoodMem server"
        embedder_id = embedders[0].get("embedderId") or embedders[0].get("id")
        assert embedder_id, "Could not extract embedder ID"

        result = await client.create_space(name="autogen-integration-test", embedder_id=embedder_id)
        print(f"\n[create_space] result: {json.dumps(result, indent=2)}")

        assert result["success"] is True
        assert "spaceId" in result
        _state["space_id"] = result["spaceId"]
        _state["embedder_id"] = embedder_id

    # -- 2. Create Memory (text) -----------------------------------------------

    async def test_02_create_memory_text(self, client: GoodMemClient) -> None:
        """Create a text memory and verify the response."""
        space_id = _state.get("space_id")
        assert space_id, "space_id not set -- test_01_create_space must run first"

        result = await client.create_memory(
            space_id=space_id,
            text_content="Agent Framework is a powerful open-source framework for building AI agents.",
        )
        print(f"\n[create_memory_text] result: {json.dumps(result, indent=2)}")

        assert result["success"] is True
        assert result.get("memoryId"), "No memoryId returned"
        _state["text_memory_id"] = result["memoryId"]

    # -- 3. Create Memory (PDF) ------------------------------------------------

    async def test_03_create_memory_pdf(self, client: GoodMemClient) -> None:
        """Create a memory from a PDF file."""
        space_id = _state.get("space_id")
        assert space_id, "space_id not set"

        if not os.path.isfile(PDF_FILE_PATH):
            pytest.skip(f"PDF file not found at {PDF_FILE_PATH}")

        result = await client.create_memory(space_id=space_id, file_path=PDF_FILE_PATH)
        print(f"\n[create_memory_pdf] result: {json.dumps(result, indent=2)}")

        assert result["success"] is True
        assert result.get("memoryId"), "No memoryId returned"
        assert result.get("contentType") == "application/pdf"
        _state["pdf_memory_id"] = result["memoryId"]

    # -- 4. Retrieve Memories --------------------------------------------------

    async def test_04_retrieve_memories(self, client: GoodMemClient) -> None:
        """Retrieve memories via semantic search and verify results."""
        space_id = _state.get("space_id")
        assert space_id, "space_id not set"

        result = await client.retrieve_memories(
            query="AI agent framework",
            space_ids=[space_id],
            max_results=5,
            wait_for_indexing=True,
        )
        print(f"\n[retrieve_memories] result: {json.dumps(result, indent=2, default=str)}")

        assert result["success"] is True
        assert result["totalResults"] > 0, "No results returned from retrieval"

    # -- 5. Get Memory ---------------------------------------------------------

    async def test_05_get_memory(self, client: GoodMemClient) -> None:
        """Fetch a specific memory by ID."""
        memory_id = _state.get("text_memory_id")
        assert memory_id, "text_memory_id not set"

        result = await client.get_memory(memory_id, include_content=True)
        print(f"\n[get_memory] result: {json.dumps(result, indent=2, default=str)}")

        assert result["success"] is True
        assert "memory" in result

    # -- 6. Delete Memory ------------------------------------------------------

    async def test_06_delete_memory(self, client: GoodMemClient) -> None:
        """Delete a memory and confirm success."""
        memory_id = _state.get("text_memory_id")
        assert memory_id, "text_memory_id not set"

        result = await client.delete_memory(memory_id)
        print(f"\n[delete_memory] result: {json.dumps(result, indent=2)}")

        assert result["success"] is True
        assert result["memoryId"] == memory_id

    # -- 7. Delete PDF memory (cleanup) ----------------------------------------

    async def test_07_cleanup_pdf_memory(self, client: GoodMemClient) -> None:
        """Clean up the PDF memory created in test_03."""
        memory_id = _state.get("pdf_memory_id")
        if not memory_id:
            pytest.skip("No PDF memory to clean up")

        result = await client.delete_memory(memory_id)
        print(f"\n[cleanup_pdf_memory] result: {json.dumps(result, indent=2)}")
        assert result["success"] is True
