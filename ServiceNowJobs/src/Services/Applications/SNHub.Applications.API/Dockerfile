FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/Services/Applications/SNHub.Applications.API/SNHub.Applications.API.csproj", "src/Services/Applications/SNHub.Applications.API/"]
COPY ["src/Services/Applications/SNHub.Applications.Application/SNHub.Applications.Application.csproj", "src/Services/Applications/SNHub.Applications.Application/"]
COPY ["src/Services/Applications/SNHub.Applications.Domain/SNHub.Applications.Domain.csproj", "src/Services/Applications/SNHub.Applications.Domain/"]
COPY ["src/Services/Applications/SNHub.Applications.Infrastructure/SNHub.Applications.Infrastructure.csproj", "src/Services/Applications/SNHub.Applications.Infrastructure/"]
RUN dotnet restore "src/Services/Applications/SNHub.Applications.API/SNHub.Applications.API.csproj"
COPY . .
RUN dotnet build "src/Services/Applications/SNHub.Applications.API/SNHub.Applications.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "src/Services/Applications/SNHub.Applications.API/SNHub.Applications.API.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "SNHub.Applications.API.dll"]
