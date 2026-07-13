from __future__ import annotations

import datetime as dt
import hashlib
import os
import secrets
import time
import uuid
from dataclasses import dataclass
from typing import Annotated

import jwt
from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
from fastapi import Depends, Header, HTTPException, Request
from sqlalchemy.orm import Session

from app.models import Device, RefreshSession, User, utcnow
from app.security.validators import clean_text, decode_b64_bytes
from core.config import get_settings
from core.redis import redis_client
from core.database import get_db


@dataclass
class CurrentAuth:
    user: User
    device: Device
    payload: dict


def _is_weak_secret(value: str) -> bool:
    lowered = (value or "").strip().lower()
    return (
        len(lowered) < 32
        or lowered.startswith("change-")
        or "change-this" in lowered
        or "dev-only" in lowered
        or lowered in {"dev-pepper", "dev-pin-pepper"}
    )


def jwt_secret() -> str:
    secret = (get_settings().jwt_secret_key or "").strip()
    if _is_weak_secret(secret):
        raise RuntimeError("JWT_SECRET_KEY tem de ser um segredo forte e exclusivo em produção.")
    return secret


def token_pepper() -> str:
    pepper = (os.getenv("TOKEN_PEPPER") or "").strip()
    if _is_weak_secret(pepper):
        raise RuntimeError("TOKEN_PEPPER tem de ser um segredo forte e exclusivo em produção.")
    return pepper


def hash_token(token: str) -> str:
    return hashlib.sha256((token_pepper() + ":" + token).encode()).hexdigest()


def create_access_token(user_id: int, email: str, device_id: str) -> tuple[str, str, dt.datetime]:
    settings = get_settings()
    now = utcnow()
    exp = now + dt.timedelta(minutes=int(settings.access_token_exp_minutes))
    jti = uuid.uuid4().hex
    token = jwt.encode({
        "sub": str(user_id),
        "email": email,
        "device_id": device_id,
        "jti": jti,
        "iat": int(now.timestamp()),
        "exp": int(exp.timestamp()),
    }, jwt_secret(), algorithm="HS256")
    return token, jti, exp


def create_refresh_session(db: Session, user_id: int, device_id: str) -> tuple[str, RefreshSession]:
    settings = get_settings()
    token = secrets.token_urlsafe(64)
    family = uuid.uuid4().hex
    exp = utcnow() + dt.timedelta(days=int(settings.refresh_token_exp_days))
    session = RefreshSession(
        user_id=user_id,
        device_id=device_id,
        family_id=family,
        token_hash=hash_token(token),
        expires_at=exp,
    )
    db.add(session)
    return token, session


def rotate_refresh_session(db: Session, session: RefreshSession) -> tuple[str, RefreshSession]:
    settings = get_settings()
    token = secrets.token_urlsafe(64)
    exp = utcnow() + dt.timedelta(days=int(settings.refresh_token_exp_days))
    new_session = RefreshSession(
        user_id=session.user_id,
        device_id=session.device_id,
        family_id=session.family_id,
        token_hash=hash_token(token),
        expires_at=exp,
    )
    db.add(new_session)
    session.replaced_by_hash = new_session.token_hash
    session.revoked_at = utcnow()
    return token, new_session


def _canonical(method: str, path: str, body_hash: str, timestamp: str, nonce: str, device_id: str) -> bytes:
    return "\n".join(["v2", method.upper(), path, body_hash, timestamp, nonce, device_id]).encode()


async def _mark_request_signature_seen(user_id: int, device_id: str, timestamp: str, nonce: str, signature_b64: str, ttl_seconds: int) -> bool:
    replay_key_material = f"{user_id}:{device_id}:{timestamp}:{nonce}:{signature_b64}".encode("utf-8")
    replay_key = "request_sig:" + hashlib.sha256(replay_key_material).hexdigest()
    return await redis_client.set_once(replay_key, max(1, ttl_seconds), "1")


async def verify_device_signature(
    request: Request,
    db: Session,
    user: User,
    require: bool = True,
) -> bool:
    device_id = (request.headers.get("X-Vanished-Device-Id") or "").strip()
    timestamp = (request.headers.get("X-Vanished-Timestamp") or "").strip()
    body_hash = (request.headers.get("X-Vanished-Body-SHA256") or "").strip().lower()
    nonce = clean_text(request.headers.get("X-Vanished-Nonce"), 128)
    signature_b64 = (request.headers.get("X-Vanished-Signature") or "").strip()

    if not all([device_id, timestamp, body_hash, nonce, signature_b64]):
        return not require

    try:
        skew = abs(time.time() - int(timestamp))
        max_skew = int(get_settings().request_signature_max_skew_seconds)
        if skew > max_skew:
            return False
    except Exception:
        return False

    if len(body_hash) != 64 or any(ch not in "0123456789abcdef" for ch in body_hash):
        return False

    body = await request.body()
    actual_hash = hashlib.sha256(body or b"").hexdigest()
    if not secrets.compare_digest(actual_hash, body_hash):
        return False

    device = db.query(Device).filter_by(user_id=user.id, device_id=device_id, revoked_at=None).first()
    if not device:
        return False

    path = request.url.path
    if request.url.query:
        path += "?" + request.url.query

    try:
        if not await _mark_request_signature_seen(user.id, device_id, timestamp, nonce, signature_b64, max_skew):
            return False

        pub = Ed25519PublicKey.from_public_bytes(decode_b64_bytes(device.public_key))
        signature = decode_b64_bytes(signature_b64, 4096)
        pub.verify(signature, _canonical(request.method, path, body_hash, timestamp, nonce, device_id))
        device.last_seen_at = utcnow()
        db.commit()
        return True
    except (InvalidSignature, ValueError, Exception):
        return False


async def require_auth(
    request: Request,
    authorization: Annotated[str | None, Header()] = None,
    db: Session = Depends(get_db),
) -> CurrentAuth:
    if not authorization or not authorization.startswith("Bearer "):
        raise HTTPException(status_code=401, detail="Token em falta.")
    token = authorization.split(" ", 1)[1].strip()
    try:
        payload = jwt.decode(token, jwt_secret(), algorithms=["HS256"])
        user = db.get(User, int(payload["sub"]))
        if not user:
            raise HTTPException(status_code=401, detail="Utilizador inválido.")
        device = db.query(Device).filter_by(user_id=user.id, device_id=payload.get("device_id"), revoked_at=None).first()
        if not device:
            raise HTTPException(status_code=401, detail="Dispositivo revogado ou inválido.")
        user.last_seen_at = utcnow()
        db.commit()
        return CurrentAuth(user=user, device=device, payload=payload)
    except jwt.ExpiredSignatureError:
        raise HTTPException(status_code=401, detail="Sessão expirada.")
    except HTTPException:
        raise
    except Exception:
        raise HTTPException(status_code=401, detail="Token inválido.")


async def require_signed_auth(
    request: Request,
    auth: CurrentAuth = Depends(require_auth),
    db: Session = Depends(get_db),
) -> CurrentAuth:
    if not await verify_device_signature(request, db, auth.user, require=True):
        raise HTTPException(status_code=401, detail="Assinatura do dispositivo inválida.")
    device = db.query(Device).filter_by(user_id=auth.user.id, device_id=auth.device.device_id, revoked_at=None).first()
    return CurrentAuth(user=auth.user, device=device or auth.device, payload=auth.payload)
