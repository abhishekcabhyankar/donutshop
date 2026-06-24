# Google Pay via Authorize.NET — Technical Build Guide

How Google Pay was added to **Sweet Ring Donuts** (ASP.NET Core 8 MVC) using
Authorize.NET as the payment gateway, deployed on AWS Elastic Beanstalk behind
CloudFront at <https://shop.poseidon-team-donuts-shop.com>.

This document records every technical step, the code, the configuration, and the
real debugging journey — including the `Invalid ownership` error and its fix.

---

## 1. Two ways to integrate Google Pay with Authorize.NET

Google Pay encrypts the shopper's card data into an opaque **payment token**. Some
party must hold the private key that decrypts that token. Authorize.NET supports
**two** integration models, and the difference is *who owns the decryption keys*:

| | **PAYMENT_GATEWAY** (what we shipped) | **DIRECT** |
|---|---|---|
| `tokenizationSpecification.type` | `PAYMENT_GATEWAY` | `DIRECT` |
| Who decrypts the token | Authorize.NET (their keys are already registered with Google) | You/Authorize.NET via a key **you** generate and upload to Google |
| Keys to manage | **None** | Generate a key set in the MINT portal, download the public key, register it with Google |
| `parameters` sent to Google | `gateway: "authorizenet"`, `gatewayMerchantId: "<Payment Gateway ID>"` | `protocolVersion: "ECv2"`, `publicKey: "<your EC public key>"` |
| Complexity | Low | High |

> **We use PAYMENT_GATEWAY.** It is the method documented in Authorize.NET's
> [in-app integration guide](https://developer.authorize.net/api/reference/features/in-app.html)
> and requires no key management — Authorize.NET is already a registered Google Pay
> gateway, so Google encrypts directly for them.

### About the MINT "Manage Your Google Pay Keys" page

During setup we also generated a **Key Set ID** and could *Download Key* from the
Authorize.NET MINT portal (`Tools → Digital Payment Solutions → Google Pay →
Manage Your Google Pay Keys`). That page exists to support the **DIRECT** model:
you download Authorize.NET's public key and register it with Google so Google
encrypts tokens for that specific key set.

**For our PAYMENT_GATEWAY integration this step is not required.** Generating the
key set does no harm, but the working transaction path never uses it — the token
is owned by the account's **Payment Gateway ID** instead (see §6). The remainder of
this guide documents the PAYMENT_GATEWAY path that is live and approved.

---

## 2. Architecture

```
Browser (Chrome / Android)
  │  pay.js  →  google.payments.api.PaymentsClient
  │  tokenizationSpecification = { gateway: "authorizenet",
  │                                gatewayMerchantId: "<Payment Gateway ID>" }
  ▼
Google Pay   ──encrypts card → opaque token──►
  │
  ▼  (token base64-encoded in the browser)
ASP.NET Core  POST /Checkout
  │  dataDescriptor = "COMMON.GOOGLE.INAPP.PAYMENT"
  │  dataValue      = btoa(token)
  ▼
AuthorizeNetPaymentService.ChargeAsync()
  │  createTransactionRequest → payment.opaqueData { dataDescriptor, dataValue }
  ▼
Authorize.NET  ──decrypts with their keys (owner = Payment Gateway ID)──►  Approved
```

Key insight: Google Pay reuses the **exact same `opaqueData` charge path** as
Accept.js (card) and Apple Pay. Only the `dataDescriptor` differs. The server-side
`ChargeAsync` needed **no changes** at all.

| Wallet | `dataDescriptor` |
|---|---|
| Card (Accept.js) | `COMMON.ACCEPT.INAPP.PAYMENT` |
| Apple Pay | `COMMON.APPLE.INAPP.PAYMENT` |
| Google Pay | `COMMON.GOOGLE.INAPP.PAYMENT` |

---

## 3. Prerequisites and the three identifiers

Google Pay + Authorize.NET PAYMENT_GATEWAY needs **three** distinct identifiers.
Mixing them up is the #1 cause of failures (see §6).

| Identifier | Example | Where it comes from | Used as |
|---|---|---|---|
| **Authorize.NET API Login ID** | `46ALvm8N8K` | Merchant Interface → Security Settings → API Credentials | Authenticates the `createTransactionRequest` (server) and Accept.js (card) |
| **Authorize.NET Payment Gateway ID** | `949217` | Merchant Interface → Account, or ["What Is My Payment Gateway ID?"](https://support.authorize.net/s/article/What-Is-My-Payment-Gateway-ID) | Google Pay `gatewayMerchantId` |
| **Google Pay merchant ID** | `BCR2DN7TTDPO56JI` | [Google Pay & Wallet Console](https://pay.google.com/business/console) | Google Pay `merchantInfo.merchantId` (PRODUCTION only) |

> ⚠️ **The Payment Gateway ID is NOT the API Login ID.** Google Pay's
> `gatewayMerchantId` must be the numeric **Payment Gateway ID**.

---

## 4. Setup steps (portals)

### 4.1 Google Pay & Wallet Console
1. Sign in to the [Google Pay & Wallet Console](https://pay.google.com/business/console).
2. Create a **Business Profile** (required before going to production).
3. Note your **Google merchant ID** (`BCR2DN…` format) → this becomes
   `merchantInfo.merchantId`. It is only validated in `PRODUCTION`; `TEST` ignores it.

### 4.2 Authorize.NET MINT portal (optional for PAYMENT_GATEWAY)
- `Tools → Digital Payment Solutions → Google Pay → Manage Your Google Pay Keys`
  shows a **Key Set ID** and a *Download Key* button. This is the **DIRECT**-model
  key material. We generated it but **do not use it** for PAYMENT_GATEWAY.

### 4.3 Find your Payment Gateway ID
- Merchant Interface → **Account**, or follow
  ["What Is My Payment Gateway ID?"](https://support.authorize.net/s/article/What-Is-My-Payment-Gateway-ID).
- Ours is **`949217`**. This is the value Google Pay tags the token's ownership with,
  and the value Authorize.NET checks when decrypting.

---

## 5. Application code

### 5.1 Options — `Models/GooglePayOptions.cs`
```csharp
public class GooglePayOptions
{
    public const string SectionName = "GooglePay";

    public bool   Enabled           { get; set; }
    public string Environment       { get; set; } = "TEST";   // TEST | PRODUCTION
    public string MerchantName      { get; set; } = "Sweet Ring Donuts";
    public string MerchantId        { get; set; } = string.Empty; // Google merchant ID (PROD only)
    public string GatewayMerchantId { get; set; } = string.Empty; // Authorize.NET Payment Gateway ID

    public bool   IsProduction      => string.Equals(Environment, "PRODUCTION", StringComparison.OrdinalIgnoreCase);
    public string ClientEnvironment => IsProduction ? "PRODUCTION" : "TEST";
    public bool   IsConfigured      => Enabled && (!IsProduction || !string.IsNullOrWhiteSpace(MerchantId));
}
```

### 5.2 Bind it — `Program.cs`
```csharp
builder.Services.Configure<GooglePayOptions>(
    builder.Configuration.GetSection(GooglePayOptions.SectionName));
```

### 5.3 Expose config to the view — `Controllers/CheckoutController.cs`
`PopulateClientConfig()` sets the ViewBag. Note the fallback: if no Payment Gateway
ID is configured, it falls back to the API Login ID (which is *wrong* for Google Pay
but keeps the old behaviour explicit).
```csharp
ViewBag.GooglePayEnabled =
    _googlePay.IsConfigured && !string.IsNullOrWhiteSpace(_authNet.ApiLoginId);
ViewBag.GooglePayEnvironment = _googlePay.ClientEnvironment;
ViewBag.GooglePayMerchantId  = _googlePay.MerchantId;        // Google merchant ID
ViewBag.GooglePayMerchantName = _googlePay.MerchantName;
ViewBag.GooglePayGatewayMerchantId =                        // Payment Gateway ID
    string.IsNullOrWhiteSpace(_googlePay.GatewayMerchantId)
        ? _authNet.ApiLoginId
        : _googlePay.GatewayMerchantId;
```

### 5.4 Client flow — `Views/Checkout/Index.cshtml`
Loaded only when `googlePayEnabled`:
```html
<script src="https://pay.google.com/gp/p/js/pay.js"></script>
```
```javascript
const tokenizationSpecification = {
    type: "PAYMENT_GATEWAY",
    parameters: {
        gateway: "authorizenet",
        gatewayMerchantId: config.gatewayMerchantId   // "949217" — NOT the API Login ID
    }
};
const baseCardPaymentMethod = {
    type: "CARD",
    parameters: {
        allowedAuthMethods: ["PAN_ONLY", "CRYPTOGRAM_3DS"],
        allowedCardNetworks: ["AMEX", "DISCOVER", "MASTERCARD", "VISA"]
    }
};
const cardPaymentMethod = Object.assign({}, baseCardPaymentMethod,
    { tokenizationSpecification });

const paymentsClient = new google.payments.api.PaymentsClient({
    environment: config.environment   // "TEST" or "PRODUCTION"
});

// 1. Show the button only if the device/browser can pay.
paymentsClient.isReadyToPay({ apiVersion: 2, apiVersionMinor: 0,
    allowedPaymentMethods: [baseCardPaymentMethod] })
  .then(r => { if (r.result) { /* createButton → append → reveal */ } });

// 2. On click, request the token.
paymentsClient.loadPaymentData({
    apiVersion: 2, apiVersionMinor: 0,
    allowedPaymentMethods: [cardPaymentMethod],
    transactionInfo: { totalPriceStatus: "FINAL", totalPrice: config.amount,
                       currencyCode: "USD", countryCode: "US" },
    merchantInfo: { merchantName: config.merchantName, merchantId: config.merchantId }
}).then(paymentData => {
    const token = paymentData.paymentMethodData.tokenizationData.token;
    document.getElementById("dataDescriptor").value = "COMMON.GOOGLE.INAPP.PAYMENT";
    document.getElementById("dataValue").value = btoa(token);   // base64 the token
    document.getElementById("paymentForm").submit();            // reuse the card POST
});
```

### 5.5 Charge path — `Services/AuthorizeNetPaymentService.cs` (UNCHANGED)
The hidden `dataDescriptor` / `dataValue` fields are the same ones the card flow
uses, so the existing `[HttpPost] Index` action and `ChargeAsync` handle Google Pay
with no new code:
```jsonc
"payment": {
  "opaqueData": {
    "dataDescriptor": "COMMON.GOOGLE.INAPP.PAYMENT",
    "dataValue": "<base64 Google Pay token>"
  }
}
```

---

## 6. The `Invalid ownership` bug (root cause + fix)

**Symptom:** Google Pay returned a token, the charge posted, and Authorize.NET
replied:

> There was an error processing the payment data. **Invalid ownership.**

**Diagnosis:** The token reached Authorize.NET as well-formed `opaqueData`, so the
descriptor and base64 encoding were correct. "Invalid ownership" means the token's
declared owner did not match a valid merchant. We had been sending the **API Login
ID** (`46ALvm8N8K`) as `gatewayMerchantId`.

**Fix:** Authorize.NET's documentation requires the **Payment Gateway ID**
(`949217`) there instead. After setting `GooglePay__GatewayMerchantId=949217` and
redeploying, the very next transaction was **Approved**.

```
Amount charged   $1.99
Transaction ID   120085226477
Status           Approved
```

**Lesson:** `gatewayMerchantId` = Payment Gateway ID, *never* the API Login ID.

---

## 7. Configuration & deployment

### 7.1 `appsettings.json` (safe defaults; disabled)
```jsonc
"GooglePay": {
  "Enabled": false,
  "Environment": "TEST",
  "MerchantName": "Sweet Ring Donuts",
  "MerchantId": "",
  "GatewayMerchantId": ""
}
```

### 7.2 Elastic Beanstalk environment variables (`deploy-aws.sh`)
Real values are injected at deploy time, never committed:
```bash
eb setenv \
  GooglePay__Enabled="true" \
  GooglePay__Environment="TEST" \
  GooglePay__MerchantName="Sweet Ring Donuts" \
  GooglePay__MerchantId="BCR2DN7TTDPO56JI" \
  GooglePay__GatewayMerchantId="949217"
```
A code change (new options/view logic) requires a full `bash deploy-aws.sh` to
publish; a value-only change can be applied with a single `eb setenv`.

### 7.3 Verifying the live config
```bash
curl -s "https://shop.poseidon-team-donuts-shop.com/Checkout" \
  | grep -oE 'environment: "[^"]*"|gateway: "[^"]*"|gatewayMerchantId: "[^"]*"'
# environment: "TEST"
# gateway: "authorizenet"
# gatewayMerchantId: "949217"
```

---

## 8. Environments must match

Google Pay's environment must line up with Authorize.NET's:

| Authorize.NET | Google Pay `environment` | Result |
|---|---|---|
| Sandbox (`apitest.authorize.net`) | `TEST` | ✅ Works (with correct gateway ID). Test tokens, no real money. |
| Production (`api.authorize.net`) | `PRODUCTION` | ✅ Real charges |
| Sandbox | `PRODUCTION` | ⚠️ Environment mismatch — avoid |
| Production | `TEST` | ⚠️ Environment mismatch — avoid |

**Current state:** Authorize.NET **sandbox** ↔ Google Pay **`TEST`**, gateway ID
`949217`. Verified Approved.

### Going fully live
Flip *both* sides together:
1. Authorize.NET → **production** credentials (`AuthorizeNet__Environment=Production`
   + production API Login ID / Transaction Key / Public Client Key).
2. `GooglePay__Environment=PRODUCTION` (the Google merchant ID `BCR2DN7TTDPO56JI` is
   then required and is already set).
3. `GooglePay__GatewayMerchantId` stays `949217`.

---

## 9. Notes & gotchas

- **Browser support:** Google Pay works in Chrome / Android / most browsers (unlike
  Apple Pay, which is Safari + Apple-hardware only). The button only appears for a
  real signed-in Google account with a saved card on an HTTPS origin — it will not
  render in a plain `curl` or a logged-out session.
- **No certificates / no domain-association file:** Unlike Apple Pay, Google Pay (in
  PAYMENT_GATEWAY mode) needs no merchant certificate, no mutual-TLS validation
  endpoint, and no `.well-known` file.
- **Secrets:** No keys or secrets are committed. The Google merchant ID and Payment
  Gateway ID are account identifiers (not secrets) injected via EB env vars.
