# Payment Service

## Overview

The Payment Service processes payments for car service bookings. It handles payment processing, validates payment information, and manages payment state through event sourcing.

## Purpose

This service is responsible for:
- Processing payments for car service bookings
- Validating payment information
- Managing payment state through event sourcing
- Handling payment compensation (refunds) when saga fails
- Publishing payment events to RabbitMQ

## Role in Saga

The Payment Service is the **second step** in the saga flow:
1. Receives payment request from Saga Orchestrator (after booking succeeds)
2. Processes payment
3. Publishes success/failure event
4. Handles compensation requests to refund payments

## API Endpoints

- `GET /` - Service health check
- `GET /health` - Health status
- `POST /api/payments` - Process payment (standalone endpoint)

## Events Subscribed To

The service subscribes to the following events from RabbitMQ:

- `saga.payment.requested` - Payment request from orchestrator
- `saga.payment.compensate` - Compensation request to refund payment

## Events Published

The service publishes the following events to RabbitMQ:

- `payment.processed` - Payment completed successfully
- `payment.failed` - Payment failed (insufficient funds, card declined, etc.)
- `payment.compensated` - Payment compensation (refund) completed

## Business Logic

### Payment Processing

- Validates payment amount
- Simulates payment processing
- Simulates failures for demo scenarios:
  - Insufficient funds
  - Card declined
  - Timeout scenarios
  - Random 10% failure chance

### Compensation

When compensation is requested:
- Refunds the payment
- Publishes `payment.compensated` event
- Handles cases where payment doesn't exist gracefully

## Database

- **Database**: `paymentdb` (PostgreSQL)
- **Purpose**: Event store for payment events
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
docker compose up -d payment-service
```

### Standalone

```bash
cd src/PaymentService
dotnet run
```

The service will be available at `http://localhost:5002` (or port 8080 in Docker).

## Dependencies

- PostgreSQL database (paymentdb)
- RabbitMQ message broker

## Demo Delays

For demo purposes, the service includes a 2-second delay when processing payments and a 1.5-second delay when processing compensation to make the saga flow more visible during tech talks.

