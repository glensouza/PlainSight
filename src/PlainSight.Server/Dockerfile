# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
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
RUN dotnet build "PlainSight.Server.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "PlainSight.Server.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

RUN apt-get update && apt-get install -y --no-install-recommends \
    ffmpeg \
    chromium \
    fonts-liberation \
    libasound2t64 \
    libatk-bridge2.0-0t64 \
    libatk1.0-0t64 \
    libcairo2 \
    libcups2t64 \
    libdbus-1-3 \
    libdrm2 \
    libexpat1 \
    libgbm1 \
    libglib2.0-0t64 \
    libgtk-3-0t64 \
    libnspr4 \
    libnss3 \
    libpango-1.0-0 \
    libpangocairo-1.0-0 \
    libx11-6 \
    libxcb1 \
    libxcomposite1 \
    libxdamage1 \
    libxext6 \
    libxfixes3 \
    libxrandr2 \
    libxrender1 \
    libxtst6 \
    xdg-utils \
    && rm -rf /var/lib/apt/lists/*

ENV PUPPETEER_EXECUTABLE_PATH=/usr/bin/chromium

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PlainSight.Server.dll"]
