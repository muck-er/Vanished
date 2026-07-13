from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


DEFAULT_CORS_ALLOWED_ORIGINS: tuple[str, ...] = (
    "https://vanished.pt",
    "https://www.vanished.pt",
    "https://api.vanished.pt",
)


def _split_csv(value: str) -> list[str]:
    return [item.strip().rstrip("/") for item in (value or "").split(",") if item.strip()]


class Settings(BaseSettings):
    model_config = SettingsConfigDict(extra="ignore")

    database_url: str = ""
    jwt_secret_key: str = ""
    cors_allowed_origins: str = ",".join(DEFAULT_CORS_ALLOWED_ORIGINS)
    redis_url: str = "redis://redis:6379/0"
    access_token_exp_minutes: int = 15
    refresh_token_exp_days: int = 14
    request_signature_max_skew_seconds: int = 120

    @property
    def cors_origins(self) -> list[str]:
        configured = _split_csv(self.cors_allowed_origins)
        allowed = set(DEFAULT_CORS_ALLOWED_ORIGINS)

        if not configured or "*" in configured:
            return list(DEFAULT_CORS_ALLOWED_ORIGINS)

        filtered = [origin for origin in configured if origin in allowed]
        return filtered or list(DEFAULT_CORS_ALLOWED_ORIGINS)


@lru_cache
def get_settings() -> Settings:
    return Settings()
