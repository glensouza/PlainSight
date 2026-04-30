using Projects;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL database with PgAdmin
IResourceBuilder<PostgresServerResource> postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Persistent);

IResourceBuilder<PostgresDatabaseResource> plainsightDb = postgres.AddDatabase("plainsightdb");

// Add PlainSight Server with database
IResourceBuilder<ProjectResource> plainsightServer = builder.AddProject<PlainSight_Server>("plainsight-server")
    .WaitFor(plainsightDb)
    .WithReference(plainsightDb);

// Aspire discovers the player's HTTP endpoint from launchSettings.json
// (profile "http", applicationUrl http://localhost:5200).
builder.AddProject<PlainSight_Player>("plainsight-player")
    .WithReference(plainsightServer)
    .WaitFor(plainsightServer);

builder.Build().Run();
