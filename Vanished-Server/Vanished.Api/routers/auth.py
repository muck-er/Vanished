from __future__ import annotations

import datetime as dt
import hashlib
import hmac
import os
import secrets
import logging
from typing import Any

from argon2.low_level import Type, hash_secret_raw

from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PublicKey
from fastapi import APIRouter, Depends, Request
from sqlalchemy import or_, func
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

from app.models import AuthChallenge, DeletedMessage, Device, EmailVerificationChallenge, EncryptedExport, KeyEnvelope, MessageThread, PrivateMessage, RefreshSession, User, UserBlock, utcnow
from app.realtime import publish_user_event
from app.security.validators import clean_text, decode_b64_bytes, normalize_email, validate_b64
from app.security.email_verification import (
    consume_registration_code,
    consume_registration_token,
    create_registration_challenge,
    issue_registration_token,
    registration_email_policy_error,
    registration_resend_cooldown_seconds,
    send_registration_verification_email,
    should_send_code_for_registration,
    validate_registration_token,
    verify_registration_code,
)
from app.security.audit import hash_identifier, record_security_event
from app.security.rate_limit import rate_limit
from core.auth import (
    CurrentAuth,
    create_access_token,
    create_refresh_session,
    hash_token,
    require_auth,
    require_signed_auth,
    rotate_refresh_session,
    token_pepper,
)
from core.database import get_db
from core.http import fail, ok
from ws.manager import connection_manager

router = APIRouter(prefix="/api/user", tags=["auth"])
logger = logging.getLogger("vanished.auth")


def _email_registered(db: Session, email: str) -> bool:
    normalized = normalize_email(email)
    if not normalized:
        return False
    return db.query(User.id).filter(func.lower(User.email) == normalized.lower()).first() is not None


def _normalize_username(value: Any) -> str:
    username = clean_text(value, 128).strip().lower()
    if username.startswith("@"):
        username = username[1:]
    return username.strip()


def _validate_username_format(username: str) -> str | None:
    if not username:
        return "Escolhe um @handle."
    if len(username) < 3:
        return "O @handle tem de ter pelo menos 3 caracteres."
    if len(username) > 32:
        return "O @handle não pode ter mais de 32 caracteres."
    allowed = set("abcdefghijklmnopqrstuvwxyz0123456789._-")
    if any(ch not in allowed for ch in username):
        return "O @handle só pode conter letras, números, ponto, hífen e underscore."
    if username[0] in ".-_" or username[-1] in ".-__":
        return "O @handle não pode começar nem acabar com ponto, hífen ou underscore."
    if ".." in username or "--" in username or "__" in username:
        return "O @handle não pode ter símbolos repetidos seguidos."
    return None


def _username_registered(db: Session, username: str) -> bool:
    normalized = _normalize_username(username)
    if not normalized:
        return False
    return db.query(User.id).filter(func.lower(User.username) == normalized.lower()).first() is not None



PIN_MIN_LENGTH = 6
PIN_MAX_LENGTH = 64
PIN_ARGON2_TIME_COST = 3
PIN_ARGON2_MEMORY_COST_KB = 65_536
PIN_ARGON2_PARALLELISM = 2


def _normalize_account_pin(value: Any) -> str:
    pin = clean_text(value, PIN_MAX_LENGTH + 20)
    return pin.strip()


def _validate_account_pin_format(pin: str) -> str | None:
    if len(pin) < PIN_MIN_LENGTH:
        return "O Vanished PIN tem de ter pelo menos 6 caracteres."
    if len(pin) > PIN_MAX_LENGTH:
        return "O Vanished PIN não pode ter mais de 64 caracteres."
    if any(ch.isspace() for ch in pin):
        return "O Vanished PIN não pode conter espaços."
    return None


def _pin_pepper() -> str:
    pepper = (os.getenv("ACCOUNT_PIN_PEPPER") or "").strip()
    if pepper:
        lowered = pepper.lower()
        if len(pepper) < 32 or lowered.startswith("change-") or "dev-only" in lowered:
            raise RuntimeError("ACCOUNT_PIN_PEPPER tem de ser um segredo forte e exclusivo.")
        return pepper
    return token_pepper()


def _hash_account_pin(pin: str, salt_hex: str) -> str:
    salt = bytes.fromhex(salt_hex)
    material = f"vanished:account-pin:v2:{_pin_pepper()}:{pin}".encode("utf-8")
    digest = hash_secret_raw(
        secret=material,
        salt=salt,
        time_cost=PIN_ARGON2_TIME_COST,
        memory_cost=PIN_ARGON2_MEMORY_COST_KB,
        parallelism=PIN_ARGON2_PARALLELISM,
        hash_len=32,
        type=Type.ID,
    )
    return (
        f"argon2id$v=19$m={PIN_ARGON2_MEMORY_COST_KB},"
        f"t={PIN_ARGON2_TIME_COST},p={PIN_ARGON2_PARALLELISM}$"
        f"{digest.hex()}"
    )


def _set_account_pin(user: User, pin: str) -> None:
    salt_hex = secrets.token_bytes(16).hex()
    user.account_pin_salt = salt_hex
    user.account_pin_hash = _hash_account_pin(pin, salt_hex)
    user.account_pin_failed_attempts = 0
    user.account_pin_locked_until = None
    user.account_pin_updated_at = utcnow()


def _verify_account_pin(user: User, pin: str) -> tuple[bool, str | None]:
    now = utcnow()
    if user.account_pin_locked_until and user.account_pin_locked_until > now:
        return False, "Vanished PIN bloqueado temporariamente. Tenta novamente mais tarde."
    if not user.account_pin_hash or not user.account_pin_salt:
        return False, "Esta conta ainda não tem Vanished PIN configurado."
    pin = _normalize_account_pin(pin)
    if not pin:
        return False, "Vanished PIN obrigatório."
    expected = _hash_account_pin(pin, user.account_pin_salt)
    is_valid = hmac.compare_digest(expected, user.account_pin_hash)

    if is_valid:
        user.account_pin_failed_attempts = 0
        user.account_pin_locked_until = None
        return True, None

    failures = int(user.account_pin_failed_attempts or 0) + 1
    user.account_pin_failed_attempts = failures
    if failures >= 10:
        user.account_pin_locked_until = now + dt.timedelta(hours=1)
    elif failures >= 5:
        user.account_pin_locked_until = now + dt.timedelta(minutes=5)
    return False, "Vanished PIN inválido."


def _get_case_insensitive(mapping: dict, *names: str, default=None):
    if not isinstance(mapping, dict):
        return default
    for name in names:
        if name in mapping:
            return mapping.get(name)
    lowered = {str(k).lower(): v for k, v in mapping.items()}
    for name in names:
        key = str(name).lower()
        if key in lowered:
            return lowered[key]
    return default


def _truthy(value: Any) -> bool:
    if isinstance(value, bool):
        return value
    if value is None:
        return False
    if isinstance(value, (int, float)):
        return value != 0
    return str(value).strip().lower() in {"1", "true", "yes", "sim", "y"}


def _parse_recovery_envelope(data: dict):
    envelope = _get_case_insensitive(data, "RecoveryEnvelope", "recoveryEnvelope", "recovery_envelope", default={}) or {}
    if envelope and not isinstance(envelope, dict):
        return None, "Recovery envelope inválido."

    ciphertext_b64 = clean_text(
        _get_case_insensitive(envelope, "CiphertextB64", "ciphertextB64", "ciphertext_b64")
        or _get_case_insensitive(data, "RecoveryEnvelopeCiphertextB64", "recoveryEnvelopeCiphertextB64", "recovery_envelope_ciphertext_b64"),
        20_000,
    )
    nonce_b64 = clean_text(
        _get_case_insensitive(envelope, "NonceB64", "nonceB64", "nonce_b64")
        or _get_case_insensitive(data, "RecoveryEnvelopeNonceB64", "recoveryEnvelopeNonceB64", "recovery_envelope_nonce_b64"),
        128,
    )

    kdf = _get_case_insensitive(envelope, "Kdf", "kdf", default={})
    if kdf and not isinstance(kdf, dict):
        return None, "KDF do recovery envelope inválido."
    if not isinstance(kdf, dict):
        kdf = {}

    name = clean_text(
        _get_case_insensitive(kdf, "Name", "name")
        or _get_case_insensitive(data, "RecoveryEnvelopeKdfName", "recoveryEnvelopeKdfName", "recovery_envelope_kdf_name")
        or "argon2id",
        80,
    )
    purpose = clean_text(
        _get_case_insensitive(kdf, "Purpose", "purpose")
        or _get_case_insensitive(data, "RecoveryEnvelopeKdfPurpose", "recoveryEnvelopeKdfPurpose", "recovery_envelope_kdf_purpose")
        or "vanished-recovery-envelope-v2",
        120,
    )
    salt_b64 = clean_text(
        _get_case_insensitive(kdf, "SaltB64", "saltB64", "salt_b64")
        or _get_case_insensitive(data, "RecoveryEnvelopeKdfSaltB64", "recoveryEnvelopeKdfSaltB64", "recovery_envelope_kdf_salt_b64"),
        128,
    )
    try:
        iterations = int(
            _get_case_insensitive(kdf, "Iterations", "iterations")
            or _get_case_insensitive(data, "RecoveryEnvelopeKdfIterations", "recoveryEnvelopeKdfIterations", "recovery_envelope_kdf_iterations")
            or 0
        )
        key_size = int(
            _get_case_insensitive(kdf, "KeySize", "keySize", "key_size")
            or _get_case_insensitive(data, "RecoveryEnvelopeKdfKeySize", "recoveryEnvelopeKdfKeySize", "recovery_envelope_kdf_key_size")
            or 32
        )
        memory_size_kb = int(
            _get_case_insensitive(kdf, "MemorySizeKb", "memorySizeKb", "memory_size_kb")
            or _get_case_insensitive(data, "RecoveryEnvelopeKdfMemorySizeKb", "recoveryEnvelopeKdfMemorySizeKb", "recovery_envelope_kdf_memory_size_kb")
            or 0
        )
        parallelism = int(
            _get_case_insensitive(kdf, "Parallelism", "parallelism")
            or _get_case_insensitive(data, "RecoveryEnvelopeKdfParallelism", "recoveryEnvelopeKdfParallelism", "recovery_envelope_kdf_parallelism")
            or 0
        )
    except Exception:
        return None, "Parâmetros KDF inválidos."

    missing = []
    if not ciphertext_b64:
        missing.append("recovery_envelope.ciphertext_b64")
    if not nonce_b64:
        missing.append("recovery_envelope.nonce_b64")
    if not salt_b64:
        missing.append("recovery_envelope.kdf.salt_b64")
    if missing:
        return None, "Recovery envelope incompleto: " + ", ".join(missing) + "."
    if not validate_b64(ciphertext_b64) or not validate_b64(nonce_b64) or not validate_b64(salt_b64):
        return None, "Recovery envelope contém base64 inválido."
    name_normalized = name.strip().lower()
    if key_size != 32:
        return None, "KDF do recovery envelope não aceite."

    if name_normalized == "argon2id":
        if iterations < 1 or iterations > 10:
            return None, "KDF do recovery envelope não aceite."
        if memory_size_kb < 19_456 or memory_size_kb > 262_144:
            return None, "KDF do recovery envelope não aceite."
        if parallelism < 1 or parallelism > 8:
            return None, "KDF do recovery envelope não aceite."
    else:
        return None, "KDF do recovery envelope não aceite."

    return {
        "ciphertext_b64": ciphertext_b64,
        "nonce_b64": nonce_b64,
        "kdf": {
            "name": name_normalized,
            "iterations": iterations,
            "salt_b64": salt_b64,
            "key_size": key_size,
            "memory_size_kb": memory_size_kb,
            "parallelism": parallelism,
            "purpose": purpose,
        },
    }, None


def _recovery_envelope_response(envelope: KeyEnvelope) -> dict:
    return {
        "ciphertext_b64": envelope.ciphertext_b64,
        "nonce_b64": envelope.nonce_b64,
        "kdf": envelope.kdf or {},
    }



def _recovery_key_fingerprint(recovery_key_hash: str) -> str:
    digest = hashlib.sha256(("vanished:recovery-key:v1:" + (recovery_key_hash or "")).encode("utf-8")).hexdigest()
    return digest[:16].upper()



def _get_device_signing_public_key(data: dict) -> str:
    return clean_text(
        _get_case_insensitive(
            data,
            "DeviceSigningPublicKey", "deviceSigningPublicKey", "device_signing_public_key",
            "DevicePublicKey", "devicePublicKey", "device_public_key",
        ),
        4096,
    )


def _get_device_encryption_public_key(data: dict) -> str:
    return clean_text(
        _get_case_insensitive(data, "DeviceEncryptionPublicKey", "deviceEncryptionPublicKey", "device_encryption_public_key"),
        4096,
    )






@router.post("/register/handle/check", dependencies=[Depends(rate_limit("auth:register_handle_check", 30, 60, ("username",)))])
def register_handle_check(data: dict, request: Request, db: Session = Depends(get_db)):
    username = _normalize_username(_get_case_insensitive(data, "Username", "username", "Handle", "handle"))
    username_error = _validate_username_format(username)
    if username_error:
        return ok({"available": False, "username": username}, username_error)

    if _username_registered(db, username):
        return ok(
            {"available": False, "username": username},
            f"@{username} já está em uso. Insere outro @handle.",
        )

    return ok({"available": True, "username": username}, f"@{username} disponível.")


@router.post("/register/email/start", dependencies=[Depends(rate_limit("auth:register_email_start", 5, 3600, ("email",)))])
def register_email_start(data: dict, request: Request, db: Session = Depends(get_db)):
    email = normalize_email(_get_case_insensitive(data, "Email", "email"))
    policy_error = registration_email_policy_error(email)
    if policy_error:
        record_security_event(db, event_type="register_email_start", outcome="invalid_email", request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail(policy_error, 400)

    if _email_registered(db, email):
        record_security_event(db, event_type="register_email_start", outcome="duplicate_email", request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail("Este email já está associado a uma conta Vanished. Inicia sessão ou usa recuperação de conta.", 409)

    if not should_send_code_for_registration(db, email):
        record_security_event(db, event_type="register_email_start", outcome="existing_or_unavailable", request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail("Este email não está disponível para criar uma nova conta.", 409)

    cooldown_seconds = registration_resend_cooldown_seconds(db, email)
    if cooldown_seconds > 0:
        record_security_event(db, event_type="register_email_start", outcome="cooldown", request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail(
            f"Aguarda {cooldown_seconds}s antes de reenviar o código.",
            429,
            resend_available_in_seconds=cooldown_seconds,
            cooldown_seconds=cooldown_seconds,
        )

    try:
        challenge, code = create_registration_challenge(db, email)
        send_registration_verification_email(email, code)
        challenge.sent_at = utcnow()
        record_security_event(db, event_type="register_email_start", outcome="sent", request=request, subject_hash=hash_identifier(email))
        db.commit()
        return ok(
            {
                "expires_in_minutes": 15,
                "resend_available_in_seconds": 60,
                "cooldown_seconds": 60,
            },
            "Enviámos um código de validação para o teu email.",
        )
    except Exception:
        db.rollback()
        logger.exception("Falha ao enviar email de validação para hash=%s", hash_identifier(email))
        return fail("Não foi possível enviar o email de validação. Verifica a configuração SMTP.", 503)


@router.post("/register/email/verify", dependencies=[Depends(rate_limit("auth:register_email_verify", 10, 900, ("email",)))])
def register_email_verify(data: dict, request: Request, db: Session = Depends(get_db)):
    email = normalize_email(_get_case_insensitive(data, "Email", "email"))
    code = clean_text(_get_case_insensitive(data, "Code", "code", "email_verification_code"), 16)
    policy_error = registration_email_policy_error(email)
    if policy_error:
        return fail(policy_error, 400)

    if _email_registered(db, email):
        record_security_event(db, event_type="register_email_verify", outcome="duplicate_email", request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail("Este email já está associado a uma conta Vanished. Inicia sessão ou usa recuperação de conta.", 409)

    valid, error, verification_token, remaining_seconds = issue_registration_token(db, email, code)
    record_security_event(
        db,
        event_type="register_email_verify",
        outcome="success" if valid else "invalid",
        request=request,
        subject_hash=hash_identifier(email),
    )
    db.commit()

    if not valid or not verification_token:
        return fail(error or "Código de validação inválido ou expirado.", 400)

    return ok(
        {
            "email_verification_token": verification_token,
            "expires_in_seconds": remaining_seconds or 0,
        },
        "Email validado. Podes continuar a criação da conta.",
    )

@router.post("/register", dependencies=[Depends(rate_limit("auth:register", 3, 3600, ("email",)))])
def register(data: dict, request: Request, db: Session = Depends(get_db)):
    forbidden = {"password", "privatekey", "private_key", "masterkey", "master_key", "recoverykey", "recovery_key"}
    if any(str(k).lower() in forbidden for k in data.keys()):
        record_security_event(db, event_type="register", outcome="rejected_zk_contract", request=request)
        db.commit()
        return fail("Contrato ZK violado: passwords/private keys/recovery keys não são aceites pela API.")

    email = normalize_email(_get_case_insensitive(data, "Email", "email"))
    email_verification_code = clean_text(_get_case_insensitive(data, "EmailVerificationCode", "emailVerificationCode", "email_verification_code", "VerificationCode", "verificationCode", "code"), 16)
    email_verification_token = clean_text(_get_case_insensitive(data, "EmailVerificationToken", "emailVerificationToken", "email_verification_token", "VerificationToken", "verificationToken"), 128)
    full_name = clean_text(_get_case_insensitive(data, "FullName", "fullName", "full_name"), 100)
    requested_username = _normalize_username(_get_case_insensitive(data, "Username", "username", "Handle", "handle"))
    identity_public_key = clean_text(_get_case_insensitive(data, "IdentityPublicKey", "identityPublicKey", "identity_public_key", "PublicKey", "publicKey", "public_key"), 4096)
    device_id = clean_text(_get_case_insensitive(data, "DeviceId", "deviceId", "device_id"), 64)
    device_signing_public_key = _get_device_signing_public_key(data)
    device_encryption_public_key = _get_device_encryption_public_key(data)
    recovery_key_hash = clean_text(_get_case_insensitive(data, "RecoveryKeyHash", "recoveryKeyHash", "recovery_key_hash"), 128)
    platform = clean_text(_get_case_insensitive(data, "ClientPlatform", "clientPlatform", "client_platform"), 200)
    local_mfa_enabled = _truthy(_get_case_insensitive(data, "LocalMfaEnabled", "localMfaEnabled", "local_mfa_enabled"))
    account_pin = _normalize_account_pin(_get_case_insensitive(data, "AccountPin", "accountPin", "account_pin", "VanishedPin", "vanishedPin", "vanished_pin"))
    recovery_envelope, envelope_error = _parse_recovery_envelope(data)

    pin_error = _validate_account_pin_format(account_pin)
    if pin_error:
        return fail(pin_error)

    if not local_mfa_enabled:
        return fail("MFA obrigatório: a conta só é criada depois de o cliente verificar TOTP localmente.")

    policy_error = registration_email_policy_error(email)
    if policy_error:
        return fail(policy_error, 400)
    if _email_registered(db, email):
        record_security_event(db, event_type="register", outcome="duplicate_email_precheck", request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail("Este email já está associado a uma conta Vanished. Inicia sessão ou usa recuperação de conta.", 409)

    username_error = _validate_username_format(requested_username)
    if username_error:
        return fail(username_error, 400)
    if _username_registered(db, requested_username):
        record_security_event(db, event_type="register", outcome="duplicate_username_precheck", request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail(f"@{requested_username} já está em uso. Insere outro @handle.", 409)

    if not email_verification_token and not email_verification_code:
        return fail("Valida o teu email antes de criar a conta.", 400)

    if email_verification_token:
        token_valid, token_error, _ = validate_registration_token(db, email, email_verification_token, lock=True)
        if not token_valid:
            db.commit()
            return fail(token_error or "Validação de email inválida ou expirada. Pede um novo código.", 400)
    else:
        code_valid, code_error, _ = verify_registration_code(
            db,
            email,
            email_verification_code,
            mark_verified=True,
            consume_code=False,
            lock=True,
        )
        if not code_valid:
            db.commit()
            return fail(code_error or "Código de validação inválido ou expirado.", 400)

    missing = []
    if not email: missing.append("email")
    if not identity_public_key: missing.append("identity_public_key")
    if not device_id: missing.append("device_id")
    if not device_signing_public_key: missing.append("device_signing_public_key")
    if not device_encryption_public_key: missing.append("device_encryption_public_key")
    if not recovery_key_hash: missing.append("recovery_key_hash")
    if missing:
        return fail("Dados ZK incompletos: " + ", ".join(missing) + ". A API só aceita public keys, hashes e envelopes cifrados.", missing_fields=missing)
    if envelope_error:
        return fail(envelope_error)
    if not validate_b64(identity_public_key) or not validate_b64(device_signing_public_key) or not validate_b64(device_encryption_public_key):
        return fail("Public key inválida.")
    if db.query(User.id).filter_by(recovery_key_hash=recovery_key_hash).first() is not None:
        record_security_event(db, event_type="register", outcome="duplicate_recovery_hash", request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail("Recovery key inválida: já existe uma conta com o mesmo hash de recovery.", 409)

    if not recovery_envelope:
        return fail("Recovery envelope em falta ou inválido.")

    try:
        username = requested_username

        user = User(
            username=username,
            full_name=full_name or username,
            email=email,
            identity_public_key=identity_public_key,
            recovery_key_hash=recovery_key_hash,
            mfa_totp_enabled=True,
        )
        _set_account_pin(user, account_pin)
        db.add(user)
        db.flush()
        db.add(Device(
            user_id=user.id,
            device_id=device_id,
            public_key=device_signing_public_key,
            encryption_public_key=device_encryption_public_key,
            name="Desktop",
            platform=platform or "unknown",
        ))
        db.add(KeyEnvelope(
            user_id=user.id,
            device_id=device_id,
            envelope_type="recovery_identity_key",
            ciphertext_b64=recovery_envelope["ciphertext_b64"],
            nonce_b64=recovery_envelope["nonce_b64"],
            kdf=recovery_envelope["kdf"],
        ))
        if email_verification_token:
            consumed, consume_error = consume_registration_token(db, email, email_verification_token, user.id)
        else:
            consumed, consume_error = consume_registration_code(db, email, email_verification_code, user.id)
        if not consumed:
            db.rollback()
            return fail(consume_error or "Validação de email inválida ou expirada. Pede um novo código.", 400)

        access_token, _, _ = create_access_token(user.id, user.email, device_id)
        refresh_token, _ = create_refresh_session(db, user.id, device_id)
        record_security_event(db, event_type="register", outcome="success", user_id=user.id, device_id=device_id, request=request, subject_hash=hash_identifier(email))
        db.commit()
    except IntegrityError:
        db.rollback()
        record_security_event(db, event_type="register", outcome="duplicate_account", request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail("Conta já existe.", 409)
    except Exception:
        db.rollback()
        logger.exception("Erro ao criar conta ZK para email=%s device_id=%s", email, device_id)
        return fail("Erro interno ao criar conta.", 500)

    return ok({
        "access_token": access_token,
        "refresh_token": refresh_token,
    }, "Conta criada. Guarda a recovery key antes de iniciares sessão.", status_code=201)


@router.post("/login/start", dependencies=[Depends(rate_limit("auth:login_start", 5, 60, ("email", "device_id")))])
def login_start(data: dict, request: Request, db: Session = Depends(get_db)):
    email = normalize_email(data.get("Email") or data.get("email"))
    device_id = clean_text(data.get("DeviceId") or data.get("device_id"), 64)
    user = db.query(User).filter_by(email=email).first()
    if not user:
        record_security_event(db, event_type="login_start", outcome="invalid", request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail("Credenciais inválidas.", 401)
    device = db.query(Device).filter_by(user_id=user.id, device_id=device_id, revoked_at=None).first()
    if not device:
        record_security_event(db, event_type="login_start", outcome="invalid_device", user_id=user.id, device_id=device_id, request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail("Credenciais inválidas.", 403)

    challenge_id = secrets.token_urlsafe(32)
    server_nonce = secrets.token_urlsafe(48)
    challenge = AuthChallenge(id=challenge_id, user_id=user.id, device_id=device_id, server_nonce=server_nonce, purpose="login", expires_at=utcnow() + dt.timedelta(minutes=3))
    db.add(challenge)
    record_security_event(db, event_type="login_start", outcome="challenge_created", user_id=user.id, device_id=device_id, request=request, subject_hash=hash_identifier(email))
    db.commit()
    return ok({"challenge_id": challenge_id, "server_nonce": server_nonce, "requires_mfa": bool(user.mfa_totp_enabled), "requires_pin": bool(user.account_pin_hash)}, "Challenge criada.")


@router.post("/login/finish", dependencies=[Depends(rate_limit("auth:login_finish", 10, 60, ("email", "device_id")))])
def login_finish(data: dict, request: Request, db: Session = Depends(get_db)):
    email = normalize_email(data.get("Email") or data.get("email"))
    device_id = clean_text(data.get("DeviceId") or data.get("device_id"), 64)
    challenge_id = clean_text(data.get("ChallengeId") or data.get("challenge_id"), 128)
    server_nonce = clean_text(data.get("ServerNonce") or data.get("server_nonce"), 128)
    signature_b64 = clean_text(data.get("Signature") or data.get("signature"), 4096)
    client_mfa_satisfied = bool(data.get("ClientMfaSatisfied") or data.get("client_mfa_satisfied"))
    use_account_pin_unlock = _truthy(data.get("UseAccountPinUnlock") or data.get("use_account_pin_unlock"))
    account_pin = _normalize_account_pin(data.get("AccountPin") or data.get("account_pin") or data.get("VanishedPin") or data.get("vanished_pin"))

    user = db.query(User).filter_by(email=email).first()
    if not user:
        record_security_event(db, event_type="login_finish", outcome="invalid", request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail("Credenciais inválidas.", 401)
    device = db.query(Device).filter_by(user_id=user.id, device_id=device_id, revoked_at=None).first()
    challenge = db.query(AuthChallenge).filter_by(id=challenge_id, user_id=user.id, device_id=device_id, used_at=None).first()
    if not device or not challenge or challenge.expires_at < utcnow() or challenge.server_nonce != server_nonce:
        record_security_event(db, event_type="login_finish", outcome="invalid_challenge", user_id=user.id, device_id=device_id, request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail("Não foi possível verificar a tua identidade.", 401)

    proof_payload = f"{challenge_id}.{server_nonce}.{email}.{device_id}".encode()
    try:
        pub = Ed25519PublicKey.from_public_bytes(decode_b64_bytes(device.public_key))
        pub.verify(decode_b64_bytes(signature_b64, 4096), proof_payload)
    except (InvalidSignature, ValueError, Exception):
        record_security_event(db, event_type="login_finish", outcome="invalid_signature", user_id=user.id, device_id=device_id, request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail("Não foi possível verificar a tua identidade.", 401)

    if user.account_pin_hash:
        pin_ok, _ = _verify_account_pin(user, account_pin)
        if not pin_ok:
            record_security_event(db, event_type="login_finish", outcome="invalid_pin", user_id=user.id, device_id=device_id, request=request, subject_hash=hash_identifier(email))
            db.commit()
            return fail("Não foi possível verificar a tua identidade.", 401)

    if user.mfa_totp_enabled and not client_mfa_satisfied and not use_account_pin_unlock:
        record_security_event(db, event_type="login_finish", outcome="mfa_not_satisfied", user_id=user.id, device_id=device_id, request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail("Não foi possível verificar a tua identidade.", 401)

    challenge.used_at = utcnow()
    user.last_seen_at = utcnow()
    device.last_seen_at = utcnow()
    access_token, _, _ = create_access_token(user.id, user.email, device.device_id)
    refresh_token, _ = create_refresh_session(db, user.id, device.device_id)
    record_security_event(db, event_type="login_finish", outcome="success", user_id=user.id, device_id=device.device_id, request=request, subject_hash=hash_identifier(email))
    db.commit()
    return ok({"access_token": access_token, "refresh_token": refresh_token}, "Login aceite por assinatura de dispositivo.")


@router.post("/refresh", dependencies=[Depends(rate_limit("auth:refresh", 60, 60))])
def refresh(data: dict, request: Request, db: Session = Depends(get_db)):
    refresh_token = clean_text(data.get("RefreshToken") or data.get("refresh_token"), 512)
    token_hash = hash_token(refresh_token)
    session = db.query(RefreshSession).filter_by(token_hash=token_hash).first()
    if not session:
        record_security_event(db, event_type="refresh", outcome="invalid", request=request)
        db.commit()
        return fail("Credenciais inválidas.", 401)

    if session.revoked_at is not None:
        db.query(RefreshSession).filter_by(
            user_id=session.user_id,
            device_id=session.device_id,
            family_id=session.family_id,
            revoked_at=None,
        ).update({"revoked_at": utcnow()}, synchronize_session=False)
        record_security_event(db, event_type="refresh", outcome="reused_revoked_token", user_id=session.user_id, device_id=session.device_id, request=request)
        db.commit()
        return fail("Credenciais inválidas.", 401)

    if session.expires_at < utcnow():
        session.revoked_at = utcnow()
        record_security_event(db, event_type="refresh", outcome="expired", user_id=session.user_id, device_id=session.device_id, request=request)
        db.commit()
        return fail("Credenciais inválidas.", 401)

    user = db.get(User, session.user_id)
    if not user:
        record_security_event(db, event_type="refresh", outcome="invalid_user", user_id=session.user_id, device_id=session.device_id, request=request)
        db.commit()
        return fail("Credenciais inválidas.", 401)

    device = db.query(Device).filter_by(user_id=user.id, device_id=session.device_id, revoked_at=None).first()
    if not device:
        session.revoked_at = utcnow()
        record_security_event(db, event_type="refresh", outcome="revoked_device", user_id=user.id, device_id=session.device_id, request=request)
        db.commit()
        return fail("Credenciais inválidas.", 401)

    new_refresh, _ = rotate_refresh_session(db, session)
    access_token, _, _ = create_access_token(user.id, user.email, session.device_id)
    record_security_event(db, event_type="refresh", outcome="success", user_id=user.id, device_id=session.device_id, request=request)
    db.commit()
    return ok({"access_token": access_token, "refresh_token": new_refresh}, "Refresh token rodado.")


@router.post("/logout")
async def logout(request: Request, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    db.query(RefreshSession).filter_by(
        user_id=auth.user.id,
        device_id=auth.device.device_id,
        revoked_at=None,
    ).update({"revoked_at": utcnow()}, synchronize_session=False)
    record_security_event(db, event_type="logout", outcome="success", user_id=auth.user.id, device_id=auth.device.device_id, request=request)
    db.commit()
    await connection_manager.force_disconnect(auth.user.id, code=4000, reason="logout")
    return ok(message="Sessão terminada.")


@router.post("/vanish")
async def vanish_account(data: dict[str, Any], auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    uid = int(auth.user.id)
    user = db.get(User, uid)
    if not user:
        return fail("Credenciais inválidas.", 401)

    account_pin = _normalize_account_pin(data.get("AccountPin") or data.get("account_pin") or data.get("VanishedPin") or data.get("vanished_pin"))
    ok_pin, _ = _verify_account_pin(user, account_pin)
    if not ok_pin:
        db.commit()
        return fail("Não foi possível verificar a tua identidade.", 401)

    try:
        thread_rows = (
            db.query(MessageThread)
            .filter(or_(
                MessageThread.user_low_id == uid,
                MessageThread.user_high_id == uid,
                MessageThread.initiator_id == uid,
                MessageThread.recipient_id == uid,
            ))
            .all()
        )
        thread_ids = [thread.id for thread in thread_rows]
        peer_ids = {
            int(thread.user_high_id if thread.user_low_id == uid else thread.user_low_id)
            for thread in thread_rows
            if thread.user_low_id and thread.user_high_id and thread.user_low_id != thread.user_high_id
        }
        peer_ids.discard(uid)

        message_query = db.query(PrivateMessage.id).filter(or_(
            PrivateMessage.sender_id == uid,
            PrivateMessage.recipient_id == uid,
        ))
        if thread_ids:
            message_query = message_query.union(db.query(PrivateMessage.id).filter(PrivateMessage.thread_id.in_(thread_ids)))
        message_ids = [row[0] for row in message_query.all()]

        if message_ids:
            db.query(DeletedMessage).filter(or_(DeletedMessage.user_id == uid, DeletedMessage.message_id.in_(message_ids))).delete(synchronize_session=False)
            db.query(PrivateMessage).filter(PrivateMessage.id.in_(message_ids)).delete(synchronize_session=False)
        else:
            db.query(DeletedMessage).filter(DeletedMessage.user_id == uid).delete(synchronize_session=False)

        if thread_ids:
            db.query(MessageThread).filter(MessageThread.id.in_(thread_ids)).delete(synchronize_session=False)

        db.query(UserBlock).filter(or_(UserBlock.blocker_id == uid, UserBlock.blocked_id == uid)).delete(synchronize_session=False)
        db.query(EncryptedExport).filter(EncryptedExport.user_id == uid).delete(synchronize_session=False)
        db.query(KeyEnvelope).filter(KeyEnvelope.user_id == uid).delete(synchronize_session=False)
        db.query(AuthChallenge).filter(AuthChallenge.user_id == uid).delete(synchronize_session=False)
        db.query(RefreshSession).filter(RefreshSession.user_id == uid).delete(synchronize_session=False)
        db.query(Device).filter(Device.user_id == uid).delete(synchronize_session=False)
        db.delete(user)
        db.commit()
    except Exception:
        db.rollback()
        logger.exception("Falha ao executar Vanish para user_id=%s", uid)
        return fail("Não foi possível apagar a conta.", 500)

    for peer_id in peer_ids:
        publish_user_event(
            peer_id,
            {"type": "user.deleted", "peer_id": uid, "user_id": uid},
            queue_if_offline=False,
        )

    await connection_manager.force_disconnect(uid, code=4001, reason="vanish")
    return ok(message="Conta apagada definitivamente.")


@router.post("/pin/verify")
def verify_pin(data: dict, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    pin = _normalize_account_pin(data.get("AccountPin") or data.get("account_pin") or data.get("VanishedPin") or data.get("vanished_pin"))
    user = db.get(User, auth.user.id)
    if not user:
        return fail("Credenciais inválidas.", 401)
    ok_pin, _ = _verify_account_pin(user, pin)
    db.commit()
    if not ok_pin:
        return fail("Não foi possível verificar a tua identidade.", 401)
    return ok(message="Vanished PIN validado.")


@router.post("/pin/change")
def change_pin(data: dict, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    current = _normalize_account_pin(data.get("CurrentPin") or data.get("current_pin"))
    new = _normalize_account_pin(data.get("NewPin") or data.get("new_pin"))
    error = _validate_account_pin_format(new)
    if error:
        return fail(error)
    user = db.get(User, auth.user.id)
    if not user:
        return fail("Credenciais inválidas.", 401)
    ok_pin, _ = _verify_account_pin(user, current)
    if not ok_pin:
        db.commit()
        return fail("Não foi possível verificar a tua identidade.", 401)
    _set_account_pin(user, new)
    db.commit()
    return ok(message="Vanished PIN alterado.")


@router.post("/mfa/local-status")
def mfa_local_status(data: dict, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    auth.user.mfa_totp_enabled = bool(data.get("Enabled") or data.get("enabled"))
    db.merge(auth.user)
    db.commit()
    return ok(message="Estado MFA local atualizado. O segredo TOTP nunca foi enviado ao servidor.")


@router.post("/devices/rotate")
def rotate_device(data: dict, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    signing_public = _get_device_signing_public_key(data)
    encryption_public = _get_device_encryption_public_key(data)
    platform = clean_text(data.get("ClientPlatform") or data.get("client_platform"), 200)
    if not signing_public or not encryption_public:
        return fail("Novas device public keys obrigatórias.")
    if not validate_b64(signing_public) or not validate_b64(encryption_public):
        return fail("Não foi possível verificar a tua identidade.")
    device = db.query(Device).filter_by(user_id=auth.user.id, device_id=auth.device.device_id, revoked_at=None).first()
    device.public_key = signing_public
    device.encryption_public_key = encryption_public
    device.platform = platform or device.platform
    device.last_seen_at = utcnow()
    db.commit()
    return ok(message="Device signing/encryption keys rodadas.")


@router.post("/update-identity")
def update_identity(data: dict, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    key = clean_text(data.get("IdentityPublicKey") or data.get("PublicKey") or data.get("identity_public_key"), 4096)
    recovery_key_hash = clean_text(data.get("RecoveryKeyHash") or data.get("recovery_key_hash"), 128)
    recovery_envelope, envelope_error = _parse_recovery_envelope(data)
    if not validate_b64(key):
        return fail("Public key inválida.")
    if envelope_error or not recovery_key_hash:
        return fail(envelope_error or "Recovery data obrigatório ao rodar identidade.")

    user = db.get(User, auth.user.id)
    if db.query(User.id).filter(User.recovery_key_hash == recovery_key_hash, User.id != user.id).first() is not None:
        return fail("Recovery key inválida: já existe uma conta com o mesmo hash de recovery.", 409)
    user.identity_public_key = key
    user.recovery_key_hash = recovery_key_hash
    user.key_version += 1
    existing = db.query(KeyEnvelope).filter_by(user_id=user.id, device_id=auth.device.device_id, envelope_type="recovery_identity_key").first()
    if existing:
        existing.ciphertext_b64 = recovery_envelope["ciphertext_b64"]
        existing.nonce_b64 = recovery_envelope["nonce_b64"]
        existing.kdf = recovery_envelope["kdf"]
    else:
        db.add(KeyEnvelope(user_id=user.id, device_id=auth.device.device_id, envelope_type="recovery_identity_key", ciphertext_b64=recovery_envelope["ciphertext_b64"], nonce_b64=recovery_envelope["nonce_b64"], kdf=recovery_envelope["kdf"]))
    db.commit()
    return ok(message="Identidade rodada. Guarda a nova recovery key.")


@router.post("/recovery/replace-device", dependencies=[Depends(rate_limit("auth:recovery_replace_device", 3, 3600, ("email",)))])
def recovery_replace_device(data: dict, request: Request, db: Session = Depends(get_db)):
    email = normalize_email(data.get("Email") or data.get("email"))
    recovery_key_hash = clean_text(data.get("RecoveryKeyHash") or data.get("recovery_key_hash"), 128)
    device_id = clean_text(data.get("DeviceId") or data.get("device_id"), 64)
    signing_public = _get_device_signing_public_key(data)
    encryption_public = _get_device_encryption_public_key(data)
    platform = clean_text(data.get("ClientPlatform") or data.get("client_platform"), 200)

    if not email or not recovery_key_hash or not device_id or not signing_public or not encryption_public:
        record_security_event(db, event_type="recovery_replace_device", outcome="invalid_payload", request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail("Não foi possível verificar a tua identidade.")
    if not validate_b64(signing_public) or not validate_b64(encryption_public):
        record_security_event(db, event_type="recovery_replace_device", outcome="invalid_public_key", request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail("Não foi possível verificar a tua identidade.")

    user = db.query(User).filter_by(email=email).first()
    if not user or not secrets.compare_digest(user.recovery_key_hash, recovery_key_hash):
        record_security_event(db, event_type="recovery_replace_device", outcome="invalid", user_id=user.id if user else None, device_id=device_id, request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail("Não foi possível verificar a tua identidade.", 401)

    envelope = db.query(KeyEnvelope).filter_by(user_id=user.id, envelope_type="recovery_identity_key").order_by(KeyEnvelope.created_at.desc()).first()
    if not envelope:
        return fail("Não foi possível verificar a tua identidade.", 404)

    device = db.query(Device).filter_by(user_id=user.id, device_id=device_id).first()
    if device:
        device.public_key = signing_public
        device.encryption_public_key = encryption_public
        device.platform = platform or "unknown"
        device.revoked_at = None
        device.last_seen_at = utcnow()
    else:
        db.add(Device(user_id=user.id, device_id=device_id, public_key=signing_public, encryption_public_key=encryption_public, name="Desktop", platform=platform or "unknown"))
    record_security_event(db, event_type="recovery_replace_device", outcome="success", user_id=user.id, device_id=device_id, request=request, subject_hash=hash_identifier(email))
    db.commit()
    return ok({"identity_public_key": user.identity_public_key, "recovery_envelope": _recovery_envelope_response(envelope)}, "Dispositivo registado por recovery key. Faz login para continuar.")


@router.post("/recovery/rotate-after-replace", dependencies=[Depends(rate_limit("auth:recovery_rotate_after_replace", 3, 3600, ("email",)))])
def recovery_rotate_after_replace(data: dict, request: Request, db: Session = Depends(get_db)):
    email = normalize_email(data.get("Email") or data.get("email"))
    old_recovery_key_hash = clean_text(data.get("OldRecoveryKeyHash") or data.get("old_recovery_key_hash"), 128)
    new_recovery_key_hash = clean_text(data.get("NewRecoveryKeyHash") or data.get("new_recovery_key_hash") or data.get("RecoveryKeyHash") or data.get("recovery_key_hash"), 128)
    device_id = clean_text(data.get("DeviceId") or data.get("device_id"), 64)
    recovery_envelope, envelope_error = _parse_recovery_envelope(data)

    if not email or not old_recovery_key_hash or not new_recovery_key_hash or not device_id:
        record_security_event(db, event_type="recovery_rotate_after_replace", outcome="invalid_payload", request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail("Não foi possível atualizar a recovery key.")
    if envelope_error or not recovery_envelope:
        record_security_event(db, event_type="recovery_rotate_after_replace", outcome="invalid_envelope", request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail(envelope_error or "Recovery envelope inválido.")
    if old_recovery_key_hash == new_recovery_key_hash:
        return fail("A nova recovery key tem de ser diferente da anterior.")

    user = db.query(User).filter_by(email=email).first()
    if not user or not secrets.compare_digest(user.recovery_key_hash, old_recovery_key_hash):
        record_security_event(db, event_type="recovery_rotate_after_replace", outcome="invalid", user_id=user.id if user else None, device_id=device_id, request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail("Não foi possível atualizar a recovery key.", 401)

    device = db.query(Device).filter_by(user_id=user.id, device_id=device_id, revoked_at=None).first()
    if not device:
        record_security_event(db, event_type="recovery_rotate_after_replace", outcome="device_not_found", user_id=user.id, device_id=device_id, request=request, subject_hash=hash_identifier(email))
        db.commit()
        return fail("Dispositivo não autorizado.", 403)

    if db.query(User.id).filter(User.recovery_key_hash == new_recovery_key_hash, User.id != user.id).first() is not None:
        return fail("Recovery key inválida: já existe uma conta com o mesmo hash de recovery.", 409)

    user.recovery_key_hash = new_recovery_key_hash
    envelope = db.query(KeyEnvelope).filter_by(user_id=user.id, device_id=device_id, envelope_type="recovery_identity_key").first()
    if envelope:
        envelope.ciphertext_b64 = recovery_envelope["ciphertext_b64"]
        envelope.nonce_b64 = recovery_envelope["nonce_b64"]
        envelope.kdf = recovery_envelope["kdf"]
    else:
        db.add(KeyEnvelope(
            user_id=user.id,
            device_id=device_id,
            envelope_type="recovery_identity_key",
            ciphertext_b64=recovery_envelope["ciphertext_b64"],
            nonce_b64=recovery_envelope["nonce_b64"],
            kdf=recovery_envelope["kdf"],
        ))

    record_security_event(db, event_type="recovery_rotate_after_replace", outcome="success", user_id=user.id, device_id=device_id, request=request, subject_hash=hash_identifier(email))
    db.commit()
    return ok(message="Recovery key rodada após reposição. Guarda a nova chave.")


def _current_refresh_token_hash(data: dict) -> str:
    token = clean_text(
        data.get("CurrentRefreshToken")
        or data.get("currentRefreshToken")
        or data.get("current_refresh_token"),
        512,
    )
    return hash_token(token) if token else ""


def _session_response(session: RefreshSession, current_device_id: str, current_refresh_hash: str = "") -> dict:
    now = utcnow()
    is_active = bool(session.revoked_at is None and session.expires_at and session.expires_at > now)
    return {
        "id": session.id,
        "device_id": session.device_id or "",
        "family_id": session.family_id or "",
        "created_at": session.created_at.isoformat() if session.created_at else "",
        "expires_at": session.expires_at.isoformat() if session.expires_at else "",
        "revoked_at": session.revoked_at.isoformat() if session.revoked_at else "",
        "is_active": is_active,
        "is_current_device": bool((session.device_id or "") == (current_device_id or "")),
        "is_current_session": bool(current_refresh_hash and hmac.compare_digest(session.token_hash or "", current_refresh_hash)),
    }


def _account_management_response(db: Session, user: User, current_device: Device, current_refresh_hash: str = "") -> dict:
    devices = (
        db.query(Device)
        .filter_by(user_id=user.id)
        .order_by(Device.revoked_at.asc().nullsfirst(), Device.created_at.desc())
        .limit(100)
        .all()
    )
    sessions = (
        db.query(RefreshSession)
        .filter_by(user_id=user.id)
        .order_by(RefreshSession.revoked_at.asc().nullsfirst(), RefreshSession.created_at.desc())
        .limit(100)
        .all()
    )
    current_recovery_hash = clean_text(user.recovery_key_hash, 128)
    return {
        "recovery_key": {
            "fingerprint": _recovery_key_fingerprint(current_recovery_hash),
            "status": "valid" if current_recovery_hash else "missing",
            "key_version": int(user.key_version or 1),
            "created_at": user.created_at.isoformat() if user.created_at else "",
        },
        "identity": {
            "key_version": int(user.key_version or 1),
        },
        "devices": [_device_response(device, current_device.device_id) for device in devices],
        "sessions": [_session_response(session, current_device.device_id, current_refresh_hash) for session in sessions],
    }


@router.post("/security/keys-sessions", dependencies=[Depends(rate_limit("auth:keys_sessions", 12, 60))])
def keys_and_sessions_management(data: dict, request: Request, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    account_pin = _normalize_account_pin(data.get("AccountPin") or data.get("account_pin") or data.get("VanishedPin") or data.get("vanished_pin"))
    current_refresh_hash = _current_refresh_token_hash(data)
    valid_pin, pin_error = _verify_account_pin(auth.user, account_pin)
    if not valid_pin:
        record_security_event(db, event_type="keys_sessions_view", outcome="invalid_pin", user_id=auth.user.id, device_id=auth.device.device_id, request=request)
        db.commit()
        return fail(pin_error or "Não foi possível verificar a tua identidade.", 401)

    payload = _account_management_response(db, auth.user, auth.device, current_refresh_hash)
    record_security_event(db, event_type="keys_sessions_view", outcome="success", user_id=auth.user.id, device_id=auth.device.device_id, request=request)
    db.commit()
    return ok(payload, "Gestão de chaves e sessões carregada.")


@router.post("/sessions/revoke", dependencies=[Depends(rate_limit("auth:session_revoke", 20, 60))])
def revoke_refresh_session(data: dict, request: Request, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    try:
        session_id = int(data.get("SessionId") or data.get("session_id") or 0)
    except (TypeError, ValueError):
        session_id = 0
    if session_id <= 0:
        return fail("Sessão inválida.")

    session = db.query(RefreshSession).filter_by(id=session_id, user_id=auth.user.id).first()
    if not session:
        return fail("Sessão inválida.", 404)
    current_refresh_hash = _current_refresh_token_hash(data)
    is_current_session = bool(current_refresh_hash and hmac.compare_digest(session.token_hash or "", current_refresh_hash))
    if is_current_session and session.revoked_at is None:
        return fail("Usa terminar sessão para encerrar a sessão atual.", 409)
    if session.device_id == auth.device.device_id and not current_refresh_hash and session.revoked_at is None:
        return fail("Não foi possível confirmar qual é a sessão atual. Atualiza a app e tenta novamente.", 409)
    if session.revoked_at is None:
        session.revoked_at = utcnow()
    record_security_event(db, event_type="session_revoke", outcome="success", user_id=auth.user.id, device_id=auth.device.device_id, request=request, metadata={"session_id": session_id})
    db.commit()
    return ok(message="Sessão revogada.")


@router.post("/export/encrypted", dependencies=[Depends(rate_limit("auth:encrypted_export", 20, 3600))])
def encrypted_export(data: dict, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    ciphertext_b64 = clean_text(data.get("CiphertextB64") or data.get("ciphertext_b64"), 50_000_000)
    nonce_b64 = clean_text(data.get("NonceB64") or data.get("nonce_b64"), 128)
    manifest = data.get("Manifest") or data.get("manifest") or {}
    if not isinstance(manifest, dict):
        return fail("Manifest inválido.")
    if not validate_b64(ciphertext_b64, 50_000_000) or not validate_b64(nonce_b64, 128):
        return fail("Exportação cifrada inválida.")
    db.add(EncryptedExport(user_id=auth.user.id, device_id=auth.device.device_id, ciphertext_b64=ciphertext_b64, nonce_b64=nonce_b64, manifest=manifest))
    db.commit()
    return ok(message="Exportação cifrada guardada. A API recebeu apenas ciphertext/nonce/manifest.", status_code=201)




def _device_response(device: Device, current_device_id: str | None = None) -> dict:
    return {
        "device_id": device.device_id,
        "name": device.name or "Desktop",
        "platform": device.platform or "unknown",
        "is_trusted": bool(device.is_trusted),
        "is_current": bool(current_device_id and device.device_id == current_device_id),
        "created_at": device.created_at.isoformat() if device.created_at else "",
        "last_seen_at": device.last_seen_at.isoformat() if device.last_seen_at else "",
        "revoked_at": device.revoked_at.isoformat() if device.revoked_at else "",
    }


@router.get("/devices")
def list_my_devices(auth: CurrentAuth = Depends(require_auth), db: Session = Depends(get_db)):
    devices = db.query(Device).filter_by(user_id=auth.user.id).order_by(Device.revoked_at.asc().nullsfirst(), Device.created_at.desc()).all()
    return ok({"devices": [_device_response(device, auth.device.device_id) for device in devices]})


@router.post("/devices/rename")
def rename_device(data: dict, request: Request, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    device_id = clean_text(data.get("DeviceId") or data.get("device_id"), 64)
    name = clean_text(data.get("Name") or data.get("name"), 100).strip()
    if not device_id or not name:
        return fail("Device inválido.")
    device = db.query(Device).filter_by(user_id=auth.user.id, device_id=device_id).first()
    if not device:
        return fail("Device inválido.", 404)
    device.name = name
    record_security_event(db, event_type="device_rename", outcome="success", user_id=auth.user.id, device_id=device_id, request=request)
    db.commit()
    return ok({"device": _device_response(device, auth.device.device_id)}, "Device atualizado.")


@router.post("/devices/revoke")
async def revoke_device(data: dict, request: Request, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    device_id = clean_text(data.get("DeviceId") or data.get("device_id"), 64)
    if not device_id:
        return fail("Device inválido.")
    if device_id == auth.device.device_id:
        return fail("Usa logout para terminar a sessão do dispositivo atual.", 409)
    device = db.query(Device).filter_by(user_id=auth.user.id, device_id=device_id, revoked_at=None).first()
    if not device:
        return fail("Device inválido.", 404)
    device.revoked_at = utcnow()
    db.query(RefreshSession).filter_by(user_id=auth.user.id, device_id=device_id, revoked_at=None).update({"revoked_at": utcnow()}, synchronize_session=False)
    record_security_event(db, event_type="device_revoke", outcome="success", user_id=auth.user.id, device_id=device_id, request=request)
    db.commit()
    await connection_manager.force_disconnect(auth.user.id, code=4002, reason="device_revoked")
    return ok(message="Device revogado.")


@router.post("/devices/revoke-others")
async def revoke_other_devices(request: Request, auth: CurrentAuth = Depends(require_signed_auth), db: Session = Depends(get_db)):
    now = utcnow()
    revoked_count = db.query(Device).filter(
        Device.user_id == auth.user.id,
        Device.device_id != auth.device.device_id,
        Device.revoked_at.is_(None),
    ).update({"revoked_at": now}, synchronize_session=False)
    db.query(RefreshSession).filter(
        RefreshSession.user_id == auth.user.id,
        RefreshSession.device_id != auth.device.device_id,
        RefreshSession.revoked_at.is_(None),
    ).update({"revoked_at": now}, synchronize_session=False)
    record_security_event(db, event_type="device_revoke_others", outcome="success", user_id=auth.user.id, device_id=auth.device.device_id, request=request, metadata={"revoked_count": int(revoked_count or 0)})
    db.commit()
    await connection_manager.force_disconnect(auth.user.id, code=4002, reason="other_devices_revoked")
    return ok({"revoked_count": int(revoked_count or 0)}, "Outros devices revogados.")


@router.post("/validate-key")
def validate_key(data: dict, db: Session = Depends(get_db)):
    email = normalize_email(data.get("Email") or data.get("email"))
    public_key = clean_text(data.get("PublicKey") or data.get("public_key"), 4096)
    user = db.query(User).filter_by(email=email).first()
    return {"IsCurrent": bool(user and user.identity_public_key == public_key)}
