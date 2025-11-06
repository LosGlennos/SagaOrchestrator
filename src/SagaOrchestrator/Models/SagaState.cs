namespace SagaOrchestrator.Models;

public enum SagaStatus
{
    Started,
    BookingInProgress,
    BookingCompleted,
    PaymentInProgress,
    PaymentCompleted,
    RentalInProgress,
    RentalCompleted,
    NotificationsInProgress,
    Completed,
    Compensating,
    Failed
}

public class SagaState
{
    public Guid SagaId { get; set; }
    public SagaStatus Status { get; set; }
    public Guid? BookingId { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? RentalId { get; set; }
    public Guid CustomerId { get; set; }
    public DateTime TimeSlot { get; set; }
    public string ServiceType { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int Version { get; set; }
    public bool? SimulatePaymentFailure { get; set; }
    public bool? SimulateRentalFailure { get; set; }
    public bool? SimulateTimeout { get; set; }
}

