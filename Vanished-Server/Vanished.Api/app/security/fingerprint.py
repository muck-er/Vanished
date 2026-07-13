from __future__ import annotations

import hashlib

from app.security.validators import decode_b64_bytes


def public_key_fingerprint(public_key_b64: str) -> str:
    raw = decode_b64_bytes(public_key_b64, 4096)
    digest = hashlib.sha256(raw).hexdigest().upper()
    return ":".join(digest[i:i + 4] for i in range(0, len(digest), 4))


def safety_number(a_user_id: int, a_public_key_b64: str, b_user_id: int, b_public_key_b64: str) -> str:
    left = (int(a_user_id), decode_b64_bytes(a_public_key_b64, 4096))
    right = (int(b_user_id), decode_b64_bytes(b_public_key_b64, 4096))
    ordered = sorted((left, right), key=lambda item: item[0])
    material = b"vanished:safety-number:v1\0" + ordered[0][0].to_bytes(8, "big") + ordered[0][1] + ordered[1][0].to_bytes(8, "big") + ordered[1][1]
    digits = str(int.from_bytes(hashlib.sha256(material).digest(), "big"))[:60].ljust(60, "0")
    return " ".join(digits[i:i + 5] for i in range(0, 60, 5))
