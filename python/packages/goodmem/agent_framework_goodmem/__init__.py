# Copyright (c) Microsoft. All rights reserved.

"""GoodMem integration for Microsoft Agent Framework.

This package exposes GoodMem operations as Agent Framework tools that can be
used with any Agent Framework agent.
"""

from __future__ import annotations

import importlib.metadata
from typing import TYPE_CHECKING

from ._client import GoodMemClient

if TYPE_CHECKING:
    from ._tools import create_goodmem_tools as create_goodmem_tools
else:

    def __getattr__(name: str):  # noqa: ANN001, ANN202
        if name == "create_goodmem_tools":
            from ._tools import create_goodmem_tools

            return create_goodmem_tools
        raise AttributeError(f"module {__name__!r} has no attribute {name!r}")


try:
    __version__ = importlib.metadata.version(__name__)
except importlib.metadata.PackageNotFoundError:
    __version__ = "0.0.0"  # Fallback for development mode

__all__ = [
    "GoodMemClient",
    "create_goodmem_tools",
    "__version__",
]
