# ── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy solution and project files
COPY *.sln ./
COPY OTT.API/*.csproj ./OTT.API/
COPY OTT.Application/*.csproj ./OTT.Application/
COPY OTT.Domain/*.csproj ./OTT.Domain/
COPY OTT.Infrastructure/*.csproj ./OTT.Infrastructure/

# Restore
RUN dotnet restore

# Copy everything else and build
COPY . .
RUN dotnet publish OTT.API/OTT.API.csproj -c Release -o /app/publish --no-restore

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install FFmpeg
RUN apt-get update && apt-get install -y \
    ffmpeg \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Create keys directory
RUN mkdir -p /app/keys

# Copy published output
COPY --from=build /app/publish .

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=60s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

EXPOSE 8080

ENTRYPOINT ["dotnet", "OTT.API.dll"]
