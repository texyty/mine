# MyCustomClient platform

Готовая платформа коммерческого Minecraft-лаунчера: FastAPI/PostgreSQL API, адаптивный web-кабинет, контент-сервер и WPF-приложение для Windows. В backend включены Alembic-миграции, поиск/пагинация и статистика админки; launcher автоматически проверяет состояние API, находит Java и ведёт локальный журнал. Сам Minecraft JAR намеренно не входит в репозиторий.

## Быстрый запуск сервера

1. Скопируйте `.env.example` в `.env` и замените все пароли и `JWT_SECRET`. Для секрета удобно использовать `python -c "import secrets; print(secrets.token_urlsafe(64))"`.
2. При публичном размещении выставьте HTTPS-домены в `CORS_ORIGINS`, а порты 8000/8080/8081 закройте reverse proxy (Caddy, nginx или ingress) с TLS.
3. Запустите `docker compose up -d --build`.
4. Web-интерфейс будет на `http://localhost:8080`, API/Swagger — `http://localhost:8000/docs`, файлы клиента — `http://localhost:8081`.

Bootstrap-admin создается лишь при первом запуске. После первого входа замените bootstrap-пароль в `.env`; существующий хеш автоматически не меняется. Регистрация по умолчанию создает пользователя без доступа, после чего администратор включает подписку в web-панели.

Контейнер backend перед каждым стартом выполняет `alembic upgrade head`. Для ручного применения миграций из `backend`: `alembic upgrade head`; новую ревизию создавайте командой `alembic revision --autogenerate -m "description"` и обязательно просматривайте сгенерированный SQL.

Для локального SQLite-запуска backend: `cd backend`, создайте venv, установите `pip install -r requirements-dev.txt`, скопируйте `.env.example` в `.env`, задайте `DATABASE_URL=sqlite+aiosqlite:///./launcher.db`, затем выполните `uvicorn app.main:app --reload`.

## Подготовка контента

Разместите относительно `content/public` следующие файлы:

```
versions/MyCustomClient/MyCustomClient.jar
versions/MyCustomClient/MyCustomClient.json
libs/.../*.jar
natives/...
assets/...
```

Сгенерируйте подписанный по хешам список (укажите настоящий main class будущего клиента):

```powershell
.\tools\generate_manifest.ps1 -ContentDirectory .\content\public -MainClass "com.example.client.Main"
```

Manifest обеспечивает контроль целостности при передаче, но для защиты от подмены самого manifest контент и API обязаны работать по HTTPS. Для повышенной защиты можно позднее добавить отдельную криптографическую подпись manifest публичным ключом, зашитым в launcher.

## Сборка лаунчера

Требуются Windows 10/11 и .NET 8 SDK. Отредактируйте `launcher/appsettings.json`: задайте HTTPS URL API/контента, измените application salt и при необходимости полный путь к `javaw.exe`. Затем:

```powershell
dotnet restore .\launcher\MyCustomLauncher.csproj
dotnet publish .\launcher\MyCustomLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

Результат находится в `launcher/bin/Release/net8.0-windows/win-x64/publish`. Токен «Запомнить меня» шифруется Windows DPAPI для текущего пользователя. Launcher передает будущему клиенту `--customToken`, который клиент должен отправить на `POST /api/launcher/session/validate` как Bearer token. Токен короткоживущий; offline здесь означает отсутствие Microsoft-аккаунта, а не обход серверной проверки платного доступа.

Если `JavaPath` оставлен как `javaw.exe`, launcher сначала проверяет `JAVA_HOME`, затем системный `PATH`. Диагностический журнал хранится в `%LOCALAPPDATA%\MyCustomLauncher\logs`; пароли и токены в него не записываются. Доступность API перепроверяется автоматически каждые 30 секунд.

## Проверки

Backend: из папки `backend` запустите `pytest -q`. Launcher: `dotnet build launcher/MyCustomLauncher.csproj -c Release`. Эти же проверки автоматически выполняет workflow `.github/workflows/ci.yml` на Windows и Linux runners.

## Эксплуатационная безопасность

- HWID не является абсолютной DRM-защитой: значения WMI можно подменить, а salt извлечь из бинарника. Решение использует HWID как привязку, а право запуска подтверждает серверным JWT.
- Не записывайте пароли, HWID и токены в логи. Ограничьте частоту `/login` на reverse proxy и включите мониторинг 401/403.
- JWT нельзя мгновенно отозвать до истечения срока, кроме отключения `has_access`, которое проверяется endpoint валидации. Для больших установок добавьте Redis-сессии/denylist и ротацию refresh token.
- Перед коммерческим запуском добавьте Alembic-миграции, резервное копирование PostgreSQL и интеграцию платежного провайдера через проверяемые webhooks.
"# mine" 
