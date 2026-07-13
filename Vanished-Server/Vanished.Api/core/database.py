from __future__ import annotations

import os
from collections.abc import Generator

from sqlalchemy.orm import Session

from app.extensions import db
from core.config import get_settings


def init_database() -> None:
    settings = get_settings()
    db.configure(settings.database_url)
    import app.models  # noqa
    if os.getenv("AUTO_CREATE_SCHEMA", "false").lower() in {"1", "true", "yes"}:
        db.create_all()


def get_db() -> Generator[Session, None, None]:
    if db.SessionLocal is None:
        init_database()
    session = db.SessionLocal()
    try:
        yield session
    finally:
        session.close()
