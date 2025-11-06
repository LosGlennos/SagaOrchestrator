using SagaOrchestrator.EventStore;
using SagaOrchestrator.Models;
using SagaOrchestrator.Services;
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

// Initialize Saga Orchestration Service
var sagaService = new SagaOrchestrationService(eventStore, rabbitMQService);
builder.Services.AddSingleton(sagaService);

// Add CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Use CORS - must be before UseRouting and Map endpoints
app.UseCors();

// Minimal API endpoints
app.MapGet("/", () => "Saga Orchestrator is running");

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "SagaOrchestrator" }));


// Start saga endpoint - initiates the booking saga
app.MapPost("/api/saga/start", async (StartSagaRequest request, SagaOrchestrationService sagaService) =>
{
    try
    {
        var sagaId = await sagaService.StartSagaAsync(request);
        return Results.Created($"/api/saga/{sagaId}", new { SagaId = sagaId, Status = "Started" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to start saga: {ex.Message}");
    }
});

// Get saga status endpoint
app.MapGet("/api/saga/{sagaId:guid}", async (Guid sagaId, SagaOrchestrationService sagaService) =>
{
    var sagaState = await sagaService.GetSagaStateAsync(sagaId);
    if (sagaState == null)
    {
        return Results.NotFound(new { Message = $"Saga {sagaId} not found" });
    }

    return Results.Ok(sagaState);
});

// Get saga events endpoint (for compensation flow visualization)
app.MapGet("/api/saga/{sagaId:guid}/events", async (Guid sagaId, EventStoreRepository eventStore) =>
{
    try
    {
        var events = await eventStore.GetEventsByAggregateIdAsync(sagaId);
        if (events == null || events.Count == 0)
        {
            return Results.NotFound(new { Message = $"No events found for saga {sagaId}" });
        }

        return Results.Ok(events);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to get saga events: {ex.Message}");
    }
});

// OPTIONS route for CORS
app.MapMethods("/api/saga/{sagaId:guid}/events", new[] { "OPTIONS" }, () => Results.Ok()).ExcludeFromDescription();

// Manual recovery endpoint for stuck notifications
app.MapPost("/api/saga/{sagaId:guid}/complete-notifications", async (Guid sagaId, SagaOrchestrationService sagaService) =>
{
    try
    {
        var sagaState = await sagaService.GetSagaStateAsync(sagaId);
        if (sagaState == null)
        {
            return Results.NotFound(new { Message = $"Saga {sagaId} not found" });
        }

        // Check if saga is stuck in NotificationsInProgress or RentalCompleted (notifications were triggered but never completed)
        if (sagaState.Status != SagaStatus.NotificationsInProgress && sagaState.Status != SagaStatus.RentalCompleted)
        {
            return Results.BadRequest(new { Message = $"Saga {sagaId} is not in a state that requires notification completion. Current status: {sagaState.Status}" });
        }

        // If saga is in RentalCompleted but has rentalId, it means notifications were triggered but never completed
        if (sagaState.Status == SagaStatus.RentalCompleted && sagaState.RentalId == null)
        {
            return Results.BadRequest(new { Message = $"Saga {sagaId} is in RentalCompleted but has no rental ID. Cannot complete notifications." });
        }

        // Manually trigger notifications completed event
        var completedEvent = new { SagaId = sagaId };
        await sagaService.HandleNotificationsCompletedAsync(System.Text.Json.JsonSerializer.Serialize(completedEvent));

        return Results.Ok(new { Message = $"Notifications completed for saga {sagaId}" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to complete notifications: {ex.Message}");
    }
});

// OPTIONS route for CORS
app.MapMethods("/api/saga/{sagaId:guid}/complete-notifications", new[] { "OPTIONS" }, () => Results.Ok()).ExcludeFromDescription();

// Get all sagas endpoint
app.MapGet("/api/saga", async (SagaOrchestrationService sagaService) =>
{
    var sagas = await sagaService.GetAllSagasAsync();
    return Results.Ok(sagas);
});

// Recover stuck saga endpoint - manually process payment completed event
app.MapPost("/api/saga/{sagaId:guid}/recover-payment", async (Guid sagaId, SagaOrchestrationService sagaService) =>
{
    try
    {
        var result = await sagaService.RecoverStuckPaymentAsync(sagaId);
        if (result)
        {
            return Results.Ok(new { Message = $"Saga {sagaId} payment recovery initiated" });
        }
        return Results.NotFound(new { Message = $"Saga {sagaId} not found or payment already processed" });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Failed to recover saga: {ex.Message}");
    }
});

// Demo endpoints for tech talk
app.MapPost("/api/saga/demo/success", async (StartSagaRequest request, SagaOrchestrationService sagaService) =>
{
    var demoRequest = request with
    {
        SimulateBookingFailure = false,
        SimulatePaymentFailure = false,
        SimulateRentalFailure = false,
        SimulateTimeout = false
    };
    var sagaId = await sagaService.StartSagaAsync(demoRequest);
    return Results.Created($"/api/saga/{sagaId}", new { SagaId = sagaId, Status = "Started", Demo = "Success" });
});

app.MapPost("/api/saga/demo/booking-failure", async (StartSagaRequest request, SagaOrchestrationService sagaService) =>
{
    var demoRequest = request with { SimulateBookingFailure = true };
    var sagaId = await sagaService.StartSagaAsync(demoRequest);
    return Results.Created($"/api/saga/{sagaId}", new { SagaId = sagaId, Status = "Started", Demo = "BookingFailure" });
});

app.MapPost("/api/saga/demo/payment-failure", async (StartSagaRequest request, SagaOrchestrationService sagaService) =>
{
    var demoRequest = request with { SimulatePaymentFailure = true };
    var sagaId = await sagaService.StartSagaAsync(demoRequest);
    return Results.Created($"/api/saga/{sagaId}", new { SagaId = sagaId, Status = "Started", Demo = "PaymentFailure" });
});

app.MapPost("/api/saga/demo/rental-failure", async (StartSagaRequest request, SagaOrchestrationService sagaService) =>
{
    var demoRequest = request with { SimulateRentalFailure = true };
    var sagaId = await sagaService.StartSagaAsync(demoRequest);
    return Results.Created($"/api/saga/{sagaId}", new { SagaId = sagaId, Status = "Started", Demo = "RentalFailure" });
});

app.MapPost("/api/saga/demo/timeout", async (StartSagaRequest request, SagaOrchestrationService sagaService) =>
{
    var demoRequest = request with { SimulateTimeout = true };
    var sagaId = await sagaService.StartSagaAsync(demoRequest);
    return Results.Created($"/api/saga/{sagaId}", new { SagaId = sagaId, Status = "Started", Demo = "Timeout" });
});

// Subscribe to events from other services
// Use separate queues for each subscription to prevent routing issues
// When multiple bindings exist on the same queue, RabbitMQ delivers messages
// matching ANY binding to ANY consumer on that queue, causing misrouting

rabbitMQService.SubscribeToQueue("saga-orchestrator-booking-completed-queue", "booking.timeslot.booked", (sender, message) =>
{
    Console.WriteLine($"[Orchestrator] Received booking completed: {message}");
    Task.Run(async () => await sagaService.HandleBookingCompletedAsync(message));
});

rabbitMQService.SubscribeToQueue("saga-orchestrator-booking-failed-queue", "booking.failed", (sender, message) =>
{
    Console.WriteLine($"[Orchestrator] Received booking failed: {message}");
    Task.Run(async () => await sagaService.HandleBookingFailedAsync(message));
});

rabbitMQService.SubscribeToQueue("saga-orchestrator-payment-processed-queue", "payment.processed", (sender, message) =>
{
    Console.WriteLine($"[Orchestrator] Received payment completed: {message}");
    Task.Run(async () => await sagaService.HandlePaymentCompletedAsync(message));
});

rabbitMQService.SubscribeToQueue("saga-orchestrator-payment-failed-queue", "payment.failed", (sender, message) =>
{
    Console.WriteLine($"[Orchestrator] Received payment failed: {message}");
    Task.Run(async () => await sagaService.HandlePaymentFailedAsync(message));
});

rabbitMQService.SubscribeToQueue("saga-orchestrator-rental-booked-queue", "rental.car.booked", (sender, message) =>
{
    Console.WriteLine($"[Orchestrator] Received rental completed: {message}");
    Task.Run(async () => await sagaService.HandleRentalCompletedAsync(message));
});

rabbitMQService.SubscribeToQueue("saga-orchestrator-rental-failed-queue", "rental.failed", (sender, message) =>
{
    Console.WriteLine($"[Orchestrator] Received rental failed: {message}");
    Task.Run(async () => await sagaService.HandleRentalFailedAsync(message));
});

rabbitMQService.SubscribeToQueue("saga-orchestrator-booking-compensated-queue", "booking.compensated", (sender, message) =>
{
    Console.WriteLine($"[Orchestrator] Received booking compensated: {message}");
    Task.Run(async () => await sagaService.HandleBookingCompensatedAsync(message));
});

rabbitMQService.SubscribeToQueue("saga-orchestrator-payment-compensated-queue", "payment.compensated", (sender, message) =>
{
    Console.WriteLine($"[Orchestrator] Received payment compensated: {message}");
    Task.Run(async () => await sagaService.HandlePaymentCompensatedAsync(message));
});

rabbitMQService.SubscribeToQueue("saga-orchestrator-notifications-completed-queue", "notifications.completed", (sender, message) =>
{
    Console.WriteLine($"[Orchestrator] Received notifications completed: {message}");
    Task.Run(async () => await sagaService.HandleNotificationsCompletedAsync(message));
});

app.Run();
