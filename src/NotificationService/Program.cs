using NotificationService.EventStore;
using NotificationService.Services;
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
app.MapGet("/", () => "Notification Service is running");

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "NotificationService" }));

// Example endpoint for sending notifications
app.MapPost("/api/notifications", async (SendNotificationRequest request, EventStoreRepository eventStore, RabbitMQService rabbitMQ) =>
{
    var notificationId = Guid.NewGuid();
    var aggregateId = notificationId;
    
    // Create notification event
    var notificationEvent = new
    {
        NotificationId = notificationId,
        Recipient = request.Recipient,
        Type = request.Type,
        Subject = request.Subject,
        Message = request.Message,
        SentAt = DateTime.UtcNow
    };

    // Append to event store
    await eventStore.AppendEventAsync(aggregateId, "NotificationSent", notificationEvent, version: 1);

    // Publish event to RabbitMQ
    rabbitMQ.PublishEvent("notification.sent", notificationEvent);

    return Results.Created($"/api/notifications/{notificationId}", new { NotificationId = notificationId, Status = "Sent" });
});

// Subscribe to saga notification requests
var queueName = "notification-service-queue";
var sagaNotificationCounts = new Dictionary<Guid, int>();
var sagaNotificationLock = new object(); // Lock for thread-safe access to sagaNotificationCounts

rabbitMQService.SubscribeToQueue(queueName, "saga.notification.requested", (sender, message) =>
{
    Console.WriteLine($"[NotificationService] Received saga notification request: {message}");
    Task.Run(async () => await HandleSagaNotificationRequestAsync(message, eventStore, rabbitMQService, sagaNotificationCounts, sagaNotificationLock));
});

async Task HandleSagaNotificationRequestAsync(string message, EventStoreRepository eventStore, RabbitMQService rabbitMQ, Dictionary<Guid, int> sagaNotificationCounts, object lockObject)
{
    try
    {
        var request = JsonSerializer.Deserialize<JsonElement>(message);
        var sagaId = request.GetProperty("SagaId").GetGuid();
        var recipient = request.GetProperty("Recipient").GetString() ?? "";
        var type = request.GetProperty("Type").GetString() ?? "";
        var subject = request.GetProperty("Subject").GetString() ?? "";
        var messageText = request.GetProperty("Message").GetString() ?? "";

        // Validate recipient
        if (string.IsNullOrWhiteSpace(recipient) || !recipient.Contains("@"))
        {
            var failureEvent = new
            {
                SagaId = sagaId,
                Recipient = recipient,
                Reason = "Invalid recipient email address"
            };
            await eventStore.AppendEventAsync(sagaId, "NotificationFailed", failureEvent, version: 1);
            rabbitMQ.PublishEvent("notification.failed", failureEvent);
            Console.WriteLine($"[NotificationService] Invalid recipient for saga {sagaId}: {recipient}");
            return;
        }

        // Track notification count per saga (thread-safe) - count both successful and failed notifications
        // Since notifications are non-critical, we count both towards completion
        int currentCount;
        bool shouldPublishCompleted = false;
        bool notificationSucceeded = false;
        
        // Simulate occasional failures (non-critical - shouldn't break saga)
        var random = new Random();
        var shouldFail = random.NextDouble() < 0.05; // 5% chance of failure

        if (shouldFail)
        {
            var failureEvent = new
            {
                SagaId = sagaId,
                Recipient = recipient,
                Reason = "Email service temporarily unavailable"
            };
            await eventStore.AppendEventAsync(sagaId, "NotificationFailed", failureEvent, version: 1);
            rabbitMQ.PublishEvent("notification.failed", failureEvent);
            Console.WriteLine($"[NotificationService] Failed to send notification for saga {sagaId}: {failureEvent.Reason}");
            // Note: We don't fail the saga, just log the failure
            notificationSucceeded = false;
        }
        else
        {
            // Add delay for demo purposes (1 second per notification)
            await Task.Delay(1000);
            
            // Success - send notification
            var notificationId = Guid.NewGuid();
            var notificationEvent = new
            {
                SagaId = sagaId,
                NotificationId = notificationId,
                Recipient = recipient,
                Type = type,
                Subject = subject,
                Message = messageText,
                SentAt = DateTime.UtcNow
            };

            await eventStore.AppendEventAsync(notificationId, "NotificationSent", notificationEvent, version: 1);
            rabbitMQ.PublishEvent("notification.sent", notificationEvent);
            notificationSucceeded = true;
            Console.WriteLine($"[NotificationService] Successfully sent notification for saga {sagaId}, notification {notificationId}");
        }
        
        // Increment count for both successful and failed notifications (thread-safe)
        lock (lockObject)
        {
            if (!sagaNotificationCounts.ContainsKey(sagaId))
            {
                sagaNotificationCounts[sagaId] = 0;
            }
            sagaNotificationCounts[sagaId]++;
            currentCount = sagaNotificationCounts[sagaId];
            
            // If this is the second notification (both shop and customer), mark notifications as completed
            if (currentCount >= 2)
            {
                shouldPublishCompleted = true;
                sagaNotificationCounts.Remove(sagaId);
            }
        }
        
        Console.WriteLine($"[NotificationService] Notification processed for saga {sagaId} (success: {notificationSucceeded}, count: {currentCount})");

        // Publish completed event only once (outside the lock to avoid deadlock)
        if (shouldPublishCompleted)
        {
            var completedEvent = new
            {
                SagaId = sagaId
            };
            rabbitMQ.PublishEvent("notifications.completed", completedEvent);
            Console.WriteLine($"[NotificationService] All notifications completed for saga {sagaId}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[NotificationService] Error handling notification request: {ex.Message}");
    }
}

app.Run();

// Models
public record SendNotificationRequest(
    string Recipient,
    string Type,
    string Subject,
    string Message
);
