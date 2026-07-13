from __future__ import annotations

import base64
from sqlalchemy import and_, or_, desc
from sqlalchemy.orm import Session

from app.models import DeletedMessage, MessageThread, PrivateMessage, User, UserBlock, utcnow


def pair(a: int, b: int) -> tuple[int, int]:
    return (a, b) if a < b else (b, a)


def public_user(user: User, db: Session | None = None, viewer_id: int | None = None) -> dict:
    avatar_b64 = base64.b64encode(user.avatar_blob).decode() if user.avatar_blob else ""
    is_blocked = False
    message_status = "none"
    request_direction = ""
    if db is not None and viewer_id and user.id != viewer_id:
        is_blocked = db.query(UserBlock.id).filter_by(blocker_id=viewer_id, blocked_id=user.id).first() is not None
        thread = thread_for(db, int(viewer_id), int(user.id))
        if thread:
            message_status = thread.status or "none"
            if thread.status == "pending":
                request_direction = "outgoing" if thread.initiator_id == viewer_id else "incoming"
    return {
        "id": user.id,
        "username": user.username,
        "full_name": user.full_name,
        "public_key": user.identity_public_key,
        "key_version": user.key_version,
        "avatar_base64": avatar_b64,
        "avatar_mime": user.avatar_mime or "",
        "is_online": bool(user.last_seen_at and (utcnow() - user.last_seen_at).total_seconds() < 15),
        "last_seen_at": user.last_seen_at.isoformat() if user.last_seen_at else "",
        "status_text": "",
        "bio": user.bio or "",
        "created_at": user.created_at.isoformat() if user.created_at else "",
        "is_blocked": is_blocked,
        "message_status": message_status,
        "request_direction": request_direction,
    }


def message_envelope_for_user(msg: PrivateMessage, viewer_id: int) -> dict:
    if msg.sender_id == viewer_id and msg.sender_ciphertext_b64:
        eph = msg.sender_eph_pub_b64
        nonce = msg.sender_nonce_b64
        cipher = msg.sender_ciphertext_b64
    else:
        eph = msg.eph_pub_b64
        nonce = msg.nonce_b64
        cipher = msg.ciphertext_b64
    return {
        "id": msg.id,
        "sender_id": msg.sender_id,
        "recipient_id": msg.recipient_id,
        "eph_pub_b64": eph,
        "nonce_b64": nonce,
        "ciphertext_b64": cipher,
        "client_msg_id": msg.client_msg_id or "",
        "created_at": msg.created_at.isoformat(),
        "is_delivered": False,
        "is_read": msg.read_at is not None,
        "delivery_state": "read" if msg.read_at else "sent",
        "is_deleted_for_all": msg.deleted_for_everyone_at is not None,
        "is_deleted_for_me": False,
        "sender_ciphertext_b64": msg.sender_ciphertext_b64 or "",
    }


def thread_for(db: Session, uid: int, peer_id: int) -> MessageThread | None:
    low, high = pair(uid, peer_id)
    return db.query(MessageThread).filter_by(user_low_id=low, user_high_id=high).first()


def not_deleted_for_user(db: Session, viewer_id: int):
    deleted_subq = db.query(DeletedMessage.message_id).filter(DeletedMessage.user_id == viewer_id)
    return ~PrivateMessage.id.in_(deleted_subq)


def last_visible_message(db: Session, thread: MessageThread, viewer_id: int, device_id: str | None = None) -> PrivateMessage | None:
    return db.query(PrivateMessage).filter(
        PrivateMessage.thread_id == thread.id,
        or_(
            PrivateMessage.sender_id == viewer_id,
            and_(
                PrivateMessage.recipient_id == viewer_id,
                or_(PrivateMessage.recipient_device_id == device_id, PrivateMessage.recipient_device_id.is_(None)),
            ),
        ),
        not_deleted_for_user(db, viewer_id),
    ).order_by(desc(PrivateMessage.id)).first()


def thread_summary(db: Session, thread: MessageThread, viewer_id: int, device_id: str | None = None) -> dict | None:
    peer_id = thread.recipient_id if thread.initiator_id == viewer_id else thread.initiator_id
    if peer_id == viewer_id:
        peer_id = thread.user_high_id if thread.user_low_id == viewer_id else thread.user_low_id
    peer = db.get(User, peer_id)
    if not peer:
        return None
    last = last_visible_message(db, thread, viewer_id, device_id)
    unread_count = db.query(PrivateMessage).filter(
        PrivateMessage.thread_id == thread.id,
        PrivateMessage.sender_id == peer_id,
        PrivateMessage.recipient_id == viewer_id,
        PrivateMessage.read_at.is_(None),
        PrivateMessage.deleted_for_everyone_at.is_(None),
        or_(PrivateMessage.recipient_device_id == device_id, PrivateMessage.recipient_device_id.is_(None)),
        not_deleted_for_user(db, viewer_id),
    ).count()
    return {
        "thread_id": thread.id,
        "status": thread.status,
        "peer": public_user(peer, db, viewer_id),
        "last": message_envelope_for_user(last, viewer_id) if last else None,
        "unread_count": unread_count,
    }


def can_view_thread(thread: MessageThread | None, uid: int) -> bool:
    if not thread:
        return False
    if thread.status == "accepted":
        return True
    return bool(thread.status == "pending" and (thread.initiator_id == uid or thread.recipient_id == uid))
