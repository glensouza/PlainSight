using Projects;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL database with PgAdmin
IResourceBuilder<PostgresServerResource> postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Persistent);


IResourceBuilder<PostgresDatabaseResource> signageDb = postgres.AddDatabase("signagedb");

// Add Signage Server with database
builder.AddProject<Signage_Server>("signage-server")
    .WaitFor(signageDb)
    .WithReference(signageDb);

// Add Signage Player (embedded Kestrel + Chromium kiosk on Linux)
builder.AddProject<Signage_Player>("signage-player");

builder.Build().Run();
