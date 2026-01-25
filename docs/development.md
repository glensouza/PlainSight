# Development Guide

Guide for setting up PlainSight for local development.

## Prerequisites

- .NET 10 SDK
- Docker Desktop
- IDE (Visual Studio, VS Code, or Rider)
- Git

## Initial Setup

### 1. Clone Repository

```bash
git clone https://github.com/glensouza/PlainSight.git
cd PlainSight
```

### 2. Restore Dependencies

```bash
dotnet restore
```

### 3. Run with .NET Aspire

The easiest way to run locally is using .NET Aspire:

```bash
dotnet run --project src/PlainSight.AppHost
```

This will:
- Start PostgreSQL in a container
- Start the Signage.Server application
- Open the Aspire dashboard

### 4. Access Applications

- **Aspire Dashboard**: http://localhost:15888
- **Signage Server**: http://localhost:5000 (check Aspire dashboard for actual port)

## Project Structure

```
PlainSight/
├── src/
│   ├── PlainSight.AppHost/          # Aspire orchestration
│   ├── PlainSight.ServiceDefaults/  # Shared Aspire config
│   ├── Signage.Server/              # Admin web application
│   ├── Signage.Player/              # Raspberry Pi client
│   └── Signage.Shared/              # Shared models
├── deployment/
│   └── raspberry-pi/                # Pi deployment files
├── docs/                            # Documentation
├── docker-compose.yml               # Production deployment
└── PlainSight.sln                   # Solution file
```

## Building

### Build Entire Solution

```bash
dotnet build
```

### Build Specific Project

```bash
dotnet build src/Signage.Server/Signage.Server.csproj
dotnet build src/Signage.Player/Signage.Player.csproj
```

### Build Docker Image

```bash
docker build -t plainsight-server -f src/Signage.Server/Dockerfile .
```

## Running

### Run Server Only

```bash
cd src/Signage.Server
dotnet run
```

**Note**: This requires PostgreSQL to be running separately.

### Run with Docker Compose

```bash
docker compose up
```

### Run Player (Development)

```bash
cd src/Signage.Player
dotnet run
```

**Environment Variables**:
- `ServerUrl`: Server URL (default: http://localhost:8080)

## Database

### Migrations (EF Core)

Install EF Core tools:

```bash
dotnet tool install --global dotnet-ef
```

Create migration:

```bash
cd src/Signage.Server
dotnet ef migrations add InitialCreate
```

Apply migrations:

```bash
dotnet ef database update
```

### Access PostgreSQL

When running with Aspire or Docker Compose:

```bash
# Via Docker
docker exec -it plainsight-postgres psql -U plainsight -d signagedb

# Via psql (if installed locally)
psql -h localhost -U plainsight -d signagedb
```

## Testing

### Run Tests (when added)

```bash
dotnet test
```

### Manual Testing

1. Start the server
2. Use curl or Postman to test API endpoints
3. Check logs in Aspire dashboard

## Debugging

### Visual Studio

1. Set `PlainSight.AppHost` as startup project
2. Press F5 to start debugging
3. Aspire will launch all services

### VS Code

1. Open folder in VS Code
2. Install C# extension
3. Use `.vscode/launch.json` configuration
4. Press F5

### Rider

1. Open solution in Rider
2. Set run configuration to `PlainSight.AppHost`
3. Start debugging

## Common Development Tasks

### Add New API Endpoint

1. Create method in controller:

```csharp
// src/Signage.Server/Controllers/DeviceController.cs
[HttpGet("{id}")]
public async Task<IActionResult> GetDevice(int id)
{
    var device = await _context.Devices.FindAsync(id);
    return device == null ? NotFound() : Ok(device);
}
```

2. Test endpoint:

```bash
curl http://localhost:5000/api/device/1
```

### Add New Database Entity

1. Create model in Signage.Shared:

```csharp
// src/Signage.Shared/Models/Content.cs
public class Content
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Url { get; set; }
}
```

2. Add DbSet to context:

```csharp
// src/Signage.Server/Data/SignageDbContext.cs
public DbSet<Content> Contents => Set<Content>();
```

3. Create and apply migration:

```bash
dotnet ef migrations add AddContent
dotnet ef database update
```

### Add New Player Service

1. Create service:

```csharp
// src/Signage.Player/Services/NewService.cs
public class NewService
{
    private readonly ILogger<NewService> _logger;
    
    public NewService(ILogger<NewService> logger)
    {
        _logger = logger;
    }
    
    public void DoSomething()
    {
        _logger.LogInformation("Doing something");
    }
}
```

2. Register in Program.cs:

```csharp
builder.Services.AddSingleton<NewService>();
```

## Code Style

### Formatting

Use .NET code style:

```bash
dotnet format
```

### Naming Conventions

- PascalCase for classes, methods, properties
- camelCase for local variables, parameters
- _camelCase for private fields

### Example

```csharp
public class DeviceService
{
    private readonly ILogger<DeviceService> _logger;
    
    public DeviceService(ILogger<DeviceService> logger)
    {
        _logger = logger;
    }
    
    public async Task<Device?> GetDeviceAsync(string deviceId)
    {
        var device = await FindDeviceByIdAsync(deviceId);
        return device;
    }
}
```

## Troubleshooting

### Port Already in Use

Change port in `launchSettings.json`:

```json
{
  "applicationUrl": "http://localhost:5001"
}
```

### Database Connection Failed

Ensure PostgreSQL container is running:

```bash
docker ps | grep postgres
```

### NuGet Package Restore Failed

Clear cache and restore:

```bash
dotnet nuget locals all --clear
dotnet restore
```

### Aspire Dashboard Not Opening

Check Aspire is installed:

```bash
dotnet workload list
```

Install if missing:

```bash
dotnet workload install aspire
```

## Performance Profiling

### dotnet-trace

```bash
dotnet tool install --global dotnet-trace
dotnet trace collect --process-id <PID>
```

### dotnet-counters

```bash
dotnet tool install --global dotnet-counters
dotnet counters monitor --process-id <PID>
```

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make changes
4. Test locally
5. Submit pull request

## Resources

- [.NET 10 Documentation](https://docs.microsoft.com/dotnet)
- [ASP.NET Core](https://docs.microsoft.com/aspnet/core)
- [Entity Framework Core](https://docs.microsoft.com/ef/core)
- [.NET Aspire](https://learn.microsoft.com/dotnet/aspire)
- [Blazor](https://docs.microsoft.com/aspnet/core/blazor)
