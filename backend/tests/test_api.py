import pytest
import pytest_asyncio
from pathlib import Path
from httpx import ASGITransport, AsyncClient

from app.main import app, bootstrap_database
from app.database import SessionLocal, engine
from app.models import User, UserRole
from sqlalchemy import select


@pytest_asyncio.fixture
async def client():
    await bootstrap_database()
    async with AsyncClient(transport=ASGITransport(app=app), base_url="http://test") as value:
        yield value
    await engine.dispose()
    Path("test_launcher_current.db").unlink(missing_ok=True)


@pytest.mark.asyncio
async def test_registration_login_and_hwid_binding(client):
    registration={"username":"player_one","email":"player@example.com","password":"a-very-safe-password"}
    assert (await client.post("/api/auth/register",json=registration)).status_code==201
    web=await client.post("/api/auth/login",json={"username":"player_one","password":registration["password"]})
    assert web.status_code==200
    admin_login=await client.post("/api/auth/login",json={"username":"admin","password":"very-secure-admin-password"})
    admin_headers={"Authorization":f"Bearer {admin_login.json()['access_token']}"}
    users=(await client.get("/api/admin/users",headers=admin_headers)).json()
    assert users["total"] >= 2 and users["limit"] == 50
    player=next(user for user in users["items"] if user["username"]=="player_one")
    assert (await client.patch(f"/api/admin/users/{player['id']}/access",json={"has_access":True},headers=admin_headers)).status_code==200
    first=await client.post("/api/launcher/login",json={"username":"player_one","password":registration["password"],"hwid":"a"*64})
    assert first.status_code==200 and first.json()["session_token"]
    mismatch=await client.post("/api/launcher/login",json={"username":"player_one","password":registration["password"],"hwid":"b"*64})
    assert mismatch.status_code==403 and mismatch.json()["detail"]=="Данный аккаунт привязан к другому ПК"
    validation=await client.post("/api/launcher/session/validate",headers={"Authorization":f"Bearer {first.json()['session_token']}"})
    assert validation.status_code==200 and validation.json()["valid"] is True


@pytest.mark.asyncio
async def test_admin_search_stats_and_health(client):
    login=await client.post("/api/auth/login",json={"username":"admin","password":"very-secure-admin-password"})
    headers={"Authorization":f"Bearer {login.json()['access_token']}"}
    search=await client.get("/api/admin/users?search=admin&limit=10",headers=headers)
    assert search.status_code==200
    assert search.json()["total"]==1 and search.json()["items"][0]["username"]=="admin"
    stats=await client.get("/api/admin/stats",headers=headers)
    assert stats.status_code==200
    assert stats.json()["administrators"]==1
    health=await client.get("/health")
    assert health.status_code==200 and health.json()["version"]=="1.2.0"


@pytest.mark.asyncio
async def test_admin_routes_reject_regular_user_and_validate_pagination(client):
    registration={"username":"ordinary_user","email":"ordinary@example.com","password":"another-safe-password"}
    assert (await client.post("/api/auth/register",json=registration)).status_code==201
    login=await client.post("/api/auth/login",json={"username":registration["username"],"password":registration["password"]})
    headers={"Authorization":f"Bearer {login.json()['access_token']}"}
    assert (await client.get("/api/admin/users",headers=headers)).status_code==403
    assert (await client.get("/api/admin/stats",headers=headers)).status_code==403

    admin=await client.post("/api/auth/login",json={"username":"admin","password":"very-secure-admin-password"})
    admin_headers={"Authorization":f"Bearer {admin.json()['access_token']}"}
    page=await client.get("/api/admin/users?offset=0&limit=1",headers=admin_headers)
    assert page.status_code==200 and len(page.json()["items"])==1 and page.json()["total"]==2
    assert (await client.get("/api/admin/users?limit=201",headers=admin_headers)).status_code==422
    assert (await client.patch("/api/admin/users/not-a-uuid/access",json={"has_access":True},headers=admin_headers)).status_code==400


@pytest.mark.asyncio
async def test_password_change_roles_and_bans(client):
    registration={"username":"managed_user","email":"managed@example.com","password":"managed-old-password"}
    assert (await client.post("/api/auth/register",json=registration)).status_code==201
    login=await client.post("/api/auth/login",json={"username":registration["username"],"password":registration["password"]})
    user_headers={"Authorization":f"Bearer {login.json()['access_token']}"}
    wrong=await client.post("/api/users/change-password",headers=user_headers,json={
        "current_password":"wrong-password","new_password":"managed-new-password","confirm_new_password":"managed-new-password"
    })
    assert wrong.status_code==400
    changed=await client.post("/api/users/change-password",headers=user_headers,json={
        "current_password":registration["password"],"new_password":"managed-new-password","confirm_new_password":"managed-new-password"
    })
    assert changed.status_code==200
    assert (await client.post("/api/auth/login",json={"username":"managed_user","password":"managed-new-password"})).status_code==200

    async with SessionLocal() as db:
        admin=await db.scalar(select(User).where(User.username=="admin"))
        admin.role=UserRole.creator
        await db.commit()
    creator_login=await client.post("/api/auth/login",json={"username":"admin","password":"very-secure-admin-password"})
    creator_headers={"Authorization":f"Bearer {creator_login.json()['access_token']}"}
    users=(await client.get("/api/admin/users",headers=creator_headers)).json()["items"]
    managed=next(user for user in users if user["username"]=="managed_user")
    promoted=await client.patch(f"/api/admin/users/{managed['id']}/role",headers=creator_headers,json={"role":"admin"})
    assert promoted.status_code==200 and promoted.json()["role"]=="admin"
    demoted=await client.patch(f"/api/admin/users/{managed['id']}/role",headers=creator_headers,json={"role":"user"})
    assert demoted.status_code==200
    banned=await client.patch(f"/api/admin/users/{managed['id']}/ban",headers=creator_headers,json={"is_banned":True,"reason":"test"})
    assert banned.status_code==200 and banned.json()["is_banned"] is True
    denied=await client.post("/api/auth/login",json={"username":"managed_user","password":"managed-new-password"})
    assert denied.status_code==403
