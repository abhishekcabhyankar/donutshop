namespace DonutShop.Models;

public class ApplePayOptions
{
    public const string SectionName = "ApplePay";

    /// <summary>Master switch. When false the Apple Pay button is never shown and the
    /// merchant-validation endpoint refuses to run.</summary>
    public bool Enabled { get; set; }

    /// <summary>The Apple Pay Merchant Identifier, e.g. "merchant.com.yourdomain.sweetring".</summary>
    public string MerchantIdentifier { get; set; } = string.Empty;

    /// <summary>Name shown in the Apple Pay sheet.</summary>
    public string DisplayName { get; set; } = "Sweet Ring Donuts";

    /// <summary>Fully-qualified domain registered with Apple (the page Apple Pay runs on).</summary>
    public string DomainName { get; set; } = string.Empty;

    /// <summary>Path to the Apple Pay Merchant Identity certificate (PEM) used for the
    /// mutual-TLS call to Apple's merchant-validation server.</summary>
    public string MerchantIdCertPath { get; set; } = string.Empty;

    /// <summary>Path to the matching private key (PEM) for the Merchant Identity certificate.</summary>
    public string MerchantIdKeyPath { get; set; } = string.Empty;

    /// <summary>True only when the master switch is on AND the certificate paths are configured.</summary>
    public bool IsConfigured =>
        Enabled &&
        !string.IsNullOrWhiteSpace(MerchantIdentifier) &&
        !string.IsNullOrWhiteSpace(DomainName) &&
        !string.IsNullOrWhiteSpace(MerchantIdCertPath) &&
        !string.IsNullOrWhiteSpace(MerchantIdKeyPath);
}
