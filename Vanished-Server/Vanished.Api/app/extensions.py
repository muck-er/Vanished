from __future__ import annotations

from sqlalchemy import (
    BigInteger,
    Boolean,
    Column,
    DateTime,
    ForeignKey,
    Integer,
    LargeBinary,
    String,
    Text,
    create_engine,
)
from sqlalchemy.orm import DeclarativeBase, scoped_session, sessionmaker, relationship


class Base(DeclarativeBase):
    pass


class _DbFacade:
    Model = Base
    Column = Column
    Integer = Integer
    BigInteger = BigInteger
    String = String
    Text = Text
    Boolean = Boolean
    DateTime = DateTime
    ForeignKey = ForeignKey
    LargeBinary = LargeBinary
    relationship = staticmethod(relationship)

    def __init__(self) -> None:
        self.engine = None
        self.SessionLocal = None
        self.session = None

    def configure(self, database_url: str) -> None:
        if self.engine is not None:
            return
        if not database_url:
            raise RuntimeError("DATABASE_URL não definida no .env")
        self.engine = create_engine(database_url, pool_size=20, max_overflow=10, pool_pre_ping=True, pool_recycle=3600, future=True)
        self.SessionLocal = sessionmaker(bind=self.engine, autocommit=False, autoflush=False, future=True)
        self.session = scoped_session(self.SessionLocal)

    def create_all(self) -> None:
        if self.engine is None:
            raise RuntimeError("db.configure(database_url) deve ser chamado antes de create_all().")
        Base.metadata.create_all(bind=self.engine)

    def remove(self) -> None:
        if self.session is not None:
            self.session.remove()


db = _DbFacade()


class _LimiterNoop:
    def limit(self, *_args, **_kwargs):
        def decorator(fn):
            return fn
        return decorator


limiter = _LimiterNoop()
