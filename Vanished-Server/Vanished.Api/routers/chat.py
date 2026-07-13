from __future__ import annotations

import base64

from fastapi import APIRouter, Depends, Query, Request
from sqlalchemy import and_, desc, or_
from sqlalchemy.orm import Session

from app.models import ContactIdentityVerification, Device, MessageThread, User, UserBlock, utcnow
from app.realtime import publish_user_event
from app.security.validators import clean_text
from app.security.audit import record_security_event
from app.security.fingerprint import public_key_fingerprint, safety_number
from app.security.rate_limit import rate_limit
from core.auth import CurrentAuth, require_auth, require_signed_auth
from core.database import get_db
from core.http import fail, ok
from services.common import pair, public_user, thread_for, thread_summary

router = APIRouter(prefix="/api/chat", tags=["chat"])


def _is_blocked_by_me(db: Session, uid: int, peer_id: int) -> bool:
    return db.query(UserBlock.id).filter_by(blocker_id=uid, blocked_id=peer_id).first() is not None


def _is_blocked_between(db: Session, a: int, b: int) -> bool:
    return db.query(UserBlock.id).filter(
        or_(
            and_(UserBlock.blocker_id == a, UserBlock.blocked_id == b),
            and_(UserBlock.blocker_id == b, UserBlock.blocked_id == a),
        )
    ).first() is not None


def _peer_id_from_data(data: dict) -> int:
    try:
        return int(data.get("peer_id") or data.get("user_id") or data.get("blocked_id") or 0)
    except Exception:
        return 0


@router.get("/me")
def me(auth: CurrentAuth = Depends(require_auth), db: Session = Depends(get_db)):
    return ok({"user": public_user(auth.user, db, auth.user.id)})


def _escape_like(value: str) -> str:
    return value.replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_")


@router.get("/users/search")
def search_users(q: str = Query(""), auth: CurrentAuth = Depends(require_auth), db: Session = Depends(get_db)):
    query_text = clean_text(q, 100).lower().lstrip("@").strip()
    if len(query_text) < 2:
        return ok({"users": []})

    like = f"%{_escape_like(query_text)}%"
    users = (
        db.query(User)
        .filter(User.id != auth.user.id)
        .filter(or_(User.username.ilike(like, escape="\\"), User.full_name.ilike(like, escape="\\")))
        .order_by(User.username.asc())
        .limit(20)
        .all()
    )
    return ok({"users": [public_user(u, db, auth.user.id) for u in users]})


@router.get("/users/{user_id}")
def get_user(user_id: int, auth: CurrentAuth = Depends(require_auth), db: Session = Depends(get_db)):
    user = db.get(User, user_id)
    if not user:
        return fail("Utilizador não encontrado.", 404)
    return ok({"user": public_user(user, db, auth.user.id)})




@router.get("/users/{user_id}/identity-fingerprint")
def get_identity_fingerprint(user_id: int, auth: CurrentAuth = Depends(require_auth), db: Session = Depends(get_db)):
    peer = db.get(User, user_id)
    if not peer or peer.id == auth.user.id:
        return fail("Utilizador não encontrado.", 404)
    verification = db.query(ContactIdentityVerification).filter_by(user_id=auth.user.id, peer_id=peer.id, revoked_at=None).first()
    fingerprint = public_key_fingerprint(peer.identity_public_key)
    return ok({
        "user_id": peer.id,
        "username": peer.username,
        "key_version": peer.key_version,
        "identity_public_key": peer.identity_public_key,
        "identity_fingerprint": fingerprint,
        "safety_number": safety_number(auth.user.id, auth.user.identity_public_key, peer.id, peer.identity_public_key),
        "verified": bool(verification and verification.peer_identity_fingerprint == fingerprint and verification.peer_key_version == peer.key_version),
        "verified_at": verification.verified_at.isoformat() if verification else "",
    })


@router.post("/users/{user_id}/verify-identity")
def verify_contact_identity(user_id: int, request: Request, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    peer = db.get(User, user_id)
    if not peer or peer.id == auth.user.id:
        return fail("Utilizador não encontrado.", 404)
    fingerprint = public_key_fingerprint(peer.identity_public_key)
    verification = db.query(ContactIdentityVerification).filter_by(user_id=auth.user.id, peer_id=peer.id).first()
    if verification:
        verification.peer_key_version = peer.key_version
        verification.peer_identity_fingerprint = fingerprint
        verification.revoked_at = None
        verification.verified_at = utcnow()
    else:
        db.add(ContactIdentityVerification(
            user_id=auth.user.id,
            peer_id=peer.id,
            peer_key_version=peer.key_version,
            peer_identity_fingerprint=fingerprint,
        ))
    record_security_event(db, event_type="contact_identity_verify", outcome="success", user_id=auth.user.id, device_id=auth.device.device_id, request=request, metadata={"peer_id": peer.id, "peer_key_version": peer.key_version})
    db.commit()
    return ok({"identity_fingerprint": fingerprint, "key_version": peer.key_version}, "Identidade do contacto marcada como verificada.")


@router.get("/users/{user_id}/identity-verification")
def contact_identity_verification_status(user_id: int, auth: CurrentAuth = Depends(require_auth), db: Session = Depends(get_db)):
    peer = db.get(User, user_id)
    if not peer or peer.id == auth.user.id:
        return fail("Utilizador não encontrado.", 404)
    current_fingerprint = public_key_fingerprint(peer.identity_public_key)
    verification = db.query(ContactIdentityVerification).filter_by(user_id=auth.user.id, peer_id=peer.id, revoked_at=None).first()
    return ok({
        "verified": bool(verification and verification.peer_identity_fingerprint == current_fingerprint and verification.peer_key_version == peer.key_version),
        "current_fingerprint": current_fingerprint,
        "verified_fingerprint": verification.peer_identity_fingerprint if verification else "",
        "current_key_version": peer.key_version,
        "verified_key_version": verification.peer_key_version if verification else 0,
        "verified_at": verification.verified_at.isoformat() if verification else "",
    })


@router.post("/profile")
def update_profile(data: dict, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    user = db.get(User, auth.user.id)
    requested_username = clean_text(data.get("username") or data.get("Username"), 50).strip()
    if requested_username and requested_username != user.username:
        return fail("O @username é imutável após criação da conta.")

    full_name = clean_text(data.get("full_name") or data.get("FullName") or data.get("display_name") or data.get("DisplayName"), 100).strip()
    bio = clean_text(data.get("bio") or data.get("Bio"), 160).strip()
    avatar_base64 = (data.get("avatar_base64") or data.get("AvatarBase64") or "").strip()
    avatar_mime = clean_text(data.get("avatar_mime") or data.get("AvatarMime"), 100).strip()

    user.full_name = full_name or user.username
    user.bio = bio

    if avatar_base64:
        try:
            raw = base64.b64decode(avatar_base64, validate=True)
            if len(raw) > 512 * 1024:
                return fail("Avatar demasiado grande. Máximo: 512KB.")
            if avatar_mime not in ("image/png", "image/jpeg", "image/webp"):
                return fail("Formato de avatar inválido.")
            user.avatar_blob = raw
            user.avatar_mime = avatar_mime
        except Exception:
            return fail("Avatar inválido.")

    db.commit()
    db.refresh(user)

    threads = db.query(MessageThread).filter(
        or_(MessageThread.user_low_id == user.id, MessageThread.user_high_id == user.id)
    ).all()
    notified_peer_ids: set[int] = set()
    for thread in threads:
        peer_id = thread.user_high_id if thread.user_low_id == user.id else thread.user_low_id
        if peer_id <= 0 or peer_id == user.id or peer_id in notified_peer_ids:
            continue
        notified_peer_ids.add(peer_id)
        publish_user_event(
            peer_id,
            {
                "type": "user.profile.updated",
                "peer_id": user.id,
                "user_id": user.id,
                "user": public_user(user, db, peer_id),
            },
            queue_if_offline=False,
        )

    return ok({"user": public_user(user, db, auth.user.id)}, "Perfil atualizado.")


@router.get("/users/{user_id}/devices")
def user_devices(user_id: int, _auth: CurrentAuth = Depends(require_auth), db: Session = Depends(get_db)):
    user = db.get(User, user_id)
    if not user:
        return fail("Utilizador não encontrado.", 404, devices=[])
    devices = db.query(Device).filter_by(user_id=user.id, revoked_at=None).order_by(Device.created_at.asc()).all()
    return ok({
        "devices": [
            {
                "device_id": d.device_id,
                "device_encryption_public_key": d.encryption_public_key or "",
                "name": d.name or "Desktop",
                "platform": d.platform or "unknown",
                "last_seen_at": d.last_seen_at.isoformat() if d.last_seen_at else "",
            }
            for d in devices if d.encryption_public_key
        ]
    })


@router.get("/conversations")
def conversations(auth: CurrentAuth = Depends(require_auth), db: Session = Depends(get_db)):
    uid = auth.user.id
    threads = db.query(MessageThread).filter(
        or_(MessageThread.user_low_id == uid, MessageThread.user_high_id == uid),
        MessageThread.status == "accepted",
    ).order_by(desc(MessageThread.updated_at)).limit(200).all()
    result = []
    for thread in threads:
        summary = thread_summary(db, thread, uid, auth.device.device_id)
        if summary:
            result.append(summary)
    return ok({"conversations": result})


@router.get("/message-requests")
def message_requests(auth: CurrentAuth = Depends(require_auth), db: Session = Depends(get_db)):
    uid = auth.user.id
    threads = db.query(MessageThread).filter_by(recipient_id=uid, status="pending").order_by(desc(MessageThread.updated_at)).limit(200).all()
    result = []
    for thread in threads:
        summary = thread_summary(db, thread, uid, auth.device.device_id)
        if summary:
            result.append(summary)
    return ok({"conversations": result})


@router.get("/message-requests/sent")
def sent_message_requests(auth: CurrentAuth = Depends(require_auth), db: Session = Depends(get_db)):
    uid = auth.user.id
    threads = db.query(MessageThread).filter_by(initiator_id=uid, status="pending").order_by(desc(MessageThread.updated_at)).limit(200).all()
    result = []
    for thread in threads:
        summary = thread_summary(db, thread, uid, auth.device.device_id)
        if summary:
            result.append(summary)
    return ok({"conversations": result})


@router.post("/message-requests/{peer_id}/cancel")
def cancel_request(peer_id: int, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    uid = auth.user.id
    thread = thread_for(db, uid, peer_id)
    if not thread or thread.initiator_id != uid or thread.status != "pending":
        return fail("Pedido não encontrado.", 404)
    recipient_id = thread.recipient_id
    thread_id = thread.id
    db.delete(thread)
    db.commit()
    publish_user_event(recipient_id, {"type": "request.cancelled", "peer_id": uid, "thread_id": thread_id}, queue_if_offline=True)
    return ok(message="Pedido cancelado.")


@router.post("/message-requests/{peer_id}/create", dependencies=[Depends(rate_limit("chat:message_request_create", 30, 3600))])
def create_message_request(peer_id: int, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    uid = auth.user.id
    if peer_id == uid:
        return fail("Não podes criar pedido para ti próprio.")
    peer = db.get(User, peer_id)
    if not peer:
        return fail("Utilizador não encontrado.", 404)
    if _is_blocked_between(db, uid, peer_id):
        return fail("Não podes enviar pedido de mensagem enquanto houver bloqueio ativo.", 403)
    existing = thread_for(db, uid, peer_id)
    if existing:
        if existing.status == "accepted":
            return fail("Já existe uma conversa com este utilizador.", 409)
        if existing.status == "pending":
            direction = "outgoing" if existing.initiator_id == uid else "incoming"
            return fail("Já existe um pedido de mensagem pendente.", 409, status="pending", request_direction=direction)
        if existing.status == "rejected":
            existing.status = "pending"
            existing.initiator_id = uid
            existing.recipient_id = peer_id
            existing.rejected_at = None
            existing.updated_at = utcnow()
            db.commit()
            publish_user_event(peer_id, {"type": "request.new", "peer_id": uid, "thread_id": existing.id}, queue_if_offline=True)
            return ok({"status": "pending"}, "Pedido enviado.", 201)
        return fail("Não é possível criar novo pedido para esta conversa.", 409)
    low, high = pair(uid, peer_id)
    thread = MessageThread(user_low_id=low, user_high_id=high, initiator_id=uid, recipient_id=peer_id, status="pending")
    db.add(thread)
    db.commit()
    publish_user_event(peer_id, {"type": "request.new", "peer_id": uid, "thread_id": thread.id}, queue_if_offline=True)
    return ok({"status": "pending"}, "Pedido enviado.", 201)


@router.post("/message-requests/{peer_id}/accept")
def accept_request(peer_id: int, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    uid = auth.user.id
    thread = thread_for(db, uid, peer_id)
    if not thread or thread.recipient_id != uid or thread.status != "pending":
        return fail("Pedido não encontrado.", 404)
    thread.status = "accepted"
    thread.accepted_at = utcnow()
    thread.updated_at = utcnow()
    db.commit()
    publish_user_event(peer_id, {"type": "request.accepted", "peer_id": uid, "thread_id": thread.id}, queue_if_offline=True)
    return ok(message="Pedido aceite.")


@router.post("/message-requests/{peer_id}/reject")
def reject_request(peer_id: int, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    uid = auth.user.id
    thread = thread_for(db, uid, peer_id)
    if not thread or thread.recipient_id != uid or thread.status != "pending":
        return fail("Pedido não encontrado.", 404)
    thread.status = "rejected"
    thread.rejected_at = utcnow()
    thread.updated_at = utcnow()
    db.commit()
    publish_user_event(peer_id, {"type": "request.rejected", "peer_id": uid, "thread_id": thread.id}, queue_if_offline=True)
    return ok(message="Pedido rejeitado.")


@router.get("/blocked-users")
def blocked_users(auth: CurrentAuth = Depends(require_auth), db: Session = Depends(get_db)):
    rows = db.query(User).join(UserBlock, UserBlock.blocked_id == User.id).filter(UserBlock.blocker_id == auth.user.id).order_by(User.username.asc()).all()
    return ok({"users": [public_user(u, db, auth.user.id) for u in rows]})


@router.post("/delete-chat")
def delete_chat(_data: dict, _auth: CurrentAuth = Depends(require_signed_auth)):
    return ok(message="Chat removido localmente.")


@router.post("/block-user", dependencies=[Depends(rate_limit("chat:block_user", 60, 3600))])
def block_user(data: dict, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    peer_id = _peer_id_from_data(data)
    if peer_id <= 0 or peer_id == auth.user.id:
        return fail("Utilizador inválido.")
    if not db.get(User, peer_id):
        return fail("Utilizador não encontrado.", 404)
    existing = db.query(UserBlock).filter_by(blocker_id=auth.user.id, blocked_id=peer_id).first()
    if not existing:
        db.add(UserBlock(blocker_id=auth.user.id, blocked_id=peer_id))
        db.commit()
    publish_user_event(peer_id, {"type": "user.blocked", "peer_id": auth.user.id}, queue_if_offline=True)
    return ok({"blocked": True}, "Utilizador bloqueado.")


@router.post("/unblock-user")
def unblock_user(data: dict, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    peer_id = _peer_id_from_data(data)
    if peer_id <= 0 or peer_id == auth.user.id:
        return fail("Utilizador inválido.")
    db.query(UserBlock).filter_by(blocker_id=auth.user.id, blocked_id=peer_id).delete(synchronize_session=False)
    db.commit()
    publish_user_event(peer_id, {"type": "user.unblocked", "peer_id": auth.user.id}, queue_if_offline=True)
    return ok({"blocked": False}, "Utilizador desbloqueado.")
