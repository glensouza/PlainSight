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

// Add Signage Player — reference signage-server so Aspire injects its URL
// for service discovery (resolves "http://signage-server" in HeartbeatService)
builder.AddProject<Signage_Player>("signage-player")
    .WithReference(signageServer)
    .WaitFor(signageServer);

builder.Build().Run();
