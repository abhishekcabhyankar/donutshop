namespace DonutShop.Models;

public class AuthorizeNetOptions
{
    public const string SectionName = "AuthorizeNet";

    /// <summary>"Sandbox" or "Production".</summary>
    public string Environment { get; set; } = "Sandbox";

    /// <summary>API Login ID (server + client). Set via user-secrets / env, never in source.</summary>
    public string ApiLoginId { get; set; } = string.Empty;

    /// <summary>Transaction Key (server only). Set via user-secrets / env, never in source.</summary>
    public string TransactionKey { get; set; } = string.Empty;

    /// <summary>Public Client Key for Accept.js (safe to expose in the browser).</summary>
    public string PublicClientKey { get; set; } = string.Empty;

    public bool IsProduction =>
        string.Equals(Environment, "Production", StringComparison.OrdinalIgnoreCase);

    public string ApiEndpoint => IsProduction
        ? "https://api.authorize.net/xml/v1/request.api"
        : "https://apitest.authorize.net/xml/v1/request.api";

    public string AcceptJsUrl => IsProduction
        ? "https://js.authorize.net/v1/Accept.js"
        : "https://jstest.authorize.net/v1/Accept.js";
}
