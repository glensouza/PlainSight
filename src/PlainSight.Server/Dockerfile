# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_VERSION=1.0.0
WORKDIR /src

# Copy solution and project files
COPY ["PlainSight.slnx", "./"]
COPY ["src/PlainSight.Server/PlainSight.Server.csproj", "src/PlainSight.Server/"]
COPY ["src/PlainSight.Shared/PlainSight.Shared.csproj", "src/PlainSight.Shared/"]
COPY ["src/PlainSight.ServiceDefaults/PlainSight.ServiceDefaults.csproj", "src/PlainSight.ServiceDefaults/"]

# Restore dependencies
RUN dotnet restore "src/PlainSight.Server/PlainSight.Server.csproj"

# Copy everything else
COPY . .

# Build the application
WORKDIR "/src/src/PlainSight.Server"
RUN dotnet build "PlainSight.Server.csproj" -c Release -o /app/build /p:Version=${BUILD_VERSION}

# Publish stage
FROM build AS publish
RUN dotnet publish "PlainSight.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false /p:Version=${BUILD_VERSION}

# Runtime stage — Ubuntu Noble (default .NET 10 base). The stock `chromium` package on
# Noble is a Snap transitional stub that does not work in Docker, so we pull a real
# Chromium .deb from the xtradeb PPA (publishes both amd64 and arm64 builds).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

RUN apt-get update && apt-get install -y --no-install-recommends \
        software-properties-common gpg-agent ca-certificates \
    && add-apt-repository -y ppa:xtradeb/apps \
    && apt-get install -y --no-install-recommends \
        ffmpeg \
        fonts-liberation \
        chromium \
    && rm -rf /var/lib/apt/lists/*

ENV PUPPETEER_EXECUTABLE_PATH=/usr/bin/chromium


COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PlainSight.Server.dll"]
