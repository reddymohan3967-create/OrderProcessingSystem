# Design Patterns Cheat-sheet

A concise one-page reference you can carry into an interview. Each entry lists where the pattern is used in the repo (relative paths / classes) and 2–3 talking bullets.

---

## Outbox Pattern
- Where: `src/OrderService/Entities/OutboxMessage.cs`, publisher in `src/OrderCreated/Worker.cs`, consumer validation in `src/OrderProcessor/OrderProcessingBatcher.cs`.
- What it solves: reliable, transactional publishes (eliminates the dual-write problem).
- Talking bullets:
  - Write message to DB in same transaction as state change, publish later from outbox worker.
  - Enables retries, durable ACKs and idempotent replays (see `RetryCount`, `PublishedAtUtc`).
  - Trade-offs: extra storage and complexity vs. much higher reliability across crashes.

---

## Producer–Consumer / Batcher
- Where: `src/OrderProcessor/OrderProcessingBatcher.cs`.
- What it solves: throughput and latency — batches work to reduce external calls and DB pressure.
- Talking bullets:
  - Uses `ConcurrentQueue<Guid>` + `SemaphoreSlim` to collect IDs and flush periodically or on signal.
  - Falls back to durable `PendingWork` rows in DB so queued work survives restarts.
  - Good for smoothing bursts and grouping DB updates into fewer transactions.

---

## Idempotent Consumer / Deduplication
- Where: `src/OrderProcessor/OrderCreatedConsumer.cs` (inserts `ProcessedMessage`), `src/OrderService/Entities/ProcessedMessage.cs`.
- What it solves: prevents duplicate processing when messages are redelivered.
- Talking bullets:
  - Use DB unique constraint on message id; handle unique-violation as 'already processed'.
  - Simple and reliable approach that avoids distributed transactions.

---

## Publish–Subscribe / Event-driven Architecture
- Where: `MassTransit` usage across consumers and publishers: `src/OrderProcessor/*`, `src/OrderCreated/*`; events `src/Shared.Contracts/Events/*.cs`.
- What it solves: decouples services so multiple independent consumers can react to events.
- Talking bullets:
  - Promotes loose coupling and horizontal scalability of consumers.
  - Design for eventual consistency and version events carefully.

---

## Background Worker / Hosted Service Pattern
- Where: `src/OrderCreated/Worker.cs`, `src/OrderProcessor/OrderStatusAdvancerService.cs`, `src/OrderProcessor/ProcessedMessagesCleanupService.cs`, plus `OrderProcessingBatcher` which runs background work.
- What it solves: runs recurring or long-running tasks separate from request flow.
- Talking bullets:
  - Use `BackgroundService`/host lifetime with cancellation tokens for graceful shutdown.
  - Useful for outbox publishing, cleanup and scheduled maintenance tasks.

---

## Unit of Work / Repository (EF Core)
- Where: `src/OrderService/Data/AppDbContext.cs` and usages in consumers/services.
- What it solves: groups related DB operations in a single transactional unit (`SaveChangesAsync`).
- Talking bullets:
  - Makes atomic updates easy (e.g., mark order + insert processed message + add pending work).
  - Facilitates rollback on failure and keeps data consistency boundaries clear.

---

## Concurrency Primitives & Thread-safety
- Where: `src/OrderProcessor/OrderProcessingBatcher.cs` (`ConcurrentQueue`, `SemaphoreSlim`, `CancellationTokenSource`).
- What it solves: safe multi-threaded enqueue/dequeue and efficient worker wakeups.
- Talking bullets:
  - Avoids heavy locking; signal worker only when necessary.
  - Discuss how to handle backpressure and capacity (not implemented but worth mentioning).

---

## Single Responsibility & Separation of Concerns
- Where: clear separation across `OrderCreatedConsumer`, `OrderProcessingBatcher`, `AppDbContext`, advancer/cleanup services.
- What it solves: each class has a focused responsibility.
- Talking bullets:
  - Improves testability and maintainability; easier to reason about failure boundaries.

---

## Quick end-to-end flow to describe in interview
1. Client creates order -> API writes order and an `OutboxMessage` in same transaction.
2. Outbox worker (`src/OrderCreated/Worker.cs`) publishes events to broker.
3. Consumer (`src/OrderProcessor/OrderCreatedConsumer.cs`) records a `ProcessedMessage` and creates `PendingWork` for durable enqueue.
4. `OrderProcessingBatcher` picks up pending work (in-memory or from DB), validates outbox publication, transitions order to `Processing`, and publishes `OrderStatusUpdatedEvent`.

---

Notes / Preparation tips
- Be ready to mention trade-offs: complexity and storage vs. reliability and recoverability.
- Explain how idempotency and outbox together provide practical exactly-once semantics for this architecture.
- If asked about alternatives, contrast with distributed transactions, broker-side exactly-once and why outbox + idempotency is commonly chosen.

---

File created in project: `docs/DesignPatterns_CheatSheet.md`

Good luck with the interview — if you want I can also export this to PDF or shorten it to a one-card summary.
