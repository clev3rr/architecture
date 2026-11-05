# Протокол перевірки LAB6

Дата: 05.11.2026 (Europe/Tallinn).

## Автоматичні перевірки

- GitHub Actions, Ubuntu: 40 passed, 0 failed, 0 skipped.
- Перевірений коміт: ab2efac1b30bee63ad053f92afd1e9dcf6e65499.
- CI: https://github.com/clev3rr/architecture/actions/runs/42097979820
- Артефакт цього запуску: tests-ci.trx, завантажений із GitHub Actions.
- Локальний Linux-контейнер: `docker build --target tests -t lab6-tests .` — 40 passed, 0 failed, 0 skipped.
- `dotnet list package --vulnerable --include-transitive`: вразливих пакетів у жодному з 4 проєктів не знайдено.

Початкові локальні запуски у Windows завершилися 40/40. Під час повторного запуску після перебудови Windows Application Control заблокував завантаження Lab6.Client.dll у 4 інтеграційних тестах (0x800711C7). Політику Windows не змінено. Повна перевірка актуального коду успішно виконана в GitHub Actions і локальному Docker. Команда для відтворення: `run-tests.ps1 -Docker`.

## Перевірка окремих процесів

Сервер запущено на http://127.0.0.1:5080, клієнт — окремим процесом .NET. Перевірено Unicode із символом замка, друге повідомлення з тим самим UUID та sequence=2, команду /new та новий UUID із sequence=1. Відповіді містять початковий текст і серверний час UTC. Повний вивід: client-demo.txt.

## Docker

- `docker build -t lab6-server .` — успішно.
- Окремий контейнер запущено на 127.0.0.1:5081 -> 8080.
- GET /health — 200, `{"status":"ok"}`.
- Повний RSA/AES обмін консольного клієнта з контейнером — успішно; вивід у docker-demo.txt.
- `docker exec lab6-verification id` — uid=1654(app), gid=1654(app).
- Контейнер після перевірки зупинено.

## OpenAPI

GET /openapi/v1.json — 200. OpenAPI 3.1.1, 6 маршрутів. Для /api/sessions описані обов’язкові x-rsa-id та x-session-id; для /api/messages — x-session-id. Перевірену специфікацію збережено в openapi.json.

## Звіт

## Перевірка скриптів запуску у Windows після виправлення

Для обох застосунків встановлено `UseAppHost=false`. Сервер запущено командою `powershell -ExecutionPolicy Bypass -File .\start-server.ps1`, клієнт — командою `powershell -ExecutionPolicy Bypass -File .\start-client.ps1`. Сервер слухав `http://localhost:5080`; `/health` повернув `{"status":"ok"}`. Клієнт надіслав два повідомлення в одній сесії (sequence 1 і 2), після `/new` відкрив нову сесію (sequence 1), отримав усі відповіді та завершився з кодом 0. Повний фактичний вивід міститься в `windows-launch-check.txt`.

## Звіт

PDF містить 8 сторінок: титульна, вимоги й архітектура, RSA/AES, діаграма обміну, API, результати перевірок, Docker/CI, висновки та джерела. Усі сторінки відрендерено й перевірено візуально.
