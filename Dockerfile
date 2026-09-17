# Multi-stage build for KestrelScope Sovereign Observability Platform
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY KestrelScope.csproj ./
RUN dotnet restore KestrelScope.csproj

# Copy application source code and web assets
COPY Constants/ ./Constants/
COPY Controllers/ ./Controllers/
COPY Models/ ./Models/
COPY Services/ ./Services/
COPY Tools/ ./Tools/
COPY DbInitializer.cs Program.cs appsettings.json ./
COPY wwwroot/ ./wwwroot/

# Publish Release build
RUN dotnet publish KestrelScope.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Install curl for container health checks
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# Ensure persistent SQLite data directory exists
RUN mkdir -p /app/data

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:5000
EXPOSE 5000

ENTRYPOINT ["dotnet", "KestrelScope.dll"]
