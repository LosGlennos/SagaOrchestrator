using BookingService.EventStore;
using BookingService.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

var rabbitMQHostName = builder.Configuration["RabbitMQ:HostName"] ?? "localhost";
var rabbitMQPort = int.Parse(builder.Configuration["RabbitMQ:Port"] ?? "5672");
var rabbitMQUserName = builder.Configuration["RabbitMQ:UserName"] ?? "guest";
var rabbitMQPassword = builder.Configuration["RabbitMQ:Password"] ?? "guest";

// Initialize Event Store
var eventStore = new EventStoreRepository(connectionString);
await eventStore.InitializeAsync();

// Initialize RabbitMQ
var rabbitMQService = new RabbitMQService(rabbitMQHostName, rabbitMQPort, rabbitMQUserName, rabbitMQPassword);
builder.Services.AddSingleton(rabbitMQService);
builder.Services.AddSingleton(eventStore);

var app = builder.Build();

// Minimal API endpoints
app.MapGet("/", () => "Booking Service is running");

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "BookingService" }));

// Example endpoint for booking a time slot
app.MapPost("/api/bookings", async (BookTimeSlotRequest request, EventStoreRepository eventStore, RabbitMQService rabbitMQ) =>
{
    var bookingId = Guid.NewGuid();
    var aggregateId = bookingId;
    
    // Create booking event
    var bookingEvent = new
    {
        BookingId = bookingId,
        CustomerId = request.CustomerId,
        TimeSlot = request.TimeSlot,
        ServiceType = request.ServiceType,
        CreatedAt = DateTime.UtcNow
    };

    // Append to event store
    await eventStore.AppendEventAsync(aggregateId, "TimeSlotBooked", bookingEvent, version: 1);

    // Publish event to RabbitMQ
    rabbitMQ.PublishEvent("booking.timeslot.booked", bookingEvent);

    return Results.Created($"/api/bookings/{bookingId}", new { BookingId = bookingId, Status = "Booked" });
});

// Subscribe to saga booking requests
var bookingQueueName = "booking-service-queue";
rabbitMQService.SubscribeToQueue(bookingQueueName, "saga.booking.requested", (sender, message) =>
{
    Console.WriteLine($"[BookingService] Received saga booking request: {message}");
    Task.Run(async () => await HandleSagaBookingRequestAsync(message, eventStore, rabbitMQService));
});

// Subscribe to compensation requests
var compensationQueueName = "booking-service-compensation-queue";
rabbitMQService.SubscribeToQueue(compensationQueueName, "saga.booking.compensate", (sender, message) =>
{
    Console.WriteLine($"[BookingService] Received compensation request: {message}");
    Task.Run(async () => await HandleCompensationRequestAsync(message, eventStore, rabbitMQService));
});

async Task HandleSagaBookingRequestAsync(string message, EventStoreRepository eventStore, RabbitMQService rabbitMQ)
{
    try
    {
        var request = JsonSerializer.Deserialize<JsonElement>(message);
        var sagaId = request.GetProperty("SagaId").GetGuid();
        var customerId = request.GetProperty("CustomerId").GetGuid();
        var timeSlot = request.GetProperty("TimeSlot").GetDateTime();
        var serviceType = request.GetProperty("ServiceType").GetString() ?? "";
        var simulateFailure = request.TryGetProperty("SimulateFailure", out var simFail) && simFail.GetBoolean();
        var simulateTimeout = request.TryGetProperty("SimulateTimeout", out var simTimeout) && simTimeout.GetBoolean();

        // Simulate timeout
        if (simulateTimeout)
        {
            await Task.Delay(5000); // Simulate timeout
            var timeoutEvent = new
            {
                SagaId = sagaId,
                Reason = "Booking service timeout - time slot validation took too long"
            };
            rabbitMQ.PublishEvent("booking.failed", timeoutEvent);
            Console.WriteLine($"[BookingService] Simulated timeout for saga {sagaId}");
            return;
        }

        // Simulate failure scenarios
        if (simulateFailure)
        {
            var failureReason = "Time slot already booked";
            var failureEvent = new
            {
                SagaId = sagaId,
                Reason = failureReason
            };
            await eventStore.AppendEventAsync(sagaId, "BookingFailed", failureEvent, version: 1);
            rabbitMQ.PublishEvent("booking.failed", failureEvent);
            Console.WriteLine($"[BookingService] Simulated failure for saga {sagaId}: {failureReason}");
            return;
        }

        // Validate time slot
        if (timeSlot < DateTime.UtcNow)
        {
            var failureEvent = new
            {
                SagaId = sagaId,
                Reason = "Invalid time slot - cannot book in the past"
            };
            await eventStore.AppendEventAsync(sagaId, "BookingFailed", failureEvent, version: 1);
            rabbitMQ.PublishEvent("booking.failed", failureEvent);
            Console.WriteLine($"[BookingService] Validation failed for saga {sagaId}: Invalid time slot");
            return;
        }

        // Add delay for demo purposes (2 seconds)
        await Task.Delay(2000);

        // Check if slot is available (simplified - in real scenario, check database)
        var isSlotAvailable = CheckSlotAvailability(timeSlot);
        if (!isSlotAvailable)
        {
            var failureEvent = new
            {
                SagaId = sagaId,
                Reason = "Time slot already booked"
            };
            await eventStore.AppendEventAsync(sagaId, "BookingFailed", failureEvent, version: 1);
            rabbitMQ.PublishEvent("booking.failed", failureEvent);
            Console.WriteLine($"[BookingService] Slot unavailable for saga {sagaId}");
            return;
        }

        // Success - book the slot
        var bookingId = Guid.NewGuid();
        var bookingEvent = new
        {
            SagaId = sagaId,
            BookingId = bookingId,
            CustomerId = customerId,
            TimeSlot = timeSlot,
            ServiceType = serviceType,
            CreatedAt = DateTime.UtcNow
        };

        await eventStore.AppendEventAsync(bookingId, "TimeSlotBooked", bookingEvent, version: 1);
        rabbitMQ.PublishEvent("booking.timeslot.booked", bookingEvent);
        Console.WriteLine($"[BookingService] Successfully booked slot for saga {sagaId}, booking {bookingId}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[BookingService] Error handling booking request: {ex.Message}");
    }
}

async Task HandleCompensationRequestAsync(string message, EventStoreRepository eventStore, RabbitMQService rabbitMQ)
{
    try
    {
        var request = JsonSerializer.Deserialize<JsonElement>(message);
        var sagaId = request.GetProperty("SagaId").GetGuid();
        
        // Check if this is actually a booking request (has SimulateFailure field)
        if (request.TryGetProperty("SimulateFailure", out _) || request.TryGetProperty("CustomerId", out _))
        {
            // This is a booking request, not a compensation request - ignore it
            Console.WriteLine($"[BookingService] Received booking request on compensation queue, ignoring: {message}");
            return;
        }

        // Check if BookingId exists
        Guid? bookingId = null;
        if (request.TryGetProperty("BookingId", out var bookingIdProp) && bookingIdProp.ValueKind != JsonValueKind.Null)
        {
            var bookingIdValue = bookingIdProp.GetGuid();
            if (bookingIdValue != Guid.Empty)
            {
                bookingId = bookingIdValue;
            }
        }

        // If no booking ID, booking might have failed
        if (bookingId == null)
        {
            var noBookingEvent = new
            {
                SagaId = sagaId,
                BookingId = (Guid?)null,
                Reason = "Compensation - no booking to compensate (booking failed)",
                CompensatedAt = DateTime.UtcNow
            };
            rabbitMQ.PublishEvent("booking.compensated", noBookingEvent);
            Console.WriteLine($"[BookingService] No booking to compensate for saga {sagaId} (booking failed)");
            return;
        }

        // Add delay for demo purposes (1.5 seconds for compensation)
        await Task.Delay(1500);

        // Cancel the booking
        var compensateEvent = new
        {
            SagaId = sagaId,
            BookingId = bookingId.Value,
            Reason = "Compensation - payment failed",
            CompensatedAt = DateTime.UtcNow
        };

        await eventStore.AppendEventAsync(bookingId.Value, "BookingCompensated", compensateEvent, version: 2);
        rabbitMQ.PublishEvent("booking.compensated", compensateEvent);
        Console.WriteLine($"[BookingService] Compensated booking {bookingId} for saga {sagaId}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[BookingService] Error handling compensation request: {ex.Message}");
    }
}

bool CheckSlotAvailability(DateTime timeSlot)
{
    // Simplified check - in real scenario, query database
    // For demo purposes, allow booking if time slot is in the future
    return timeSlot > DateTime.UtcNow;
}

app.Run();

// Models
public record BookTimeSlotRequest(
    Guid CustomerId,
    DateTime TimeSlot,
    string ServiceType
);
