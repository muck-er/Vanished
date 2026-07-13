from app.extensions import db


def ensure_schema() -> bool:
    db.create_all()
    return True
