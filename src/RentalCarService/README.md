# Rental Car Service

## Overview

The Rental Car Service handles automatic rental car bookings for customers who have booked car service appointments. It processes rental requests, checks car availability, and manages rental state through event sourcing.

## Purpose

This service is responsible for:
- Booking rental cars for customers
- Validating car availability and date ranges
- Managing rental state through event sourcing
- Handling rental compensation (cancellation) when saga fails
- Publishing rental events to RabbitMQ

## Role in Saga

The Rental Car Service is the **third step** in the saga flow:
1. Receives rental request from Saga Orchestrator (after payment succeeds)
2. Checks car availability
3. Books rental car and publishes success/failure event
4. Handles compensation requests to cancel rentals

## API Endpoints

- `GET /` - Service health check
- `GET /health` - Health status
- `POST /api/rentals` - Book a rental car (standalone endpoint)

## Events Subscribed To

The service subscribes to the following events from RabbitMQ:

- `saga.rental.requested` - Rental request from orchestrator
- `saga.rental.compensate` - Compensation request to cancel rental

## Events Published

The service publishes the following events to RabbitMQ:

- `rental.car.booked` - Rental completed successfully
- `rental.failed` - Rental failed (no cars available, invalid dates, etc.)
- `rental.compensated` - Rental compensation (cancellation) completed

## Business Logic

### Rental Booking

- Validates date range (end date must be after start date)
- Checks car availability (simplified - in production would check database)
- Simulates failures for demo scenarios:
  - No cars available
  - Invalid date range
  - Timeout scenarios

### Compensation

When compensation is requested:
- Cancels the rental
- Publishes `rental.compensated` event
- Handles cases where rental doesn't exist gracefully

## Database

- **Database**: `rentaldb` (PostgreSQL)
- **Purpose**: Event store for rental events
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
docker compose up -d rental-car-service
```

### Standalone

```bash
cd src/RentalCarService
dotnet run
```

The service will be available at `http://localhost:5003` (or port 8080 in Docker).

## Dependencies

- PostgreSQL database (rentaldb)
- RabbitMQ message broker

## Demo Delays

For demo purposes, the service includes a 2-second delay when processing rentals and a 1.5-second delay when processing compensation to make the saga flow more visible during tech talks.

