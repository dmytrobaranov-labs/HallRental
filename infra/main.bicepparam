using './main.bicep'

param appName = 'hallrental'
param location = 'swedencentral'
param appServiceSku = 'F1'
param useFreeSqlOffer = true

// Passed from the command line / GitHub secret — do NOT store it here:
param jwtSigningKey = readEnvironmentVariable('JWT_SIGNING_KEY', '')
