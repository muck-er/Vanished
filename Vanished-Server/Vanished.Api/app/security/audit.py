from __future__ import annotations

import hashlib
import json
import logging
from typing import Any

from fastapi import Request
from sqlalchemy.orm import Session

from app.models import SecurityAuditEvent, utcnow


logger = logging.getLogger("vanished.security.audit")


def hash_identifier(value: str | None) -> str:
    if not value:
        return ""
    return hashlib.sha256(value.strip().lower().encode("utf-8")).hexdigest()


def client_ip(request: Request | None) -> str:
    if request is None:
        return ""
    forwarded = (request.headers.get("cf-connecting-ip") or request.headers.get("x-forwarded-for") or "").split(",", 1)[0].strip()
    return forwarded or (request.client.host if request.client else "")


def user_agent(request: Request | None) -> str:
    if request is None:
        return ""
    return (request.headers.get("user-agent") or "")[:300]


def record_security_event(
    db: Session,
    *,
    event_type: str,
    outcome: str,
    user_id: int | None = None,
    device_id: str | None = None,
    request: Request | None = None,
    subject_hash: str | None = None,
    metadata: dict[str, Any] | None = None,
) -> None:

    try:
        safe_metadata = metadata or {}
        safe_metadata = json.loads(json.dumps(safe_metadata, default=str)) if safe_metadata else {}
        db.add(
            SecurityAuditEvent(
                event_type=event_type[:80],
                outcome=outcome[:20],
                user_id=user_id,
                device_id=(device_id or "")[:64] or None,
                subject_hash=(subject_hash or "")[:64] or None,
                ip_hash=hash_identifier(client_ip(request)) or None,
                user_agent_hash=hash_identifier(user_agent(request)) or None,
                event_metadata=safe_metadata,
                created_at=utcnow(),
            )
        )
    except Exception as exc:
        logger.warning(
            "security_audit_event_persist_failed",
            extra={
                "event_type": event_type[:80],
                "outcome": outcome[:20],
                "user_id": user_id,
                "error_type": type(exc).__name__,
            },
        )
