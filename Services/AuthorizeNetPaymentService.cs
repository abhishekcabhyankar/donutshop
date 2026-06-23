using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DonutShop.Models;
using Microsoft.Extensions.Options;

namespace DonutShop.Services;

public record PaymentResult(bool Success, string? TransactionId, string? Message);

public interface IPaymentService
{
    Task<PaymentResult> ChargeAsync(
        decimal amount,
        string opaqueDataDescriptor,
        string opaqueDataValue,
        CheckoutViewModel customer,
        Cart cart,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Talks to the Authorize.NET JSON transaction API using the opaque payment
/// nonce produced client-side by Accept.js. Raw card data never touches this app.
/// </summary>
public class AuthorizeNetPaymentService : IPaymentService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly AuthorizeNetOptions _options;
    private readonly ILogger<AuthorizeNetPaymentService> _logger;

    public AuthorizeNetPaymentService(
        HttpClient http,
        IOptions<AuthorizeNetOptions> options,
        ILogger<AuthorizeNetPaymentService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PaymentResult> ChargeAsync(
        decimal amount,
        string opaqueDataDescriptor,
        string opaqueDataValue,
        CheckoutViewModel customer,
        Cart cart,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiLoginId) ||
            string.IsNullOrWhiteSpace(_options.TransactionKey))
        {
            return new PaymentResult(false, null,
                "Payment is not configured. Set AuthorizeNet credentials via user-secrets or environment variables.");
        }

        var request = new
        {
            createTransactionRequest = new
            {
                merchantAuthentication = new
                {
                    name = _options.ApiLoginId,
                    transactionKey = _options.TransactionKey,
                },
                refId = $"ord-{DateTime.UtcNow:yyyyMMddHHmmss}",
                transactionRequest = new
                {
                    transactionType = "authCaptureTransaction",
                    amount = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    payment = new
                    {
                        opaqueData = new
                        {
                            dataDescriptor = opaqueDataDescriptor,
                            dataValue = opaqueDataValue,
                        },
                    },
                    lineItems = new
                    {
                        lineItem = cart.Items.Take(30).Select(i => new
                        {
                            itemId = i.DonutId.ToString(),
                            name = Truncate(i.Name, 31),
                            quantity = i.Quantity.ToString(),
                            unitPrice = i.Price.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                        }).ToArray(),
                    },
                    customer = new { email = customer.Email },
                    billTo = new
                    {
                        firstName = Truncate(FirstName(customer.FullName), 50),
                        lastName = Truncate(LastName(customer.FullName), 50),
                        address = Truncate(customer.Address, 60),
                        city = Truncate(customer.City, 40),
                        state = Truncate(customer.State, 40),
                        zip = Truncate(customer.Zip, 20),
                    },
                },
            },
        };

        using var content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _http.PostAsync(_options.ApiEndpoint, content, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);

        // Authorize.NET prefixes JSON responses with a UTF-8 BOM that breaks parsers.
        raw = raw.TrimStart('\uFEFF', '\u200B', ' ', '\n', '\r', '\t');

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Authorize.NET HTTP {Status}: {Body}", (int)response.StatusCode, raw);
            return new PaymentResult(false, null, "Payment gateway returned an error. Please try again.");
        }

        try
        {
            var result = JsonSerializer.Deserialize<AuthNetResponse>(raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var tx = result?.TransactionResponse;
            if (tx is not null && tx.ResponseCode == "1")
            {
                return new PaymentResult(true, tx.TransId, "Approved");
            }

            var errorMessage =
                tx?.Errors?.FirstOrDefault()?.ErrorText
                ?? result?.Messages?.Message?.FirstOrDefault()?.Text
                ?? "The transaction was declined.";

            _logger.LogWarning("Authorize.NET declined: {Message} | {Body}", errorMessage, raw);
            return new PaymentResult(false, tx?.TransId, errorMessage);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Authorize.NET response: {Body}", raw);
            return new PaymentResult(false, null, "Unexpected response from the payment gateway.");
        }
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) || value.Length <= max ? value : value[..max];

    private static string FirstName(string fullName)
    {
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : fullName;
    }

    private static string LastName(string fullName)
    {
        var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : string.Empty;
    }

    // --- Minimal response shapes ---

    private sealed class AuthNetResponse
    {
        public TransactionResponse? TransactionResponse { get; set; }
        public MessagesEnvelope? Messages { get; set; }
    }

    private sealed class TransactionResponse
    {
        public string? ResponseCode { get; set; }
        public string? AuthCode { get; set; }
        public string? TransId { get; set; }
        public List<TransactionError>? Errors { get; set; }
    }

    private sealed class TransactionError
    {
        public string? ErrorCode { get; set; }
        public string? ErrorText { get; set; }
    }

    private sealed class MessagesEnvelope
    {
        public string? ResultCode { get; set; }
        public List<MessageItem>? Message { get; set; }
    }

    private sealed class MessageItem
    {
        public string? Code { get; set; }
        public string? Text { get; set; }
    }
}
