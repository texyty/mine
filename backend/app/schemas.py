import uuid
from datetime import datetime

from pydantic import BaseModel, ConfigDict, EmailStr, Field, field_validator, model_validator

from .models import UserRole


class RegisterRequest(BaseModel):
    username: str = Field(min_length=3, max_length=32, pattern=r"^[A-Za-z0-9_]+$")
    email: EmailStr
    password: str = Field(min_length=10, max_length=128)

    @field_validator("username")
    @classmethod
    def normalize_username(cls, value: str) -> str:
        return value.strip().lower()


class LoginRequest(BaseModel):
    username: str
    password: str


class ChangePasswordRequest(BaseModel):
    current_password: str = Field(min_length=1, max_length=128)
    new_password: str = Field(min_length=10, max_length=128)
    confirm_new_password: str = Field(min_length=10, max_length=128)

    @model_validator(mode="after")
    def validate_passwords(self):
        if self.new_password != self.confirm_new_password:
            raise ValueError("Новые пароли не совпадают")
        if self.current_password == self.new_password:
            raise ValueError("Новый пароль должен отличаться от текущего")
        return self


class LauncherLoginRequest(LoginRequest):
    hwid: str = Field(pattern=r"^[a-fA-F0-9]{64}$")


class LauncherWebAuthStartRequest(BaseModel):
    hwid: str = Field(pattern=r"^[a-fA-F0-9]{64}$")


class LauncherWebAuthStartResponse(BaseModel):
    request_id: str
    expires_in: int


class LauncherWebAuthCompleteRequest(BaseModel):
    request_id: str = Field(min_length=20, max_length=128)


class LauncherWebAuthPollResponse(BaseModel):
    status: str
    session_token: str | None = None
    username: str | None = None
    detail: str | None = None


class TokenResponse(BaseModel):
    access_token: str
    token_type: str = "bearer"
    expires_in: int


class LauncherTokenResponse(BaseModel):
    session_token: str
    token_type: str = "bearer"
    expires_in: int
    username: str


class UserResponse(BaseModel):
    model_config = ConfigDict(from_attributes=True)
    id: uuid.UUID
    username: str
    email: EmailStr
    hwid_bound: bool
    has_access: bool
    role: UserRole
    is_banned: bool
    ban_reason: str | None
    created_at: datetime
    last_login_at: datetime | None

    @classmethod
    def from_user(cls, user):
        return cls(
            id=user.id, username=user.username, email=user.email,
            hwid_bound=user.hwid is not None, has_access=user.has_access,
            role=user.role, is_banned=user.is_banned, ban_reason=user.ban_reason,
            created_at=user.created_at, last_login_at=user.last_login_at,
        )


class HwidResetRequest(BaseModel):
    user_id: uuid.UUID


class AccessUpdateRequest(BaseModel):
    has_access: bool


class BanUpdateRequest(BaseModel):
    is_banned: bool
    reason: str | None = Field(default=None, max_length=300)


class RoleUpdateRequest(BaseModel):
    role: UserRole


class MessageResponse(BaseModel):
    message: str


class SessionValidationResponse(BaseModel):
    valid: bool
    username: str
    user_id: uuid.UUID


class UserPageResponse(BaseModel):
    items: list[UserResponse]
    total: int
    offset: int
    limit: int


class AdminStatsResponse(BaseModel):
    total_users: int
    active_users: int
    bound_devices: int
    administrators: int
    creators: int
    banned_users: int
