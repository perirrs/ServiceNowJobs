# ServiceNowJobs (SNHub) — Auth Service

> ServiceNow-exclusive job portal, AI matching engine, and community platform.
> Built on **.NET 10** · Hosted on **Microsoft Azure**.

---

## Project Structure

```
ServiceNowJobs/
├── src/
│   ├── Services/
│   │   └── Auth/
│   │       ├── SNHub.Auth.API           # ASP.NET Core 10 Controllers
│   │       ├── SNHub.Auth.Application   # CQRS · MediatR · Validators
│   │       ├── SNHub.Auth.Domain        # Entities · Events · Exceptions
│   │       └── SNHub.Auth.Infrastructure # EF Core · Redis · Azure Services
│   └── Shared/
│       └── SNHub.Shared                 # Shared models and exceptions
├── tests/
│   ├── SNHub.Auth.UnitTests
│   └── SNHub.Auth.IntegrationTests
├── infra/
│   └── bicep/
│       └── main.bicep                   # Azure infrastructure as code
├── .github/workflows/
│   └── auth-service.yml                 # CI/CD → Azure AKS
└── docker-compose.yml                   # Local dev stack
```

## Azure Services Used

| Purpose | Azure Service |
|---|---|
| Container orchestration | Azure Kubernetes Service (AKS) |
| Container registry | Azure Container Registry (ACR) |
| Primary database | Azure Database for PostgreSQL Flexible Server |
| Cache | Azure Cache for Redis |
| Object storage | Azure Blob Storage |
| Messaging | Azure Service Bus |
| Secrets | Azure Key Vault |
| Monitoring | Azure Application Insights + Log Analytics |
| Email | Azure Communication Services |
| Identity | Azure Entra ID B2C (future) |
| CDN / Load balancing | Azure Front Door (future) |

## Quick Start

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop)

### Run locally

```bash
# 1. Clone
git clone https://github.com/perirrs/ServiceNowJobs.git
cd ServiceNowJobs

# 2. Start local infrastructure (PostgreSQL + Redis + Azurite)
docker compose up postgres redis azurite -d

# 3. Run Auth Service
cd src/Services/Auth/SNHub.Auth.API
dotnet run
```

API: `http://localhost:5001`
API Docs: `http://localhost:5001/scalar/v1`
Health: `http://localhost:5001/health`

### Run full stack with Docker

```bash
docker compose up --build
```

## Auth API Endpoints

| Method | Endpoint | Description |
|---|---|---|
| POST | `/api/v1/auth/register` | Register new user |
| POST | `/api/v1/auth/login` | Login |
| POST | `/api/v1/auth/refresh` | Refresh access token |
| POST | `/api/v1/auth/revoke` | Logout / revoke token |
| POST | `/api/v1/auth/forgot-password` | Request password reset |
| POST | `/api/v1/auth/reset-password` | Complete password reset |
| GET | `/api/v1/auth/verify-email` | Verify email address |

## Running Tests

```bash
# Unit tests
dotnet test tests/SNHub.Auth.UnitTests

# Integration tests (Docker required for Testcontainers)
dotnet test tests/SNHub.Auth.IntegrationTests

# All tests with coverage
dotnet test SNHub.sln --collect:"XPlat Code Coverage"
```

## Deploy to Azure

```bash
# 1. Provision infrastructure
az group create --name snhub-prod-rg --location uksouth
az deployment group create \
  --resource-group snhub-prod-rg \
  --template-file infra/bicep/main.bicep \
  --parameters env=prod dbAdminPassword=YOURPASSWORD jwtSecret=YOURSECRET

# 2. CI/CD auto-deploys via GitHub Actions on push to main
```

## GitHub Secrets Required

Add these in **GitHub → Settings → Secrets and Variables → Actions**:

| Secret | Description |
|---|---|
| `ACR_USERNAME` | Azure Container Registry username |
| `ACR_PASSWORD` | Azure Container Registry password |
| `AZURE_CREDENTIALS_STAGING` | Azure service principal JSON (staging) |
| `AZURE_CREDENTIALS_PROD` | Azure service principal JSON (production) |

## Make Repo Private

Your repo is currently public. To make it private:
**GitHub → Settings → Danger Zone → Change repository visibility → Private**
