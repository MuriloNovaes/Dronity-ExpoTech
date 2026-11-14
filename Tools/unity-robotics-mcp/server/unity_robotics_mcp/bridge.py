"""Cliente TCP da ponte com o Unity Editor.

Protocolo: uma requisição JSON por linha ({"id", "command", "params"}) e uma
resposta JSON por linha ({"id", "ok", "result" | "error"}). O Unity recarrega o
domínio a cada compilação, derrubando o socket por alguns segundos; por isso
cada chamada abre uma conexão nova e tenta de novo enquanto o editor volta.
"""

from __future__ import annotations

import json
import os
import socket
import time
import uuid
from typing import Any

try:  # SDK MCP 1.x
    from mcp.server.fastmcp.exceptions import ToolError
except ImportError:  # SDK MCP 2.x
    from mcp.server.mcpserver.exceptions import ToolError

DEFAULT_HOST = os.environ.get("UNITY_MCP_HOST", "127.0.0.1")
DEFAULT_PORT = int(os.environ.get("UNITY_MCP_PORT", "8765"))


class UnityBridgeError(ToolError):
    """Erro esperado: a mensagem chega ao modelo em vez de um genérico 'Error executing tool'."""


class UnityBridge:
    def __init__(self, host: str = DEFAULT_HOST, port: int = DEFAULT_PORT):
        self.host = host
        self.port = port

    def call(
        self,
        command: str,
        params: dict[str, Any] | None = None,
        timeout: float = 60.0,
        retry_for: float = 20.0,
    ) -> Any:
        request = {"id": uuid.uuid4().hex, "command": command, "params": params or {}}
        payload = (json.dumps(request) + "\n").encode("utf-8")
        deadline = time.monotonic() + retry_for
        last_error: Exception | None = None

        while True:
            try:
                with socket.create_connection((self.host, self.port), timeout=5) as sock:
                    sock.settimeout(timeout)
                    sock.sendall(payload)
                    raw = self._read_line(sock)
                break
            except (ConnectionRefusedError, ConnectionResetError, socket.timeout, OSError) as exc:
                last_error = exc
                if time.monotonic() >= deadline:
                    raise UnityBridgeError(
                        f"Não foi possível falar com o Unity em {self.host}:{self.port} ({exc}). "
                        "Confira se o projeto está aberto, o pacote 'Robotics MCP Bridge' está "
                        "instalado e a janela Tools > Robotics MCP mostra 'Listening'."
                    ) from last_error
                time.sleep(1.0)

        response = json.loads(raw)
        if not response.get("ok"):
            raise UnityBridgeError(response.get("error", "erro desconhecido no Unity"))
        return response.get("result")

    @staticmethod
    def _read_line(sock: socket.socket) -> str:
        chunks: list[bytes] = []
        while True:
            chunk = sock.recv(65536)
            if not chunk:
                break
            chunks.append(chunk)
            if chunk.endswith(b"\n"):
                break
        data = b"".join(chunks).decode("utf-8").strip()
        if not data:
            raise ConnectionResetError("conexão fechada sem resposta (domain reload?)")
        return data
