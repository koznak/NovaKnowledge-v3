# Deploy text-embedding-3-large to Azure OpenAI (Option A).
# Prereq: az login
# Usage: .\scripts\deploy-azure-openai-embeddings.ps1 -OpenAiAccount <your-openai-resource-name>

param(
    [Parameter(Mandatory = $true)]
    [string]$OpenAiAccount
)

$ErrorActionPreference = "Stop"

$openAiAccount = $OpenAiAccount
$deploymentName = "text-embedding-3-large"
$modelName = "text-embedding-3-large"
$modelVersion = "1"

Write-Host "Checking Azure CLI login..."
$account = az account show 2>$null | ConvertFrom-Json
if (-not $account) {
    throw "Not logged in. Run: az login"
}
Write-Host "  Account: $($account.name)"

Write-Host "Looking up resource group for $openAiAccount..."
$resourceGroup = az cognitiveservices account show --name $openAiAccount --query resourceGroup -o tsv
if (-not $resourceGroup) {
    throw "Could not find Azure OpenAI account '$openAiAccount'. Check the account name."
}
Write-Host "  Resource group: $resourceGroup"

Write-Host "Checking existing deployments..."
$existing = az cognitiveservices account deployment show `
    --name $openAiAccount `
    --resource-group $resourceGroup `
    --deployment-name $deploymentName 2>$null
if ($LASTEXITCODE -eq 0) {
    Write-Host "Deployment '$deploymentName' already exists. No action needed."
    exit 0
}

Write-Host "Creating deployment '$deploymentName' (this may take 1-2 minutes)..."
az cognitiveservices account deployment create `
    --name $openAiAccount `
    --resource-group $resourceGroup `
    --deployment-name $deploymentName `
    --model-name $modelName `
    --model-version $modelVersion `
    --model-format OpenAI `
    --sku-name Standard `
    --sku-capacity 10

if ($LASTEXITCODE -ne 0) {
    throw "Deployment failed. If the model is unavailable in your region, try text-embedding-3-small in the portal and update EmbeddingDeployment in appsettings.json."
}

Write-Host ""
Write-Host "Done. Deployment '$deploymentName' is ready."
Write-Host "Restart Search.Api, then test:"
Write-Host '  curl -X POST http://localhost:5000/api/search/query -H "Content-Type: application/json" -d "{\"query\":\"PTO policy\",\"tenantId\":\"contoso\",\"topK\":5}"'
