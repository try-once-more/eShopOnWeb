using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace DeliveryOrderProcessor;

public class DeliveryOrderProcessor(ILogger<DeliveryOrderProcessor> logger, Container container)
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new() { PropertyNameCaseInsensitive = true };

    [Function(nameof(DeliveryOrderProcessor))]
    public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest request)
    {
        try
        {
            var order = await JsonSerializer.DeserializeAsync<Order>(request.Body, _jsonSerializerOptions);
            if (order is null)
                return new BadRequestObjectResult("Invalid or empty JSON payload");

            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(order, new ValidationContext(order), results, validateAllProperties: true))
                return new BadRequestObjectResult(results);

            logger.LogInformation("Order {orderId} received", order.Id);
            await container.CreateItemAsync(order, new PartitionKey(order.CustomerId));
            logger.LogInformation("Order {orderId} saved", order.Id);

            return new OkResult();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            return new ObjectResult($"Internal error: {ex.Message}")
            { 
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }
}

file class Order : IValidatableObject
{
    [Required]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [Required]
    [JsonPropertyName("customerId")]
    public required string CustomerId { get; init; }

    [Required]
    [JsonPropertyName("address")]
    public required Address Address { get; init; }

    [Range(0, double.MaxValue)]
    [JsonPropertyName("totalAmount")]
    public decimal TotalAmount { get; init; }

    [Required]
    [MinLength(1)]
    [JsonPropertyName("items")]
    public required List<OrderItem> Items { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(Address, new ValidationContext(Address), results, validateAllProperties: true);
        Items.ForEach(item => Validator.TryValidateObject(item, new ValidationContext(item), results, true));
        return results;
    }
}

file class OrderItem
{
    [Required]
    [Range(1, long.MaxValue)]
    [JsonPropertyName("itemId")]
    public long ItemId { get; init; }

    [Range(0, int.MaxValue)]
    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }

    [Range(0, double.MaxValue)]
    [JsonPropertyName("amount")]
    public decimal Amount { get; init; }
}

file class Address
{
    [Required]
    [JsonPropertyName("street")]
    public required string Street { get; init; }

    [Required]
    [JsonPropertyName("city")]
    public required string City { get; init; }

    [Required]
    [JsonPropertyName("state")]
    public required string State { get; init; }

    [Required]
    [JsonPropertyName("country")]
    public required string Country { get; init; }

    [Required]
    [JsonPropertyName("zipCode")]
    public required string ZipCode { get; init; }
}

