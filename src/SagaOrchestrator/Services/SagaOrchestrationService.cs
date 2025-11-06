using SagaOrchestrator.EventStore;
using SagaOrchestrator.Models;
using SagaOrchestrator.Services;
using System.Text.Json;

namespace SagaOrchestrator.Services;

public class SagaOrchestrationService
{
    private readonly EventStoreRepository _eventStore;
    private readonly RabbitMQService _rabbitMQ;
    private readonly Dictionary<Guid, SagaState> _sagaStates = new();

    public SagaOrchestrationService(EventStoreRepository eventStore, RabbitMQService rabbitMQ)
    {
        _eventStore = eventStore;
        _rabbitMQ = rabbitMQ;
    }

    public async Task<Guid> StartSagaAsync(StartSagaRequest request)
    {
        var sagaId = Guid.NewGuid();
        var sagaState = new SagaState
        {
            SagaId = sagaId,
            Status = SagaStatus.Started,
            CustomerId = request.CustomerId,
            TimeSlot = request.TimeSlot,
            ServiceType = request.ServiceType,
            Price = request.Price,
            CreatedAt = DateTime.UtcNow,
            Version = 1,
            SimulatePaymentFailure = request.SimulatePaymentFailure,
            SimulateRentalFailure = request.SimulateRentalFailure,
            SimulateTimeout = request.SimulateTimeout
        };

        _sagaStates[sagaId] = sagaState;

        var sagaEvent = new
        {
            SagaId = sagaId,
            CustomerId = request.CustomerId,
            TimeSlot = request.TimeSlot,
            ServiceType = request.ServiceType,
            Price = request.Price,
            Status = "Started",
            StartedAt = DateTime.UtcNow
        };

        await _eventStore.AppendEventAsync(sagaId, "SagaStarted", sagaEvent, version: 1);

        // Trigger booking
        await TriggerBookingAsync(sagaId, request);

        return sagaId;
    }

    private Task TriggerBookingAsync(Guid sagaId, StartSagaRequest request)
    {
        var sagaState = _sagaStates[sagaId];
        sagaState.Status = SagaStatus.BookingInProgress;
        sagaState.UpdatedAt = DateTime.UtcNow;

        var bookingRequest = new
        {
            SagaId = sagaId,
            CustomerId = request.CustomerId,
            TimeSlot = request.TimeSlot,
            ServiceType = request.ServiceType,
            SimulateFailure = request.SimulateBookingFailure ?? false,
            SimulateTimeout = request.SimulateTimeout ?? false
        };

        _rabbitMQ.PublishEvent("saga.booking.requested", bookingRequest);
        Console.WriteLine($"[Saga {sagaId}] Triggered booking request");
        return Task.CompletedTask;
    }

    public async Task HandleBookingCompletedAsync(string message)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(message);
            var root = jsonDoc.RootElement;
            
            // Validate this is actually a booking event (has BookingId and no PaymentId/RentalId)
            if (root.TryGetProperty("PaymentId", out var paymentIdProp) && paymentIdProp.ValueKind != JsonValueKind.Null)
            {
                Console.WriteLine($"[SagaOrchestrator] Received payment event on booking.timeslot.booked subscription, ignoring: {message}");
                return;
            }
            
            if (root.TryGetProperty("RentalId", out var rentalIdProp) && rentalIdProp.ValueKind != JsonValueKind.Null)
            {
                Console.WriteLine($"[SagaOrchestrator] Received rental event on booking.timeslot.booked subscription, ignoring: {message}");
                return;
            }
            
            // Check if this is actually a failure event (has Reason field)
            if (root.TryGetProperty("Reason", out _))
            {
                // This is a failure event, handle it as such
                await HandleBookingFailedAsync(message);
                return;
            }

            var bookingEvent = JsonSerializer.Deserialize<BookingCompletedEvent>(message);
            if (bookingEvent == null) 
            {
                Console.WriteLine($"[SagaOrchestrator] Failed to deserialize booking completed event: {message}");
                return;
            }

            var sagaId = bookingEvent.SagaId;
            if (!_sagaStates.TryGetValue(sagaId, out var sagaState))
            {
                Console.WriteLine($"[SagaOrchestrator] Saga {sagaId} not found for booking completed event");
                return;
            }

            sagaState.Status = SagaStatus.BookingCompleted;
            sagaState.BookingId = bookingEvent.BookingId;
            sagaState.UpdatedAt = DateTime.UtcNow;
            sagaState.Version++;

            await _eventStore.AppendEventAsync(sagaId, "BookingCompleted", bookingEvent, version: sagaState.Version);

            // Trigger payment
            await TriggerPaymentAsync(sagaId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SagaOrchestrator] Error handling booking completed: {ex.Message}");
        }
    }

    public async Task HandleBookingFailedAsync(string message)
    {
        try
        {
            // Validate this is actually a booking failed event (has Reason and no PaymentId/RentalId)
            var jsonDoc = JsonDocument.Parse(message);
            var root = jsonDoc.RootElement;
            
            // Check if this is actually a payment or rental event (has PaymentId or RentalId)
            if (root.TryGetProperty("PaymentId", out var paymentIdProp) && paymentIdProp.ValueKind != JsonValueKind.Null)
            {
                Console.WriteLine($"[SagaOrchestrator] Received payment event on booking.failed subscription, ignoring: {message}");
                return;
            }
            
            if (root.TryGetProperty("RentalId", out var rentalIdProp) && rentalIdProp.ValueKind != JsonValueKind.Null)
            {
                Console.WriteLine($"[SagaOrchestrator] Received rental event on booking.failed subscription, ignoring: {message}");
                return;
            }
            
            var failureEvent = JsonSerializer.Deserialize<BookingFailedEvent>(message);
            if (failureEvent == null)
            {
                Console.WriteLine($"[SagaOrchestrator] Failed to deserialize booking failed event: {message}");
                return;
            }

            var sagaId = failureEvent.SagaId;
            if (!_sagaStates.TryGetValue(sagaId, out var sagaState))
            {
                Console.WriteLine($"[SagaOrchestrator] Saga {sagaId} not found for booking failed event");
                return;
            }

            sagaState.Status = SagaStatus.Failed;
            sagaState.FailureReason = failureEvent.Reason;
            sagaState.UpdatedAt = DateTime.UtcNow;
            sagaState.Version++;

            await _eventStore.AppendEventAsync(sagaId, "BookingFailed", failureEvent, version: sagaState.Version);
            await _eventStore.AppendEventAsync(sagaId, "SagaFailed", new { SagaId = sagaId, Reason = failureEvent.Reason }, version: sagaState.Version + 1);

            _rabbitMQ.PublishEvent("saga.failed", new { SagaId = sagaId, Reason = failureEvent.Reason });
            Console.WriteLine($"[Saga {sagaId}] Failed: {failureEvent.Reason}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SagaOrchestrator] Error handling booking failed: {ex.Message}");
        }
    }

    private Task TriggerPaymentAsync(Guid sagaId)
    {
        var sagaState = _sagaStates[sagaId];
        sagaState.Status = SagaStatus.PaymentInProgress;
        sagaState.UpdatedAt = DateTime.UtcNow;

        var paymentRequest = new
        {
            SagaId = sagaId,
            BookingId = sagaState.BookingId,
            Amount = sagaState.Price,
            Currency = "USD",
            SimulateFailure = sagaState.SimulatePaymentFailure ?? false,
            SimulateTimeout = sagaState.SimulateTimeout ?? false
        };

        _rabbitMQ.PublishEvent("saga.payment.requested", paymentRequest);
        Console.WriteLine($"[Saga {sagaId}] Triggered payment request");
        return Task.CompletedTask;
    }

    public async Task HandlePaymentCompletedAsync(string message)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(message);
            var root = jsonDoc.RootElement;
            
            // Validate this is actually a payment event (has PaymentId and no RentalId)
            if (root.TryGetProperty("RentalId", out var rentalIdProp) && rentalIdProp.ValueKind != JsonValueKind.Null)
            {
                Console.WriteLine($"[SagaOrchestrator] Received rental event on payment.processed subscription, ignoring: {message}");
                return;
            }
            
            // Check if this is actually a failure event (has Reason field)
            if (root.TryGetProperty("Reason", out _))
            {
                // This is a failure event, handle it as such
                await HandlePaymentFailedAsync(message);
                return;
            }
            
            // Validate it has PaymentId (payment events should have PaymentId)
            if (!root.TryGetProperty("PaymentId", out var paymentIdProp) || paymentIdProp.ValueKind == JsonValueKind.Null)
            {
                Console.WriteLine($"[SagaOrchestrator] Received non-payment event on payment.processed subscription, ignoring: {message}");
                return;
            }

            var paymentEvent = JsonSerializer.Deserialize<PaymentCompletedEvent>(message);
            if (paymentEvent == null)
            {
                Console.WriteLine($"[SagaOrchestrator] Failed to deserialize payment completed event: {message}");
                return;
            }

            var sagaId = paymentEvent.SagaId;
            if (!_sagaStates.TryGetValue(sagaId, out var sagaState))
            {
                Console.WriteLine($"[SagaOrchestrator] Saga {sagaId} not found for payment completed event");
                return;
            }

            sagaState.Status = SagaStatus.PaymentCompleted;
            sagaState.PaymentId = paymentEvent.PaymentId;
            sagaState.UpdatedAt = DateTime.UtcNow;
            sagaState.Version++;

            await _eventStore.AppendEventAsync(sagaId, "PaymentCompleted", paymentEvent, version: sagaState.Version);

            // Trigger rental car booking
            await TriggerRentalAsync(sagaId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SagaOrchestrator] Error handling payment completed: {ex.Message}");
        }
    }

    public async Task HandlePaymentFailedAsync(string message)
    {
        try
        {
            // Validate this is actually a payment failed event (has Reason and no RentalId)
            var jsonDoc = JsonDocument.Parse(message);
            var root = jsonDoc.RootElement;
            
            // Check if this is actually a rental event (has RentalId)
            if (root.TryGetProperty("RentalId", out var rentalIdProp) && rentalIdProp.ValueKind != JsonValueKind.Null)
            {
                Console.WriteLine($"[SagaOrchestrator] Received rental event on payment.failed subscription, ignoring: {message}");
                return;
            }
            
            // Check if this is actually a booking event (has BookingId but no PaymentId and no RentalId)
            if (root.TryGetProperty("BookingId", out var bookingIdProp) && bookingIdProp.ValueKind != JsonValueKind.Null)
            {
                if (!root.TryGetProperty("PaymentId", out var paymentIdProp) || paymentIdProp.ValueKind == JsonValueKind.Null)
                {
                    if (!root.TryGetProperty("RentalId", out _))
                    {
                        Console.WriteLine($"[SagaOrchestrator] Received booking event on payment.failed subscription, ignoring: {message}");
                        return;
                    }
                }
            }
            
            var failureEvent = JsonSerializer.Deserialize<PaymentFailedEvent>(message);
            if (failureEvent == null)
            {
                Console.WriteLine($"[SagaOrchestrator] Failed to deserialize payment failed event: {message}");
                return;
            }

            var sagaId = failureEvent.SagaId;
            if (!_sagaStates.TryGetValue(sagaId, out var sagaState))
            {
                Console.WriteLine($"[SagaOrchestrator] Saga {sagaId} not found for payment failed event");
                return;
            }

            sagaState.Status = SagaStatus.Compensating;
            sagaState.FailureReason = failureEvent.Reason;
            sagaState.UpdatedAt = DateTime.UtcNow;
            sagaState.Version++;

            await _eventStore.AppendEventAsync(sagaId, "PaymentFailed", failureEvent, version: sagaState.Version);
            await _eventStore.AppendEventAsync(sagaId, "SagaCompensating", new { SagaId = sagaId, Reason = failureEvent.Reason }, version: sagaState.Version + 1);

            // Compensate booking
            await CompensateBookingAsync(sagaId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SagaOrchestrator] Error handling payment failed: {ex.Message}");
        }
    }

    private Task CompensateBookingAsync(Guid sagaId)
    {
        var sagaState = _sagaStates[sagaId];
        var compensateRequest = new
        {
            SagaId = sagaId,
            BookingId = sagaState.BookingId
        };

        _rabbitMQ.PublishEvent("saga.booking.compensate", compensateRequest);
        Console.WriteLine($"[Saga {sagaId}] Triggered booking compensation");
        return Task.CompletedTask;
    }

    public async Task HandleBookingCompensatedAsync(string message)
    {
        try
        {
            var compensateEvent = JsonSerializer.Deserialize<BookingCompensatedEvent>(message);
            if (compensateEvent == null)
            {
                Console.WriteLine($"[SagaOrchestrator] Failed to deserialize booking compensated event: {message}");
                return;
            }

            var sagaId = compensateEvent.SagaId;
            if (!_sagaStates.TryGetValue(sagaId, out var sagaState))
            {
                Console.WriteLine($"[SagaOrchestrator] Saga {sagaId} not found for booking compensated event");
                return;
            }

            sagaState.Status = SagaStatus.Failed;
            // Preserve the original failure reason (don't overwrite with "Compensation completed")
            // The failure reason was already set when the failure event was received
            sagaState.UpdatedAt = DateTime.UtcNow;
            sagaState.Version++;

            await _eventStore.AppendEventAsync(sagaId, "BookingCompensated", compensateEvent, version: sagaState.Version);
            // Use the original failure reason if available, otherwise use "Compensation completed"
            var finalReason = sagaState.FailureReason ?? "Compensation completed";
            await _eventStore.AppendEventAsync(sagaId, "SagaFailed", new { SagaId = sagaId, Reason = finalReason }, version: sagaState.Version + 1);

            _rabbitMQ.PublishEvent("saga.failed", new { SagaId = sagaId, Reason = finalReason });
            Console.WriteLine($"[Saga {sagaId}] Compensation completed, saga failed: {finalReason}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SagaOrchestrator] Error handling booking compensated: {ex.Message}");
        }
    }

    private Task TriggerRentalAsync(Guid sagaId)
    {
        var sagaState = _sagaStates[sagaId];
        sagaState.Status = SagaStatus.RentalInProgress;
        sagaState.UpdatedAt = DateTime.UtcNow;

        var rentalRequest = new
        {
            SagaId = sagaId,
            BookingId = sagaState.BookingId,
            CarType = "Standard",
            StartDate = sagaState.TimeSlot,
            EndDate = sagaState.TimeSlot.AddDays(1),
            SimulateFailure = sagaState.SimulateRentalFailure ?? false,
            SimulateTimeout = sagaState.SimulateTimeout ?? false
        };

        _rabbitMQ.PublishEvent("saga.rental.requested", rentalRequest);
        Console.WriteLine($"[Saga {sagaId}] Triggered rental car request");
        return Task.CompletedTask;
    }

    public async Task HandleRentalCompletedAsync(string message)
    {
        try
        {
            var jsonDoc = JsonDocument.Parse(message);
            var root = jsonDoc.RootElement;
            
            // Check if this is actually a failure event (has Reason field)
            if (root.TryGetProperty("Reason", out _))
            {
                // This is a failure event, handle it as such
                await HandleRentalFailedAsync(message);
                return;
            }

            var rentalEvent = JsonSerializer.Deserialize<RentalCompletedEvent>(message);
            if (rentalEvent == null)
            {
                Console.WriteLine($"[SagaOrchestrator] Failed to deserialize rental completed event: {message}");
                return;
            }

            var sagaId = rentalEvent.SagaId;
            if (!_sagaStates.TryGetValue(sagaId, out var sagaState))
            {
                Console.WriteLine($"[SagaOrchestrator] Saga {sagaId} not found for rental completed event");
                return;
            }

            sagaState.Status = SagaStatus.RentalCompleted;
            sagaState.RentalId = rentalEvent.RentalId;
            sagaState.UpdatedAt = DateTime.UtcNow;
            sagaState.Version++;

            await _eventStore.AppendEventAsync(sagaId, "RentalCompleted", rentalEvent, version: sagaState.Version);

            // Trigger notifications
            await TriggerNotificationsAsync(sagaId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SagaOrchestrator] Error handling rental completed: {ex.Message}");
        }
    }

    public async Task HandleRentalFailedAsync(string message)
    {
        try
        {
            var failureEvent = JsonSerializer.Deserialize<RentalFailedEvent>(message);
            if (failureEvent == null)
            {
                Console.WriteLine($"[SagaOrchestrator] Failed to deserialize rental failed event: {message}");
                return;
            }

            var sagaId = failureEvent.SagaId;
            if (!_sagaStates.TryGetValue(sagaId, out var sagaState))
            {
                Console.WriteLine($"[SagaOrchestrator] Saga {sagaId} not found for rental failed event");
                return;
            }

            sagaState.Status = SagaStatus.Compensating;
            sagaState.FailureReason = failureEvent.Reason;
            sagaState.UpdatedAt = DateTime.UtcNow;
            sagaState.Version++;

            await _eventStore.AppendEventAsync(sagaId, "RentalFailed", failureEvent, version: sagaState.Version);
            await _eventStore.AppendEventAsync(sagaId, "SagaCompensating", new { SagaId = sagaId, Reason = failureEvent.Reason }, version: sagaState.Version + 1);

            // Compensate payment and booking
            await CompensatePaymentAsync(sagaId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SagaOrchestrator] Error handling rental failed: {ex.Message}");
        }
    }

    private Task CompensatePaymentAsync(Guid sagaId)
    {
        var sagaState = _sagaStates[sagaId];
        var compensateRequest = new
        {
            SagaId = sagaId,
            PaymentId = sagaState.PaymentId
        };

        _rabbitMQ.PublishEvent("saga.payment.compensate", compensateRequest);
        Console.WriteLine($"[Saga {sagaId}] Triggered payment compensation");
        return Task.CompletedTask;
    }

    public async Task HandlePaymentCompensatedAsync(string message)
    {
        try
        {
            var compensateEvent = JsonSerializer.Deserialize<PaymentCompensatedEvent>(message);
            if (compensateEvent == null)
            {
                Console.WriteLine($"[SagaOrchestrator] Failed to deserialize payment compensated event: {message}");
                return;
            }

            var sagaId = compensateEvent.SagaId;
            if (!_sagaStates.TryGetValue(sagaId, out var sagaState))
            {
                Console.WriteLine($"[SagaOrchestrator] Saga {sagaId} not found for payment compensated event");
                return;
            }

            sagaState.Version++;
            await _eventStore.AppendEventAsync(sagaId, "PaymentCompensated", compensateEvent, version: sagaState.Version);

            // Continue with booking compensation
            await CompensateBookingAsync(sagaId);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SagaOrchestrator] Error handling payment compensated: {ex.Message}");
        }
    }

    private Task TriggerNotificationsAsync(Guid sagaId)
    {
        var sagaState = _sagaStates[sagaId];
        sagaState.Status = SagaStatus.NotificationsInProgress;
        sagaState.UpdatedAt = DateTime.UtcNow;

        // Send shop notification
        var shopNotification = new
        {
            SagaId = sagaId,
            Recipient = "shop@example.com",
            Type = "ShopNotification",
            Subject = "New Service Booking",
            Message = $"New booking for {sagaState.ServiceType} on {sagaState.TimeSlot}"
        };

        _rabbitMQ.PublishEvent("saga.notification.requested", shopNotification);

        // Send customer notification
        var customerNotification = new
        {
            SagaId = sagaId,
            Recipient = $"customer{sagaState.CustomerId}@example.com",
            Type = "CustomerNotification",
            Subject = "Service Booking Confirmed",
            Message = $"Your {sagaState.ServiceType} service is booked for {sagaState.TimeSlot}. Rental car booked."
        };

        _rabbitMQ.PublishEvent("saga.notification.requested", customerNotification);
        Console.WriteLine($"[Saga {sagaId}] Triggered notifications");
        return Task.CompletedTask;
    }

    public async Task HandleNotificationsCompletedAsync(string message)
    {
        try
        {
            var notificationEvent = JsonSerializer.Deserialize<NotificationsCompletedEvent>(message);
            if (notificationEvent == null)
            {
                Console.WriteLine($"[SagaOrchestrator] Failed to deserialize notifications completed event: {message}");
                return;
            }

            var sagaId = notificationEvent.SagaId;
            if (!_sagaStates.TryGetValue(sagaId, out var sagaState))
            {
                Console.WriteLine($"[SagaOrchestrator] Saga {sagaId} not found for notifications completed event");
                return;
            }

            // Prevent duplicate processing - check if already completed
            if (sagaState.Status == SagaStatus.Completed)
            {
                Console.WriteLine($"[SagaOrchestrator] Saga {sagaId} is already completed, ignoring duplicate notifications.completed event");
                return;
            }

            sagaState.Status = SagaStatus.Completed;
            sagaState.UpdatedAt = DateTime.UtcNow;
            sagaState.Version++;

            await _eventStore.AppendEventAsync(sagaId, "NotificationsCompleted", notificationEvent, version: sagaState.Version);
            await _eventStore.AppendEventAsync(sagaId, "SagaCompleted", new { SagaId = sagaId, CompletedAt = DateTime.UtcNow }, version: sagaState.Version + 1);

            _rabbitMQ.PublishEvent("saga.completed", new { SagaId = sagaId });
            Console.WriteLine($"[Saga {sagaId}] Saga completed successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SagaOrchestrator] Error handling notifications completed: {ex.Message}");
        }
    }

    public async Task<SagaState?> GetSagaStateAsync(Guid sagaId)
    {
        // Check in-memory state first
        if (_sagaStates.TryGetValue(sagaId, out var state))
        {
            return state;
        }

        // If not in memory, try to reconstruct from event store
        try
        {
            Console.WriteLine($"[SagaOrchestrator] Reconstructing saga {sagaId} from event store...");
            var events = await _eventStore.GetEventsByAggregateIdAsync(sagaId);
            if (events == null || events.Count == 0)
            {
                Console.WriteLine($"[SagaOrchestrator] No events found for saga {sagaId}");
                return null;
            }
            
            Console.WriteLine($"[SagaOrchestrator] Found {events.Count} events for saga {sagaId}");

            // Reconstruct saga state from events
            var sagaState = new SagaState
            {
                SagaId = sagaId,
                Version = 0
            };

            // First pass: collect the actual failure reason from failure events (PaymentFailed, BookingFailed, RentalFailed)
            string? originalFailureReason = null;
            foreach (var evt in events.OrderBy(e => e.Version))
            {
                var eventData = JsonDocument.Parse(evt.EventData);
                var root = eventData.RootElement;

                // Extract failure reason from actual failure events
                if (evt.EventType == "PaymentFailed" || evt.EventType == "BookingFailed" || evt.EventType == "RentalFailed")
                {
                    if (root.TryGetProperty("Reason", out var reasonProp))
                    {
                        var reason = reasonProp.GetString();
                        if (!string.IsNullOrEmpty(reason) && reason != "Compensation completed")
                        {
                            originalFailureReason = reason;
                        }
                    }
                }
            }

            // Second pass: reconstruct state
            foreach (var evt in events.OrderBy(e => e.Version))
            {
                var eventData = JsonDocument.Parse(evt.EventData);
                var root = eventData.RootElement;

                // Update saga state based on event type
                switch (evt.EventType)
                {
                    case "SagaStarted":
                        if (root.TryGetProperty("CustomerId", out var customerIdProp))
                            sagaState.CustomerId = customerIdProp.GetGuid();
                        if (root.TryGetProperty("TimeSlot", out var timeSlotProp))
                            sagaState.TimeSlot = timeSlotProp.GetDateTime();
                        if (root.TryGetProperty("ServiceType", out var serviceTypeProp))
                            sagaState.ServiceType = serviceTypeProp.GetString() ?? string.Empty;
                        if (root.TryGetProperty("Price", out var priceProp))
                            sagaState.Price = priceProp.GetDecimal();
                        sagaState.CreatedAt = evt.Timestamp;
                        sagaState.Status = SagaStatus.Started;
                        break;

                    case "BookingCompleted":
                        if (root.TryGetProperty("BookingId", out var bookingIdProp))
                            sagaState.BookingId = bookingIdProp.GetGuid();
                        sagaState.Status = SagaStatus.BookingCompleted;
                        break;

                    case "BookingFailed":
                        if (root.TryGetProperty("Reason", out var reasonProp))
                        {
                            var reason = reasonProp.GetString();
                            sagaState.FailureReason = reason ?? "Booking failed";
                        }
                        sagaState.Status = SagaStatus.Failed;
                        break;

                    case "PaymentCompleted":
                        if (root.TryGetProperty("PaymentId", out var paymentIdProp))
                            sagaState.PaymentId = paymentIdProp.GetGuid();
                        sagaState.Status = SagaStatus.PaymentCompleted;
                        break;

                    case "PaymentFailed":
                        if (root.TryGetProperty("Reason", out var paymentReasonProp))
                        {
                            var paymentReason = paymentReasonProp.GetString();
                            sagaState.FailureReason = paymentReason ?? "Payment failed";
                        }
                        sagaState.Status = SagaStatus.Compensating;
                        break;

                    case "RentalCompleted":
                        if (root.TryGetProperty("RentalId", out var rentalIdProp))
                            sagaState.RentalId = rentalIdProp.GetGuid();
                        sagaState.Status = SagaStatus.RentalCompleted;
                        break;

                    case "RentalFailed":
                        if (root.TryGetProperty("Reason", out var rentalReasonProp))
                        {
                            var rentalReason = rentalReasonProp.GetString();
                            sagaState.FailureReason = rentalReason ?? "Rental failed";
                        }
                        sagaState.Status = SagaStatus.Compensating;
                        break;

                    case "NotificationsCompleted":
                        sagaState.Status = SagaStatus.Completed;
                        break;

                    case "SagaCompleted":
                        sagaState.Status = SagaStatus.Completed;
                        break;

                    case "SagaFailed":
                        sagaState.Status = SagaStatus.Failed;
                        // Preserve the failure reason if already set from PaymentFailed/BookingFailed/RentalFailed
                        // Otherwise, use the original failure reason from first pass, or the SagaFailed reason (but not "Compensation completed")
                        if (string.IsNullOrEmpty(sagaState.FailureReason))
                        {
                            if (!string.IsNullOrEmpty(originalFailureReason))
                            {
                                sagaState.FailureReason = originalFailureReason;
                            }
                            else if (root.TryGetProperty("Reason", out var sagaReasonProp))
                            {
                                var sagaReason = sagaReasonProp.GetString();
                                if (sagaReason != "Compensation completed" && !string.IsNullOrEmpty(sagaReason))
                                {
                                    sagaState.FailureReason = sagaReason;
                                }
                            }
                        }
                        // If sagaState.FailureReason is already set (from PaymentFailed/BookingFailed/RentalFailed), keep it
                        break;

                    case "SagaCompensating":
                        sagaState.Status = SagaStatus.Compensating;
                        break;
                }

                sagaState.Version = evt.Version;
                sagaState.UpdatedAt = evt.Timestamp;
            }

            // Store in memory for future lookups
            _sagaStates[sagaId] = sagaState;
            Console.WriteLine($"[SagaOrchestrator] Successfully reconstructed saga {sagaId} with status {sagaState.Status}, failure reason: {sagaState.FailureReason ?? "null"}");
            return sagaState;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SagaOrchestrator] Error reconstructing saga state from event store: {ex.Message}");
            Console.WriteLine($"[SagaOrchestrator] Stack trace: {ex.StackTrace}");
            return null;
        }
    }

    public SagaState? GetSagaState(Guid sagaId)
    {
        // For synchronous access, check in-memory only
        return _sagaStates.TryGetValue(sagaId, out var state) ? state : null;
    }

    public async Task<List<SagaState>> GetAllSagasAsync()
    {
        var allSagas = new List<SagaState>();
        
        // Get all aggregate IDs from event store
        var aggregateIds = await _eventStore.GetAllAggregateIdsAsync();
        
        foreach (var sagaId in aggregateIds)
        {
            var sagaState = await GetSagaStateAsync(sagaId);
            if (sagaState != null)
            {
                allSagas.Add(sagaState);
            }
        }
        
        return allSagas;
    }

    public async Task<bool> RecoverStuckPaymentAsync(Guid sagaId)
    {
        // Check if saga exists and is stuck in PaymentInProgress or BookingCompleted without payment
        var sagaState = await GetSagaStateAsync(sagaId);
        if (sagaState == null)
        {
            return false;
        }
        
        // Check if saga is stuck: BookingCompleted but no PaymentId, or PaymentInProgress
        if (sagaState.Status != SagaStatus.PaymentInProgress && 
            (sagaState.Status != SagaStatus.BookingCompleted || sagaState.PaymentId != null))
        {
            return false;
        }

        // Try to find payment completed event from payment service database
        // For now, we'll manually trigger the payment completed handler
        // by checking if payment was processed but event was lost
        
        // Since we can't directly query payment service DB from orchestrator,
        // we'll create a synthetic payment completed event based on the saga state
        // This is a recovery mechanism for lost events
        
        Console.WriteLine($"[SagaOrchestrator] Attempting to recover stuck payment for saga {sagaId}");
        
        // Check if we have booking ID (payment should have been triggered)
        if (sagaState.BookingId == null)
        {
            Console.WriteLine($"[SagaOrchestrator] Cannot recover - no booking ID for saga {sagaId}");
            return false;
        }

        // Create a recovery payment completed event
        // Note: This assumes payment was processed but event was lost
        // In production, you'd query the payment service to verify payment status
        var recoveryEvent = new
        {
            SagaId = sagaId,
            PaymentId = Guid.NewGuid(), // Generate new payment ID for recovery
            BookingId = sagaState.BookingId,
            Amount = sagaState.Price,
            Currency = "USD",
            Status = "Processed",
            ProcessedAt = DateTime.UtcNow,
            Recovered = true // Mark as recovered
        };

        // Process the payment completed event
        var message = System.Text.Json.JsonSerializer.Serialize(recoveryEvent);
        await HandlePaymentCompletedAsync(message);
        
        Console.WriteLine($"[SagaOrchestrator] Successfully recovered payment for saga {sagaId}");
        return true;
    }
}

// Event Models
public record StartSagaRequest(
    Guid CustomerId,
    DateTime TimeSlot,
    string ServiceType,
    decimal Price,
    bool? SimulateBookingFailure = null,
    bool? SimulatePaymentFailure = null,
    bool? SimulateRentalFailure = null,
    bool? SimulateTimeout = null
);

public record BookingCompletedEvent(
    Guid SagaId,
    Guid BookingId,
    Guid CustomerId,
    DateTime TimeSlot,
    string ServiceType,
    DateTime? CreatedAt = null
);

public record BookingFailedEvent(
    Guid SagaId,
    string Reason
);

public record PaymentCompletedEvent(
    Guid SagaId,
    Guid PaymentId,
    Guid BookingId,
    decimal Amount
);

public record PaymentFailedEvent(
    Guid SagaId,
    string Reason
);

public record RentalCompletedEvent(
    Guid SagaId,
    Guid RentalId,
    Guid BookingId
);

public record RentalFailedEvent(
    Guid SagaId,
    string Reason
);

public record BookingCompensatedEvent(
    Guid SagaId,
    Guid BookingId
);

public record PaymentCompensatedEvent(
    Guid SagaId,
    Guid PaymentId
);

public record NotificationsCompletedEvent(
    Guid SagaId
);

