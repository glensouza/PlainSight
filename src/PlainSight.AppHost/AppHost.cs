using Projects;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL database with PgAdmin
IResourceBuilder<PostgresServerResource> postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Persistent);


IResourceBuilder<PostgresDatabaseResource> signageDb = postgres.AddDatabase("signagedb");

// Add Signage Server with database
IResourceBuilder<ProjectResource> signageServer = builder.AddProject<Signage_Server>("signage-server")
    .WaitFor(signageDb)
    .WithReference(signageDb);

// Add Signage Player — WithHttpEndpoint() lets Aspire assign a free port
// dynamically (sets ASPNETCORE_URLS) so there are no fixed-port conflicts.
builder.AddProject<Signage_Player>("signage-player")
    .WithHttpEndpoint(name: "http")
    .WithReference(signageServer)
    .WaitFor(signageServer);

builder.Build().Run();
