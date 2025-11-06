# Saga Orchestrator Service

## Overview

The Saga Orchestrator is the central coordination service that manages the entire car service booking saga. It orchestrates the flow between multiple services, handles state transitions, and manages compensation logic when failures occur.

## Purpose

This service implements the **Saga Orchestrator Pattern**, which coordinates a distributed transaction across multiple microservices. It ensures that either all steps complete successfully, or all completed steps are compensated (rolled back) in reverse order.

## Saga Flow

The orchestrator manages the following saga flow:

1. **Start** → Triggers booking request
2. **Booking** → Waits for booking completion/failure
3. **Payment** → Waits for payment completion/failure
4. **Rental** → Waits for rental car booking completion/failure
5. **Notifications** → Waits for all notifications to complete
6. **Complete** → Saga successfully completed

If any step fails, the orchestrator triggers compensation in reverse order:
- Rental failed → Compensate Payment → Compensate Booking → Failed
- Payment failed → Compensate Booking → Failed
- Booking failed → Failed

## Key Responsibilities

- **State Management**: Tracks saga state and transitions
- **Event Coordination**: Publishes commands and subscribes to events from other services
- **Compensation Logic**: Triggers compensation when failures occur
- **Event Sourcing**: Stores all saga events in PostgreSQL for state reconstruction
- **Recovery**: Provides manual recovery endpoints for stuck sagas

## API Endpoints

### Core Endpoints

- `GET /` - Service health check
- `GET /health` - Health status
- `POST /api/saga/start` - Start a new saga
- `GET /api/saga/{sagaId}` - Get saga status
- `GET /api/saga` - Get all sagas
- `GET /api/saga/{sagaId}/events` - Get all events for a saga

### Recovery Endpoints

- `POST /api/saga/{sagaId}/recover-payment` - Manually recover stuck payment
- `POST /api/saga/{sagaId}/complete-notifications` - Manually complete stuck notifications

### Demo Endpoints

- `POST /api/saga/demo/success` - Start success scenario
- `POST /api/saga/demo/booking-failure` - Start booking failure scenario
- `POST /api/saga/demo/payment-failure` - Start payment failure scenario
- `POST /api/saga/demo/rental-failure` - Start rental failure scenario
- `POST /api/saga/demo/timeout` - Start timeout scenario

## Events Published

The orchestrator publishes the following events to RabbitMQ:

- `saga.booking.requested` - Request booking from Booking Service
- `saga.payment.requested` - Request payment from Payment Service
- `saga.rental.requested` - Request rental from Rental Car Service
- `saga.notification.requested` - Request notification from Notification Service
- `saga.booking.compensate` - Request booking compensation
- `saga.payment.compensate` - Request payment compensation
- `saga.rental.compensate` - Request rental compensation
- `saga.completed` - Saga completed successfully
- `saga.failed` - Saga failed

## Events Subscribed To

The orchestrator subscribes to the following events from RabbitMQ:

- `booking.timeslot.booked` - Booking completed
- `booking.failed` - Booking failed
- `payment.processed` - Payment completed
- `payment.failed` - Payment failed
- `rental.car.booked` - Rental completed
- `rental.failed` - Rental failed
- `booking.compensated` - Booking compensation completed
- `payment.compensated` - Payment compensation completed
- `notifications.completed` - All notifications completed

## Database

- **Database**: `orchestratordb` (PostgreSQL)
- **Purpose**: Event store for saga events
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
docker compose up -d saga-orchestrator
```

### Standalone

```bash
cd src/SagaOrchestrator
dotnet run
```

The service will be available at `http://localhost:5010` (or port 8080 in Docker).

## Dependencies

- PostgreSQL database (orchestratordb)
- RabbitMQ message broker
- All other saga services (Booking, Payment, Rental, Notification)

