import base64
import binascii
import re
from email_validator import validate_email, EmailNotValidError

EMAIL_MAX = 254
NAME_MAX = 100
PUBLIC_KEY_MAX = 4096
_B64_RE = re.compile(r"^[A-Za-z0-9+/=_-]+$")


def clean_text(value, max_len=255):
    value = (value or "").strip()
    value = re.sub(r"[\x00-\x08\x0b\x0c\x0e-\x1f]", "", value)
    return value[:max_len]


def normalize_email(value):
    value = clean_text(value, EMAIL_MAX).lower()
    try:
        return validate_email(value, check_deliverability=False).normalized.lower()
    except EmailNotValidError:
        return ""


def username_from_email(email):
    local = email.split("@", 1)[0]
    return re.sub(r"[^a-zA-Z0-9_.-]", "_", local)[:50] or "user"


def username_from_display_name(display_name, fallback_email=""):
    value = (display_name or "").strip().lower()
    value = re.sub(r"\s+", "_", value)
    value = re.sub(r"[^a-z0-9_.-]", "", value)
    value = re.sub(r"_+", "_", value).strip("_.-")
    if len(value) < 3:
        value = username_from_email(fallback_email)
    return (value[:32] or "user")


def decode_b64_bytes(value, max_len=PUBLIC_KEY_MAX) -> bytes:

    value = clean_text(value, max_len)
    if not value or len(value) > max_len or not _B64_RE.fullmatch(value):
        raise ValueError("invalid_base64")

    normalized = value.replace("-", "+").replace("_", "/")
    normalized += "=" * (-len(normalized) % 4)

    try:
        return base64.b64decode(normalized, validate=True)
    except (binascii.Error, ValueError) as exc:
        raise ValueError("invalid_base64") from exc


def validate_b64(value, max_len=PUBLIC_KEY_MAX):
    try:
        decode_b64_bytes(value, max_len)
        return True
    except ValueError:
        return False
