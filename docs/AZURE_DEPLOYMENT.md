# Deploying HallRental to Azure — step by step

Target architecture: App Service (Linux, .NET 10) + Azure SQL (serverless, free offer) +
Key Vault + Application Insights, all created by Bicep and deployed by GitHub Actions
using OIDC (no stored passwords). The app talks to SQL and Key Vault through a
user-assigned Managed Identity.

Expected cost for ~10 days: roughly $4–6 (almost all of it is the B1 App Service plan).
SQL runs on the free offer and pauses instead of billing when the free limit is used up.

Commands below are bash. The easiest place to run them is **Azure Cloud Shell (Bash)**
in the portal, since `az` is preinstalled and already logged in. On Windows you can also use Git Bash
with the Azure CLI installed.

---

## Step 0 — Prerequisites

- .NET 10 SDK, Git, Azure CLI (`az version` ≥ 2.60).
- EF Core CLI tool:
  ```bash
  dotnet tool install --global dotnet-ef
  # or, if already installed:
  dotnet tool update --global dotnet-ef
  ```
- Your Azure trial subscription is active (check the expiry date in the portal).

---

## Step 1 — Apply the code changes and bring the tests into the repo

1. In your local clone, apply the patch (or copy files from the zip):
   ```bash
   git checkout -b azure-deployment
   git am < 0001-Prepare-HallRental-for-Azure-deployment.patch
   ```
   The patch moves the API into `src/HallRental.API/` and adds `infra/`, `.github/workflows/`, `docs/`.

2. Move your test projects **into** the repository (today they live next to it, so CI can't see them):
   ```bash
   # run from the repository root; adjust the source paths to where your tests really are
   mkdir -p tests
   cp -r ../HallRental.UnitTests        tests/HallRental.UnitTests
   cp -r ../HallRental.IntegrationTests tests/HallRental.IntegrationTests
   rm -rf tests/*/bin tests/*/obj
   ```

3. Fix the project references in both test `.csproj` files. The API moved, so change
   ```xml
   <ProjectReference Include="..\HallRental\HallRental.API.csproj" />
   ```
   (or whatever path you have now) to
   ```xml
   <ProjectReference Include="..\..\src\HallRental.API\HallRental.API.csproj" />
   ```

4. Build and run the tests:
   ```bash
   dotnet restore HallRental.slnx
   dotnet build HallRental.slnx
   dotnet test HallRental.slnx
   ```
   Why the existing tests should keep passing:
   - `new HallService()` still works, because a parameterless constructor uses the in-memory repository.
   - Integration tests via `WebApplicationFactory<Program>` run in the `Development` environment
     by default, where there is no connection string (so the in-memory store is used) and the old dev
     JWT key is in `appsettings.Development.json`, so the README tokens stay valid.
   - If your integration tests set a custom environment (e.g. `builder.UseEnvironment("Testing")`),
     add `Jwt:SigningKey` for it, e.g. an `appsettings.Testing.json` or
     `builder.UseSetting("Jwt:SigningKey", "...")`.
   - One behavioural nuance: the in-memory repository is registered as a singleton, the same as the old
     singleton `HallService`, so state is shared across requests within one factory instance, just as before.

---

## Step 2 — Create the EF Core migration

This must be done on your machine because it needs to compile the project:

```bash
dotnet ef migrations add InitialCreate \
  --project src/HallRental.API/HallRental.API.csproj \
  --output-dir Data/Migrations
```

- No database connection is needed. `DesignTimeDbContextFactory` supplies a dummy SQL Server
  connection string only to build the model.
- Open the generated migration and check it creates `Halls` (with an `Accessories` JSON column)
  and `Bookings` (with `SelectedAccessories` as JSON and an index on `HallId, StartTime, EndTime`).
- Commit it:
  ```bash
  git add .
  git commit -m "Move tests into repo, add InitialCreate migration"
  ```

The app applies migrations at startup when `Database:MigrateOnStartup=true` (set by Bicep).

---

## Step 3 — Run locally (optional sanity check)

```bash
dotnet run --project src/HallRental.API
```
- Open `https://localhost:7125/swagger`.
- Get a token: `POST /api/v1/auth/demo-token?role=Admin` (enabled in Development only).
- `GET /health` should return `Healthy`.

---

## Step 4 — One-time Azure setup

### 4.1 Variables

```bash
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
TENANT_ID=$(az account show --query tenantId -o tsv)
LOCATION=northeurope          # fallbacks: westeurope, swedencentral, polandcentral
RG=rg-hallrental
GH_REPO=dmytrobaranov-labs/HallRental
```

### 4.2 Register resource providers (new trial subscriptions often need this)

```bash
for ns in Microsoft.Web Microsoft.Sql Microsoft.KeyVault Microsoft.Insights \
          Microsoft.OperationalInsights Microsoft.ManagedIdentity; do
  az provider register --namespace $ns
done
# wait until all show "Registered"
az provider list --query "[?contains('Microsoft.Web Microsoft.Sql Microsoft.KeyVault Microsoft.Insights Microsoft.OperationalInsights Microsoft.ManagedIdentity', namespace)].{ns:namespace, state:registrationState}" -o table
```

### 4.3 Resource group

```bash
az group create --name $RG --location $LOCATION
```

### 4.4 Budget alert (do this before deploying anything)

Portal → **Cost Management** → **Budgets** → **Add**:
- Scope: your subscription. Amount: `150`. Reset: Monthly.
- Alerts: Actual 25%, 50%, 90% → your email.

### 4.5 Identity for GitHub Actions (OIDC, no client secret)

```bash
APP_ID=$(az ad app create --display-name gh-hallrental-deploy --query appId -o tsv)
az ad sp create --id $APP_ID

# Trust tokens issued by GitHub for the "production" environment of your repo
az ad app federated-credential create --id $APP_ID --parameters "{
  \"name\": \"github-production\",
  \"issuer\": \"https://token.actions.githubusercontent.com\",
  \"subject\": \"repo:${GH_REPO}:environment:production\",
  \"audiences\": [\"api://AzureADTokenExchange\"]
}"

# Owner on the resource group ONLY (needed because Bicep creates role assignments)
az role assignment create \
  --assignee $APP_ID \
  --role Owner \
  --scope /subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RG

echo "AZURE_CLIENT_ID=$APP_ID"
echo "AZURE_TENANT_ID=$TENANT_ID"
echo "AZURE_SUBSCRIPTION_ID=$SUBSCRIPTION_ID"
```

> ⚠️ The `subject` must match exactly. The workflow's deploy job uses
> `environment: production`, so the subject is `...:environment:production`, **not**
> `...:ref:refs/heads/main`. A mismatch shows up as `AADSTS70021: No matching federated identity record found`.

If `az role assignment create` fails right after creating the SP, wait 30–60 seconds and retry,
because Entra replication is sometimes slow.

### 4.6 JWT signing key

```bash
openssl rand -base64 48
```
Copy the output. It goes to GitHub only, never into the repo.

### 4.7 GitHub configuration

In GitHub → your repo → **Settings**:
1. **Environments** → **New environment** → `production`. Optionally add yourself as a required
   reviewer to get a manual approval gate before each deploy.
2. **Secrets and variables** → **Actions** → **New repository secret**, four times:
   - `AZURE_CLIENT_ID`
   - `AZURE_TENANT_ID`
   - `AZURE_SUBSCRIPTION_ID`
   - `JWT_SIGNING_KEY`

---

## Step 5 — Dry run of the infrastructure (recommended)

Catch quota and region problems before involving CI:

```bash
export JWT_SIGNING_KEY='<the key from 4.6>'
az deployment group what-if \
  --resource-group $RG \
  --parameters infra/main.bicepparam
```

Common trial-subscription errors and fixes:
- **SQL: "Location is not accepting creation of new Windows Azure SQL Database servers"**
  → delete the RG, recreate it in another region (4.3) and update `LOCATION`.
- **App Service: "SubscriptionIsOverQuotaForSku" / quota 0 for Basic**
  → set `param appServiceSku = 'F1'` in `infra/main.bicepparam`. F1 is free but has no Always On,
  so the first request after idle is slow.
- **SQL free offer rejected** → set `param useFreeSqlOffer = false` (Basic tier, roughly $5/month, so about $2 for your window).

To deploy manually once and watch it:
```bash
az deployment group create --resource-group $RG --parameters infra/main.bicepparam -o table
```

---

## Step 6 — Deploy through GitHub Actions

```bash
git push -u origin azure-deployment
```
1. Open a Pull Request to `main`. Only the **Build & test** job runs, which shows CI gating PRs.
2. Merge it. Now **Build & test** → **Deploy to Azure** runs:
   Bicep → zip deploy → smoke test of `/health` and `/health/ready`.
3. The run summary shows the Swagger URL.

The first start can take 1–2 minutes: the SQL database resumes from auto-pause and migrations run.
The smoke test retries for that reason.

---

## Step 7 — Verify the deployment

```bash
APP=$(az webapp list -g $RG --query "[0].name" -o tsv)
URL="https://$(az webapp show -g $RG -n $APP --query defaultHostName -o tsv)"

curl $URL/health
curl $URL/health/ready
```

Enable the demo token endpoint **temporarily**:
```bash
az webapp config appsettings set -g $RG -n $APP --settings Auth__DemoTokenEndpointEnabled=true
sleep 30
ADMIN=$(curl -s -X POST "$URL/api/v1/auth/demo-token?role=Admin" | jq -r .token)

# create a hall
HALL=$(curl -s -X POST "$URL/api/v1/halls" \
  -H "Authorization: Bearer $ADMIN" -H "Content-Type: application/json" \
  -d '{"name":"Зал А","capacity":50,"basePricePerHour":2000,"accessories":[{"name":"Проєктор","price":500}]}' | jq -r .id)

# book it
curl -s -X POST "$URL/api/v1/halls/book" \
  -H "Authorization: Bearer $ADMIN" -H "Content-Type: application/json" \
  -d "{\"hallId\":\"$HALL\",\"startTime\":\"2026-10-01T12:00:00\",\"endTime\":\"2026-10-01T14:00:00\",\"selectedAccessories\":[\"Проєктор\"]}"

# analytics
curl -s "$URL/api/v1/analytics/summary" -H "Authorization: Bearer $ADMIN"

# restart the app: the data must survive (it is in SQL now, not in memory)
az webapp restart -g $RG -n $APP
sleep 60
curl -s "$URL/api/v1/halls?minCapacity=1"

# switch the demo endpoint off again
az webapp config appsettings set -g $RG -n $APP --settings Auth__DemoTokenEndpointEnabled=false
```

Also confirm in the portal:
- **Key Vault** → Secrets: `JwtSigningKey` exists.
- **App Service** → Environment variables: `Jwt__SigningKey` shows a green ✔ "Key Vault Reference".
  A red ✖ means the identity can't read the secret; check the role assignment.
- **SQL database** → Query editor won't let *you* in (see Hardening). This is expected with the lab setup.

---

## Step 8 — Observability (Application Insights + KQL)

Generate some interesting traffic, e.g. book the same slot twice. The second attempt returns
500 because the service throws a plain `Exception` for a booking conflict (a real bug worth fixing later: it should be 409).

Portal → Application Insights → **Logs**:

```kusto
// Request volume and failures by endpoint
requests
| where timestamp > ago(1d)
| summarize count(), failures = countif(success == false), p95 = percentile(duration, 95) by name
| order by count_ desc
```

```kusto
// The exceptions behind 5xx responses
exceptions
| where timestamp > ago(1d)
| project timestamp, type, outerMessage, operation_Name, operation_Id
| order by timestamp desc
```

```kusto
// SQL calls made by the app (dependency tracking)
dependencies
| where timestamp > ago(1d) and type == "SQL"
| summarize count(), avg(duration), max(duration) by target, name
```

```kusto
// Cold start: how long did the first request after SQL auto-pause take?
requests
| where timestamp > ago(1d)
| top 10 by duration desc
| project timestamp, name, duration, resultCode, operation_Id
```

Also look at **Transaction search** (pick one failed request and see the full chain request → SQL → exception)
and **Live metrics**. Take screenshots for the README and for interviews.

---

## Step 9 — Teardown (before October 2)

```bash
az group delete --name $RG --yes

# Key Vault is soft-deleted, so purge it to free the name
az keyvault list-deleted --query "[].name" -o tsv
az keyvault purge --name <kv-name-from-above>

# remove the GitHub deployment identity
az ad app delete --id $APP_ID
```
Keep the code, Bicep, workflow and screenshots. Anyone (including you, later) can recreate
everything with one pipeline run.

---

## Hardening (next steps / interview talking points)

1. **Least-privilege SQL access.** In the lab, the app identity is the SQL Entra admin (no manual T-SQL).
   The production-grade setup:
   - Make an Entra group (e.g. `sql-admins-hallrental`) the server admin.
   - Connect as a member and run:
     ```sql
     CREATE USER [id-hallrental] FROM EXTERNAL PROVIDER;
     ALTER ROLE db_datareader ADD MEMBER [id-hallrental];
     ALTER ROLE db_datawriter ADD MEMBER [id-hallrental];
     ```
   - Run migrations from CI with an EF **migration bundle** (`dotnet ef migrations bundle`) instead of at app startup.
2. **Private networking.** VNet integration + private endpoints for SQL and Key Vault, then disable public network access.
3. **Double-booking race condition.** Two concurrent requests can both pass the "is it booked?" check.
   Fix it with a serializable transaction or `UPDLOCK, HOLDLOCK` on the range check, or a DB-level constraint.
4. **Error semantics.** Map booking conflicts to `409 Conflict` and invalid time ranges to `400`
   (today both are plain `Exception` → 500).
5. **Real identity provider.** Replace the self-issued JWT + demo endpoint with Microsoft Entra ID
   (app roles `Admin` / `User`).
6. **Zero-downtime deploys.** Deployment slots (need Standard tier) with swap after the smoke test.
