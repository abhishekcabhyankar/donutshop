#!/usr/bin/env bash
#
# Deploy Sweet Ring Donuts to AWS Elastic Beanstalk (.NET 8 on Amazon Linux 2023).
#
# Prerequisites (one-time):
#   1. An AWS account with IAM credentials configured:  aws configure  (or  eb init  will ask)
#   2. The EB CLI on PATH (installed via: python3 -m pip install --user awsebcli)
#
# Safe to re-run: the first run creates the environment, later runs just deploy.

set -euo pipefail

# ---- Settings ---------------------------------------------------------------
APP_NAME="sweet-ring-donuts"
ENV_NAME="sweet-ring-donuts-env"
REGION="us-east-1"
PLATFORM="64bit Amazon Linux 2023 v3.11.2 running .NET 8"
INSTANCE_TYPE="t3.small"

# ---- Tooling on PATH --------------------------------------------------------
export DOTNET_ROOT="$HOME/.dotnet"
eval "$(/opt/homebrew/bin/brew shellenv)"   # puts aws + eb on PATH
export PATH="$HOME/.dotnet:$PATH"

PROJECT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$PROJECT_DIR"

# ---- 1. Publish a Release build --------------------------------------------
echo "==> Publishing Release build..."
rm -rf ./publish ./deploy.zip
dotnet publish -c Release -o ./publish

# ---- 2. Bundle the publish output with the Procfile at the archive root -----
cp Procfile ./publish/Procfile
( cd publish && zip -qr ../deploy.zip . )
echo "==> Created deploy.zip"

# ---- 3. Initialize EB (idempotent) -----------------------------------------
if [ ! -f .elasticbeanstalk/config.yml ]; then
  eb init "$APP_NAME" --region "$REGION" --platform "$PLATFORM"
fi

# Deploy the pre-built artifact instead of zipping the whole source tree.
if ! grep -q "artifact:" .elasticbeanstalk/config.yml 2>/dev/null; then
  cat >> .elasticbeanstalk/config.yml <<'YAML'
deploy:
  artifact: deploy.zip
YAML
fi

# ---- 4. Create the environment on first run, otherwise deploy ---------------
if eb status "$ENV_NAME" >/dev/null 2>&1; then
  echo "==> Deploying to existing environment $ENV_NAME..."
  eb deploy "$ENV_NAME"
else
  echo "==> Creating single-instance environment $ENV_NAME..."
  eb create "$ENV_NAME" --instance-type "$INSTANCE_TYPE" --single
fi

# ---- 5. Push app configuration as environment variables ---------------------
# Values are read from local user-secrets so no secret is hardcoded in this file.
echo "==> Applying application settings..."
secrets="$(dotnet user-secrets list)"
get() { echo "$secrets" | sed -n "s/^AuthorizeNet:$1 = //p"; }

eb setenv \
  ASPNETCORE_ENVIRONMENT=Production \
  ASPNETCORE_URLS="http://0.0.0.0:5000" \
  AuthorizeNet__Environment="$(get Environment)" \
  AuthorizeNet__ApiLoginId="$(get ApiLoginId)" \
  AuthorizeNet__TransactionKey="$(get TransactionKey)" \
  AuthorizeNet__PublicClientKey="$(get PublicClientKey)"

echo "==> Deployment complete."
eb status "$ENV_NAME"
