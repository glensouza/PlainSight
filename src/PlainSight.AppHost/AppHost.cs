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

// Player is a Raspberry Pi Linux service and cannot run on Windows dev machines.
if (!OperatingSystem.IsWindows())
{
    builder.AddProject<PlainSight_Player>("plainsight-player")
        .WithReference(plainsightServer)
        .WaitFor(plainsightServer);
}

builder.Build().Run();
