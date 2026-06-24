namespace DonutShop.Models;

public class GooglePayOptions
{
    public const string SectionName = "GooglePay";

    /// <summary>Master switch. When false the Google Pay button is never shown.</summary>
    public bool Enabled { get; set; }

    /// <summary>"TEST" (sandbox) or "PRODUCTION".</summary>
    public string Environment { get; set; } = "TEST";

    /// <summary>Name shown in the Google Pay sheet.</summary>
    public string MerchantName { get; set; } = "Sweet Ring Donuts";

    /// <summary>Google Pay merchant ID from the Google Pay &amp; Wallet Console.
    /// Required only in PRODUCTION; ignored in TEST.</summary>
    public string MerchantId { get; set; } = string.Empty;

    /// <summary>The Authorize.NET account's numeric Payment Gateway ID, used as the
    /// Google Pay tokenizationSpecification gatewayMerchantId. This is NOT the API
    /// Login ID. Find it in the Merchant Interface (see "What Is My Payment Gateway ID?").
    /// If left blank, the caller falls back to the API Login ID.</summary>
    public string GatewayMerchantId { get; set; } = string.Empty;

    public bool IsProduction =>
        string.Equals(Environment, "PRODUCTION", StringComparison.OrdinalIgnoreCase);

    /// <summary>The Google Pay JS environment value ("TEST" or "PRODUCTION").</summary>
    public string ClientEnvironment => IsProduction ? "PRODUCTION" : "TEST";

    /// <summary>True when enabled and (in production) a Google merchant ID is present.</summary>
    public bool IsConfigured =>
        Enabled && (!IsProduction || !string.IsNullOrWhiteSpace(MerchantId));
}
