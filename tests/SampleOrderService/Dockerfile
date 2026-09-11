# Multi-stage build for SampleOrderService Telemetry Test Service
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY tests/SampleOrderService/SampleOrderService.csproj tests/SampleOrderService/
RUN dotnet restore tests/SampleOrderService/SampleOrderService.csproj

COPY tests/SampleOrderService/ tests/SampleOrderService/
WORKDIR /src/tests/SampleOrderService
RUN dotnet publish SampleOrderService.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Install curl for container health checks
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "SampleOrderService.dll"]
