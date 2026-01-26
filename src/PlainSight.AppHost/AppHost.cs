IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL database with PgAdmin
IResourceBuilder<PostgresServerResource> postgres = builder.AddPostgres("postgres")
    .WithPgAdmin()
    .WithLifetime(ContainerLifetime.Persistent);


IResourceBuilder<PostgresDatabaseResource> signageDb = postgres.AddDatabase("signagedb");

// Add Signage Server with database
builder.AddProject<Projects.Signage_Server>("signage-server")
    .WaitFor(signageDb)
    .WithReference(signageDb);

builder.Build().Run();
