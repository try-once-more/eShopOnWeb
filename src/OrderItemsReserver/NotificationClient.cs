using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OrderItemsReserver;

public class NotificationClient(HttpClient httpClient, ILogger<NotificationClient> logger)
{
    public async Task NotifyAsync(string messageId, Exception exception)
    {
        try
        {
            var payload = new
            {
                Error = new
                {
                    Type = exception.GetType().FullName,
                    Message = exception.Message,
                    StackTrace = exception.StackTrace,
                },
                MessageId = messageId,
                Timestamp = DateTime.UtcNow
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, MediaTypeNames.Application.Json);
            var response = await httpClient.PostAsync(string.Empty, content);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
        }
    }
}
