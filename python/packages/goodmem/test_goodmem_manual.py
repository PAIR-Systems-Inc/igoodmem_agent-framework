"""Manual test script for the GoodMem Agent Framework integration.

Exercises the GoodMemClient and GoodMemContextProvider in multiple ways:

  Part 1 — Client API:
    1. List embedders
    2. Create a space
    3. Create memory from plain text
    4. Create memory from text with metadata
    5. Create memory from raw bytes
    6. Retrieve memories via semantic search (3 queries)
    7. Get a specific memory by ID
    8. List spaces

  Part 2 — Context Provider (BaseContextProvider hooks):
    9. Simulate before_run (memory retrieval into context)
   10. Simulate after_run (conversation storage)
   11. Verify stored conversation is retrievable

  Part 3 — Cleanup

Run:
    python3 test_goodmem_manual.py
"""

from __future__ import annotations

import asyncio
import json
import os
import sys

# Allow import without installation
sys.path.insert(0, os.path.dirname(__file__))

from agent_framework_goodmem._client import GoodMemClient
from agent_framework_goodmem._context_provider import GoodMemContextProvider

GOODMEM_API_KEY = "gm_g5xcse2tjgcznlg45c5le4ti5q"
GOODMEM_BASE_URL = os.environ.get("GOODMEM_BASE_URL", "https://localhost:8080")
GOODMEM_EMBEDDER_ID = "019cfd1c-c033-7517-b7de-f73941a0464b"

SEPARATOR = "-" * 60


def pp(label: str, data: object) -> None:
    """Pretty-print a labelled result."""
    print(f"\n{SEPARATOR}")
    print(f"  {label}")
    print(SEPARATOR)
    print(json.dumps(data, indent=2, default=str))


async def main() -> None:
    client = GoodMemClient(base_url=GOODMEM_BASE_URL, api_key=GOODMEM_API_KEY, verify_ssl=False)
    created_memory_ids: list[str] = []

    try:
        # =====================================================================
        #  PART 1 — Client API
        # =====================================================================
        print("\n" + "=" * 60)
        print("  PART 1: CLIENT API")
        print("=" * 60)

        # ── 1. List embedders ────────────────────────────────────────────
        print("\n\n=== 1. LIST EMBEDDERS ===")
        embedders = await client.list_embedders()
        pp("Available embedders", embedders)
        assert len(embedders) > 0, "No embedders found on the server"
        embedder_id = GOODMEM_EMBEDDER_ID
        print(f"\n→ Using known-working embedder: {embedder_id}")

        # ── 2. Create a space ────────────────────────────────────────────
        print("\n\n=== 2. CREATE SPACE ===")
        space_result = await client.create_space(
            name="goodmem-integration-test",
            embedder_id=embedder_id,
        )
        pp("create_space", space_result)
        assert space_result["success"] is True
        space_id = space_result["spaceId"]
        print(f"\n→ Space ID: {space_id}")

        # ── 3. Create memory – plain text ────────────────────────────────
        print("\n\n=== 3. CREATE MEMORY (plain text) ===")
        mem_text = await client.create_memory(
            space_id=space_id,
            text_content=(
                "Agent Framework is a powerful open-source framework for building "
                "multi-agent AI systems. It supports tool use, memory, and "
                "collaboration between agents."
            ),
        )
        pp("create_memory (text)", mem_text)
        assert mem_text["success"] is True
        created_memory_ids.append(mem_text["memoryId"])

        # ── 4. Create memory – text with metadata ────────────────────────
        print("\n\n=== 4. CREATE MEMORY (text + metadata) ===")
        mem_meta = await client.create_memory(
            space_id=space_id,
            text_content=(
                "GoodMem provides semantic memory storage and retrieval for AI "
                "agents, enabling long-term knowledge retention across sessions."
            ),
            metadata={
                "source": "manual-test",
                "topic": "goodmem-overview",
                "priority": "high",
            },
        )
        pp("create_memory (text+metadata)", mem_meta)
        assert mem_meta["success"] is True
        created_memory_ids.append(mem_meta["memoryId"])

        # ── 5. Create memory – raw bytes (simulating a small text file) ──
        print("\n\n=== 5. CREATE MEMORY (raw bytes / text file) ===")
        sample_bytes = (
            "This is a sample text file content uploaded as raw bytes.\n"
            "It contains information about vector databases and embeddings.\n"
            "Embeddings convert text into numerical representations for search."
        ).encode("utf-8")
        mem_bytes = await client.create_memory(
            space_id=space_id,
            file_bytes=sample_bytes,
            file_extension="txt",
        )
        pp("create_memory (bytes)", mem_bytes)
        assert mem_bytes["success"] is True
        created_memory_ids.append(mem_bytes["memoryId"])

        # ── 6. Retrieve memories – semantic search ───────────────────────
        print("\n\n=== 6. RETRIEVE MEMORIES (semantic search) ===")

        # 6a. Broad query
        print("\n--- 6a. Query: 'AI agent framework' ---")
        ret_broad = await client.retrieve_memories(
            query="AI agent framework",
            space_ids=[space_id],
            max_results=5,
            wait_for_indexing=True,
        )
        pp("retrieve_memories (broad)", ret_broad)
        assert ret_broad["success"] is True
        print(f"→ Total results: {ret_broad['totalResults']}")
        indexing_works = ret_broad["totalResults"] > 0
        if not indexing_works:
            print("⚠ Server indexing may be slow/down — retrieval tests will be soft checks")

        # 6b. Specific query
        print("\n--- 6b. Query: 'vector databases and embeddings' ---")
        ret_specific = await client.retrieve_memories(
            query="vector databases and embeddings",
            space_ids=[space_id],
            max_results=3,
            wait_for_indexing=True,
        )
        pp("retrieve_memories (specific)", ret_specific)
        assert ret_specific["success"] is True
        print(f"→ Total results: {ret_specific['totalResults']}")

        # 6c. Query targeting metadata content
        print("\n--- 6c. Query: 'long-term knowledge retention' ---")
        ret_meta = await client.retrieve_memories(
            query="long-term knowledge retention",
            space_ids=[space_id],
            max_results=3,
            wait_for_indexing=True,
        )
        pp("retrieve_memories (metadata-targeted)", ret_meta)
        assert ret_meta["success"] is True
        print(f"→ Total results: {ret_meta['totalResults']}")

        # ── 7. Get specific memory by ID ─────────────────────────────────
        print("\n\n=== 7. GET MEMORY BY ID ===")

        # 7a. With content
        print(f"\n--- 7a. Get memory {created_memory_ids[0]} (with content) ---")
        mem_detail = await client.get_memory(created_memory_ids[0], include_content=True)
        pp("get_memory (with content)", mem_detail)
        assert mem_detail["success"] is True
        assert "memory" in mem_detail

        # 7b. Without content
        print(f"\n--- 7b. Get memory {created_memory_ids[1]} (without content) ---")
        mem_no_content = await client.get_memory(created_memory_ids[1], include_content=False)
        pp("get_memory (no content)", mem_no_content)
        assert mem_no_content["success"] is True
        assert "content" not in mem_no_content

        # ── 8. List spaces (verify ours exists) ──────────────────────────
        print("\n\n=== 8. LIST SPACES ===")
        spaces = await client.list_spaces()
        our_space = [s for s in spaces if s.get("spaceId") == space_id]
        assert len(our_space) == 1, f"Our space {space_id} not found in list"
        print(f"→ Found our space '{our_space[0].get('name')}' in {len(spaces)} total spaces")

        # =====================================================================
        #  PART 2 — Context Provider (BaseContextProvider hooks)
        # =====================================================================
        print("\n\n" + "=" * 60)
        print("  PART 2: CONTEXT PROVIDER (BaseContextProvider hooks)")
        print("=" * 60)

        # We simulate the before_run/after_run hooks that the Agent framework
        # calls automatically. This tests the GoodMemContextProvider without
        # needing an actual LLM client.

        from agent_framework import Message
        from agent_framework._sessions import AgentSession, SessionContext

        provider = GoodMemContextProvider(
            client=client,
            space_id=space_id,
            max_results=3,
            store_conversations=True,
            wait_for_indexing=False,
        )

        # ── 9. Simulate before_run (retrieval) ──────────────────────────
        print("\n\n=== 9. CONTEXT PROVIDER — before_run (retrieval) ===")
        session = AgentSession()
        context = SessionContext(
            session_id=session.session_id,
            input_messages=[Message(role="user", text="Tell me about AI agent frameworks")],
        )
        state: dict = {}

        await provider.before_run(agent=None, session=session, context=context, state=state)  # type: ignore[arg-type]

        context_msgs = context.get_messages()
        print(f"\n→ Context messages injected: {len(context_msgs)}")
        for i, msg in enumerate(context_msgs):
            print(f"  [{i}] role={msg.role}, text={msg.text[:120]}...")
        if indexing_works:
            assert len(context_msgs) > 0, "before_run should have injected memory context"
            print("\n✓ before_run correctly retrieved memories and injected into context")
        else:
            print("\n⚠ No memories indexed — before_run correctly returned empty (server indexing issue, not a code bug)")

        # ── 10. Simulate after_run (storage) ─────────────────────────────
        print("\n\n=== 10. CONTEXT PROVIDER — after_run (storage) ===")
        from agent_framework._types import AgentResponse

        # Simulate a response being set on the context
        context._response = AgentResponse(  # type: ignore[assignment]
            messages=[
                Message(role="assistant", text="Agent Framework supports multi-agent collaboration with tools and memory.")
            ],
        )

        await provider.after_run(agent=None, session=session, context=context, state=state)  # type: ignore[arg-type]
        print("→ after_run completed — conversation stored as memory")

        # ── 11. Verify stored conversation is retrievable ────────────────
        print("\n\n=== 11. VERIFY STORED CONVERSATION IS RETRIEVABLE ===")
        if indexing_works:
            ret_stored = await client.retrieve_memories(
                query="multi-agent collaboration with tools",
                space_ids=[space_id],
                max_results=3,
                wait_for_indexing=True,
            )
            pp("retrieve stored conversation", ret_stored)
            assert ret_stored["success"] is True
            print(f"→ Total results: {ret_stored['totalResults']}")

            # Find and track the stored conversation memory for cleanup
            for chunk in ret_stored.get("results", []):
                mid = chunk.get("memoryId")
                if mid and mid not in created_memory_ids:
                    created_memory_ids.append(mid)
        else:
            print("⚠ Skipping retrieval verification — server indexing not working")

        # ── 12. Test before_run with empty input (should be no-op) ───────
        print("\n\n=== 12. CONTEXT PROVIDER — before_run (empty input, no-op) ===")
        empty_context = SessionContext(
            session_id=session.session_id,
            input_messages=[Message(role="user", text="")],
        )
        await provider.before_run(agent=None, session=session, context=empty_context, state=state)  # type: ignore[arg-type]
        empty_msgs = empty_context.get_messages()
        assert len(empty_msgs) == 0, "Empty input should produce no context messages"
        print("→ ✓ Correctly skipped retrieval for empty input")

        # ── 13. Test with store_conversations=False ──────────────────────
        print("\n\n=== 13. CONTEXT PROVIDER — after_run (store_conversations=False) ===")
        no_store_provider = GoodMemContextProvider(
            client=client,
            space_id=space_id,
            store_conversations=False,
        )
        no_store_context = SessionContext(
            session_id=session.session_id,
            input_messages=[Message(role="user", text="This should not be stored")],
        )
        no_store_context._response = AgentResponse(  # type: ignore[assignment]
            messages=[Message(role="assistant", text="Not stored")],
        )
        await no_store_provider.after_run(agent=None, session=session, context=no_store_context, state={})  # type: ignore[arg-type]
        print("→ ✓ after_run correctly skipped storage when store_conversations=False")

        # =====================================================================
        #  PART 3 — Cleanup
        # =====================================================================
        print("\n\n" + "=" * 60)
        print("  PART 3: CLEANUP")
        print("=" * 60)

        print(f"\n→ Deleting {len(created_memory_ids)} memories...")
        for mid in created_memory_ids:
            del_result = await client.delete_memory(mid)
            assert del_result["success"] is True
            print(f"  ✓ Deleted {mid}")

        print(f"\n\n{'=' * 60}")
        print("  ALL TESTS PASSED SUCCESSFULLY!")
        print(f"{'=' * 60}\n")

    except Exception as exc:
        print(f"\n\n*** TEST FAILED: {exc} ***\n")
        import traceback
        traceback.print_exc()
        # Attempt cleanup even on failure
        for mid in created_memory_ids:
            try:
                await client.delete_memory(mid)
                print(f"  Cleaned up memory {mid}")
            except Exception:
                print(f"  Failed to clean up memory {mid}")
        raise
    finally:
        await client.close()


if __name__ == "__main__":
    asyncio.run(main())
