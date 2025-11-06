# Notification Service

## Overview

The Notification Service sends notifications to both the car service shop and the customer when a booking saga completes successfully. It handles email notifications and manages notification state through event sourcing.

## Purpose

This service is responsible for:
- Sending notifications to the car service shop
- Sending notifications to the customer
- Managing notification state through event sourcing
- Tracking notification completion to determine when all notifications are sent
- Publishing notification events to RabbitMQ

## Role in Saga

The Notification Service is the **final step** in the saga flow:
1. Receives notification requests from Saga Orchestrator (after rental succeeds)
2. Sends shop notification
3. Sends customer notification
4. Publishes completion event when both notifications are sent

## API Endpoints

- `GET /` - Service health check
- `GET /health` - Health status
- `POST /api/notifications` - Send notification (standalone endpoint)

## Events Subscribed To

The service subscribes to the following events from RabbitMQ:

- `saga.notification.requested` - Notification request from orchestrator

## Events Published

The service publishes the following events to RabbitMQ:

- `notification.sent` - Individual notification sent successfully
- `notification.failed` - Individual notification failed (non-critical)
- `notifications.completed` - All notifications completed (both shop and customer)

## Business Logic

### Notification Processing

- Validates recipient email address
- Sends shop notification with booking details
- Sends customer notification with booking details, price, and rental information
- Simulates occasional failures (5% chance) - non-critical, doesn't break saga
- Tracks notification count per saga (thread-safe)

### Completion Logic

- Counts both successful and failed notifications towards completion
- Publishes `notifications.completed` when both notifications are processed
- Uses thread-safe locking to prevent duplicate completion events

## Database

- **Database**: `notificationdb` (PostgreSQL)
- **Purpose**: Event store for notification events
- **Schema**: Events table with `event_id`, `aggregate_id`, `event_type`, `event_data`, `timestamp`, `version`

## Configuration

### Environment Variables

- `ConnectionStrings__DefaultConnection` - PostgreSQL connection string
- `RabbitMQ__HostName` - RabbitMQ hostname
- `RabbitMQ__Port` - RabbitMQ port
- `RabbitMQ__UserName` - RabbitMQ username
- `RabbitMQ__Password` - RabbitMQ password

## Technology Stack

- **.NET 9**
- **ASP.NET Core Minimal APIs**
- **PostgreSQL** (Event Store)
- **RabbitMQ** (Message Broker)
- **Npgsql** (PostgreSQL driver)

## Running the Service

### With Docker Compose

The service is automatically started with Docker Compose:

```bash
docker compose up -d notification-service
```

### Standalone

```bash
cd src/NotificationService
dotnet run
```

The service will be available at `http://localhost:5004` (or port 8080 in Docker).

## Dependencies

- PostgreSQL database (notificationdb)
- RabbitMQ message broker

## Demo Delays

For demo purposes, the service includes a 1-second delay per notification (2 seconds total for both shop and customer notifications) to make the saga flow more visible during tech talks.

## Important Notes

- **Non-Critical Failures**: Notification failures are non-critical and don't break the saga. If a notification fails, it's logged but the saga continues.
- **Thread Safety**: The service uses thread-safe locking to prevent race conditions when tracking notification counts.
- **Completion Logic**: Both successful and failed notifications count towards completion to ensure the saga progresses even if one notification fails.

