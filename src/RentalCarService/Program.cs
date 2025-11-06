using RentalCarService.EventStore;
using RentalCarService.Services;
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
app.MapGet("/", () => "Rental Car Service is running");

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "RentalCarService" }));

// Example endpoint for booking a rental car
app.MapPost("/api/rentals", async (BookRentalCarRequest request, EventStoreRepository eventStore, RabbitMQService rabbitMQ) =>
{
    var rentalId = Guid.NewGuid();
    var aggregateId = rentalId;
    
    // Create rental booking event
    var rentalEvent = new
    {
        RentalId = rentalId,
        BookingId = request.BookingId,
        CarType = request.CarType,
        StartDate = request.StartDate,
        EndDate = request.EndDate,
        Status = "Booked",
        BookedAt = DateTime.UtcNow
    };

    // Append to event store
    await eventStore.AppendEventAsync(aggregateId, "RentalCarBooked", rentalEvent, version: 1);

    // Publish event to RabbitMQ
    rabbitMQ.PublishEvent("rental.car.booked", rentalEvent);

    return Results.Created($"/api/rentals/{rentalId}", new { RentalId = rentalId, Status = "Booked" });
});

// Subscribe to saga rental requests
var rentalQueueName = "rental-service-queue";
rabbitMQService.SubscribeToQueue(rentalQueueName, "saga.rental.requested", (sender, message) =>
{
    Console.WriteLine($"[RentalCarService] Received saga rental request: {message}");
    Task.Run(async () => await HandleSagaRentalRequestAsync(message, eventStore, rabbitMQService));
});

// Subscribe to compensation requests
var compensationQueueName = "rental-service-compensation-queue";
rabbitMQService.SubscribeToQueue(compensationQueueName, "saga.rental.compensate", (sender, message) =>
{
    Console.WriteLine($"[RentalCarService] Received compensation request: {message}");
    Task.Run(async () => await HandleCompensationRequestAsync(message, eventStore, rabbitMQService));
});

async Task HandleSagaRentalRequestAsync(string message, EventStoreRepository eventStore, RabbitMQService rabbitMQ)
{
    try
    {
        var request = JsonSerializer.Deserialize<JsonElement>(message);
        var sagaId = request.GetProperty("SagaId").GetGuid();
        
        // Handle null BookingId (when booking failed)
        Guid? bookingId = null;
        if (request.TryGetProperty("BookingId", out var bookingIdProp) && bookingIdProp.ValueKind != JsonValueKind.Null)
        {
            bookingId = bookingIdProp.GetGuid();
        }
        
        var carType = request.GetProperty("CarType").GetString() ?? "Standard";
        var startDate = request.GetProperty("StartDate").GetDateTime();
        var endDate = request.GetProperty("EndDate").GetDateTime();
        var simulateFailure = request.TryGetProperty("SimulateFailure", out var simFail) && simFail.GetBoolean();
        var simulateTimeout = request.TryGetProperty("SimulateTimeout", out var simTimeout) && simTimeout.GetBoolean();

        // Simulate timeout
        if (simulateTimeout)
        {
            await Task.Delay(5000); // Simulate timeout
            var timeoutEvent = new
            {
                SagaId = sagaId,
                Reason = "Rental service timeout - availability check took too long"
            };
            rabbitMQ.PublishEvent("rental.failed", timeoutEvent);
            Console.WriteLine($"[RentalCarService] Simulated timeout for saga {sagaId}");
            return;
        }

        // Simulate failure scenarios
        if (simulateFailure)
        {
            var failureReason = "No cars available";
            var failureEvent = new
            {
                SagaId = sagaId,
                Reason = failureReason
            };
            await eventStore.AppendEventAsync(sagaId, "RentalFailed", failureEvent, version: 1);
            rabbitMQ.PublishEvent("rental.failed", failureEvent);
            Console.WriteLine($"[RentalCarService] Simulated failure for saga {sagaId}: {failureReason}");
            return;
        }

        // Validate date range
        if (endDate <= startDate)
        {
            var failureEvent = new
            {
                SagaId = sagaId,
                Reason = "Invalid date range - end date must be after start date"
            };
            await eventStore.AppendEventAsync(sagaId, "RentalFailed", failureEvent, version: 1);
            rabbitMQ.PublishEvent("rental.failed", failureEvent);
            Console.WriteLine($"[RentalCarService] Validation failed for saga {sagaId}: Invalid date range");
            return;
        }

        // Check car availability (simplified - in real scenario, check database)
        var isCarAvailable = CheckCarAvailability(carType, startDate, endDate);
        if (!isCarAvailable)
        {
            var failureEvent = new
            {
                SagaId = sagaId,
                Reason = "No cars available for the requested dates"
            };
            await eventStore.AppendEventAsync(sagaId, "RentalFailed", failureEvent, version: 1);
            rabbitMQ.PublishEvent("rental.failed", failureEvent);
            Console.WriteLine($"[RentalCarService] No cars available for saga {sagaId}");
            return;
        }

        // Add delay for demo purposes (2 seconds)
        await Task.Delay(2000);

        // Success - book rental car
        var rentalId = Guid.NewGuid();
        var rentalEvent = new
        {
            SagaId = sagaId,
            RentalId = rentalId,
            BookingId = bookingId.HasValue ? bookingId.Value : (Guid?)null,
            CarType = carType,
            StartDate = startDate,
            EndDate = endDate,
            Status = "Booked",
            BookedAt = DateTime.UtcNow
        };

        await eventStore.AppendEventAsync(rentalId, "RentalCarBooked", rentalEvent, version: 1);
        rabbitMQ.PublishEvent("rental.car.booked", rentalEvent);
        Console.WriteLine($"[RentalCarService] Successfully booked rental car for saga {sagaId}, rental {rentalId}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[RentalCarService] Error handling rental request: {ex.Message}");
    }
}

async Task HandleCompensationRequestAsync(string message, EventStoreRepository eventStore, RabbitMQService rabbitMQ)
{
    try
    {
        var request = JsonSerializer.Deserialize<JsonElement>(message);
        var sagaId = request.GetProperty("SagaId").GetGuid();
        var rentalId = request.GetProperty("RentalId").GetGuid();

        // Add delay for demo purposes (1.5 seconds for compensation)
        await Task.Delay(1500);

        // Cancel the rental
        var compensateEvent = new
        {
            SagaId = sagaId,
            RentalId = rentalId,
            Reason = "Compensation - notification failed",
            CancelledAt = DateTime.UtcNow
        };

        await eventStore.AppendEventAsync(rentalId, "RentalCompensated", compensateEvent, version: 2);
        rabbitMQ.PublishEvent("rental.compensated", compensateEvent);
        Console.WriteLine($"[RentalCarService] Compensated rental {rentalId} for saga {sagaId}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[RentalCarService] Error handling compensation request: {ex.Message}");
    }
}

bool CheckCarAvailability(string carType, DateTime startDate, DateTime endDate)
{
    // Simplified check - in real scenario, query database
    // For demo purposes, allow booking if dates are valid
    return startDate < endDate && startDate >= DateTime.UtcNow;
}

app.Run();

// Models
public record BookRentalCarRequest(
    Guid BookingId,
    string CarType,
    DateTime StartDate,
    DateTime EndDate
);
