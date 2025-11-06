# Saga Orchestrator POC

A Proof of Concept (POC) implementing the Saga Orchestrator Pattern for a car service booking system. This system allows users to book time slots for car service, process payments, automatically book rental cars, and send notifications.

## Architecture

The system consists of 5 microservices:

1. **SagaOrchestrator** - Main orchestrator service that coordinates the saga
2. **BookingService** - Handles time slot booking
3. **PaymentService** - Processes payments
4. **RentalCarService** - Handles rental car bookings
5. **NotificationService** - Sends notifications to shop and customers

## Saga Flow

1. Book time slot → BookingService
2. Slot booked event → SagaOrchestrator
3. Process payment → PaymentService
4. Payment processed event → SagaOrchestrator
5. Book rental car → RentalCarService
6. Rental booked event → SagaOrchestrator
7. Send shop notification → NotificationService
8. Send customer notification → NotificationService

## Technical Stack

- **.NET 9** - Latest .NET framework
- **ASP.NET Core Minimal APIs** - API endpoints using Minimal API pattern
- **PostgreSQL** - Database for each service (Npgsql)
- **RabbitMQ** - Message broker for event-driven communication
- **Event Sourcing** - Custom PostgreSQL-based event store (event table per service)
- **Docker & Docker Compose** - Containerization and orchestration

## Prerequisites

- .NET 9 SDK
- Docker and Docker Compose

## Getting Started

### Running with Docker Compose

1. Clone the repository:
```bash
git clone <repository-url>
cd SagaOrchestrator
```

2. Build and start all services:
```bash
docker compose up --build -d
```

This will start:
- 5 PostgreSQL databases (one per service)
- RabbitMQ message broker
- All 5 microservices
- Frontend web interface

### Service Endpoints

- **Frontend Demo**: http://localhost:8080 (Web interface for tech talk)
- **Saga Orchestrator**: http://localhost:5010
- **Booking Service**: http://localhost:5001
- **Payment Service**: http://localhost:5002
- **Rental Car Service**: http://localhost:5003
- **Notification Service**: http://localhost:5004
- **RabbitMQ Management UI**: http://localhost:15672 (username: sagauser, password: sagapass)

### Running Services Individually

Each service can be run independently:

```bash
# Navigate to a service directory
cd src/BookingService

# Run the service
dotnet run
```

Make sure to configure the connection strings and RabbitMQ settings in `appsettings.json` or environment variables.

## Project Structure

```
SagaOrchestrator/
├── .cursorrules                          # Project rules and structure documentation
├── docker-compose.yml                     # Docker compose for all services
├── README.md                              # Project documentation
├── SagaOrchestrator.sln                   # Solution file
│
├── src/
│   ├── SagaOrchestrator/                 # Main orchestrator service
│   ├── BookingService/                   # Time slot booking service
│   ├── PaymentService/                   # Payment processing service
│   ├── RentalCarService/                 # Rental car booking service
│   └── NotificationService/             # Notification service
│
└── docker/
    ├── postgres-init/                    # PostgreSQL initialization scripts
    └── rabbitmq/                         # RabbitMQ configuration (if needed)
```

## Event Sourcing

Each service maintains its own event store table in PostgreSQL. Events are:
- Immutable and append-only
- Stored with: EventId, AggregateId, EventType, EventData (JSONB), Timestamp, Version
- Published to RabbitMQ after being persisted

## Development

### Building the Solution

```bash
dotnet build
```

### Running Tests

```bash
dotnet test
```

## Environment Variables

Each service uses environment variables for:
- Database connection strings (`ConnectionStrings:DefaultConnection`)
- RabbitMQ connection settings (`RabbitMQ:HostName`, `RabbitMQ:Port`, `RabbitMQ:UserName`, `RabbitMQ:Password`)

These are configured in `docker-compose.yml` for containerized deployments.

## Demo Scenarios for Tech Talk

The system includes demo endpoints to simulate different scenarios:

### 1. Happy Path (Success)
```bash
POST http://localhost:5000/api/saga/demo/success
{
  "customerId": "123e4567-e89b-12d3-a456-426614174000",
  "timeSlot": "2024-12-15T10:00:00Z",
  "serviceType": "Oil Change",
  "price": 50.00
}
```
All steps complete successfully: Booking → Payment → Rental → Notifications

### 2. Booking Failure
```bash
POST http://localhost:5000/api/saga/demo/booking-failure
{
  "customerId": "123e4567-e89b-12d3-a456-426614174000",
  "timeSlot": "2024-12-15T10:00:00Z",
  "serviceType": "Oil Change",
  "price": 50.00
}
```
Booking fails (slot unavailable) → Saga terminates immediately

### 3. Payment Failure
```bash
POST http://localhost:5000/api/saga/demo/payment-failure
{
  "customerId": "123e4567-e89b-12d3-a456-426614174000",
  "timeSlot": "2024-12-15T10:00:00Z",
  "serviceType": "Oil Change",
  "price": 50.00
}
```
Booking succeeds → Payment fails (insufficient funds) → Booking compensation → Saga fails

### 4. Rental Failure
```bash
POST http://localhost:5000/api/saga/demo/rental-failure
{
  "customerId": "123e4567-e89b-12d3-a456-426614174000",
  "timeSlot": "2024-12-15T10:00:00Z",
  "serviceType": "Oil Change",
  "price": 50.00
}
```
Booking succeeds → Payment succeeds → Rental fails (no cars available) → Payment compensation → Booking compensation → Saga fails

### 5. Timeout Scenario
```bash
POST http://localhost:5000/api/saga/demo/timeout
{
  "customerId": "123e4567-e89b-12d3-a456-426614174000",
  "timeSlot": "2024-12-15T10:00:00Z",
  "serviceType": "Oil Change",
  "price": 50.00
}
```
Simulates timeout errors at different stages

### 6. Check Saga Status
```bash
GET http://localhost:5000/api/saga/{sagaId}
```
Returns current saga state, status, and history

## Failure Scenarios

### Booking Failures
- **Slot Unavailable**: Time slot already booked
- **Invalid Time Slot**: Time slot in the past
- **System Error**: Database connection failure (simulated)

### Payment Failures
- **Insufficient Funds**: Customer doesn't have enough money
- **Card Declined**: Payment card rejected
- **Payment Gateway Timeout**: External service timeout
- **Invalid Amount**: Negative or zero amount

### Rental Car Failures
- **No Cars Available**: No rental cars available for the date
- **System Error**: Rental service unavailable
- **Invalid Date Range**: End date before start date

### Notification Failures
- **Email Service Down**: Notification service unavailable (non-critical, doesn't break saga)
- **Invalid Recipient**: Email address invalid

## Compensation Logic

The system implements compensation (rollback) when failures occur:

1. **Payment Failure** → Compensate Booking (cancel booking)
2. **Rental Failure** → Compensate Payment (refund) → Compensate Booking (cancel)
3. **Notification Failure** → Non-critical, saga completes (optional rental compensation)

## Saga States

- `Started` - Saga initiated
- `BookingInProgress` - Booking request sent
- `BookingCompleted` - Booking successful
- `PaymentInProgress` - Payment request sent
- `PaymentCompleted` - Payment successful
- `RentalInProgress` - Rental request sent
- `RentalCompleted` - Rental successful
- `NotificationsInProgress` - Sending notifications
- `Completed` - Saga completed successfully
- `Compensating` - Compensation in progress
- `Failed` - Saga failed

## Frontend Demo

A web-based frontend is available for demonstrating the saga flow during tech talks.

### Running the Frontend

The frontend is automatically started with Docker Compose:

1. Start all services:
   ```bash
   docker compose up -d
   ```

2. Open http://localhost:8080 in your web browser

3. Use the form to start different saga scenarios

4. Watch the saga progress in real-time with visual indicators

Alternatively, you can run the frontend locally:
```bash
cd frontend
./serve.sh
```
Then open http://localhost:8080 in your browser.

### Frontend Features

- **Start Sagas**: Trigger different scenarios (success, booking failure, payment failure, rental failure, timeout)
- **Real-time Status**: Automatically polls and updates saga status every 2 seconds
- **Visual Flow**: Shows the saga flow with visual indicators for each step
- **Saga Details**: Displays booking ID, payment ID, rental ID, and failure reasons

See `frontend/README.md` for more details.

## License

This is a POC project for demonstration purposes.
