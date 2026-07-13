import logging

from sqlalchemy import and_, or_

from app.extensions import db
from app.models import UserBlock
from core.database import init_database
from core.redis import redis_client

ONLINE_TTL_SECONDS = 12
logger = logging.getLogger("vanished.ws.handlers")


def _session():
    if db.SessionLocal is None:
        init_database()
    return db.SessionLocal()


def _safe_int(value) -> int:
    try:
        return int(value or 0)
    except (TypeError, ValueError):
        return 0


def _is_blocked_between(user_id: str | int, peer_id: str | int) -> bool:
    uid = _safe_int(user_id)
    pid = _safe_int(peer_id)
    if uid <= 0 or pid <= 0:
        return True

    session = _session()
    try:
        return session.query(UserBlock.id).filter(
            or_(
                and_(UserBlock.blocker_id == uid, UserBlock.blocked_id == pid),
                and_(UserBlock.blocker_id == pid, UserBlock.blocked_id == uid),
            )
        ).first() is not None
    except Exception:
        logger.warning("websocket_block_state_check_failed", exc_info=True)
        return True
    finally:
        session.close()


async def handle_client_event(manager, user_id: str, event: dict):
    kind = event.get('type')

    if kind == 'ping':
        await redis_client.setex(f'online:{user_id}', ONLINE_TTL_SECONDS, '1')
        await manager.send_to_user(user_id, {'type': 'pong'})
        return

    if kind in {'typing.start', 'typing.stop'}:
        peer_id = str(event.get('peer_id') or event.get('recipient_id') or '')
        if peer_id and not _is_blocked_between(user_id, peer_id):
            await manager.send_to_user(peer_id, {
                'type': kind,
                'user_id': user_id,
                'peer_id': int(user_id),
                'conversation_id': event.get('conversation_id'),
            })
        return

    if kind == 'message.read':
        sender_id = str(event.get('sender_id') or event.get('peer_id') or '')
        if sender_id and not _is_blocked_between(user_id, sender_id):
            await manager.send_to_user(sender_id, {
                'type': 'message.read',
                'message_ids': event.get('message_ids') or [],
                'up_to_id': event.get('up_to_id'),
                'read_by': user_id,
                'peer_id': int(user_id),
            })
        return

    if kind == 'status.update':
        await redis_client.setex(f'online:{user_id}', ONLINE_TTL_SECONDS, '1')
        return
