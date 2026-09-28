// =====================================================================
// HallRental — інфраструктура Azure (Bicep)
//
// Створює:
//   - User-assigned managed identity (єдина "особа" застосунку в Azure)
//   - Log Analytics + Application Insights (телеметрія, KQL)
//   - Key Vault (секрет JWT) + роль "Key Vault Secrets User" для identity
//   - Azure SQL server (лише Entra-автентифікація) + serverless БД (free offer)
//   - App Service plan (Linux) + Web App (.NET 10)
// =====================================================================

targetScope = 'resourceGroup'

@description('Коротка назва застосунку, використовується як префікс імен ресурсів.')
@minLength(3)
@maxLength(12)
param appName string = 'hallrental'

param location = 'swedencentral'


@description('SKU App Service plan. B1 — рекомендовано; F1 — безкоштовний fallback, якщо на B1 немає квоти.')
@allowed([ 'B1', 'F1' ])
param appServiceSku string = 'B1'

@description('Використати безкоштовну пропозицію Azure SQL (serverless, 100k vCore-секунд на місяць). false -> Basic (~5 USD/міс).')
param useFreeSqlOffer bool = true

@description('Ключ підпису JWT (мінімум 32 символи). Передається з GitHub secret, зберігається в Key Vault.')
@secure()
@minLength(32)
param jwtSigningKey string

@description('Публічна IP-адреса розробника для доступу до SQL (необов\'язково).')
param developerIpAddress string = ''

var suffix = uniqueString(resourceGroup().id)
var names = {
  identity: 'id-${appName}'
  logAnalytics: 'log-${appName}-${suffix}'
  appInsights: 'appi-${appName}'
  keyVault: take('kv-${appName}-${suffix}', 24)
  sqlServer: 'sql-${appName}-${suffix}'
  sqlDatabase: 'hallrental'
  plan: 'asp-${appName}'
  webApp: 'app-${appName}-${suffix}'
}

var tags = {
  project: 'HallRental'
  purpose: 'portfolio-lab'
}

// ---------------------------------------------------------------------
// Identity
// ---------------------------------------------------------------------
resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: names.identity
  location: location
  tags: tags
}

// ---------------------------------------------------------------------
// Observability
// ---------------------------------------------------------------------
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: names.logAnalytics
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: {
      dailyQuotaGb: 1 // запобіжник від несподіваних витрат
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: names.appInsights
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ---------------------------------------------------------------------
// Key Vault
// ---------------------------------------------------------------------
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: names.keyVault
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

resource jwtSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'JwtSigningKey'
  properties: {
    value: jwtSigningKey
  }
}

// Вбудована роль "Key Vault Secrets User"
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource kvRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, identity.id, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------
// Azure SQL
// ---------------------------------------------------------------------
// СПРОЩЕННЯ ДЛЯ ЛАБИ: Entra-адміністратором сервера є identity застосунку.
// Так не потрібен ручний T-SQL, але застосунок має права адміністратора сервера.
// Продакшн-варіант описано в docs/AZURE_DEPLOYMENT.md (розділ "Hardening").
resource sqlServer 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: names.sqlServer
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: identity.name
      sid: identity.properties.principalId
      tenantId: subscription().tenantId
      principalType: 'Application'
    }
  }
}

// 0.0.0.0 — спеціальне правило "Allow Azure services" (дозволяє App Service підключатися)
resource sqlFirewallAzure 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

resource sqlFirewallDeveloper 'Microsoft.Sql/servers/firewallRules@2023-08-01-preview' = if (!empty(developerIpAddress)) {
  parent: sqlServer
  name: 'Developer'
  properties: {
    startIpAddress: developerIpAddress
    endIpAddress: developerIpAddress
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sqlServer
  name: names.sqlDatabase
  location: location
  tags: tags
  sku: useFreeSqlOffer
    ? { name: 'GP_S_Gen5_2', tier: 'GeneralPurpose', family: 'Gen5', capacity: 2 }
    : { name: 'Basic', tier: 'Basic' }
  properties: useFreeSqlOffer
    ? {
        useFreeLimit: true
        freeLimitExhaustionBehavior: 'AutoPause' // ніколи не виставить рахунок понад безкоштовний ліміт
        autoPauseDelay: 60
        minCapacity: json('0.5')
        maxSizeBytes: 34359738368 // 32 GB — максимум free offer
        zoneRedundant: false
        requestedBackupStorageRedundancy: 'Local'
      }
    : {
        maxSizeBytes: 2147483648
        requestedBackupStorageRedundancy: 'Local'
      }
}

// ---------------------------------------------------------------------
// App Service
// ---------------------------------------------------------------------
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: names.plan
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: appServiceSku
  }
  properties: {
    reserved: true // обов'язково для Linux
  }
}

var sqlConnectionString = 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${names.sqlDatabase};Authentication=Active Directory Managed Identity;User Id=${identity.properties.clientId};Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;'

resource webApp 'Microsoft.Web/sites@2023-12-01' = {
  name: names.webApp
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identity.id}': {}
    }
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    keyVaultReferenceIdentity: identity.id
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: appServiceSku != 'F1'
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      http20Enabled: true
      healthCheckPath: '/health'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'ASPNETCORE_FORWARDEDHEADERS_ENABLED', value: 'true' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }
        { name: 'AZURE_CLIENT_ID', value: identity.properties.clientId }
        { name: 'ConnectionStrings__HallRental', value: sqlConnectionString }
        { name: 'Jwt__SigningKey', value: '@Microsoft.KeyVault(SecretUri=${jwtSecret.properties.secretUri})' }
        { name: 'Database__MigrateOnStartup', value: 'true' }
        { name: 'Auth__DemoTokenEndpointEnabled', value: 'false' }
        { name: 'Swagger__Enabled', value: 'true' }
      ]
    }
  }
  dependsOn: [
    kvRoleAssignment // секрет має бути доступний до старту застосунку
    sqlDatabase
  ]
}

// ---------------------------------------------------------------------
// Outputs (читаються workflow GitHub Actions)
// ---------------------------------------------------------------------
output webAppName string = webApp.name
output webAppUrl string = 'https://${webApp.properties.defaultHostName}'
output sqlServerFqdn string = sqlServer.properties.fullyQualifiedDomainName
output keyVaultName string = keyVault.name
output appInsightsName string = appInsights.name
