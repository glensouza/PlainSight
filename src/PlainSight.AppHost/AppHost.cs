var builder = DistributedApplication.CreateBuilder(args);

// Add PostgreSQL database
var postgres = builder.AddPostgres("postgres")
    .WithLifetime(ContainerLifetime.Persistent);

var signageDb = postgres.AddDatabase("signagedb");

// Add Signage Server with database
var server = builder.AddProject<Projects.Signage_Server>("signage-server")
    .WithReference(signageDb);

builder.Build().Run();
