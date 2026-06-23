# 🍩 Sweet Ring Donuts

A sample donut e-commerce website built with **ASP.NET Core 8 (MVC)** and integrated with
the **Authorize.NET** payment gateway. It supports both **Accept.js** (client-side card
tokenization) and **Apple Pay on the Web** — both routed through the *same* Authorize.NET
opaque-data charge path.

Raw card data is sent directly from the browser/device to Authorize.NET and **never touches
this server**, which keeps your PCI compliance scope to the minimum (SAQ A-EP).

> 📖 For the complete start-to-finish build story (GitHub, .NET, AWS, GoDaddy domain,
> certificates, Apple Pay, and every gotcha), see [docs/BUILD-GUIDE.md](docs/BUILD-GUIDE.md).

**Live:** <https://shop.poseidon-team-donuts-shop.com>

## Features

- Donut menu with an in-memory catalog
- Session-based shopping cart (add / update / remove)
- Checkout with delivery details + Accept.js card fields
- **Apple Pay** button (Safari/Apple devices) with server-side merchant validation
- Server-side payment capture via the Authorize.NET JSON transaction API
- Order confirmation with transaction ID
- HTTPS on a custom domain via AWS CloudFront + ACM

## Project layout

| Path | Purpose |
| --- | --- |
| `Controllers/HomeController.cs` | Donut menu |
| `Controllers/CartController.cs` | Cart operations |
| `Controllers/CheckoutController.cs` | Checkout, payment, and Apple Pay merchant validation |
| `Services/DonutCatalog.cs` | Product data |
| `Services/CartService.cs` | Session cart storage |
| `Services/AuthorizeNetPaymentService.cs` | Authorize.NET JSON API client |
| `Models/AuthorizeNetOptions.cs` | Gateway config + endpoint selection |
| `Models/ApplePayOptions.cs` | Apple Pay config (merchant id, domain, cert paths) |
| `Views/Checkout/Index.cshtml` | Accept.js + Apple Pay integration |
| `wwwroot/.well-known/` | Apple Pay domain-association file |
| `deploy-aws.sh` | One-command publish + deploy to Elastic Beanstalk |
| `docs/BUILD-GUIDE.md` | Full build documentation |

---

## 1. Run locally

The .NET 8 SDK is required. (It was installed to `~/.dotnet` on this machine.)

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
cd ~/Projects/DonutShop/donutshop
dotnet run --launch-profile https
```

Then open the HTTPS URL shown in the console (e.g. `https://localhost:7292`).

---

## 2. Configure Authorize.NET credentials

> ⚠️ **Never commit credentials.** Use user-secrets locally and environment variables /
> App Settings in production. Get sandbox credentials at
> <https://developer.authorize.net/hello_world/sandbox.html>.

You need three values from the Merchant Interface
(**Account → Settings → Security Settings**):

| Setting | Where it is used | Notes |
| --- | --- | --- |
| **API Login ID** | server + browser | Account → API Credentials & Keys |
| **Transaction Key** | server only | generate under API Credentials & Keys |
| **Public Client Key** | browser (Accept.js) | Manage Public Client Key |

Set them with user-secrets (run from the project folder):

```bash
dotnet user-secrets init
dotnet user-secrets set "AuthorizeNet:ApiLoginId"       "YOUR_API_LOGIN_ID"
dotnet user-secrets set "AuthorizeNet:TransactionKey"   "YOUR_TRANSACTION_KEY"
dotnet user-secrets set "AuthorizeNet:PublicClientKey"  "YOUR_PUBLIC_CLIENT_KEY"
dotnet user-secrets set "AuthorizeNet:Environment"      "Sandbox"
```

Switch `Environment` to `Production` when you go live (it automatically selects the live
API endpoint and the production Accept.js script).

### Sandbox test card

| Field | Value |
| --- | --- |
| Card number | `4111 1111 1111 1111` |
| Expiry | any future month/year |
| CVV | `123` |
| ZIP | any 5 digits |

---

## 3. Deploy to AWS Elastic Beanstalk

This project is deployed on **AWS Elastic Beanstalk** (.NET 8 on Amazon Linux 2023),
fronted by **CloudFront** for HTTPS on a custom domain. A helper script does the whole
thing:

```bash
# Prereqs: AWS CLI + EB CLI on PATH, and `aws configure` already done.
bash deploy-aws.sh
```

The script publishes a Release build, bundles it with the `Procfile`, deploys with
`eb deploy`, and pushes configuration via `eb setenv` (reading secrets from local
user-secrets so nothing is hardcoded). Note the double underscore `__` in env-var names —
that's how .NET maps to the nested `AuthorizeNet:Key` configuration keys.

```
App / Env:    sweet-ring-donuts / sweet-ring-donuts-env
Platform:     .NET 8 on 64-bit Amazon Linux 2023 (t3.small, single-instance)
Procfile:     web: dotnet DonutShop.dll   (app listens on http://0.0.0.0:5000)
```

### HTTPS, CloudFront & the custom domain

Beanstalk serves plain HTTP, so **CloudFront** sits in front to provide managed TLS:

- Distribution `E1KAUBU3LAW72X`, HTTP-only origin, **redirect-to-HTTPS**, forwards all
  cookies + query string + the `CloudFront-Forwarded-Proto` header, TTL 0 (dynamic app).
- Because TLS terminates at the edge and the origin hop is HTTP, `Program.cs` uses
  `UseForwardedHeaders` with `ForwardedProtoHeaderName = "CloudFront-Forwarded-Proto"` so
  the app sees the real HTTPS scheme (otherwise secure cookies drop and redirects loop).
- The custom domain `shop.poseidon-team-donuts-shop.com` (registered at GoDaddy) uses an
  **ACM certificate** (us-east-1) attached to CloudFront, with a DNS `CNAME` pointing the
  subdomain at the distribution.

See [docs/BUILD-GUIDE.md](docs/BUILD-GUIDE.md) for the exact ACM/CloudFront/DNS steps.

---

## 4. Apple Pay (optional)

Apple Pay on the Web is wired through Authorize.NET and is **disabled by default**
(`ApplePay:Enabled=false`) so it never interferes with the card flow. It only appears in
**Safari on Apple devices** — every other browser falls back to the card form.

Key facts:

- Apple Pay reuses the **same** Authorize.NET opaque-data charge path; only the
  `dataDescriptor` differs (`COMMON.APPLE.INAPP.PAYMENT`). Authorize.NET decrypts the token.
- The **Payment Processing Certificate** CSR is generated by **Authorize.NET** — they hold
  that private key. You only upload their CSR to Apple.
- The **Merchant Identity Certificate** (you own the key) is used for the server-side
  merchant-validation mutual-TLS call. Ship `apple_merchant_id.pem` + `.key` to the server
  (the deploy script bundles them from `~/applepay-certs/` into a `certs/` folder).
- Requires a **verified custom domain** (Apple rejects `*.cloudfront.net`).

Configuration (set on Beanstalk via `deploy-aws.sh` / `eb setenv`):

```
ApplePay__Enabled            true
ApplePay__MerchantIdentifier merchant.com.yourdomain.sweetring
ApplePay__DisplayName        Sweet Ring Donuts
ApplePay__DomainName         shop.poseidon-team-donuts-shop.com
ApplePay__MerchantIdCertPath certs/apple_merchant_id.pem
ApplePay__MerchantIdKeyPath  certs/apple_merchant_id.key
```

> Apple Pay can only be tested in Safari on an Apple device with an Apple **Sandbox Tester**
> account and a test card in Wallet — not in Chrome/Playwright.

---

## Security checklist

- [x] Card data tokenized client-side (Accept.js) / device-side (Apple Pay) — server never sees PANs
- [x] Credentials read from secrets/env, not source
- [x] Certificate/key material git-ignored (`*.pem`, `*.key`, `*.p12`, `*.pfx`, `*.cer`, `*.csr`)
- [x] Anti-forgery tokens on all POST forms (and the Apple Pay JSON validation call, via header)
- [x] Apple Pay merchant validation is SSRF-guarded to `*.apple.com` over HTTPS only
- [x] Session cookie is HttpOnly + Secure
- [x] HTTPS enforced (CloudFront redirect-to-HTTPS, TLSv1.2_2021)
- [ ] Move the Apple Pay merchant identity key to AWS Secrets Manager for production
- [ ] Add real product persistence, inventory, and order storage before production
- [ ] Add authentication if you need customer accounts
