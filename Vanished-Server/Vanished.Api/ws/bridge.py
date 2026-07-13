import asyncio
from concurrent.futures import TimeoutError
from typing import Any

_loop: asyncio.AbstractEventLoop | None = None
_manager: Any = None


def configure(manager: Any):
    global _loop, _manager
    _loop = asyncio.get_running_loop()
    _manager = manager


def _run_bool(coro, timeout: float = 0.75) -> bool:
    if _loop is None:
        return False
    try:
        future = asyncio.run_coroutine_threadsafe(coro, _loop)
        return bool(future.result(timeout=timeout))
    except (TimeoutError, Exception):
        return False


def emit_to_user(user_id: int | str, event: dict) -> bool:
    if _loop is None or _manager is None:
        return False
    return _run_bool(_manager.send_to_user(str(user_id), event))


def queue_to_user(user_id: int | str, event: dict) -> bool:
    if _loop is None or _manager is None:
        return False
    return _run_bool(_manager.queue_for_user(str(user_id), event))
