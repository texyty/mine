# Deploy: Vercel + Railway

Эта схема разделяет публичный сайт, API и игровые файлы:

| Компонент | Площадка | Исходная папка |
| --- | --- | --- |
| Сайт и кабинет | Vercel | `web-frontend` |
| FastAPI и PostgreSQL | Railway | `backend` |
| Minecraft-версии, библиотеки и assets | Railway (отдельный сервис) | `content` |

Сначала отправьте репозиторий в GitHub. Не добавляйте в Git секреты, `.env`, `content/public` с приватными игровыми файлами или собранный launcher.

## 1. API и база данных в Railway

1. Создайте новый Railway Project.
2. Добавьте сервис **PostgreSQL**.
3. Добавьте сервис из GitHub-репозитория. В настройках сервиса задайте **Root Directory**: `backend`. Railway возьмёт `backend/Dockerfile` и `backend/railway.toml` автоматически.
4. В Variables API-сервиса добавьте значения:

```text
DATABASE_URL=postgresql+asyncpg://${{Postgres.PGUSER}}:${{Postgres.PGPASSWORD}}@${{Postgres.PGHOST}}:${{Postgres.PGPORT}}/${{Postgres.PGDATABASE}}
ENVIRONMENT=production
APP_VERSION=1.1.0
JWT_SECRET=<случайная строка не короче 64 символов>
JWT_ISSUER=minecraft-launcher
WEB_TOKEN_MINUTES=60
LAUNCHER_TOKEN_MINUTES=15
CORS_ORIGINS=https://<ваш-проект>.vercel.app
BOOTSTRAP_ADMIN_USERNAME=admin
BOOTSTRAP_ADMIN_EMAIL=<ваш-email>
BOOTSTRAP_ADMIN_PASSWORD=<уникальный длинный пароль>
```

`Postgres` в ссылках выше — имя PostgreSQL-сервиса. Если вы его переименовали, замените это имя в выражениях Railway.

5. Откройте **Settings → Networking → Generate Domain** у API-сервиса. Получится адрес вида `https://<api-service>.up.railway.app`.
6. Убедитесь, что `https://<api-service>.up.railway.app/health` отвечает JSON с `"status":"ok"`.

Миграции Alembic применяются автоматически перед запуском FastAPI. После первого развёртывания войдите bootstrap-администратором и удалите либо замените переменную `BOOTSTRAP_ADMIN_PASSWORD`: созданная учётная запись уже не изменится автоматически.

## 2. Сайт в Vercel

1. В Vercel нажмите **Add New → Project**, импортируйте тот же GitHub-репозиторий.
2. В поле **Root Directory** укажите `web-frontend`.
3. Vercel сам использует `vercel.json`, `npm run build` и каталог `dist`.
4. Добавьте Environment Variable для окружений Production и Preview:

```text
LAUNCHER_API=https://<api-service>.up.railway.app
```

5. Нажмите Deploy.
6. Скопируйте URL Vercel в `CORS_ORIGINS` Railway API. Для кастомного домена добавьте оба адреса через запятую:

```text
CORS_ORIGINS=https://<ваш-проект>.vercel.app,https://example.com
```

7. Выполните Redeploy API-сервиса в Railway после изменения CORS.

`LAUNCHER_API` — публичный адрес, поэтому он намеренно встраивается в frontend-сборку. Никогда не помещайте туда `JWT_SECRET`, строку PostgreSQL или пароль администратора.

## 3. Файлы игрового клиента в Railway

1. В текущем Railway Project добавьте ещё один сервис из того же GitHub-репозитория.
2. Для него задайте **Root Directory**: `content`.
3. Сгенерируйте публичный домен сервиса.
4. Загрузите подготовленные файлы в `content/public` через GitHub или используйте Railway Volume, если файлы не должны попадать в Git. После обновления файлов создайте manifest:

```powershell
.\tools\generate_manifest.ps1 -ContentDirectory .\content\public -MainClass "com.example.client.Main"
```

5. В [launcher/appsettings.json](launcher/appsettings.json) задайте production-адреса:

```json
{
  "ApiBaseUrl": "https://<api-service>.up.railway.app",
  "ContentBaseUrl": "https://<content-service>.up.railway.app"
}
```

Для крупных архивов и частых обновлений лучше перенести `content/public` в Cloudflare R2 или S3 с CDN; Railway Content Service хорошо подходит для первого запуска и небольшого объёма.

## Проверка перед релизом

1. Откройте Vercel-домен и зарегистрируйте тестовую учётную запись.
2. Войдите bootstrap-администратором и активируйте ей доступ.
3. Проверьте `GET /health` на Railway API.
4. Соберите launcher с production URL и проверьте авторизацию, первичную HWID-привязку и скачивание manifest.

## Локальная проверка frontend production-сборки

```powershell
cd web-frontend
$env:LAUNCHER_API="https://example.up.railway.app"
npm run build
```

В `web-frontend/dist/runtime-config.js` должен появиться именно указанный API URL.
