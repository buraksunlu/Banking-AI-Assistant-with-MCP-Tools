"""What the chat needs from the place its tools run, and the errors that place can raise (AI-14a, MCP-b).

The tools themselves (names, descriptions, argument schemas, computation) live on the .NET MCP server and are discovered
from it (MCP-a K3-A); mcp_gateway.McpToolGateway is the implementation. The model never names a customer: the server
takes it from the user's token.
"""

from typing import Protocol


class BackendUnavailable(Exception):
    """The MCP server cannot be reached or failed on its side."""


class SessionInvalid(Exception):
    """The MCP server refused the user's token (expired, or the customer no longer exists)."""


class ToolCallRejected(Exception):
    """The MCP server refused the call itself (for example a tool it does not publish)."""


class ToolArgumentsRejected(Exception):
    """The MCP server refused the arguments the model produced (MCP-b): the chat asks the user back."""


class BackendTimeout(Exception):
    """The MCP server did not answer within the time left for the chat."""


class ToolGateway(Protocol):
    """Lists and runs tools for the user whose token is given."""

    def list_tools(self, token: str, deadline: float) -> list[dict]: ...

    def call(self, tool: str, arguments: dict, token: str, deadline: float) -> dict: ...
