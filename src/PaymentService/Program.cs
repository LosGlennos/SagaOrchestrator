using PaymentService.EventStore;
using PaymentService.Services;
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
app.MapGet("/", () => "Payment Service is running");

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "PaymentService" }));

// Example endpoint for processing payment
app.MapPost("/api/payments", async (ProcessPaymentRequest request, EventStoreRepository eventStore, RabbitMQService rabbitMQ) =>
{
    var paymentId = Guid.NewGuid();
    var aggregateId = paymentId;
    
    // Simulate payment processing
    var paymentEvent = new
    {
        PaymentId = paymentId,
        BookingId = request.BookingId,
        Amount = request.Amount,
        Currency = request.Currency,
        Status = "Processed",
        ProcessedAt = DateTime.UtcNow
    };

    // Append to event store
    await eventStore.AppendEventAsync(aggregateId, "PaymentProcessed", paymentEvent, version: 1);

    // Publish event to RabbitMQ
    rabbitMQ.PublishEvent("payment.processed", paymentEvent);

    return Results.Created($"/api/payments/{paymentId}", new { PaymentId = paymentId, Status = "Processed" });
});

// Subscribe to saga payment requests
var paymentQueueName = "payment-service-queue";
rabbitMQService.SubscribeToQueue(paymentQueueName, "saga.payment.requested", (sender, message) =>
{
    Console.WriteLine($"[PaymentService] Received saga payment request: {message}");
    Task.Run(async () => await HandleSagaPaymentRequestAsync(message, eventStore, rabbitMQService));
});

// Subscribe to compensation requests
var compensationQueueName = "payment-service-compensation-queue";
rabbitMQService.SubscribeToQueue(compensationQueueName, "saga.payment.compensate", (sender, message) =>
{
    Console.WriteLine($"[PaymentService] Received compensation request: {message}");
    Task.Run(async () => await HandleCompensationRequestAsync(message, eventStore, rabbitMQService));
});

async Task HandleSagaPaymentRequestAsync(string message, EventStoreRepository eventStore, RabbitMQService rabbitMQ)
{
    try
    {
        var request = JsonSerializer.Deserialize<JsonElement>(message);
        var sagaId = request.GetProperty("SagaId").GetGuid();
        var bookingId = request.GetProperty("BookingId").GetGuid();
        var amount = request.GetProperty("Amount").GetDecimal();
        var currency = request.GetProperty("Currency").GetString() ?? "USD";
        var simulateFailure = request.TryGetProperty("SimulateFailure", out var simFail) && simFail.GetBoolean();
        var simulateTimeout = request.TryGetProperty("SimulateTimeout", out var simTimeout) && simTimeout.GetBoolean();

        // Simulate timeout
        if (simulateTimeout)
        {
            await Task.Delay(5000); // Simulate timeout
            var timeoutEvent = new
            {
                SagaId = sagaId,
                Reason = "Payment gateway timeout - external service took too long"
            };
            rabbitMQ.PublishEvent("payment.failed", timeoutEvent);
            Console.WriteLine($"[PaymentService] Simulated timeout for saga {sagaId}");
            return;
        }

        // Simulate failure scenarios
        if (simulateFailure)
        {
            var failureReason = "Insufficient funds";
            var failureEvent = new
            {
                SagaId = sagaId,
                Reason = failureReason
            };
            await eventStore.AppendEventAsync(sagaId, "PaymentFailed", failureEvent, version: 1);
            rabbitMQ.PublishEvent("payment.failed", failureEvent);
            Console.WriteLine($"[PaymentService] Simulated failure for saga {sagaId}: {failureReason}");
            return;
        }

        // Validate amount
        if (amount <= 0)
        {
            var failureEvent = new
            {
                SagaId = sagaId,
                Reason = "Invalid amount - must be greater than zero"
            };
            await eventStore.AppendEventAsync(sagaId, "PaymentFailed", failureEvent, version: 1);
            rabbitMQ.PublishEvent("payment.failed", failureEvent);
            Console.WriteLine($"[PaymentService] Validation failed for saga {sagaId}: Invalid amount");
            return;
        }

        // Simulate payment processing with random failures for demo
        var random = new Random();
        var shouldFail = random.NextDouble() < 0.1; // 10% chance of random failure

        if (shouldFail)
        {
            var failureReasons = new[]
            {
                "Card declined",
                "Insufficient funds",
                "Payment gateway error"
            };
            var failureReason = failureReasons[random.Next(failureReasons.Length)];
            
            var failureEvent = new
            {
                SagaId = sagaId,
                Reason = failureReason
            };
            await eventStore.AppendEventAsync(sagaId, "PaymentFailed", failureEvent, version: 1);
            rabbitMQ.PublishEvent("payment.failed", failureEvent);
            Console.WriteLine($"[PaymentService] Payment failed for saga {sagaId}: {failureReason}");
            return;
        }

        // Add delay for demo purposes (2 seconds)
        await Task.Delay(2000);

        // Success - process payment
        var paymentId = Guid.NewGuid();
        var paymentEvent = new
        {
            SagaId = sagaId,
            PaymentId = paymentId,
            BookingId = bookingId,
            Amount = amount,
            Currency = currency,
            Status = "Processed",
            ProcessedAt = DateTime.UtcNow
        };

        await eventStore.AppendEventAsync(paymentId, "PaymentProcessed", paymentEvent, version: 1);
        rabbitMQ.PublishEvent("payment.processed", paymentEvent);
        Console.WriteLine($"[PaymentService] Successfully processed payment for saga {sagaId}, payment {paymentId}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[PaymentService] Error handling payment request: {ex.Message}");
    }
}

async Task HandleCompensationRequestAsync(string message, EventStoreRepository eventStore, RabbitMQService rabbitMQ)
{
    try
    {
        var request = JsonSerializer.Deserialize<JsonElement>(message);
        var sagaId = request.GetProperty("SagaId").GetGuid();
        
        // Handle null PaymentId (when payment failed before being processed)
        if (!request.TryGetProperty("PaymentId", out var paymentIdProp) || paymentIdProp.ValueKind == JsonValueKind.Null)
        {
            var noPaymentEvent = new
            {
                SagaId = sagaId,
                Reason = "Compensation - no payment to compensate (payment failed)"
            };
            rabbitMQ.PublishEvent("payment.compensated", noPaymentEvent);
            Console.WriteLine($"[PaymentService] No payment to compensate for saga {sagaId} (payment failed)");
            return;
        }
        
        var paymentId = paymentIdProp.GetGuid();

        // Add delay for demo purposes (1.5 seconds for compensation)
        await Task.Delay(1500);

        // Refund the payment
        var compensateEvent = new
        {
            SagaId = sagaId,
            PaymentId = paymentId,
            Reason = "Compensation - rental car booking failed",
            RefundedAt = DateTime.UtcNow
        };

        await eventStore.AppendEventAsync(paymentId, "PaymentCompensated", compensateEvent, version: 2);
        rabbitMQ.PublishEvent("payment.compensated", compensateEvent);
        Console.WriteLine($"[PaymentService] Compensated payment {paymentId} for saga {sagaId}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[PaymentService] Error handling compensation request: {ex.Message}");
    }
}

app.Run();

// Models
public record ProcessPaymentRequest(
    Guid BookingId,
    decimal Amount,
    string Currency
);
