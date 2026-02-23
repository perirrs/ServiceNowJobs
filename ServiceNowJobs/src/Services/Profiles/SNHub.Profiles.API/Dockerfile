FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Services/Profiles/SNHub.Profiles.API/SNHub.Profiles.API.csproj", "src/Services/Profiles/SNHub.Profiles.API/"]
COPY ["src/Services/Profiles/SNHub.Profiles.Application/SNHub.Profiles.Application.csproj", "src/Services/Profiles/SNHub.Profiles.Application/"]
COPY ["src/Services/Profiles/SNHub.Profiles.Domain/SNHub.Profiles.Domain.csproj", "src/Services/Profiles/SNHub.Profiles.Domain/"]
COPY ["src/Services/Profiles/SNHub.Profiles.Infrastructure/SNHub.Profiles.Infrastructure.csproj", "src/Services/Profiles/SNHub.Profiles.Infrastructure/"]
RUN dotnet restore "src/Services/Profiles/SNHub.Profiles.API/SNHub.Profiles.API.csproj"
COPY . .
RUN dotnet publish "src/Services/Profiles/SNHub.Profiles.API/SNHub.Profiles.API.csproj" -c Release -o /app/publish
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SNHub.Profiles.API.dll"]
