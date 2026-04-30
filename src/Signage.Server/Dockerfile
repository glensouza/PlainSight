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

RUN apt-get update && apt-get install -y --no-install-recommends \
    ffmpeg \
    chromium \
    fonts-liberation \
    libasound2 \
    libatk-bridge2.0-0 \
    libatk1.0-0 \
    libcairo2 \
    libcups2 \
    libdbus-1-3 \
    libdrm2 \
    libexpat1 \
    libgbm1 \
    libglib2.0-0 \
    libgtk-3-0 \
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
ENTRYPOINT ["dotnet", "Signage.Server.dll"]
