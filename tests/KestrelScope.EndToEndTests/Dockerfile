# Dockerfile for containerized execution of KestrelScope End-to-End Tests
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS test
WORKDIR /src

COPY tests/KestrelScope.EndToEndTests/KestrelScope.EndToEndTests.csproj tests/KestrelScope.EndToEndTests/
RUN dotnet restore tests/KestrelScope.EndToEndTests/KestrelScope.EndToEndTests.csproj

COPY tests/KestrelScope.EndToEndTests/ tests/KestrelScope.EndToEndTests/
WORKDIR /src/tests/KestrelScope.EndToEndTests

ENV KESTRELSCOPE_URL=http://kestrelscope:5000
ENV SAMPLE_SERVICE_URL=http://sample-service:8080
ENV INTERNAL_KESTRELSCOPE_URL=http://kestrelscope:5000/api/test-webhook

ENTRYPOINT ["dotnet", "test", "--verbosity", "normal", "--logger", "console;verbosity=detailed"]
