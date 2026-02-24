# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files first for layer caching
COPY src/Services/Auth/SNHub.Auth.API/SNHub.Auth.API.csproj                       src/Services/Auth/SNHub.Auth.API/
COPY src/Services/Auth/SNHub.Auth.Application/SNHub.Auth.Application.csproj       src/Services/Auth/SNHub.Auth.Application/
COPY src/Services/Auth/SNHub.Auth.Domain/SNHub.Auth.Domain.csproj                 src/Services/Auth/SNHub.Auth.Domain/
COPY src/Services/Auth/SNHub.Auth.Infrastructure/SNHub.Auth.Infrastructure.csproj src/Services/Auth/SNHub.Auth.Infrastructure/
COPY src/Shared/SNHub.Shared/SNHub.Shared.csproj                                   src/Shared/SNHub.Shared/

RUN dotnet restore src/Services/Auth/SNHub.Auth.API/SNHub.Auth.API.csproj

# Copy full source and publish
COPY src/ src/
RUN dotnet publish src/Services/Auth/SNHub.Auth.API/SNHub.Auth.API.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

# Non-root user for security
RUN addgroup --system snhub && adduser --system --ingroup snhub snhub

WORKDIR /app
COPY --from=build /app/publish .
RUN chown -R snhub:snhub /app
USER snhub

HEALTHCHECK --interval=30s --timeout=10s --start-period=30s --retries=3 \
    CMD curl -f http://localhost:8080/health/live || exit 1

EXPOSE 8080

ENV ASPNETCORE_URLS="http://+:8080" \
    ASPNETCORE_ENVIRONMENT="Production" \
    DOTNET_RUNNING_IN_CONTAINER=true

ENTRYPOINT ["dotnet", "SNHub.Auth.API.dll"]
