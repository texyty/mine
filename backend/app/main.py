from contextlib import asynccontextmanager
from datetime import datetime, timezone, timedelta
import secrets
import uuid

from fastapi import Depends, FastAPI, HTTPException, Query, status
from fastapi.middleware.cors import CORSMiddleware
from fastapi.security import HTTPAuthorizationCredentials
from sqlalchemy import func, or_, select, text
from sqlalchemy.exc import IntegrityError
from sqlalchemy.ext.asyncio import AsyncSession

from .config import get_settings
from .database import Base, SessionLocal, engine, get_db
from .models import User, UserRole
from .schemas import (AccessUpdateRequest, HwidResetRequest, LauncherLoginRequest,
                      LauncherTokenResponse, LauncherWebAuthStartRequest, LauncherWebAuthStartResponse,
                      LauncherWebAuthCompleteRequest, LauncherWebAuthPollResponse, LoginRequest, MessageResponse,
                      RegisterRequest, SessionValidationResponse, TokenResponse,
                      UserResponse, UserPageResponse, AdminStatsResponse)
from .security import (admin_user, bearer, create_token, current_user, decode_token,
                       hash_password, verify_password)


async def bootstrap_database() -> None:
    settings = get_settings()
    async with engine.begin() as connection:
        await connection.run_sync(Base.metadata.create_all)
    if not all((settings.bootstrap_admin_username, settings.bootstrap_admin_email, settings.bootstrap_admin_password)):
        return
    async with SessionLocal() as db:
        existing = await db.scalar(select(User).where(User.username == settings.bootstrap_admin_username.lower()))
        if existing is None:
            db.add(User(username=settings.bootstrap_admin_username.lower(), email=settings.bootstrap_admin_email.lower(),
                        password_hash=hash_password(settings.bootstrap_admin_password), role=UserRole.admin,
                        has_access=True))
            await db.commit()


@asynccontextmanager
async def lifespan(_: FastAPI):
    await bootstrap_database()
    yield


settings = get_settings()
app = FastAPI(title="Minecraft Launcher API", version=settings.app_version, lifespan=lifespan)
app.add_middleware(CORSMiddleware, allow_origins=[item.strip() for item in settings.cors_origins.split(",") if item.strip()], allow_credentials=False,
                   allow_methods=["GET", "POST", "PATCH"], allow_headers=["Authorization", "Content-Type"])

# Short-lived bridge between an already authenticated browser and the desktop launcher.
# Entries are deliberately one-time and never persisted with users or passwords.
launcher_web_requests: dict[str, dict] = {}


def expire_launcher_web_requests() -> None:
    now = datetime.now(timezone.utc)
    for request_id, item in list(launcher_web_requests.items()):
        if item["expires_at"] <= now:
            launcher_web_requests.pop(request_id, None)


@app.get("/health")
async def health():
    async with SessionLocal() as db:
        await db.execute(text("SELECT 1"))
    return {"status": "ok", "version": settings.app_version, "environment": settings.environment}


@app.post("/api/auth/register", response_model=UserResponse, status_code=201)
async def register(body: RegisterRequest, db: AsyncSession = Depends(get_db)):
    email = body.email.lower()
    duplicate = await db.scalar(select(User).where(or_(User.username == body.username, User.email == email)))
    if duplicate:
        raise HTTPException(status_code=409, detail="Имя пользователя или email уже заняты")
    user = User(username=body.username, email=email, password_hash=hash_password(body.password))
    db.add(user)
    try:
        await db.commit()
    except IntegrityError as exc:
        await db.rollback()
        raise HTTPException(status_code=409, detail="Имя пользователя или email уже заняты") from exc
    await db.refresh(user)
    return UserResponse.from_user(user)


@app.post("/api/auth/login", response_model=TokenResponse)
async def web_login(body: LoginRequest, db: AsyncSession = Depends(get_db)):
    user = await db.scalar(select(User).where(User.username == body.username.strip().lower()))
    if user is None or not verify_password(body.password, user.password_hash):
        raise HTTPException(status_code=401, detail="Неверный логин или пароль")
    user.last_login_at = datetime.now(timezone.utc)
    await db.commit()
    token, expires = create_token(user, "web", settings.web_token_minutes)
    return TokenResponse(access_token=token, expires_in=expires)


@app.post("/api/launcher/login", response_model=LauncherTokenResponse)
async def launcher_login(body: LauncherLoginRequest, db: AsyncSession = Depends(get_db)):
    user = await db.scalar(select(User).where(User.username == body.username.strip().lower()).with_for_update())
    if user is None or not verify_password(body.password, user.password_hash):
        raise HTTPException(status_code=401, detail="Неверный логин или пароль")
    if not user.has_access:
        raise HTTPException(status_code=403, detail="Нет активной подписки")
    normalized_hwid = body.hwid.lower()
    if user.hwid is None:
        user.hwid = normalized_hwid
    elif user.hwid != normalized_hwid:
        raise HTTPException(status_code=403, detail="Данный аккаунт привязан к другому ПК")
    user.last_login_at = datetime.now(timezone.utc)
    await db.commit()
    token, expires = create_token(user, "launcher", settings.launcher_token_minutes)
    return LauncherTokenResponse(session_token=token, expires_in=expires, username=user.username)


@app.post("/api/launcher/web-auth/start", response_model=LauncherWebAuthStartResponse)
async def launcher_web_auth_start(body: LauncherWebAuthStartRequest):
    expire_launcher_web_requests()
    request_id = secrets.token_urlsafe(32)
    launcher_web_requests[request_id] = {
        "hwid": body.hwid.lower(),
        "expires_at": datetime.now(timezone.utc) + timedelta(minutes=3),
        "status": "pending",
    }
    return LauncherWebAuthStartResponse(request_id=request_id, expires_in=180)


@app.post("/api/launcher/web-auth/complete", response_model=MessageResponse)
async def launcher_web_auth_complete(body: LauncherWebAuthCompleteRequest, user: User = Depends(current_user),
                                     db: AsyncSession = Depends(get_db)):
    expire_launcher_web_requests()
    item = launcher_web_requests.get(body.request_id)
    if item is None:
        raise HTTPException(status_code=404, detail="Запрос входа не найден или истёк")
    if not user.has_access:
        item.update(status="denied", detail="Нет активной подписки")
        return MessageResponse(message="Доступ к лаунчеру не активен")
    if user.hwid is None:
        user.hwid = item["hwid"]
    elif user.hwid != item["hwid"]:
        item.update(status="denied", detail="Данный аккаунт привязан к другому ПК")
        return MessageResponse(message="Аккаунт привязан к другому ПК")
    user.last_login_at = datetime.now(timezone.utc)
    await db.commit()
    token, _ = create_token(user, "launcher", settings.launcher_token_minutes)
    item.update(status="approved", session_token=token, username=user.username)
    return MessageResponse(message="Вход в лаунчер подтверждён")


@app.get("/api/launcher/web-auth/{request_id}", response_model=LauncherWebAuthPollResponse)
async def launcher_web_auth_poll(request_id: str):
    expire_launcher_web_requests()
    item = launcher_web_requests.get(request_id)
    if item is None:
        return LauncherWebAuthPollResponse(status="expired", detail="Время ожидания истекло")
    response = LauncherWebAuthPollResponse(status=item["status"], detail=item.get("detail"))
    if item["status"] == "approved":
        response.session_token = item["session_token"]
        response.username = item["username"]
        launcher_web_requests.pop(request_id, None)
    return response


@app.get("/api/users/me", response_model=UserResponse)
async def me(user: User = Depends(current_user)):
    return UserResponse.from_user(user)


@app.get("/api/admin/users", response_model=UserPageResponse)
async def list_users(
    _: User = Depends(admin_user), db: AsyncSession = Depends(get_db),
    search: str = Query(default="", max_length=100), offset: int = Query(default=0, ge=0),
    limit: int = Query(default=50, ge=1, le=200),
):
    filters = []
    if value := search.strip().lower():
        escaped = value.replace("%", r"\%").replace("_", r"\_")
        filters.append(or_(User.username.ilike(f"%{escaped}%", escape="\\"), User.email.ilike(f"%{escaped}%", escape="\\")))
    query = select(User)
    count_query = select(func.count()).select_from(User)
    if filters:
        query = query.where(*filters)
        count_query = count_query.where(*filters)
    total = int(await db.scalar(count_query) or 0)
    users = (await db.scalars(query.order_by(User.created_at.desc()).offset(offset).limit(limit))).all()
    return UserPageResponse(items=[UserResponse.from_user(user) for user in users], total=total, offset=offset, limit=limit)


@app.get("/api/admin/stats", response_model=AdminStatsResponse)
async def admin_stats(_: User = Depends(admin_user), db: AsyncSession = Depends(get_db)):
    total = int(await db.scalar(select(func.count()).select_from(User)) or 0)
    active = int(await db.scalar(select(func.count()).select_from(User).where(User.has_access.is_(True))) or 0)
    bound = int(await db.scalar(select(func.count()).select_from(User).where(User.hwid.is_not(None))) or 0)
    admins = int(await db.scalar(select(func.count()).select_from(User).where(User.role == UserRole.admin)) or 0)
    return AdminStatsResponse(total_users=total, active_users=active, bound_devices=bound, administrators=admins)


@app.post("/api/admin/hwid-reset", response_model=MessageResponse)
async def reset_hwid(body: HwidResetRequest, _: User = Depends(admin_user), db: AsyncSession = Depends(get_db)):
    user = await db.get(User, body.user_id)
    if user is None:
        raise HTTPException(status_code=404, detail="Пользователь не найден")
    user.hwid = None
    await db.commit()
    return MessageResponse(message="HWID успешно сброшен")


@app.patch("/api/admin/users/{user_id}/access", response_model=UserResponse)
async def update_access(user_id: str, body: AccessUpdateRequest, _: User = Depends(admin_user),
                        db: AsyncSession = Depends(get_db)):
    try:
        parsed_user_id = uuid.UUID(user_id)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail="Некорректный ID пользователя") from exc
    user = await db.get(User, parsed_user_id)
    if user is None:
        raise HTTPException(status_code=404, detail="Пользователь не найден")
    user.has_access = body.has_access
    await db.commit()
    await db.refresh(user)
    return UserResponse.from_user(user)


@app.post("/api/launcher/session/validate", response_model=SessionValidationResponse)
async def validate_launcher_session(credentials: HTTPAuthorizationCredentials | None = Depends(bearer),
                                    db: AsyncSession = Depends(get_db)):
    if credentials is None:
        raise HTTPException(status_code=401, detail="Требуется токен сессии")
    payload = decode_token(credentials.credentials, "launcher")
    try:
        user_id = uuid.UUID(payload["sub"])
    except (KeyError, ValueError) as exc:
        raise HTTPException(status_code=401, detail="Недействительный токен") from exc
    user = await db.get(User, user_id)
    if user is None or not user.has_access:
        raise HTTPException(status_code=403, detail="Сессия отозвана или подписка неактивна")
    return SessionValidationResponse(valid=True, username=user.username, user_id=user.id)
