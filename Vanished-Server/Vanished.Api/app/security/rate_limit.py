from __future__ import annotations

import hashlib
from typing import Iterable

from fastapi import HTTPException, Request, WebSocket

from app.security.validators import clean_text, normalize_email
from core.redis import redis_client


GENERIC_RATE_LIMIT_MESSAGE = "Muitas tentativas. Tenta novamente dentro de instantes."


def _client_ip(request: Request) -> str:
    forwarded = (request.headers.get("cf-connecting-ip") or request.headers.get("x-forwarded-for") or "").split(",", 1)[0].strip()
    return forwarded or (request.client.host if request.client else "unknown")


def _bucket(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest()[:32]


def _body_value(body: dict, name: str) -> str:
    candidates = {
        name,
        name.lower(),
        name.upper(),
        "".join(part.title() for part in name.split("_")),
    }
    lowered = {str(k).lower(): v for k, v in body.items()} if isinstance(body, dict) else {}
    for candidate in candidates:
        if isinstance(body, dict) and candidate in body:
            return str(body.get(candidate) or "")
        if candidate.lower() in lowered:
            return str(lowered.get(candidate.lower()) or "")
    return ""


async def _json_body(request: Request) -> dict:
    try:
        body = await request.json()
        return body if isinstance(body, dict) else {}
    except Exception:
        return {}


async def enforce_rate_limit(scope: str, identifier: str, limit: int, window_seconds: int) -> None:
    key = f"rl:{scope}:{_bucket(identifier)}"
    count = await redis_client.incr_with_ttl(key, window_seconds)
    if count > limit:
        raise HTTPException(status_code=429, detail=GENERIC_RATE_LIMIT_MESSAGE)


def rate_limit(scope: str, limit: int, window_seconds: int, body_fields: Iterable[str] = ()):
    fields = tuple(body_fields)

    async def dependency(request: Request) -> None:
        body = await _json_body(request) if fields else {}
        identifiers = [f"ip:{_client_ip(request)}"]

        for field in fields:
            raw = _body_value(body, field)
            if not raw:
                continue
            normalized = normalize_email(raw) if field.lower() == "email" else clean_text(raw, 200).lower()
            if normalized:
                identifiers.append(f"{field}:{normalized}")

        for identifier in identifiers:
            await enforce_rate_limit(scope, identifier, limit, window_seconds)

    return dependency


async def enforce_websocket_rate_limit(websocket: WebSocket, user_id: str) -> None:
    client_host = websocket.client.host if websocket.client else "unknown"
    await enforce_rate_limit("ws:connect:ip", f"ip:{client_host}", 30, 60)
    await enforce_rate_limit("ws:connect:user", f"user:{user_id}", 20, 60)
