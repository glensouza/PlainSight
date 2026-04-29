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

// Aspire discovers the player's HTTP endpoint from launchSettings.json
// (profile "http", applicationUrl http://localhost:5200).
builder.AddProject<Signage_Player>("signage-player")
    .WithReference(signageServer)
    .WaitFor(signageServer);

builder.Build().Run();
