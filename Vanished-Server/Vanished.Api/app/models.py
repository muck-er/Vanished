from datetime import datetime, timezone
from sqlalchemy import UniqueConstraint, Index
from sqlalchemy.dialects.postgresql import JSONB
from app.extensions import db


def utcnow():
    return datetime.now(timezone.utc)


class User(db.Model):
    __tablename__ = "users"

    id = db.Column(db.Integer, primary_key=True)
    username = db.Column(db.String(50), nullable=False, unique=True, index=True)
    full_name = db.Column(db.String(100), nullable=False, default="")
    email = db.Column(db.String(254), nullable=False, unique=True, index=True)
    identity_public_key = db.Column(db.Text, nullable=False)
    key_version = db.Column(db.Integer, nullable=False, default=1)
    recovery_key_hash = db.Column(db.String(128), nullable=False, unique=True, index=True)
    avatar_blob = db.Column(db.LargeBinary, nullable=True)
    avatar_mime = db.Column(db.String(100), nullable=True)
    bio = db.Column(db.String(160), nullable=False, default="")
    mfa_totp_enabled = db.Column(db.Boolean, nullable=False, default=False)
    account_pin_hash = db.Column(db.String(128), nullable=True)
    account_pin_salt = db.Column(db.String(64), nullable=True)
    account_pin_failed_attempts = db.Column(db.Integer, nullable=False, default=0)
    account_pin_locked_until = db.Column(db.DateTime(timezone=True), nullable=True)
    account_pin_updated_at = db.Column(db.DateTime(timezone=True), nullable=True)
    created_at = db.Column(db.DateTime(timezone=True), nullable=False, default=utcnow)
    last_seen_at = db.Column(db.DateTime(timezone=True), nullable=True)

    devices = db.relationship("Device", back_populates="user", cascade="all, delete-orphan")


class Device(db.Model):
    __tablename__ = "devices"

    id = db.Column(db.Integer, primary_key=True)
    user_id = db.Column(db.Integer, db.ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True)
    device_id = db.Column(db.String(64), nullable=False)
    public_key = db.Column(db.Text, nullable=False)  # Ed25519 signing public key
    encryption_public_key = db.Column(db.Text, nullable=False, default="")  # X25519 public key for per-device envelopes
    name = db.Column(db.String(100), nullable=False, default="Desktop")
    platform = db.Column(db.String(200), nullable=False, default="unknown")
    is_trusted = db.Column(db.Boolean, nullable=False, default=True)
    created_at = db.Column(db.DateTime(timezone=True), nullable=False, default=utcnow)
    last_seen_at = db.Column(db.DateTime(timezone=True), nullable=True)
    revoked_at = db.Column(db.DateTime(timezone=True), nullable=True)

    user = db.relationship("User", back_populates="devices")
    __table_args__ = (UniqueConstraint("user_id", "device_id", name="uq_device_user_device_id"),)


class AuthChallenge(db.Model):
    __tablename__ = "auth_challenges"

    id = db.Column(db.String(64), primary_key=True)
    user_id = db.Column(db.Integer, db.ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True)
    device_id = db.Column(db.String(64), nullable=False)
    server_nonce = db.Column(db.String(128), nullable=False)
    purpose = db.Column(db.String(50), nullable=False, default="login")
    expires_at = db.Column(db.DateTime(timezone=True), nullable=False)
    used_at = db.Column(db.DateTime(timezone=True), nullable=True)
    created_at = db.Column(db.DateTime(timezone=True), nullable=False, default=utcnow)




class EmailVerificationChallenge(db.Model):
    __tablename__ = "email_verification_challenges"

    id = db.Column(db.String(64), primary_key=True)
    email = db.Column(db.String(254), nullable=False, index=True)
    purpose = db.Column(db.String(50), nullable=False, default="registration")
    code_hash = db.Column(db.String(128), nullable=False)
    code_salt = db.Column(db.String(64), nullable=False)
    attempts = db.Column(db.Integer, nullable=False, default=0)
    expires_at = db.Column(db.DateTime(timezone=True), nullable=False)
    verified_at = db.Column(db.DateTime(timezone=True), nullable=True)
    used_at = db.Column(db.DateTime(timezone=True), nullable=True)
    user_id = db.Column(db.Integer, db.ForeignKey("users.id", ondelete="SET NULL"), nullable=True, index=True)
    created_at = db.Column(db.DateTime(timezone=True), nullable=False, default=utcnow)
    sent_at = db.Column(db.DateTime(timezone=True), nullable=True)

    __table_args__ = (
        Index("ix_email_verification_email_active", "email", "purpose", "used_at", "expires_at"),
    )
class RefreshSession(db.Model):
    __tablename__ = "refresh_sessions"

    id = db.Column(db.Integer, primary_key=True)
    user_id = db.Column(db.Integer, db.ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True)
    device_id = db.Column(db.String(64), nullable=False, index=True)
    family_id = db.Column(db.String(64), nullable=False, index=True)
    token_hash = db.Column(db.String(128), nullable=False, unique=True, index=True)
    replaced_by_hash = db.Column(db.String(128), nullable=True)
    expires_at = db.Column(db.DateTime(timezone=True), nullable=False)
    revoked_at = db.Column(db.DateTime(timezone=True), nullable=True)
    created_at = db.Column(db.DateTime(timezone=True), nullable=False, default=utcnow)


class KeyEnvelope(db.Model):
    __tablename__ = "key_envelopes"

    id = db.Column(db.Integer, primary_key=True)
    user_id = db.Column(db.Integer, db.ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True)
    device_id = db.Column(db.String(64), nullable=False, index=True)
    envelope_type = db.Column(db.String(50), nullable=False)
    ciphertext_b64 = db.Column(db.Text, nullable=False)
    nonce_b64 = db.Column(db.String(64), nullable=False)
    kdf = db.Column(JSONB, nullable=False, default=dict)
    created_at = db.Column(db.DateTime(timezone=True), nullable=False, default=utcnow)
    __table_args__ = (UniqueConstraint("user_id", "device_id", "envelope_type", name="uq_envelope_user_device_type"),)


class MessageThread(db.Model):
    __tablename__ = "message_threads"

    id = db.Column(db.BigInteger, primary_key=True)
    user_low_id = db.Column(db.Integer, db.ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True)
    user_high_id = db.Column(db.Integer, db.ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True)
    initiator_id = db.Column(db.Integer, db.ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True)
    recipient_id = db.Column(db.Integer, db.ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True)
    status = db.Column(db.String(20), nullable=False, default="pending")
    created_at = db.Column(db.DateTime(timezone=True), nullable=False, default=utcnow)
    updated_at = db.Column(db.DateTime(timezone=True), nullable=False, default=utcnow)
    accepted_at = db.Column(db.DateTime(timezone=True), nullable=True)
    rejected_at = db.Column(db.DateTime(timezone=True), nullable=True)

    __table_args__ = (
        UniqueConstraint("user_low_id", "user_high_id", name="uq_message_thread_pair"),
        Index("idx_message_thread_recipient_status", "recipient_id", "status"),
    )

class PrivateMessage(db.Model):
    __tablename__ = "private_messages"

    id = db.Column(db.BigInteger, primary_key=True)
    thread_id = db.Column(db.BigInteger, db.ForeignKey("message_threads.id", ondelete="SET NULL"), nullable=True, index=True)
    sender_id = db.Column(db.Integer, db.ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True)
    recipient_id = db.Column(db.Integer, db.ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True)
    sender_device_id = db.Column(db.String(64), nullable=False)
    recipient_device_id = db.Column(db.String(64), nullable=True)
    eph_pub_b64 = db.Column(db.Text, nullable=False)
    nonce_b64 = db.Column(db.String(64), nullable=False)
    ciphertext_b64 = db.Column(db.Text, nullable=False)
    sender_eph_pub_b64 = db.Column(db.Text, nullable=True)
    sender_nonce_b64 = db.Column(db.String(64), nullable=True)
    sender_ciphertext_b64 = db.Column(db.Text, nullable=True)
    client_msg_id = db.Column(db.String(64), nullable=True)
    created_at = db.Column(db.DateTime(timezone=True), nullable=False, default=utcnow, index=True)
    read_at = db.Column(db.DateTime(timezone=True), nullable=True)
    deleted_for_everyone_at = db.Column(db.DateTime(timezone=True), nullable=True)

    __table_args__ = (
        UniqueConstraint("sender_id", "client_msg_id", name="uq_pm_sender_client"),
        Index("idx_pm_pair_id", "sender_id", "recipient_id", "id"),
    )


class DeletedMessage(db.Model):
    __tablename__ = "deleted_messages"
    id = db.Column(db.BigInteger, primary_key=True)
    user_id = db.Column(db.Integer, db.ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True)
    message_id = db.Column(db.BigInteger, db.ForeignKey("private_messages.id", ondelete="CASCADE"), nullable=False, index=True)
    created_at = db.Column(db.DateTime(timezone=True), nullable=False, default=utcnow)
    __table_args__ = (UniqueConstraint("user_id", "message_id", name="uq_deleted_messages_pair"),)


class UserBlock(db.Model):
    __tablename__ = "user_blocks"
    id = db.Column(db.BigInteger, primary_key=True)
    blocker_id = db.Column(db.Integer, db.ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True)
    blocked_id = db.Column(db.Integer, db.ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True)
    created_at = db.Column(db.DateTime(timezone=True), nullable=False, default=utcnow)
    __table_args__ = (UniqueConstraint("blocker_id", "blocked_id", name="uq_user_blocks_pair"),)


class EncryptedExport(db.Model):
    __tablename__ = "encrypted_exports"
    id = db.Column(db.Integer, primary_key=True)
    user_id = db.Column(db.Integer, db.ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True)
    device_id = db.Column(db.String(64), nullable=False)
    ciphertext_b64 = db.Column(db.Text, nullable=False)
    nonce_b64 = db.Column(db.String(64), nullable=False)
    manifest = db.Column(JSONB, nullable=False, default=dict)
    created_at = db.Column(db.DateTime(timezone=True), nullable=False, default=utcnow)




class SecurityAuditEvent(db.Model):
    __tablename__ = "security_audit_events"

    id = db.Column(db.BigInteger, primary_key=True)
    event_type = db.Column(db.String(80), nullable=False, index=True)
    outcome = db.Column(db.String(20), nullable=False, index=True)
    user_id = db.Column(db.Integer, db.ForeignKey("users.id", ondelete="SET NULL"), nullable=True, index=True)
    device_id = db.Column(db.String(64), nullable=True)
    subject_hash = db.Column(db.String(64), nullable=True, index=True)
    ip_hash = db.Column(db.String(64), nullable=True, index=True)
    user_agent_hash = db.Column(db.String(64), nullable=True)
    event_metadata = db.Column("metadata", JSONB, nullable=False, default=dict)
    created_at = db.Column(db.DateTime(timezone=True), nullable=False, default=utcnow, index=True)

    __table_args__ = (
        Index("idx_security_audit_events_user_created", "user_id", "created_at"),
        Index("idx_security_audit_events_type_created", "event_type", "created_at"),
    )


class ContactIdentityVerification(db.Model):
    __tablename__ = "contact_identity_verifications"

    id = db.Column(db.BigInteger, primary_key=True)
    user_id = db.Column(db.Integer, db.ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True)
    peer_id = db.Column(db.Integer, db.ForeignKey("users.id", ondelete="CASCADE"), nullable=False, index=True)
    peer_key_version = db.Column(db.Integer, nullable=False)
    peer_identity_fingerprint = db.Column(db.String(255), nullable=False)
    verified_at = db.Column(db.DateTime(timezone=True), nullable=False, default=utcnow)
    revoked_at = db.Column(db.DateTime(timezone=True), nullable=True)

    __table_args__ = (
        UniqueConstraint("user_id", "peer_id", name="uq_contact_identity_verification_pair"),
        Index("idx_contact_identity_verification_user_peer", "user_id", "peer_id"),
    )
