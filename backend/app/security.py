import uuid
from datetime import datetime, timedelta, timezone

import jwt
from argon2 import PasswordHasher
from argon2.exceptions import InvalidHashError, VerificationError
from fastapi import Depends, HTTPException, status
from fastapi.security import HTTPAuthorizationCredentials, HTTPBearer
from sqlalchemy.ext.asyncio import AsyncSession

from .config import get_settings
from .database import get_db
from .models import User, UserRole

password_hasher = PasswordHasher()
bearer = HTTPBearer(auto_error=False)


def hash_password(password: str) -> str:
    return password_hasher.hash(password)


def verify_password(password: str, encoded: str) -> bool:
    try:
        return password_hasher.verify(encoded, password)
    except (VerificationError, InvalidHashError):
        return False


def create_token(user: User, purpose: str, minutes: int) -> tuple[str, int]:
    settings = get_settings()
    now = datetime.now(timezone.utc)
    expires = now + timedelta(minutes=minutes)
    payload = {
        "sub": str(user.id), "username": user.username, "role": user.role.value,
        "purpose": purpose, "iat": now, "exp": expires, "iss": settings.jwt_issuer,
        "jti": str(uuid.uuid4()),
    }
    return jwt.encode(payload, settings.jwt_secret, algorithm="HS256"), int((expires - now).total_seconds())


def decode_token(token: str, purpose: str | None = None) -> dict:
    settings = get_settings()
    try:
        payload = jwt.decode(token, settings.jwt_secret, algorithms=["HS256"], issuer=settings.jwt_issuer)
    except jwt.PyJWTError as exc:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Недействительный или истекший токен") from exc
    if purpose is not None and payload.get("purpose") != purpose:
        raise HTTPException(status_code=status.HTTP_401_UNAUTHORIZED, detail="Неверный тип токена")
    return payload


async def current_user(
    credentials: HTTPAuthorizationCredentials | None = Depends(bearer),
    db: AsyncSession = Depends(get_db),
) -> User:
    if credentials is None:
        raise HTTPException(status_code=401, detail="Требуется авторизация")
    payload = decode_token(credentials.credentials, "web")
    try:
        user_id = uuid.UUID(payload["sub"])
    except (KeyError, ValueError) as exc:
        raise HTTPException(status_code=401, detail="Недействительный токен") from exc
    user = await db.get(User, user_id)
    if user is None:
        raise HTTPException(status_code=401, detail="Пользователь не найден")
    return user


async def admin_user(user: User = Depends(current_user)) -> User:
    if user.role != UserRole.admin:
        raise HTTPException(status_code=403, detail="Требуются права администратора")
    return user

