from functools import lru_cache

from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    environment: str = "development"
    app_version: str = "1.2.0"
    database_url: str = "sqlite+aiosqlite:///./launcher.db"
    jwt_secret: str = "development-only-change-this-secret"
    jwt_issuer: str = "minecraft-launcher"
    web_token_minutes: int = 60
    launcher_token_minutes: int = 15
    cors_origins: str = "https://mine-web-ten.vercel.app,https://nursultan.fun,http://localhost:8080,http://localhost:5173"
    bootstrap_admin_username: str | None = None
    bootstrap_admin_email: str | None = None
    bootstrap_admin_password: str | None = None
    admin_page_size: int = 50

    model_config = SettingsConfigDict(env_file=".env", extra="ignore")

@lru_cache
def get_settings() -> Settings:
    return Settings()
