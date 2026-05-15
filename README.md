
# OrderProcessingSystem

Comprehensive example of an order processing pipeline implemented with .NET 10. The solution demonstrates:

- Transactional event publishing using the Outbox pattern
- Message delivery with MassTransit and RabbitMQ
- Simple Web API for order management
- Background workers for publishing, processing and notification
- Local SQLite persistence for easy development

Contents

- `src/OrderService` — ASP.NET Core Web API: create orders, update status, list orders. Implements the outbox pattern and optional in-app outbox publisher.
- `src/OrderCreated` — Publisher-only worker that publishes outbox messages to RabbitMQ (alternative to running publisher inside `OrderService`).
- `src/OrderProcessor` — Background services/consumers that process created orders and advance status.
- `src/NotificationService` — Example service that demonstrates sending notifications (e.g., email) when events occur.
- `src/Shared.Contracts` — Shared DTOs and event definitions used across services.

Key concepts

- Outbox pattern: events are written to an `OutboxMessages` table inside the same transaction that mutates the domain state. A separate worker publishes pending outbox rows to the message broker and marks them as published.
- MassTransit + RabbitMQ: MassTransit is used as the messaging library and RabbitMQ as the transport. Queue names and credentials are configurable.
- Rate limiting: the API uses ASP.NET Core rate limiting middleware (per-IP fixed window). Settings are configurable.

Prerequisites

- .NET 10 SDK
- RabbitMQ (for full end-to-end testing) — optional to exercise local DB and worker behaviour
- Docker (optional, for MailHog / smtp4dev)

Getting started

1. Build the solution:

   dotnet build

2. Run the API (`OrderService`):

   dotnet run --project src/OrderService

   The API uses an SQLite DB by default. See the DB section below for the location.

3. Run a publisher/worker (optional):

   - You can run the outbox publisher from within `OrderService` (it can host the worker), or run `OrderCreated` as a dedicated publisher:
     dotnet run --project src/OrderCreated

4. Run consumers / processors:

   dotnet run --project src/OrderProcessor

Configuration

- RabbitMQ
  - Configure RabbitMQ via `appsettings.*.json` or environment variables.
  - Keys:
    - `RabbitMq:Host` (default: `localhost`)
    - `RabbitMq:Username` (default: `guest`)
    - `RabbitMq:Password` (default: `guest`)
    - `RabbitMq:Queue` - queue for `OrderCreatedEvent` (can be overridden by env var `RABBITMQ_QUEUE`)
    - `RabbitMq:QueueStatus` - queue for `OrderStatusUpdatedEvent` (can be overridden by env var `RABBITMQ_QUEUE_STATUS`)

- Rate Limiting
  - Defaults are set in `OrderService`:
    - `PermitLimit` = 600 (requests per window)
    - `WindowSeconds` = 60
  - Override in configuration under `RateLimiting` (or via environment variables):

    ```json
    "RateLimiting": {
      "PermitLimit": 1200,
      "WindowSeconds": 60
    }
    ```

- Database (SQLite)
  - Projects use SQLite for local development. By default the DB is resolved relative to each project's content root so a project-local `data/orders.db` can be used.
  - The `OrderService` is responsible for creating/preparing the DB (it will create or copy the project-local DB into the configured location).
  - For production, prefer a managed RDBMS (Postgres, SQL Server) and secure connection strings.

Events and outbox

- `OrderService` writes `OrderCreatedEvent` and `OrderStatusUpdatedEvent` into the `OutboxMessages` table when actions occur.
- A background worker (hosted either in `OrderService` or `OrderCreated`) reads pending outbox messages (`PublishedAtUtc == null`) and publishes them to RabbitMQ using MassTransit. On success the worker sets `PublishedAtUtc` so messages are not re-sent.

Local email testing

- Use MailHog or smtp4dev for local SMTP sink testing. Example (MailHog):

  docker run -d -p 1025:1025 -p 8025:8025 mailhog/mailhog

  Then set NotificationService SMTP config to `localhost:1025` and open MailHog UI at `http://localhost:8025`.

Testing

- Unit tests are located under `test/`. Run with:

  dotnet test

Development notes

- The outbox worker includes reasonable defaults (poll interval, batch size) but can be tuned in `src/OrderService/Worker.cs` or `src/OrderCreated/Worker.cs`.
- If you want a clean separation, run the publisher as a separate process (`OrderCreated`) and keep the API strictly for HTTP endpoints.

Contributing

- Contributions welcome. Open issues or pull requests describing changes or improvements.

License

- This repository is provided for demonstration purposes. Add a LICENSE file if you wish to publish under a specific license.

Support

- If you need help running the project, open an issue and include OS, .NET SDK version and any error logs.

