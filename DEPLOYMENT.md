# 🍩 Sweet Ring Donuts — Build & Deploy Guide

End-to-end guide for building, running, and deploying the app to AWS. The live
stack is **Elastic Beanstalk** (single instance, .NET 8 on Amazon Linux 2023)
fronted by **CloudFront** for HTTPS.

> **Live URL:** https://dv2ih1brr0xrd.cloudfront.net
> **Origin (EB):** http://sweet-ring-donuts-env.eba-yj5nqhek.us-east-1.elasticbeanstalk.com

---

## 1. Prerequisites / tooling

| Tool | Version used | Install |
| --- | --- | --- |
| .NET SDK | 8.0.x | installed at `~/.dotnet` (not on PATH by default) |
| Homebrew | 6.x | https://brew.sh |
| AWS CLI | 2.x | `brew install awscli` |
| EB CLI | 3.x | `brew install aws-elasticbeanstalk` |

The .NET SDK is **not on the default PATH** on this machine. Every shell that runs
`dotnet` needs:

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
```

Homebrew tools (`aws`, `eb`) are activated with:

```bash
eval "$(/opt/homebrew/bin/brew shellenv)"
```

(`deploy-aws.sh` sets both of these automatically.)

---

## 2. Build & run locally

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"

dotnet build
dotnet run --launch-profile https     # https://localhost:7292  (http://localhost:5147)
```

First time only, trust the local HTTPS dev certificate (needed so Accept.js can
tokenize cards):

```bash
dotnet dev-certs https --trust
```

### Sandbox test card

| Field | Value |
| --- | --- |
| Card number | `4111 1111 1111 1111` |
| Expiry | any future month/year |
| CVV | `123` |
| ZIP | any 5 digits |

---

## 3. Configuration (secrets)

Credentials are **never committed**. Locally they live in .NET user-secrets; in AWS
they are environment variables (the deploy script copies them from user-secrets).

```bash
dotnet user-secrets set "AuthorizeNet:Environment"      "Sandbox"
dotnet user-secrets set "AuthorizeNet:ApiLoginId"       "YOUR_API_LOGIN_ID"
dotnet user-secrets set "AuthorizeNet:TransactionKey"   "YOUR_TRANSACTION_KEY"
dotnet user-secrets set "AuthorizeNet:PublicClientKey"  "YOUR_PUBLIC_CLIENT_KEY"

dotnet user-secrets list   # verify
```

| Setting | Used by | Notes |
| --- | --- | --- |
| `ApiLoginId` | server + browser | Account → API Credentials & Keys |
| `TransactionKey` | server only | **secret** — generate under API Credentials & Keys |
| `PublicClientKey` | browser (Accept.js) | safe to expose; Manage Public Client Key |
| `Environment` | server | `Sandbox` or `Production` (selects API + Accept.js endpoints) |

---

## 4. AWS architecture

```
Browser ──HTTPS──▶ CloudFront (dv2ih1brr0xrd.cloudfront.net)
                      │  *.cloudfront.net cert, viewer protocol = redirect-to-https
                      │  forwards all cookies + querystring + CloudFront-Forwarded-Proto, no caching
                      ▼  HTTP (http-only origin)
              Elastic Beanstalk  (single t3.small, .NET 8 / Amazon Linux 2023)
                      └─ nginx ─▶ Kestrel  (dotnet DonutShop.dll on :5000)
```

Why the moving parts matter:

- **CloudFront** provides a free HTTPS endpoint without owning a domain (Accept.js
  and the secure session cookie both require HTTPS in the browser).
- **`UseForwardedHeaders` + `CloudFront-Forwarded-Proto`** in `Program.cs` lets the
  app know the original request was HTTPS even though CloudFront reaches the EB
  origin over HTTP. Without it the secure session cookie (the cart) is dropped and
  HTTPS redirection loops.
- **`Procfile`** (`web: dotnet DonutShop.dll`) tells the EB .NET platform how to
  start the published app. EB serves it on port 5000 behind nginx.

| Resource | Identifier |
| --- | --- |
| EB application | `sweet-ring-donuts` |
| EB environment | `sweet-ring-donuts-env` |
| EB platform | `64bit Amazon Linux 2023 v3.11.2 running .NET 8` |
| CloudFront distribution | `E1KAUBU3LAW72X` |
| Region | `us-east-1` |
| IAM deploy user | `donutshop-deployer` |

---

## 5. First-time AWS setup

Only needed once per machine / account.

### 5a. Create an IAM user + access keys
1. AWS Console → **IAM → Users → Create user** (e.g. `donutshop-deployer`).
2. Attach a policy (quick start: `AdministratorAccess`; tighten later).
3. Open the user → **Security credentials → Create access key → Command Line Interface (CLI)**.
4. Copy the **Access key ID** and **Secret access key** (shown once).

### 5b. Configure the CLI
```bash
aws configure          # enter key id, secret, region=us-east-1, output=json
aws sts get-caller-identity   # verify
```

The credentials are written to `~/.aws/credentials` and `~/.aws/config`
(permissions `600`). Never commit these.

---

## 6. Deploy

A single script does everything: publish → zip with the Procfile → create/deploy
the EB environment → push config as environment variables (read from user-secrets,
so no secret is hardcoded).

```bash
bash deploy-aws.sh
```

- **First run** creates the EB application and environment.
- **Later runs** just deploy the new version and re-apply settings.
- CloudFront needs **no change** on code redeploys (same origin).

### What the script applies as EB environment variables
```
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:5000
AuthorizeNet__Environment / __ApiLoginId / __TransactionKey / __PublicClientKey
```
(Double underscore `__` maps to the nested `AuthorizeNet:Key` config keys.)

---

## 7. Set up / change HTTPS (CloudFront)

The distribution already exists (`E1KAUBU3LAW72X`). To recreate it from scratch,
the config used is a custom (legacy `ForwardedValues`) behavior with:

- Origin = the EB CNAME, `OriginProtocolPolicy: http-only`.
- `ViewerProtocolPolicy: redirect-to-https`, default `*.cloudfront.net` certificate.
- Forward **all cookies**, **querystring**, and the **`CloudFront-Forwarded-Proto`**
  header; `MinTTL/DefaultTTL/MaxTTL = 0` (no caching of dynamic content).

```bash
aws cloudfront create-distribution --distribution-config file://cf-config.json
aws cloudfront get-distribution --id <ID> --query "Distribution.Status"   # wait for "Deployed"
```

To use a **custom domain** instead, request an ACM certificate (in `us-east-1`),
add it + the domain as an Alternate Domain Name (CNAME) on the distribution, and
point DNS at the CloudFront domain.

---

## 8. Going to production

1. Switch to live Authorize.NET credentials:
   ```bash
   dotnet user-secrets set "AuthorizeNet:Environment"     "Production"
   dotnet user-secrets set "AuthorizeNet:ApiLoginId"      "LIVE_ID"
   dotnet user-secrets set "AuthorizeNet:TransactionKey"  "LIVE_KEY"
   dotnet user-secrets set "AuthorizeNet:PublicClientKey" "LIVE_PUBLIC_KEY"
   ```
2. `bash deploy-aws.sh`
3. (Recommended) add real order/inventory persistence, lock the EB origin to only
   accept CloudFront traffic, and add monitoring/alerts.

---

## 9. Common operations

```bash
# Status / health / logs
eb status sweet-ring-donuts-env
eb health sweet-ring-donuts-env
eb logs   sweet-ring-donuts-env

# Open the EB origin URL in a browser
eb open sweet-ring-donuts-env

# Tear everything down (stops billing)
eb terminate sweet-ring-donuts-env
aws cloudfront get-distribution-config --id E1KAUBU3LAW72X   # disable, then:
aws cloudfront delete-distribution --id E1KAUBU3LAW72X --if-match <ETag>
```

> **Cost note:** the `t3.small` instance + Elastic IP bill continuously; CloudFront
> is mostly usage-based. Terminate the environment when you're done testing.

---

## 10. Troubleshooting

| Symptom | Cause / fix |
| --- | --- |
| `dotnet: command not found` | export `DOTNET_ROOT`/`PATH` (see §1). |
| `aws`/`eb: command not found` | `eval "$(/opt/homebrew/bin/brew shellenv)"`. |
| "Payment is not configured yet" on checkout | `AuthorizeNet` env vars missing — re-run `bash deploy-aws.sh`, or `eb setenv ...`. |
| Cart empties between pages on AWS | App isn't seeing HTTPS → secure cookie dropped. Ensure `CloudFront-Forwarded-Proto` is forwarded and `Program.cs` reads it; browse via the CloudFront URL, not the raw EB URL. |
| Accept.js "HTTPS connection required" | You're on an HTTP URL — use the CloudFront HTTPS URL (or local `https://` profile). |
| EB deploy succeeds but 502 | App not listening on `:5000` — confirm `ASPNETCORE_URLS` env var and the `Procfile`. |
