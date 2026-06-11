# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# NOTE: At this time, this is used by the build pipeline only

# This Dockerfile uses Linux-based images for compatibility with AWS ECS Fargate and Azure DevOps Ubuntu agents
# BuildKit inline cache is enabled in CI/CD pipeline for faster builds by reusing layers from previous builds

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

# Set environment variable for ASP.NET Core to use port 80
ENV ASPNETCORE_URLS=http://+:80

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Copy the contents of dotnet publish in the CI/CD step the docker image is being built in the context of
COPY . ./

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final

# Configure OpenTelemetry environment variables for AWS X-Ray
# These can be overridden at runtime via container orchestration
ENV OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
ENV OTEL_EXPORTER_OTLP_PROTOCOL=grpc
ENV OTEL_TRACES_SAMPLER=always_on
ENV OTEL_PROPAGATORS=xray,tracecontext,baggage

# Ensure ASP.NET Core runs on port 80
ENV ASPNETCORE_URLS=http://+:80

ENTRYPOINT ["dotnet", "Wayroo.Payments.API.dll"]
