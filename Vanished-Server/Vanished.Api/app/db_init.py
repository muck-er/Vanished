from __future__ import annotations

import os
import sys
import time
from pathlib import Path

import psycopg2
from psycopg2 import OperationalError


ROOT = Path(__file__).resolve().parents[1]
SCHEMA_FILE = ROOT / "db" / "schema.sql"


def _database_dsn() -> str:
    url = os.getenv("DATABASE_URL", "").strip()
    if not url:
        raise RuntimeError("DATABASE_URL não definida no ambiente")

    # SQLAlchemy accepts postgresql+psycopg2://..., but psycopg2 expects postgresql://...
    return url.replace("postgresql+psycopg2://", "postgresql://", 1)


def wait_for_database(dsn: str, timeout_seconds: int = 60) -> None:
    deadline = time.time() + timeout_seconds
    last_error: Exception | None = None

    while time.time() < deadline:
        try:
            with psycopg2.connect(dsn) as conn:
                with conn.cursor() as cur:
                    cur.execute("SELECT 1")
                    cur.fetchone()
            return
        except OperationalError as exc:
            last_error = exc
            print("[db-init] PostgreSQL ainda não está pronto. A tentar novamente...", flush=True)
            time.sleep(2)

    raise RuntimeError(f"PostgreSQL não ficou pronto em {timeout_seconds}s: {last_error}")


def apply_schema(dsn: str) -> None:
    if not SCHEMA_FILE.exists():
        raise FileNotFoundError(f"Schema SQL não encontrado: {SCHEMA_FILE}")

    sql = SCHEMA_FILE.read_text(encoding="utf-8")
    with psycopg2.connect(dsn) as conn:
        conn.autocommit = False
        with conn.cursor() as cur:
            cur.execute(sql)
            cur.execute("SELECT COUNT(*) FROM pg_indexes WHERE schemaname = 'public'")
            index_count = cur.fetchone()[0]
            cur.execute(
                """
                SELECT indexname
                FROM pg_indexes
                WHERE schemaname = 'public'
                  AND (tablename IN ('users','devices','auth_challenges','refresh_sessions')
                       OR indexname LIKE 'ix_auth_%')
                ORDER BY indexname
                """
            )
            auth_indexes = [row[0] for row in cur.fetchall()]
        conn.commit()
    print(f"[db-init] Índices disponíveis no schema public: {index_count}", flush=True)
    print("[db-init] Índices auth/devices:", ", ".join(auth_indexes) or "nenhum", flush=True)


def main() -> int:
    dsn = _database_dsn()
    wait_for_database(dsn)
    apply_schema(dsn)
    print("[db-init] Schema PostgreSQL garantido.", flush=True)
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as exc:
        print(f"[db-init] ERRO: {exc}", file=sys.stderr, flush=True)
        raise
