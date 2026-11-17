# ==============================================================================
# Multi-Stage Dockerfile for Model Context Protocol (MCP) Todo Server
# Complies with: D-33 (alpine/runtime non-root), D-35 & CD-01 (zero secrets), CD-03
# ==============================================================================

# ------------------------------------------------------------------------------
# Stage 1: Build & Publish
# ------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Layer caching: copy project file and restore dependencies first
COPY ["mcp-todo.csproj", "./"]
RUN dotnet restore "mcp-todo.csproj"

# Copy remaining source code and publish release binaries
COPY . .
RUN dotnet publish "mcp-todo.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ------------------------------------------------------------------------------
# Stage 2: Final Minimal Runtime
# ------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS final
WORKDIR /app

# Security Hardening (D-33, T-06-03): Run as unprivileged non-root user
USER $APP_UID

# Copy compiled application from build stage
COPY --from=build /app/publish .

# Zero-Secret Rule (D-35, CD-01): Credentials must be injected via runtime environment variables
# Expected env vars: ConnectionStrings__Supabase, DESCOPE_PROJECT_ID, DESCOPE_USER_TOKEN
ENTRYPOINT ["dotnet", "mcp-todo.dll"]
