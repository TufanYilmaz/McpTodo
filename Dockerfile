# ==============================================================================
# Multi-Stage Dockerfile for Model Context Protocol (MCP) Todo Server
# ==============================================================================

# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Layer caching: copy project file and restore dependencies first
COPY ["mcp-todo/mcp-todo.csproj", "mcp-todo/"]
RUN dotnet restore "mcp-todo/mcp-todo.csproj"

# Copy remaining source code and publish release binaries
COPY . .
WORKDIR "/src/mcp-todo"
RUN dotnet publish "mcp-todo.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: Final Minimal ASP.NET Core Runtime (Supports Web & Stdio)
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app
EXPOSE 8080
ENV PORT=8080

# Security Hardening: Run as unprivileged non-root user
USER $APP_UID

# Copy compiled application from build stage
COPY --from=build /app/publish .

# Zero-Secret Rule: Credentials injected via runtime environment variables
ENTRYPOINT ["dotnet", "mcp-todo.dll"]
