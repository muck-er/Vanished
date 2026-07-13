import asyncio
import hashlib
import json
import logging
import time
from collections import defaultdict

from fastapi import WebSocket

from core.redis import redis_client
from ws.handlers import handle_client_event


ONLINE_TTL_SECONDS = 12
STALE_SOCKET_SECONDS = 24
WATCHDOG_INTERVAL_SECONDS = 5
logger = logging.getLogger("vanished.ws")


def _safe_user_ref(user_id: str | int) -> str:
    return hashlib.sha256(str(user_id).encode("utf-8")).hexdigest()[:16]


class ConnectionManager:
    def __init__(self):
        self._connections: dict[str, set[WebSocket]] = defaultdict(set)
        self._socket_users: dict[WebSocket, str] = {}
        self._last_seen: dict[WebSocket, float] = {}
        self._watchdog_tasks: dict[WebSocket, asyncio.Task] = {}

    async def connect(self, user_id: str, websocket: WebSocket):
        user_key = str(user_id)
        await websocket.accept()
        self._connections[user_key].add(websocket)
        self._socket_users[websocket] = user_key
        self._last_seen[websocket] = time.monotonic()
        self._watchdog_tasks[websocket] = asyncio.create_task(self._watchdog(user_key, websocket))

        await redis_client.setex(f"online:{user_key}", ONLINE_TTL_SECONDS, "1")
        await self._send_online_snapshot(user_key)
        await self._broadcast_status(user_key, True)
        await self._deliver_pending(user_key)

    async def disconnect(self, user_id: str, websocket: WebSocket):
        await self._remove_socket(str(user_id), websocket, broadcast_if_offline=True)

    async def force_disconnect(self, user_id: str | int, code: int = 1000, reason: str = ""):
        key = str(user_id)
        sockets = list(self._connections.get(key, set()))
        for ws in sockets:
            try:
                await ws.close(code=code, reason=reason)
            except Exception as exc:
                logger.debug(
                    "websocket_close_failed",
                    extra={"user_ref": _safe_user_ref(key), "code": code, "error_type": type(exc).__name__},
                )
            finally:
                await self._remove_socket(key, ws, broadcast_if_offline=False)

        await redis_client.delete(f"online:{key}")
        await self._broadcast_status(key, False)

    async def listen(self, user_id: str, websocket: WebSocket):
        async for raw in websocket.iter_text():
            self._touch_socket(str(user_id), websocket)
            try:
                event = json.loads(raw)
                if isinstance(event, dict):
                    await handle_client_event(self, user_id, event)
            except json.JSONDecodeError:
                logger.warning(
                    "websocket_malformed_json_dropped",
                    extra={"user_ref": _safe_user_ref(user_id)},
                )
            except Exception as exc:
                logger.warning(
                    "websocket_client_event_failed",
                    extra={"user_ref": _safe_user_ref(user_id), "error_type": type(exc).__name__},
                )

    async def send_to_user(self, user_id: str, event: dict):
        user_key = str(user_id)
        sockets = set(self._connections.get(user_key, set()))
        if not sockets:
            return False

        delivered = False
        for ws in sockets:
            try:
                await ws.send_json(event)
                delivered = True
            except Exception as exc:
                logger.debug(
                    "websocket_send_failed",
                    extra={"user_ref": _safe_user_ref(user_key), "error_type": type(exc).__name__},
                )
                await self._remove_socket(user_key, ws, broadcast_if_offline=True)

        return delivered

    async def queue_for_user(self, user_id: str, event: dict):
        await redis_client.lpush(f"pending:{user_id}", json.dumps(event))

    async def is_online(self, user_id: str) -> bool:
        await self._prune_stale_user(str(user_id))
        return str(user_id) in self._connections or bool(await redis_client.exists(f"online:{user_id}"))

    async def _deliver_pending(self, user_id: str):
        key = f"pending:{user_id}"
        pending = await redis_client.lrange(key, 0, -1)
        if not pending:
            return
        await redis_client.delete(key)
        for raw in reversed(pending):
            try:
                event = json.loads(raw)
                if isinstance(event, dict):
                    await self.send_to_user(user_id, event)
            except json.JSONDecodeError:
                logger.warning(
                    "websocket_pending_event_malformed_dropped",
                    extra={"user_ref": _safe_user_ref(user_id)},
                )
            except Exception as exc:
                logger.warning(
                    "websocket_pending_event_delivery_failed",
                    extra={"user_ref": _safe_user_ref(user_id), "error_type": type(exc).__name__},
                )

    async def _send_online_snapshot(self, user_id: str):
        await self._prune_all_stale()
        for peer_id, sockets in list(self._connections.items()):
            if peer_id == str(user_id) or not sockets:
                continue
            try:
                await self.send_to_user(user_id, {
                    "type": "user.status",
                    "user_id": str(peer_id),
                    "peer_id": int(peer_id),
                    "online": True,
                })
            except (TypeError, ValueError):
                logger.debug(
                    "websocket_online_snapshot_invalid_peer",
                    extra={"user_ref": _safe_user_ref(user_id), "peer_ref": _safe_user_ref(peer_id)},
                )

    async def _broadcast_status(self, user_id: str, online: bool):
        try:
            peer_id = int(user_id)
        except (TypeError, ValueError):
            return

        event = {"type": "user.status", "user_id": str(user_id), "peer_id": peer_id, "online": bool(online)}
        for target_user_id in list(self._connections.keys()):
            if target_user_id != str(user_id):
                await self.send_to_user(target_user_id, event)

    def _touch_socket(self, user_id: str, websocket: WebSocket):
        self._socket_users[websocket] = str(user_id)
        self._last_seen[websocket] = time.monotonic()

    async def _remove_socket(self, user_id: str, websocket: WebSocket, *, broadcast_if_offline: bool):
        user_key = str(user_id)
        sockets = self._connections.get(user_key)
        was_online = bool(sockets)

        if sockets:
            sockets.discard(websocket)
            if not sockets:
                self._connections.pop(user_key, None)

        self._socket_users.pop(websocket, None)
        self._last_seen.pop(websocket, None)
        task = self._watchdog_tasks.pop(websocket, None)
        current_task = asyncio.current_task()
        if task is not None and task is not current_task:
            task.cancel()

        now_offline = user_key not in self._connections
        if was_online and now_offline:
            await redis_client.delete(f"online:{user_key}")
            if broadcast_if_offline:
                await self._broadcast_status(user_key, False)

    async def _watchdog(self, user_id: str, websocket: WebSocket):
        try:
            while True:
                await asyncio.sleep(WATCHDOG_INTERVAL_SECONDS)
                last_seen = self._last_seen.get(websocket)
                if last_seen is None:
                    return

                if time.monotonic() - last_seen <= STALE_SOCKET_SECONDS:
                    continue

                logger.debug(
                    "websocket_stale_socket_pruned",
                    extra={"user_ref": _safe_user_ref(user_id)},
                )
                try:
                    await websocket.close(code=1001, reason="presence timeout")
                except Exception:
                    pass
                await self._remove_socket(user_id, websocket, broadcast_if_offline=True)
                return
        except asyncio.CancelledError:
            return

    async def _prune_stale_user(self, user_id: str):
        for ws in list(self._connections.get(str(user_id), set())):
            last_seen = self._last_seen.get(ws, 0)
            if time.monotonic() - last_seen > STALE_SOCKET_SECONDS:
                await self._remove_socket(str(user_id), ws, broadcast_if_offline=True)

    async def _prune_all_stale(self):
        for user_id in list(self._connections.keys()):
            await self._prune_stale_user(user_id)


connection_manager = ConnectionManager()
