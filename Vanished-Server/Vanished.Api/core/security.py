import jwt
from core.auth import jwt_secret


def verify_ws_token(token: str) -> dict | None:
    if not token:
        return None
    try:
        payload = jwt.decode(token, jwt_secret(), algorithms=['HS256'])
        if not payload.get('sub'):
            return None
        return payload
    except jwt.ExpiredSignatureError:
        return None
    except Exception:
        return None
