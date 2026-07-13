from __future__ import annotations

import json
import os
from datetime import datetime, timezone

from argon2.low_level import Type, hash_secret_raw
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from fastapi import APIRouter, Depends, File, Form, HTTPException, UploadFile
from fastapi.responses import Response
from sqlalchemy.orm import Session

from app.models import EncryptedExport, MessageThread, PrivateMessage, User
from app.security.rate_limit import rate_limit
from core.auth import CurrentAuth, require_signed_auth
from core.database import get_db
from core.http import ok
from services.common import message_envelope_for_user, thread_summary

router = APIRouter(prefix="/api/export", tags=["export"])
MAGIC = b"VNSHD002"
ARGON2_TIME_COST = 3
ARGON2_MEMORY_COST_KB = 65_536
ARGON2_PARALLELISM = 2
AAD = b"Vanished export VNSHD002 AES-256-GCM Argon2id"


def _derive_key(password: str, salt: bytes) -> bytes:
    return hash_secret_raw(
        secret=("vanished:server-export:v2:" + (password or "")).encode("utf-8"),
        salt=salt,
        time_cost=ARGON2_TIME_COST,
        memory_cost=ARGON2_MEMORY_COST_KB,
        parallelism=ARGON2_PARALLELISM,
        hash_len=32,
        type=Type.ID,
    )


def _encrypt_json(payload: dict, password: str) -> bytes:
    raw = json.dumps(payload, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    salt = os.urandom(16)
    nonce = os.urandom(12)
    key = _derive_key(password, salt)
    ciphertext = AESGCM(key).encrypt(nonce, raw, AAD)
    return MAGIC + salt + nonce + ciphertext


def _decrypt_payload(raw_file: bytes, password: str) -> dict:
    if len(raw_file) >= 52 and raw_file[:8] == MAGIC:
        salt = raw_file[8:24]
        nonce = raw_file[24:36]
        ciphertext = raw_file[36:]
        key = _derive_key(password, salt)
        data = AESGCM(key).decrypt(nonce, ciphertext, AAD)
        return json.loads(data.decode("utf-8"))

    raise ValueError("bad_magic")


def _build_export_data(db: Session, user: User, device_id: str) -> dict:
    threads = db.query(MessageThread).filter(
        (MessageThread.user_low_id == user.id) | (MessageThread.user_high_id == user.id)
    ).order_by(MessageThread.updated_at.desc()).limit(500).all()

    conversations = []
    for thread in threads:
        summary = thread_summary(db, thread, user.id, device_id)
        if summary:
            conversations.append(summary)

    messages = db.query(PrivateMessage).filter(
        (PrivateMessage.sender_id == user.id) | (PrivateMessage.recipient_id == user.id)
    ).order_by(PrivateMessage.id.asc()).limit(5000).all()

    encrypted_exports = db.query(EncryptedExport).filter_by(user_id=user.id).order_by(EncryptedExport.created_at.desc()).limit(100).all()

    return {
        "version": "2.0",
        "exported_at": datetime.now(timezone.utc).isoformat(),
        "format": "VNSHD002/AES-256-GCM/Argon2id",
        "kdf": {
            "name": "argon2id",
            "time_cost": ARGON2_TIME_COST,
            "memory_cost_kb": ARGON2_MEMORY_COST_KB,
            "parallelism": ARGON2_PARALLELISM,
            "salt_size": 16,
            "key_size": 32,
        },
        "user": {
            "user_id": str(user.id),
            "username": user.username,
            "display_name": user.full_name,
            "bio": user.bio or "",
            "created_at": user.created_at.isoformat() if user.created_at else "",
        },
        "conversations": conversations,
        "messages": [message_envelope_for_user(m, user.id) for m in messages],
        "encrypted_exports": [
            {
                "id": e.id,
                "device_id": e.device_id,
                "ciphertext_b64": e.ciphertext_b64,
                "nonce_b64": e.nonce_b64,
                "manifest": e.manifest or {},
                "created_at": e.created_at.isoformat() if e.created_at else "",
            }
            for e in encrypted_exports
        ],
    }


@router.post("/create", dependencies=[Depends(rate_limit("export:create", 10, 3600))])
def create_export(data: dict, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    password = str(data.get("password") or "")
    mfa_code = str(data.get("mfa_code") or "")
    if len(password) < 8:
        raise HTTPException(status_code=403, detail="Não foi possível verificar a tua identidade.")
    if len(mfa_code.strip()) != 6 or not mfa_code.strip().isdigit():
        raise HTTPException(status_code=403, detail="Não foi possível verificar a tua identidade.")

    payload = _encrypt_json(_build_export_data(db, auth.user, auth.device.device_id), password)
    filename = f"vanished_export_{auth.user.username}_{datetime.now(timezone.utc).strftime('%Y%m%d')}.vne"
    return Response(
        content=payload,
        media_type="application/octet-stream",
        headers={"Content-Disposition": f'attachment; filename="{filename}"'},
    )


@router.post("/import", dependencies=[Depends(rate_limit("export:import", 10, 3600))])
async def import_export(file: UploadFile = File(...), password: str = Form(...), mfa_code: str = Form(...), db: Session = Depends(get_db)):
    raw_file = await file.read()
    try:
        export_data = _decrypt_payload(raw_file, password)
    except Exception:
        raise HTTPException(status_code=400, detail="Não foi possível verificar a tua identidade.")

    user_info = export_data.get("user") or {}
    try:
        user_id = int(user_info.get("user_id") or 0)
    except Exception:
        user_id = 0
    user = db.get(User, user_id)
    if not user:
        raise HTTPException(status_code=404, detail="Não foi possível verificar a tua identidade.")
    if len((mfa_code or "").strip()) != 6 or not (mfa_code or "").strip().isdigit():
        raise HTTPException(status_code=403, detail="Não foi possível verificar a tua identidade.")

    return ok({
        "user": {
            "user_id": user.id,
            "username": user.username,
            "display_name": user.full_name,
            "bio": user.bio or "",
            "created_at": user.created_at.isoformat() if user.created_at else "",
        }
    }, "Exportação validada. Para iniciar sessão neste dispositivo, conclui a recuperação/adicionar dispositivo.")
