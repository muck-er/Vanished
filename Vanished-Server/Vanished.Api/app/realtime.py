from ws.bridge import emit_to_user, queue_to_user


def publish_user_event(user_id: int | str, event: dict, queue_if_offline: bool = False) -> None:
    delivered = emit_to_user(user_id, event)
    if queue_if_offline and not delivered:
        queue_to_user(user_id, event)
