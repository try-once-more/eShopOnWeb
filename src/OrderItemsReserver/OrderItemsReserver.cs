using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace OrderItemsReserver;
public class OrderItemsReserver(BlobContainerClient blobContainer, NotificationService notificationService, ILogger<OrderItemsReserver> logger)
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

    [Function(nameof(OrderItemsReserver))]
    public async Task Run([ServiceBusTrigger("%AzureServiceBus:Queue%", Connection = "AzureServiceBus:ConnectionString")]
        ServiceBusReceivedMessage message,
        ServiceBusMessageActions messageActions)
    {
        var messageId = message.MessageId;
        try
        {
            var order = JsonSerializer.Deserialize<Order>(message.Body, _jsonSerializerOptions)
                ?? throw new InvalidDataException("Invalid message: missing or invalid 'id' property");

            var validationResults = new List<ValidationResult>();
            bool isValid = Validator.TryValidateObject(order, new ValidationContext(order), validationResults);
            if (!isValid)
                throw new InvalidDataException($"Order validation failed: {string.Join("; ", validationResults.Select(vr => vr.ErrorMessage))}");

            logger.LogInformation("MessageId {messageId}, Order {orderId} received", messageId, order.Id);
            var blob = blobContainer.GetBlobClient($"Order_{order.Id}.json");
            await blob.UploadAsync(message.Body, overwrite: true);
            logger.LogInformation("MessageId {messageId}, Order {blobName} sent for reservation", messageId, blob.Name);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            await notificationService.NotifyAsync(messageId, ex);
            await messageActions.DeadLetterMessageAsync(message);
        }
    }
}

internal partial class Order : IValidatableObject
{
    [GeneratedRegex("^[a-zA-Z0-9_-]+$")]
    private static partial Regex IdValidationRegex();

    [Required(ErrorMessage = "Id is required")]
    [MaxLength(50, ErrorMessage = "Id cannot be longer than 50 characters")]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!IdValidationRegex().IsMatch(Id))
        {
            yield return new ValidationResult("Id contains invalid characters");
        }
    }
}
