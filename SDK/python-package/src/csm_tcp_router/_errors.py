"""Shared server-error parsing helper.

Internal module – nothing is re-exported from the package.
"""

from __future__ import annotations

from .exceptions import ServerError
from .models import Packet

__all__: list = []  # internal; nothing re-exported


def _parse_server_error(packet: Packet) -> ServerError:
    """Extract code and message from a CSM Error format ``[Error: <code>] <msg>``."""
    text = packet.data.decode("utf-8", errors="replace").strip()
    code = ""
    msg = text
    if text.startswith("[Error:"):
        try:
            end_idx = text.index("]")
            code = text[7:end_idx].strip()
            msg = text[end_idx + 1:].strip()
        except ValueError:
            pass
    return ServerError(msg, code)
