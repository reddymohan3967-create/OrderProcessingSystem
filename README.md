# OrderProcessingSystem - Local Development README

This repository contains multiple services that share a common SQLite database (`Orders.db`). To make local development and demos convenient, the code resolves a single shared DB path used by the API and background workers.

Default behavior
- By default the shared database is placed under the machine-wide ProgramData folder:
  - Windows: `%PROGRAMDATA%\OrderProcessing\Orders.db` (e.g. `C:\ProgramData\OrderProcessing\Orders.db`)
- The `OrderService` (the API) is the authoritative creator/preparer of the database. On startup it will copy any project-local `orders.db` into the shared location (backing up an existing publish DB with a timestamp) or create the database using EF Core migrations if it does not exist.
- Other services (`OrderProcessor`, `OrderCreated`, etc.) will use the same shared path but will not create or modify the DB. They log a warning if the DB is missing.
 - Other services (`OrderProcessor`, `OrderCreated`, etc.) will use the same shared path but will not create or modify the DB. They log a warning if the DB is missing. Start `OrderService` first so it can prepare/create the DB.

Override the shared DB path
You can override the shared DB location in three ways (ordered by precedence):

1. Environment variable `ORDERS_DB_PATH`
   - Set this environment variable to the desired full path to `Orders.db`.
   - Example (PowerShell):
     ```powershell
     $env:ORDERS_DB_PATH = "C:\Users\You\data\Orders.db"
     dotnet run --project .\OrderService\
     ```
   - Visual Studio launch profiles in `Properties/launchSettings.json` can include `ORDERS_DB_PATH` as an environment variable.

2. App configuration `SharedDb:Path`
   - Add the following to `appsettings.json` for a project to set a shared path (absolute):
     ```json
     "SharedDb": {
       "Path": "C:\\path\\to\\Orders.db"
     }
     ```
   - This will be respected by the DB resolver.

3. Default (no override)
   - Uses `%PROGRAMDATA%\OrderProcessing\Orders.db`.

Requirement: Launch OrderService first
- The `OrderService` is responsible for preparing/creating the shared DB when required. Start the API first so it can create the database and apply EF Core migrations.
- Once `OrderService` is running, start `OrderProcessor`, `OrderCreated`, or other worker services — they will use the prepared shared DB.

Notes
- The resolver backs up any existing shared DB before copying a project-local DB into the shared location (timestamped `.bak`).
- If copy fails during prepare, the process will log an error and fail fast to avoid inconsistent state.
- If you prefer a per-user DB instead of machine-wide, set `SharedDb:Path` or `ORDERS_DB_PATH` to a per-user location (for example under `%LOCALAPPDATA%`).

Troubleshooting
- If other services log a warning that the shared DB is missing, ensure `OrderService` has been started and that it created the DB at the resolved path.
- To inspect the exact DB path each service uses, check the application console output — the resolver logs the resolved path on startup.

Security and production
- For production deployments, prefer a proper database server (Postgres, SQL Server) instead of SQLite and configure connection strings via secure configuration.

Local email testing with MailHog (recommended)
---------------------------------------------

For local development it's recommended to use a local SMTP sink instead of sending real emails via Gmail. MailHog is simple and easy to run via Docker:

1. Run MailHog with Docker:

```powershell
docker run -d -p 1025:1025 -p 8025:8025 mailhog/mailhog
```

2. Configure the NotificationService to use MailHog. You can set environment variables (recommended) or user-secrets in the `NotificationService` project folder.

PowerShell (current session):

```powershell
$env:Smtp__Host = 'localhost'
$env:Smtp__Port = '1025'
$env:Smtp__UseStartTls = 'false'
$env:Smtp__From = 'dev@example.com'
dotnet run --project .\NotificationService\NotificationService.csproj
```

3. Open MailHog UI at http://localhost:8025 to inspect sent messages.

Alternatives: `smtp4dev` (desktop app or Docker) provides similar functionality and UI.


