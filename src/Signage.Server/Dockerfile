# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["PlainSight.slnx", "./"]
COPY ["src/Signage.Server/Signage.Server.csproj", "src/Signage.Server/"]
COPY ["src/Signage.Shared/Signage.Shared.csproj", "src/Signage.Shared/"]
COPY ["src/PlainSight.ServiceDefaults/PlainSight.ServiceDefaults.csproj", "src/PlainSight.ServiceDefaults/"]

# Restore dependencies
RUN dotnet restore "src/Signage.Server/Signage.Server.csproj"

# Copy everything else
COPY . .

# Build the application
WORKDIR "/src/src/Signage.Server"
RUN dotnet build "Signage.Server.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "Signage.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Note: Puppeteer dependencies removed as WebsiteRecorder service is incomplete
# and requires FFmpeg integration for production use (see SECURITY.md)

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Signage.Server.dll"]
