# 🍩 Sweet Ring Donuts

A sample donut e-commerce website built with **ASP.NET Core 8 (MVC)** and integrated with
the **Authorize.NET** payment gateway using **Accept.js** (client-side card tokenization).

Raw card data is sent directly from the browser to Authorize.NET and **never touches this
server**, which keeps your PCI compliance scope to the minimum (SAQ A-EP).

## Features

- Donut menu with an in-memory catalog
- Session-based shopping cart (add / update / remove)
- Checkout with delivery details + Accept.js card fields
- Server-side payment capture via the Authorize.NET JSON transaction API
- Order confirmation with transaction ID

## Project layout

| Path | Purpose |
| --- | --- |
| `Controllers/HomeController.cs` | Donut menu |
| `Controllers/CartController.cs` | Cart operations |
| `Controllers/CheckoutController.cs` | Checkout + payment |
| `Services/DonutCatalog.cs` | Product data |
| `Services/CartService.cs` | Session cart storage |
| `Services/AuthorizeNetPaymentService.cs` | Authorize.NET JSON API client |
| `Models/AuthorizeNetOptions.cs` | Gateway config + endpoint selection |
| `Views/Checkout/Index.cshtml` | Accept.js integration |

---

## 1. Run locally

The .NET 8 SDK is required. (It was installed to `~/.dotnet` on this machine.)

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
cd ~/Projects/DonutShop
dotnet run
```

Then open the HTTPS URL shown in the console (e.g. `https://localhost:5001`).

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

## 3. Deploy to Azure App Service

```bash
export PATH="$HOME/.dotnet:$PATH"

# Publish a release build
dotnet publish -c Release -o ./publish

# Login + create resources (requires Azure CLI: https://aka.ms/azure-cli)
az login
az group create --name donutshop-rg --location eastus
az appservice plan create --name donutshop-plan --resource-group donutshop-rg --sku B1 --is-linux
az webapp create --resource-group donutshop-rg --plan donutshop-plan \
  --name <your-unique-app-name> --runtime "DOTNETCORE:8.0"

# Store credentials as App Settings (NOT in source control)
az webapp config appsettings set --resource-group donutshop-rg --name <your-unique-app-name> \
  --settings \
  AuthorizeNet__Environment="Sandbox" \
  AuthorizeNet__ApiLoginId="YOUR_API_LOGIN_ID" \
  AuthorizeNet__TransactionKey="YOUR_TRANSACTION_KEY" \
  AuthorizeNet__PublicClientKey="YOUR_PUBLIC_CLIENT_KEY"

# Zip-deploy the published output
cd publish && zip -r ../app.zip . && cd ..
az webapp deploy --resource-group donutshop-rg --name <your-unique-app-name> \
  --src-path app.zip --type zip
```

> Note the double underscore `__` in App Setting names — that's how Azure maps to the
> nested `AuthorizeNet:Key` configuration keys.

App Service serves over HTTPS by default, which Accept.js and PCI both require.

### AWS alternative

Publish the same way and host on **AWS Elastic Beanstalk** (.NET 8 on Linux) or a
container on **ECS/Fargate**. Store the three credentials as environment variables
(`AuthorizeNet__ApiLoginId`, etc.) in the environment configuration.

---

## Security checklist

- [x] Card data tokenized client-side (Accept.js) — server never sees PANs
- [x] Credentials read from secrets/env, not source
- [x] Anti-forgery tokens on all POST forms
- [x] Session cookie is HttpOnly + Secure
- [x] HTTPS enforced
- [ ] Add real product persistence, inventory, and order storage before production
- [ ] Add authentication if you need customer accounts
