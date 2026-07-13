import os

import uvicorn


def _bool_env(name: str, default: bool = False) -> bool:
    value = os.getenv(name)
    if value is None:
        return default
    return value.strip().lower() in {"1", "true", "yes", "on"}


if __name__ == "__main__":
    uvicorn.run(
        "main:app",
        host=os.getenv("VANISHED_API_HOST", "127.0.0.1"),
        port=int(os.getenv("PORT", "5000")),
        reload=_bool_env("VANISHED_RELOAD"),
    )
