using './main.bicep'

param appName = 'hallrental'
param appServiceSku = 'B1'
param useFreeSqlOffer = true

// Передається з командного рядка / GitHub secret, НЕ зберігайте тут:
param jwtSigningKey = readEnvironmentVariable('JWT_SIGNING_KEY', '')
