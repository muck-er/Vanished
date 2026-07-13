from __future__ import annotations

from datetime import timedelta

from fastapi import APIRouter, Depends, Query
from sqlalchemy import and_, asc, or_
from sqlalchemy.orm import Session

from app.models import DeletedMessage, Device, MessageThread, PrivateMessage, User, UserBlock, utcnow
from app.realtime import publish_user_event
from app.security.validators import clean_text, validate_b64
from app.security.rate_limit import rate_limit
from core.auth import CurrentAuth, require_auth, require_signed_auth
from core.database import get_db
from core.http import fail, ok
from services.common import can_view_thread, message_envelope_for_user, not_deleted_for_user, pair, thread_for

router = APIRouter(prefix="/api/messages", tags=["messages"])
_typing: dict[tuple[int, int], tuple[bool, object]] = {}
_conversation_rate: dict[int, list[object]] = {}

CONVERSATION_DEFAULT_LIMIT = 50
CONVERSATION_MAX_LIMIT = 500


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


def _logical_id(value: str | None) -> str | None:
    if not value:
        return None
    parts = value.rsplit("-d", 1)
    return parts[0] if len(parts) == 2 and parts[1].isdigit() else value


def _escape_like(value: str) -> str:
    return value.replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_")


def _check_conversation_rate_limit(user_id: int):
    now = utcnow()
    window_start = now - timedelta(minutes=1)
    bucket = [ts for ts in _conversation_rate.get(user_id, []) if ts >= window_start]
    if len(bucket) >= 240:
        return False
    bucket.append(now)
    _conversation_rate[user_id] = bucket
    return True


def _get_or_create_thread(db: Session, sender_id: int, recipient_id: int):
    low, high = pair(sender_id, recipient_id)
    thread = db.query(MessageThread).filter_by(user_low_id=low, user_high_id=high).first()
    if thread:
        return thread, False
    thread = MessageThread(user_low_id=low, user_high_id=high, initiator_id=sender_id, recipient_id=recipient_id, status="pending")
    db.add(thread)
    db.flush()
    return thread, True


@router.post("/send", dependencies=[Depends(rate_limit("messages:send", 120, 60))])
def send_message(data: dict, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    try:
        recipient_id = int(data.get("recipient_id") or 0)
    except Exception:
        recipient_id = 0
    recipient = db.get(User, recipient_id)
    if not recipient or recipient.id == auth.user.id:
        return fail("Destinatário inválido.")
    if _is_blocked_between(db, auth.user.id, recipient.id):
        return fail("Não podes enviar mensagens enquanto houver bloqueio ativo.", 403)

    recipient_device_id = clean_text(data.get("recipient_device_id"), 64) or None
    if recipient_device_id and not db.query(Device).filter_by(user_id=recipient.id, device_id=recipient_device_id, revoked_at=None).first():
        return fail("Recipient device inválido.")

    thread, _created = _get_or_create_thread(db, auth.user.id, recipient.id)
    if thread.status == "rejected":
        return fail("Pedido de mensagem rejeitado.", 403)
    if thread.status == "pending":
        return fail("Não podes enviar mensagens até o pedido ser aceite.", 403)

    eph = clean_text(data.get("eph_pub_b64"), 4096)
    nonce = clean_text(data.get("nonce_b64"), 128)
    cipher = clean_text(data.get("ciphertext_b64"), 10_000_000)
    if not (validate_b64(eph) and validate_b64(nonce, 128) and validate_b64(cipher, 10_000_000)):
        return fail("Envelope cifrado inválido.")

    client_msg_id = clean_text(data.get("client_msg_id"), 64) or None
    existing = None
    if client_msg_id:
        existing = db.query(PrivateMessage).filter_by(sender_id=auth.user.id, client_msg_id=client_msg_id).first()
    if existing:
        return ok({"id": existing.id, "created_at": existing.created_at.isoformat(), "thread_status": thread.status, "deduped": True}, "Envelope já armazenado.")

    msg = PrivateMessage(
        thread_id=thread.id,
        sender_id=auth.user.id,
        recipient_id=recipient.id,
        sender_device_id=auth.device.device_id,
        recipient_device_id=recipient_device_id,
        eph_pub_b64=eph,
        nonce_b64=nonce,
        ciphertext_b64=cipher,
        sender_eph_pub_b64=clean_text(data.get("sender_eph_pub_b64"), 4096) or None,
        sender_nonce_b64=clean_text(data.get("sender_nonce_b64"), 128) or None,
        sender_ciphertext_b64=clean_text(data.get("sender_ciphertext_b64"), 10_000_000) or None,
        client_msg_id=client_msg_id,
    )
    thread.updated_at = utcnow()
    db.add(msg)
    db.commit()
    db.refresh(msg)

    envelope_for_recipient = message_envelope_for_user(msg, recipient.id)
    envelope_for_sender = message_envelope_for_user(msg, auth.user.id)
    publish_user_event(
        recipient.id,
        {
            "type": "message.new",
            "message": envelope_for_recipient,
            "thread_status": thread.status,
            "peer_id": auth.user.id,
            "sender_id": auth.user.id,
            "sender_display_name": auth.user.full_name or auth.user.username,
        },
        queue_if_offline=True,
    )
    publish_user_event(auth.user.id, {"type": "message.sent", "message": envelope_for_sender, "message_id": msg.id, "status": "sent", "peer_id": recipient.id}, queue_if_offline=False)

    return ok({"id": msg.id, "created_at": msg.created_at.isoformat(), "thread_status": thread.status}, "Envelope armazenado.", 201)


@router.get("/conversation")
def conversation(
    peer_id: int = Query(0),
    limit: int = Query(CONVERSATION_DEFAULT_LIMIT, ge=1, le=CONVERSATION_MAX_LIMIT),
    before_id: int = Query(0),
    auth: CurrentAuth = Depends(require_auth),
    db: Session = Depends(get_db),
):
    uid = auth.user.id
    if not _check_conversation_rate_limit(uid):
        return fail("Muitos pedidos de carregamento de conversa. Tenta novamente dentro de instantes.", 429)
    thread = thread_for(db, uid, int(peer_id or 0))
    if not can_view_thread(thread, uid):
        return ok({"messages": [], "thread_status": "none", "next_before_id": 0})

    query = db.query(PrivateMessage).filter(
        PrivateMessage.thread_id == thread.id,
        or_(
            PrivateMessage.sender_id == uid,
            and_(PrivateMessage.recipient_id == uid, or_(PrivateMessage.recipient_device_id == auth.device.device_id, PrivateMessage.recipient_device_id.is_(None))),
        ),
        not_deleted_for_user(db, uid),
    )
    if before_id and before_id > 0:
        query = query.filter(PrivateMessage.id < int(before_id))

    page_size = min(max(int(limit or CONVERSATION_DEFAULT_LIMIT), 1), CONVERSATION_MAX_LIMIT)
    rows_desc = query.order_by(PrivateMessage.id.desc()).limit(page_size).all()
    rows = list(reversed(rows_desc))
    next_before_id = rows[0].id if rows else 0
    return ok({"messages": [message_envelope_for_user(m, uid) for m in rows], "thread_status": thread.status, "next_before_id": next_before_id})


@router.get("/pull")
def pull(since_id: int = Query(0), limit: int = Query(100, ge=1, le=200), auth: CurrentAuth = Depends(require_auth), db: Session = Depends(get_db)):
    limit = min(int(limit or 100), 200)
    rows = db.query(PrivateMessage).join(MessageThread, PrivateMessage.thread_id == MessageThread.id).filter(
        PrivateMessage.recipient_id == auth.user.id,
        PrivateMessage.id > int(since_id or 0),
        MessageThread.status == "accepted",
        or_(PrivateMessage.recipient_device_id == auth.device.device_id, PrivateMessage.recipient_device_id.is_(None)),
        not_deleted_for_user(db, auth.user.id),
    ).order_by(asc(PrivateMessage.id)).limit(limit).all()
    return ok({"messages": [message_envelope_for_user(m, auth.user.id) for m in rows]})


@router.post("/mark-read", dependencies=[Depends(rate_limit("messages:mark_read", 120, 60))])
def mark_read(data: dict, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    peer_id = int(data.get("peer_id") or 0)
    up_to_id = int(data.get("up_to_id") or 0)
    db.query(PrivateMessage).filter(
        PrivateMessage.sender_id == peer_id,
        PrivateMessage.recipient_id == auth.user.id,
        PrivateMessage.id <= up_to_id,
        PrivateMessage.read_at.is_(None),
        PrivateMessage.deleted_for_everyone_at.is_(None),
        not_deleted_for_user(db, auth.user.id),
    ).update({"read_at": utcnow()}, synchronize_session=False)
    db.commit()
    publish_user_event(peer_id, {"type": "message.read", "peer_id": auth.user.id, "read_by": auth.user.id, "up_to_id": up_to_id})
    return ok(message="Marcado como lido.")


@router.post("/typing", dependencies=[Depends(rate_limit("messages:typing", 120, 60))])
def typing(data: dict, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    peer_id = int(data.get("peer_id") or 0)
    is_typing = bool(data.get("is_typing"))
    if peer_id <= 0 or not db.get(User, peer_id):
        return fail("Utilizador inválido.")
    if _is_blocked_between(db, auth.user.id, peer_id):
        _typing.pop((auth.user.id, peer_id), None)
        return ok({"is_typing": False}, "Estado de escrita ignorado enquanto houver bloqueio ativo.")
    _typing[(auth.user.id, peer_id)] = (is_typing, utcnow())
    publish_user_event(peer_id, {"type": "typing.start" if is_typing else "typing.stop", "peer_id": auth.user.id, "user_id": auth.user.id})
    return ok()


@router.get("/typing-status")
def typing_status(peer_id: int = Query(0), auth: CurrentAuth = Depends(require_auth), db: Session = Depends(get_db)):
    peer_id = int(peer_id or 0)
    if peer_id <= 0 or _is_blocked_between(db, auth.user.id, peer_id):
        _typing.pop((peer_id, auth.user.id), None)
        return ok({"is_typing": False, "peer_id": peer_id})
    state, ts = _typing.get((peer_id, auth.user.id), (False, None))
    is_typing = bool(state and ts and (utcnow() - ts).total_seconds() <= 4)
    return ok({"is_typing": is_typing, "peer_id": peer_id})


@router.post("/delete-message", dependencies=[Depends(rate_limit("messages:delete", 60, 60))])
def delete_message(data: dict, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    message_id = int(data.get("message_id") or 0)
    scope = clean_text(data.get("scope"), 20) or "me"
    msg = db.get(PrivateMessage, message_id)
    if not msg or (msg.sender_id != auth.user.id and msg.recipient_id != auth.user.id):
        return fail("Mensagem não encontrada.", 404)

    logical = _logical_id(msg.client_msg_id)
    base_query = db.query(PrivateMessage).filter(PrivateMessage.id == msg.id)
    if logical:
        base_query = db.query(PrivateMessage).filter(PrivateMessage.thread_id == msg.thread_id, PrivateMessage.sender_id == msg.sender_id, PrivateMessage.client_msg_id.ilike(f"{_escape_like(logical)}%", escape="\\"))

    targets = base_query.all()
    if scope == "all":
        if msg.sender_id != auth.user.id:
            return fail("Só podes apagar para todos mensagens enviadas por ti.", 403)
        if utcnow() - msg.created_at > timedelta(minutes=5):
            return fail("Limite de tempo para apagar para todos expirou.", 403)
        for item in targets:
            item.deleted_for_everyone_at = utcnow()
        if msg.thread_id:
            thread = db.get(MessageThread, msg.thread_id)
            if thread:
                thread.updated_at = utcnow()
        db.commit()
        publish_user_event(msg.recipient_id, {"type": "message.deleted", "message_id": msg.id, "scope": "all", "deleted_by": auth.user.id}, queue_if_offline=True)
        publish_user_event(auth.user.id, {"type": "message.deleted", "message_id": msg.id, "scope": "all", "deleted_by": auth.user.id})
        return ok(message="Mensagem apagada para todos.")

    for item in targets:
        exists = db.query(DeletedMessage).filter_by(user_id=auth.user.id, message_id=item.id).first()
        if not exists:
            db.add(DeletedMessage(user_id=auth.user.id, message_id=item.id))
    db.commit()
    return ok(message="Mensagem apagada para ti.")


@router.post("/delete-chat")
def delete_chat(_data: dict, _auth: CurrentAuth = Depends(require_signed_auth)):
    return ok(message="Chat removido localmente.")


@router.post("/delete_chat")
def delete_chat_underscore(_data: dict, _auth: CurrentAuth = Depends(require_signed_auth)):
    return ok(message="Chat removido localmente.")


def _block_user_impl(data: dict, auth: CurrentAuth, db: Session):
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


def _unblock_user_impl(data: dict, auth: CurrentAuth, db: Session):
    peer_id = _peer_id_from_data(data)
    if peer_id <= 0 or peer_id == auth.user.id:
        return fail("Utilizador inválido.")
    db.query(UserBlock).filter_by(blocker_id=auth.user.id, blocked_id=peer_id).delete(synchronize_session=False)
    db.commit()
    publish_user_event(peer_id, {"type": "user.unblocked", "peer_id": auth.user.id}, queue_if_offline=True)
    return ok({"blocked": False}, "Utilizador desbloqueado.")


@router.post("/block-user")
def block_user(data: dict, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    return _block_user_impl(data, auth, db)


@router.post("/block_user")
def block_user_underscore(data: dict, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    return _block_user_impl(data, auth, db)


@router.post("/unblock-user")
def unblock_user(data: dict, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    return _unblock_user_impl(data, auth, db)


@router.post("/unblock_user")
def unblock_user_underscore(data: dict, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    return _unblock_user_impl(data, auth, db)
