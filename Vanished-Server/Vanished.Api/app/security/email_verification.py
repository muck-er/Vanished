from __future__ import annotations

import datetime as dt
import html
import hmac
import os
import secrets
import smtplib
import ssl
from dataclasses import dataclass
from email.message import EmailMessage
from email.utils import formataddr, formatdate, make_msgid
from typing import Any

from app.models import EmailVerificationChallenge, User, utcnow
from app.security.validators import normalize_email
from core.auth import token_pepper


REGISTRATION_PURPOSE = "registration"
CODE_TTL_MINUTES = 15
MAX_ATTEMPTS = 5
RESEND_COOLDOWN_SECONDS = 60

_BLOCKED_REGISTRATION_DOMAINS = {
    "test.com",
    "example.com",
    "example.org",
    "example.net",
    "invalid",
    "localhost",
    "local",
    "test",
}


class EmailVerificationError(RuntimeError):
    pass

@dataclass(frozen=True)
class SmtpDeliveryConfig:
    host: str
    port: int
    username: str
    password: str
    from_addr: str
    from_name: str
    reply_to: str | None
    use_tls: bool
    use_starttls: bool
    timeout_seconds: float
    require_auth: bool


def _truthy(value: Any, default: bool = False) -> bool:
    if value is None:
        return default
    return str(value).strip().lower() in {"1", "true", "yes", "on", "sim", "y"}


def _clean_header(value: str, max_len: int = 320) -> str:
    cleaned = (value or "").strip()
    cleaned = cleaned.replace("\r", "").replace("\n", "")
    return cleaned[:max_len]


def _int_env(name: str, default: int) -> int:
    try:
        return int(os.getenv(name) or default)
    except (TypeError, ValueError):
        raise EmailVerificationError(f"{name} inválido.")


def _float_env(name: str, default: float) -> float:
    try:
        return float(os.getenv(name) or default)
    except (TypeError, ValueError):
        raise EmailVerificationError(f"{name} inválido.")


def _blocked_domains() -> set[str]:
    extra = os.getenv("BLOCKED_REGISTRATION_EMAIL_DOMAINS", "")
    domains = {d.strip().lower() for d in extra.split(",") if d.strip()}
    return _BLOCKED_REGISTRATION_DOMAINS | domains


def registration_email_policy_error(email: str) -> str | None:
    normalized = normalize_email(email)
    if not normalized or "@" not in normalized:
        return "Email inválido."

    domain = normalized.rsplit("@", 1)[1].lower()
    if domain in _blocked_domains():
        return "Usa um email real. Domínios de teste não são aceites."

    if "." not in domain:
        return "Usa um email real com domínio válido."

    return None


def generate_code() -> str:
    return f"{secrets.randbelow(1_000_000):06d}"


def _hash_code(email: str, code: str, salt: str) -> str:
    material = f"vanished:email-verification:v1:{email}:{code}:{salt}".encode("utf-8")
    return hmac.new(token_pepper().encode("utf-8"), material, "sha256").hexdigest()


def create_registration_challenge(db, email: str) -> tuple[EmailVerificationChallenge, str]:
    normalized = normalize_email(email)
    code = generate_code()
    salt = secrets.token_hex(16)
    now = utcnow()

    for existing in (
        db.query(EmailVerificationChallenge)
        .filter(
            EmailVerificationChallenge.email == normalized,
            EmailVerificationChallenge.purpose == REGISTRATION_PURPOSE,
            EmailVerificationChallenge.user_id.is_(None),
            EmailVerificationChallenge.expires_at > now,
        )
        .all()
    ):
        existing.used_at = existing.used_at or now
        existing.expires_at = now

    challenge = EmailVerificationChallenge(
        id=secrets.token_urlsafe(32),
        email=normalized,
        purpose=REGISTRATION_PURPOSE,
        code_hash=_hash_code(normalized, code, salt),
        code_salt=salt,
        attempts=0,
        expires_at=now + dt.timedelta(minutes=CODE_TTL_MINUTES),
        created_at=now,
    )
    db.add(challenge)
    return challenge, code


def _apply_row_lock(query, lock: bool):
    return query.with_for_update() if lock else query


def _latest_active_registration_challenge(db, email: str, lock: bool = False) -> EmailVerificationChallenge | None:
    normalized = normalize_email(email)
    now = utcnow()
    query = (
        db.query(EmailVerificationChallenge)
        .filter(
            EmailVerificationChallenge.email == normalized,
            EmailVerificationChallenge.purpose == REGISTRATION_PURPOSE,
            EmailVerificationChallenge.used_at.is_(None),
            EmailVerificationChallenge.expires_at > now,
        )
        .order_by(EmailVerificationChallenge.created_at.desc())
    )
    return _apply_row_lock(query, lock).first()


def _registration_token_challenge(db, email: str, token: str, lock: bool = False) -> EmailVerificationChallenge | None:
    normalized = normalize_email(email)
    candidate = str(token or "").strip()
    if not candidate or len(candidate) > 128:
        return None

    query = db.query(EmailVerificationChallenge).filter(
        EmailVerificationChallenge.id == candidate,
        EmailVerificationChallenge.email == normalized,
        EmailVerificationChallenge.purpose == REGISTRATION_PURPOSE,
        EmailVerificationChallenge.verified_at.is_not(None),
        EmailVerificationChallenge.used_at.is_not(None),
        EmailVerificationChallenge.user_id.is_(None),
        EmailVerificationChallenge.expires_at > utcnow(),
    )
    return _apply_row_lock(query, lock).first()


def verify_registration_code(
    db,
    email: str,
    code: str,
    mark_verified: bool = True,
    consume_code: bool = False,
    lock: bool = False,
) -> tuple[bool, str | None, EmailVerificationChallenge | None]:
    normalized = normalize_email(email)
    candidate = "".join(ch for ch in str(code or "") if ch.isdigit())
    if len(candidate) != 6:
        return False, "Código de validação inválido ou expirado.", None

    challenge = _latest_active_registration_challenge(db, normalized, lock=lock)
    if challenge is None:
        return False, "Código de validação inválido ou expirado.", None

    if int(challenge.attempts or 0) >= MAX_ATTEMPTS:
        challenge.used_at = utcnow()
        return False, "Código de validação inválido ou expirado.", challenge

    expected = _hash_code(normalized, candidate, challenge.code_salt)
    if not hmac.compare_digest(expected, challenge.code_hash):
        challenge.attempts = int(challenge.attempts or 0) + 1
        if challenge.attempts >= MAX_ATTEMPTS:
            challenge.used_at = utcnow()
        return False, "Código de validação inválido ou expirado.", challenge

    now = utcnow()
    if mark_verified and challenge.verified_at is None:
        challenge.verified_at = now
    if consume_code and challenge.used_at is None:
        challenge.used_at = now
    return True, None, challenge


def issue_registration_token(db, email: str, code: str) -> tuple[bool, str | None, str | None, int | None]:
    valid, error, challenge = verify_registration_code(
        db,
        email,
        code,
        mark_verified=True,
        consume_code=True,
        lock=True,
    )
    if not valid or challenge is None:
        return False, error or "Código de validação inválido ou expirado.", None, None

    remaining_seconds = max(0, int((challenge.expires_at - utcnow()).total_seconds()))
    return True, None, challenge.id, remaining_seconds


def validate_registration_token(db, email: str, token: str, lock: bool = False) -> tuple[bool, str | None, EmailVerificationChallenge | None]:
    challenge = _registration_token_challenge(db, email, token, lock=lock)
    if challenge is None:
        return False, "Validação de email inválida ou expirada. Pede um novo código.", None
    return True, None, challenge


def consume_registration_token(db, email: str, token: str, user_id: int) -> tuple[bool, str | None]:
    valid, error, challenge = validate_registration_token(db, email, token, lock=True)
    if not valid or challenge is None:
        return False, error or "Validação de email inválida ou expirada. Pede um novo código."

    challenge.user_id = user_id
    return True, None


def consume_registration_code(db, email: str, code: str, user_id: int) -> tuple[bool, str | None]:
    valid, error, challenge = verify_registration_code(
        db,
        email,
        code,
        mark_verified=True,
        consume_code=True,
        lock=True,
    )
    if not valid or challenge is None:
        return False, error or "Código de validação inválido ou expirado."

    challenge.user_id = user_id
    return True, None


def registration_resend_cooldown_seconds(db, email: str) -> int:
    normalized = normalize_email(email)
    if not normalized:
        return RESEND_COOLDOWN_SECONDS

    challenge = (
        db.query(EmailVerificationChallenge)
        .filter(
            EmailVerificationChallenge.email == normalized,
            EmailVerificationChallenge.purpose == REGISTRATION_PURPOSE,
            EmailVerificationChallenge.user_id.is_(None),
        )
        .order_by(EmailVerificationChallenge.created_at.desc())
        .first()
    )
    if challenge is None:
        return 0

    reference = challenge.sent_at or challenge.created_at
    if reference is None:
        return 0

    elapsed = int((utcnow() - reference).total_seconds())
    return max(0, RESEND_COOLDOWN_SECONDS - elapsed)


def _smtp_delivery_config() -> SmtpDeliveryConfig:
    host = _clean_header(os.getenv("SMTP_HOST") or "")
    use_tls = _truthy(os.getenv("SMTP_USE_TLS"), default=False)
    use_starttls = _truthy(os.getenv("SMTP_USE_STARTTLS"), default=not use_tls)
    port = _int_env("SMTP_PORT", 465 if use_tls else 587)
    username = _clean_header(os.getenv("SMTP_USERNAME") or "", 512)
    password = os.getenv("SMTP_PASSWORD") or ""
    from_addr = _clean_header(os.getenv("SMTP_FROM") or os.getenv("SMTP_FROM_EMAIL") or "", 320)
    from_name = _clean_header(os.getenv("SMTP_FROM_NAME") or "Vanished", 120) or "Vanished"
    reply_to = _clean_header(os.getenv("SMTP_REPLY_TO") or "", 320) or None
    timeout_seconds = _float_env("SMTP_TIMEOUT_SECONDS", 10.0)
    require_auth = _truthy(os.getenv("SMTP_REQUIRE_AUTH"), default=True)

    if not host:
        raise EmailVerificationError("SMTP_HOST não configurado.")
    if not 1 <= port <= 65535:
        raise EmailVerificationError("SMTP_PORT inválido.")
    if use_tls and use_starttls:
        raise EmailVerificationError("SMTP_USE_TLS e SMTP_USE_STARTTLS não podem estar ambos ativos.")
    if not from_addr or registration_email_policy_error(from_addr):
        raise EmailVerificationError("SMTP_FROM inválido.")
    if require_auth and (not username or not password):
        raise EmailVerificationError("SMTP_USERNAME/SMTP_PASSWORD não configurados.")
    if timeout_seconds <= 0 or timeout_seconds > 60:
        raise EmailVerificationError("SMTP_TIMEOUT_SECONDS inválido.")

    return SmtpDeliveryConfig(
        host=host,
        port=port,
        username=username,
        password=password,
        from_addr=from_addr,
        from_name=from_name,
        reply_to=reply_to,
        use_tls=use_tls,
        use_starttls=use_starttls,
        timeout_seconds=timeout_seconds,
        require_auth=require_auth,
    )


def _smtp_configured() -> bool:
    try:
        _smtp_delivery_config()
        return True
    except EmailVerificationError:
        return False


def _build_registration_message(config: SmtpDeliveryConfig, email: str, code: str) -> EmailMessage:
    recipient = normalize_email(email)
    sender_domain = config.from_addr.rsplit("@", 1)[1].lower()
    safe_recipient = html.escape(recipient)
    safe_code = html.escape(code)
    grouped_code = html.escape(f"{code[:3]} {code[3:]}")

    text_body = "\n".join(
        [
            "Vanished Security",
            "",
            "Recebemos um pedido para criar uma conta Vanished com este email.",
            "",
            f"Código de validação: {code}",
            "",
            f"Este código expira em {CODE_TTL_MINUTES} minutos e é invalidado assim que for usado.",
            "",
            "Nunca partilhes este código com ninguém. A equipa Vanished nunca te vai pedir este código por chat, chamada ou email.",
            "",
            "Se não foste tu a pedir este código, podes ignorar este email em segurança.",
            "",
            "Vanished",
            "Secure. Private. Ephemeral.",
        ]
    )

    html_body = f"""\
        <!doctype html>
        <html lang="pt">
        <head>
            <meta charset="utf-8">
            <title>Código de validação Vanished</title>
        </head>
        <body style="margin:0;padding:0;background:#0a0f1a;font-family:Arial,Helvetica,sans-serif;color:#e6edf7;">
            <div style="display:none;max-height:0;overflow:hidden;opacity:0;">
            O teu código Vanished é {safe_code}. Expira em {CODE_TTL_MINUTES} minutos.
            </div>

            <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#0a0f1a;margin:0;padding:32px 16px;">
            <tr>
                <td align="center">
                <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:620px;background:#161b27;border-radius:24px;overflow:hidden;border:1px solid #212a3f;box-shadow:0 20px 70px rgba(0,0,0,0.6);">
                    <tr>
                    <td style="background:linear-gradient(135deg,#0f1626 0%,#1a2338 100%);padding:32px 40px 24px;text-align:center;border-bottom:1px solid #2a3549;">
                        <img src="https://vanished.pt/assets/LogoWithoutText.png" 
                            alt="Vanished" 
                            width="92" 
                            height="92"
                            style="display:block;margin:0 auto 16px;border-radius:16px;">
                        <div style="font-size:28px;font-weight:800;letter-spacing:-0.8px;color:#58a6ff;">Vanished</div>
                    </td>
                    </tr>

                    <tr>
                    <td style="padding:40px 40px 20px;background:#161b27;">
                        <h1 style="margin:0 0 16px;font-size:27px;line-height:1.15;color:#ffffff;letter-spacing:-0.7px;">
                        Valida o teu email
                        </h1>
                        <p style="margin:0;color:#a3b0c2;font-size:16px;line-height:1.65;">
                        Recebemos um pedido para criar uma conta Vanished com 
                        <strong style="color:#58a6ff;">{safe_recipient}</strong>.<br>
                        Usa o código abaixo para continuar o registo.
                        </p>
                    </td>
                    </tr>
                    <tr>
                    <td style="padding:10px 40px 30px;background:#161b27;">
                        <div style="border:2px solid #212a3f;border-radius:20px;background:#0f1626;padding:32px;text-align:center;">
                        <div style="margin-bottom:12px;color:#8b9cb0;font-size:13px;font-weight:700;letter-spacing:2px;text-transform:uppercase;">CÓDIGO DE VALIDAÇÃO</div>
                        <div style="font-family:'SFMono-Regular','Consolas','Menlo',monospace;font-size:42px;line-height:1;font-weight:900;letter-spacing:12px;color:#58a6ff;">{grouped_code}</div>
                        <div style="margin-top:18px;color:#8b9cb0;font-size:14px;">
                            Expira em <strong>{CODE_TTL_MINUTES} minutos</strong><br>
                            Torna-se inválido após utilização
                        </div>
                        </div>
                    </td>
                    </tr>
                    <tr>
                    <td style="padding:0 40px 36px;background:#161b27;">
                        <div style="border-left:5px solid #f0883e;background:rgba(240,136,62,0.08);border-radius:12px;padding:20px 24px;color:#ffcc99;font-size:15px;line-height:1.6;">
                        <strong>Nunca partilhes este código.</strong><br>
                        A equipa Vanished nunca te pedirá este código por email, chat ou chamada telefónica.
                        </div>
                    </td>
                    </tr>
                    <tr>
                    <td style="background:#0f1626;border-top:1px solid #2a3549;padding:28px 40px;color:#8b9cb0;font-size:13px;line-height:1.6;">
                        <strong style="color:#e6edf7;">® Vanished | 2026</strong> — Secure. Private. Ephemeral.<br>
                        Email automático enviado por {html.escape(config.from_addr)}<br><br>
                        <span style="font-size:12px;">Se não foste tu a pedir este código, podes ignorar este email em segurança.</span>
                    </td>
                    </tr>
                </table>
                </td>
            </tr>
            </table>
        </body>
        </html>
"""

    msg = EmailMessage()
    msg["From"] = formataddr((config.from_name, config.from_addr))
    msg["To"] = recipient
    msg["Subject"] = "O teu código de validação Vanished"
    msg["Date"] = formatdate(localtime=False)
    msg["Message-ID"] = make_msgid(domain=sender_domain)
    msg["X-Auto-Response-Suppress"] = "All"
    if config.reply_to:
        msg["Reply-To"] = config.reply_to

    msg.set_content(text_body)
    msg.add_alternative(html_body, subtype="html")
    return msg


def send_registration_verification_email(email: str, code: str) -> None:
    config = _smtp_delivery_config()
    msg = _build_registration_message(config, email, code)
    context = ssl.create_default_context()

    try:
        if config.use_tls:
            with smtplib.SMTP_SSL(config.host, config.port, timeout=config.timeout_seconds, context=context) as smtp:
                if config.username:
                    smtp.login(config.username, config.password)
                smtp.send_message(msg)
            return

        with smtplib.SMTP(config.host, config.port, timeout=config.timeout_seconds) as smtp:
            smtp.ehlo()
            if config.use_starttls:
                smtp.starttls(context=context)
                smtp.ehlo()
            if config.username:
                smtp.login(config.username, config.password)
            smtp.send_message(msg)
    except EmailVerificationError:
        raise
    except Exception as exc:
        raise EmailVerificationError("Não foi possível enviar o email de validação.") from exc


def should_send_code_for_registration(db, email: str) -> bool:
    normalized = normalize_email(email)
    if not normalized:
        return False
    return db.query(User.id).filter(User.email == normalized).first() is None
