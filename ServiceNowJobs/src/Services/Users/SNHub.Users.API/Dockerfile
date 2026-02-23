FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Services/Users/SNHub.Users.API/SNHub.Users.API.csproj", "src/Services/Users/SNHub.Users.API/"]
COPY ["src/Services/Users/SNHub.Users.Application/SNHub.Users.Application.csproj", "src/Services/Users/SNHub.Users.Application/"]
COPY ["src/Services/Users/SNHub.Users.Domain/SNHub.Users.Domain.csproj", "src/Services/Users/SNHub.Users.Domain/"]
COPY ["src/Services/Users/SNHub.Users.Infrastructure/SNHub.Users.Infrastructure.csproj", "src/Services/Users/SNHub.Users.Infrastructure/"]
RUN dotnet restore "src/Services/Users/SNHub.Users.API/SNHub.Users.API.csproj"
COPY . .
RUN dotnet publish "src/Services/Users/SNHub.Users.API/SNHub.Users.API.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 CMD curl -f http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "SNHub.Users.API.dll"]
