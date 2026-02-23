FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Services/Jobs/SNHub.Jobs.API/SNHub.Jobs.API.csproj", "src/Services/Jobs/SNHub.Jobs.API/"]
COPY ["src/Services/Jobs/SNHub.Jobs.Application/SNHub.Jobs.Application.csproj", "src/Services/Jobs/SNHub.Jobs.Application/"]
COPY ["src/Services/Jobs/SNHub.Jobs.Domain/SNHub.Jobs.Domain.csproj", "src/Services/Jobs/SNHub.Jobs.Domain/"]
COPY ["src/Services/Jobs/SNHub.Jobs.Infrastructure/SNHub.Jobs.Infrastructure.csproj", "src/Services/Jobs/SNHub.Jobs.Infrastructure/"]
RUN dotnet restore "src/Services/Jobs/SNHub.Jobs.API/SNHub.Jobs.API.csproj"
COPY . .
RUN dotnet publish "src/Services/Jobs/SNHub.Jobs.API/SNHub.Jobs.API.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "SNHub.Jobs.API.dll"]
