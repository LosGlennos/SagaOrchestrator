# Booking Service

## Overview

The Booking Service handles time slot bookings for car service appointments. It processes booking requests, validates time slots, and manages booking state through event sourcing.

## Purpose

This service is responsible for:
- Validating and booking time slots for car service
- Managing booking state through event sourcing
- Handling booking compensation (cancellation) when saga fails
- Publishing booking events to RabbitMQ

## Role in Saga

The Booking Service is the **first step** in the saga flow:
1. Receives booking request from Saga Orchestrator
2. Validates time slot availability
3. Creates booking and publishes success/failure event
4. Handles compensation requests to cancel bookings

## API Endpoints

- `GET /` - Service health check
- `GET /health` - Health status
- `POST /api/bookings` - Book a time slot (standalone endpoint)

## Events Subscribed To

The service subscribes to the following events from RabbitMQ:

- `saga.booking.requested` - Booking request from orchestrator
- `saga.booking.compensate` - Compensation request to cancel booking

## Events Published

The service publishes the following events to RabbitMQ:

- `booking.timeslot.booked` - Booking completed successfully
- `booking.failed` - Booking failed (slot unavailable, invalid time, etc.)
- `booking.compensated` - Booking compensation (cancellation) completed

## Business Logic

### Booking Validation

- Validates time slot is in the future
- Checks time slot availability (simplified - in production would check database)
- Simulates failures for demo scenarios:
  - Slot unavailable
  - Invalid time slot
  - Timeout scenarios

### Compensation

When compensation is requested:
- Cancels the booking
- Publishes `booking.compensated` event
- Handles cases where booking doesn't exist gracefully

## Database

- **Database**: `bookingdb` (PostgreSQL)
- **Purpose**: Event store for booking events
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
docker compose up -d booking-service
```

### Standalone

```bash
cd src/BookingService
dotnet run
```

The service will be available at `http://localhost:5001` (or port 8080 in Docker).

## Dependencies

- PostgreSQL database (bookingdb)
- RabbitMQ message broker

## Demo Delays

For demo purposes, the service includes a 2-second delay when processing bookings and a 1.5-second delay when processing compensation to make the saga flow more visible during tech talks.

