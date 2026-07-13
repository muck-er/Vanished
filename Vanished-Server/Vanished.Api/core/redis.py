import asyncio
import logging
from typing import Any

try:
    import redis.asyncio as redis
    from redis.exceptions import RedisError, TimeoutError as RedisTimeoutError
except ModuleNotFoundError:
    redis = None
    RedisError = Exception
    RedisTimeoutError = TimeoutError

from core.config import get_settings


logger = logging.getLogger("vanished.redis")


class RedisClient:
    _RATE_LIMIT_LUA = """
    local current = redis.call("INCR", KEYS[1])
    if current == 1 then
        redis.call("EXPIRE", KEYS[1], ARGV[1])
    end
    return current
    """

    def __init__(self):
        self._client = None
        self._memory: dict[str, Any] = {}
        self._expirations: dict[str, float] = {}

    async def initialize(self):
        url = get_settings().redis_url
        try:
            if redis is None:
                self._client = None
                logger.warning("redis package is not installed; using in-memory fallback")
                return

            self._client = redis.from_url(
                url,
                decode_responses=True,
                socket_connect_timeout=2,
                socket_timeout=2,
                health_check_interval=30,
                retry_on_timeout=True,
            )
            await asyncio.wait_for(self._client.ping(), timeout=2)
        except Exception as exc:
            logger.warning("Redis unavailable during startup; using in-memory fallback: %s", exc)
            self._client = None

    async def close(self):
        if self._client is not None:
            await self._client.aclose()

    def _expired(self, key: str) -> bool:
        loop = asyncio.get_running_loop()
        exp = self._expirations.get(key)
        if exp is not None and exp <= loop.time():
            self._memory.pop(key, None)
            self._expirations.pop(key, None)
            return True
        return False

    def _fallback_setex(self, key: str, seconds: int, value: str):
        self._memory[key] = value
        self._expirations[key] = asyncio.get_running_loop().time() + max(1, int(seconds or 1))
        return True

    def _fallback_set_once(self, key: str, seconds: int, value: str = "1") -> bool:
        if not self._expired(key) and key in self._memory:
            return False
        self._fallback_setex(key, seconds, value)
        return True

    def _fallback_incr_with_ttl(self, key: str, seconds: int) -> int:
        seconds = max(1, int(seconds or 1))
        if self._expired(key) or key not in self._memory:
            self._memory[key] = 0
            self._expirations[key] = asyncio.get_running_loop().time() + seconds
        self._memory[key] = int(self._memory.get(key, 0)) + 1
        return int(self._memory[key])

    def _disable_redis_after_failure(self, exc: Exception) -> None:
        logger.warning("Redis operation failed; degrading to in-memory fallback: %s", exc)
        self._client = None

    async def setex(self, key: str, seconds: int, value: str):
        if self._client is not None:
            try:
                return await asyncio.wait_for(self._client.setex(key, seconds, value), timeout=2)
            except (RedisError, RedisTimeoutError, asyncio.TimeoutError) as exc:
                self._disable_redis_after_failure(exc)
        return self._fallback_setex(key, seconds, value)

    async def set_once(self, key: str, seconds: int, value: str = "1") -> bool:
        if self._client is not None:
            try:
                result = await asyncio.wait_for(
                    self._client.set(key, value, ex=max(1, int(seconds or 1)), nx=True),
                    timeout=2,
                )
                return bool(result)
            except (RedisError, RedisTimeoutError, asyncio.TimeoutError) as exc:
                self._disable_redis_after_failure(exc)
        return self._fallback_set_once(key, seconds, value)

    async def incr_with_ttl(self, key: str, seconds: int) -> int:
        seconds = max(1, int(seconds or 1))
        if self._client is not None:
            try:
                result = await asyncio.wait_for(
                    self._client.eval(self._RATE_LIMIT_LUA, 1, key, seconds),
                    timeout=2,
                )
                return int(result)
            except (RedisError, RedisTimeoutError, asyncio.TimeoutError) as exc:
                self._disable_redis_after_failure(exc)
        return self._fallback_incr_with_ttl(key, seconds)

    async def exists(self, key: str) -> int:
        if self._client is not None:
            try:
                return int(await asyncio.wait_for(self._client.exists(key), timeout=2))
            except (RedisError, RedisTimeoutError, asyncio.TimeoutError) as exc:
                self._disable_redis_after_failure(exc)
        return 0 if self._expired(key) or key not in self._memory else 1

    async def get(self, key: str):
        if self._client is not None:
            try:
                return await asyncio.wait_for(self._client.get(key), timeout=2)
            except (RedisError, RedisTimeoutError, asyncio.TimeoutError) as exc:
                self._disable_redis_after_failure(exc)
        if self._expired(key):
            return None
        return self._memory.get(key)

    async def delete(self, key: str):
        if self._client is not None:
            try:
                return await asyncio.wait_for(self._client.delete(key), timeout=2)
            except (RedisError, RedisTimeoutError, asyncio.TimeoutError) as exc:
                self._disable_redis_after_failure(exc)
        self._memory.pop(key, None)
        self._expirations.pop(key, None)
        return 1

    async def lpush(self, key: str, value: str):
        if self._client is not None:
            try:
                return await asyncio.wait_for(self._client.lpush(key, value), timeout=2)
            except (RedisError, RedisTimeoutError, asyncio.TimeoutError) as exc:
                self._disable_redis_after_failure(exc)
        self._memory.setdefault(key, [])
        self._memory[key].insert(0, value)
        return len(self._memory[key])

    async def lrange(self, key: str, start: int, end: int):
        if self._client is not None:
            try:
                return await asyncio.wait_for(self._client.lrange(key, start, end), timeout=2)
            except (RedisError, RedisTimeoutError, asyncio.TimeoutError) as exc:
                self._disable_redis_after_failure(exc)
        items = list(self._memory.get(key, []))
        if end == -1:
            return items[start:]
        return items[start:end + 1]


redis_client = RedisClient()
