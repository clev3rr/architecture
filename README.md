# Лабораторна робота 6

Розподілена система для обміну зашифрованими повідомленнями. Сервер ASP.NET Core та консольний клієнт C# використовують спільну бібліотеку криптографії. RSA-3072 OAEP-SHA256 передає параметри сесії; AES-256-GCM шифрує повідомлення та відповіді.

Автор: Малютін Максим, КС-31. Дисципліна: «Архітектура, адміністрування та безпека інформаційних мереж».

## Швидкий запуск у Windows

З кореня LAB6 у першому вікні PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\start-server.ps1
```

У другому вікні:

```powershell
powershell -ExecutionPolicy Bypass -File .\start-client.ps1
```

Введіть повідомлення. `/new` відкриває нову сесію для наступного повідомлення, `/exit` завершує роботу. Порожній рядок також є повідомленням. Клієнт показує ID RSA, ID сесії, порядковий номер, шифротекст, розшифровану відповідь і час приймання сервером. Сервер може запускатися кілька секунд: спочатку він створює 10 пар RSA.

Локальна копія SDK міститься в `.tools/dotnet` і автоматично використовується скриптами. На іншому комп'ютері потрібно встановити .NET SDK 10; SDK і результати збірки до Git не включаються.

## Команди .NET

```powershell
dotnet restore Lab6.slnx
dotnet build Lab6.slnx -c Release
dotnet test Lab6.slnx -c Release
dotnet run --project src/Lab6.Server --urls http://localhost:5080
dotnet run --project src/Lab6.Client -- http://localhost:5080
```

У підготовленій локальній папці замість `dotnet` можна використовувати `./scripts/dotnet.ps1`. Для тестів є `run-tests.ps1`.

Якщо Windows Application Control блокує завантаження зібраної DLL під час тестів (0x800711C7), можна виконати ту саму збірку й усі тести в Linux-контейнері:

```powershell
powershell -ExecutionPolicy Bypass -File .\run-tests.ps1 -Docker
```

Або `docker build --target tests -t lab6-tests .`. Цей режим потребує запущеного Docker Desktop. Політику Windows змінювати не потрібно.

## Docker

```powershell
docker compose up --build -d
docker compose logs server
docker compose down
```

Або:

```powershell
docker build -t lab6-server .
docker run --rm -p 5080:8080 lab6-server
```

Клієнт запускається на хості. Порт сервера у контейнері - 8080, зовні - 5080. Фінальний образ використовує користувача без root-прав. Перезапуск сервера очищає ключі та сесії, оскільки сховище розташоване в пам'яті.

## Структура

```text
src/Lab6.Crypto/     IRsaService, IAesService, реалізації, спільні DTO
src/Lab6.Server/     HTTP API, сховище ключів та сесій
src/Lab6.Client/     SecureChannel і консольний інтерфейс
tests/Lab6.Tests/    unit та інтеграційні тести
docs/               звіт, протокол перевірки, OpenAPI
.github/workflows/   GitHub Actions: restore, build, tests, Docker build
```

## HTTP API

OpenAPI: [http://localhost:5080/openapi/v1.json](http://localhost:5080/openapi/v1.json). Це машинозчитувана специфікація OpenAPI; її можна імпортувати в Postman або Swagger Editor. Обов'язкові заголовки описані як параметри операцій.

| Метод | Шлях | Результат |
| --- | --- | --- |
| GET | `/health` | 200, стан сервера |
| POST | `/api/crypto-keys/generate/rsa-keys` | 200, integer ID; 400, помилка генерації |
| GET | `/api/crypto-keys/rsa-public-key/{id}` | 200, JSON-рядок Base64; 404, ключ відсутній |
| GET | `/api/crypto-keys/rsa-public-key` | 200, випадковий ключ, заголовок відповіді `x-rsa-id` |
| POST | `/api/sessions` | 201, ID та термін сесії; 400/404/409 |
| POST | `/api/messages` | 200, зашифрована відповідь; 400/404/409/410 |

Створення сесії потребує заголовків `x-rsa-id` (integer), `x-session-id` (UUID). Передавання повідомлення потребує `x-session-id`. Тіло обох POST-запитів:

```json
{"cipherText":"Base64..."}
```

Публічний ключ: DER SubjectPublicKeyInfo у Base64. Приватний ключ: DER PKCS#8 у Base64, доступний лише всередині сервера. Додаткових API для видачі приватних ключів або відкритого AES-ключа немає.

## Протокол

1. Сервер під час запуску генерує 10 пар RSA й зберігає їх за integer ID.
2. Після введення першого повідомлення клієнт отримує випадковий публічний ключ разом із `x-rsa-id`.
3. Клієнт створює AES-ключ (32 байти), базовий IV (12 байтів) та UUID сесії.
4. JSON `{sessionId, aesKey: {key, iv}}` шифрується RSA OAEP-SHA256 і надсилається на `/api/sessions` з обома заголовками.
5. Сервер розшифровує пакет приватним ключем, звіряє ID сесії, зберігає AES-параметри на 30 хвилин і повертає підтвердження.
6. Клієнт шифрує JSON `{sessionId, sequence, text}` AES-GCM та надсилає його на `/api/messages`.
7. Сервер перевіряє GCM-тег, ID сесії та послідовність, додає `receivedAt` у UTC і шифрує відповідь `{sessionId, sequence, text, receivedAt}`.
8. Клієнт розшифровує відповідь, звіряє сесію та номер повідомлення, показує результат і пропонує ввести наступний текст.

Наступні повідомлення використовують ту саму сесію зі зростанням `sequence`. `/new` або спливання 30 хвилин призводить до нового handshake.

## Криптографічний формат

AES-пакет: `Base64(nonce[12] || tag[16] || ciphertext[N])`.

Базовий `IV` генерується для сесії. Для кожного шифрування `nonce = random[12] XOR IV`; це зберігає рівномірний випадковий розподіл та дає новий nonce для запитів і відповідей. IV також входить до додатково автентифікованих даних GCM. Фактичний nonce передається всередині пакета. Однаковий відкритий текст дає різні шифротексти. Тег GCM відхиляє зміну nonce, шифротексту чи параметрів сесії.

RSA шифрує тільки короткі параметри сесії. Межа OAEP-SHA256 для RSA-3072: `384 - 2*32 - 2 = 318` байтів, перевіряється до шифрування. Текст повідомлень має межу 16000 UTF-16 code units; HTTP-тіло обмежене 128 KiB. Шифрування виконує `System.Security.Cryptography`.

Це навчальний протокол із PDF. Початковий RSA-ключ передається через HTTP без перевірки автентичності сервера, тому активний посередник може його підмінити. Для реального застосування потрібен HTTPS із перевіркою сертифіката. GCM забезпечує цілісність зашифрованого пакета, а не автентичність початкового публічного ключа. Втрата відповіді після приймання запиту потребує нової сесії; автоматичного повтору клієнт не робить.

## Перевірка

40 тестів: RSA/AES round trip для Unicode, порожнього й довгого тексту; формати ключів; межа OAEP; незалежність ключів і nonce; зміна nonce/тега/шифротексту; сторонній ключ; HTTP handshake; кілька повідомлень та клієнтів; заголовки; 400/404/409/410; повторний пакет; прив'язка повідомлення до сесії; OpenAPI. Інтеграційні тести запускають справжній ASP.NET Core pipeline через `WebApplicationFactory` і використовують реальний клас клієнта.

Результати фактичного запуску та приклад обміну зберігаються в `docs/verification.md`, `docs/client-demo.txt`, `docs/openapi.json`. Тести не потребують Docker.

## GitHub Actions

Workflow `.github/workflows/ci.yml` запускається для push і pull request. Він збирає всі проєкти, виконує тести, зберігає TRX як artifact та збирає Docker-образ. Поточний стан запусків: [Actions](https://github.com/clev3rr/architecture/actions).

## Матеріали для захисту

**Навіщо два алгоритми?** RSA дозволяє передати секрет без попереднього обміну AES-ключем. AES підходить для багаторазового шифрування великих повідомлень.

**Чим Base64 відрізняється від шифрування?** Base64 лише кодує байти для JSON. Конфіденційність забезпечують RSA та AES.

**Навіщо IV і nonce?** Повтор nonce з одним ключем у GCM порушує безпеку. Тут кожне шифрування створює новий випадковий nonce.

**Чому потрібні обидва заголовки?** `x-rsa-id` визначає приватний ключ для handshake; `x-session-id` визначає AES-параметри для повідомлення.

**Навіщо sequence?** Він відхиляє повтор уже прийнятого пакета. ID сесії всередині зашифрованого JSON захищає від перенесення пакета між сесіями.

**Чому це не TLS?** Протокол не автентифікує початковий ключ сервера та не має механізму forward secrecy.

## Джерела

- Методичні вказівки «Лабораторна робота №6. Розробка розподіленої системи, що дозволяє встановити шифрований комунікаційний канал», ХНУ імені В. Н. Каразіна.
- [System.Security.Cryptography](https://learn.microsoft.com/en-us/dotnet/standard/security/cryptography-model)
- [ASP.NET Core integration tests](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0)
- [ASP.NET Core OpenAPI](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview?view=aspnetcore-10.0)
