# syntax=docker/dockerfile:1
# Multi-stage build for OrderAggregator.Api (.NET 10).
# Build context is the `src/` directory (cross-project ProjectReferences).

# --- build stage ----------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy build-wide conventions + project files first so `dotnet restore` is
# cached as its own layer and only re-runs when a .csproj/props changes.
COPY Directory.Build.props .editorconfig ./
COPY OrderAggregator.Abstractions/OrderAggregator.Abstractions.csproj OrderAggregator.Abstractions/
COPY OrderAggregator.Models/OrderAggregator.Models.csproj                 OrderAggregator.Models/
COPY OrderAggregator.Contracts/OrderAggregator.Contracts.csproj           OrderAggregator.Contracts/
COPY OrderAggregator.Resources/OrderAggregator.Resources.csproj           OrderAggregator.Resources/
COPY OrderAggregator.Shared/OrderAggregator.Shared.csproj                 OrderAggregator.Shared/
COPY OrderAggregator.Services/OrderAggregator.Services.csproj             OrderAggregator.Services/
COPY OrderAggregator.Api/OrderAggregator.Api.csproj                       OrderAggregator.Api/

RUN dotnet restore OrderAggregator.Api/OrderAggregator.Api.csproj

# Copy the rest of the sources and publish.
COPY . .
RUN dotnet publish OrderAggregator.Api/OrderAggregator.Api.csproj \
    -c Release --no-restore -o /app/publish

# --- runtime stage --------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl backs the compose healthcheck against /health/live (not in the base image).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

# HTTP-only inside the container; TLS is terminated by a reverse proxy in prod.
EXPOSE 5047
ENV ASPNETCORE_URLS=http://+:5047

ENTRYPOINT ["dotnet", "OrderAggregator.Api.dll"]
