FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Services/Notifications/SNHub.Notifications.API/SNHub.Notifications.API.csproj", "src/Services/Notifications/SNHub.Notifications.API/"]
COPY ["src/Services/Notifications/SNHub.Notifications.Application/SNHub.Notifications.Application.csproj", "src/Services/Notifications/SNHub.Notifications.Application/"]
COPY ["src/Services/Notifications/SNHub.Notifications.Domain/SNHub.Notifications.Domain.csproj", "src/Services/Notifications/SNHub.Notifications.Domain/"]
COPY ["src/Services/Notifications/SNHub.Notifications.Infrastructure/SNHub.Notifications.Infrastructure.csproj", "src/Services/Notifications/SNHub.Notifications.Infrastructure/"]
RUN dotnet restore "src/Services/Notifications/SNHub.Notifications.API/SNHub.Notifications.API.csproj"
COPY . .
RUN dotnet publish "src/Services/Notifications/SNHub.Notifications.API/SNHub.Notifications.API.csproj" -c Release -o /app/publish
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SNHub.Notifications.API.dll"]
