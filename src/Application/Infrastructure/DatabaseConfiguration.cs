using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System;

namespace Application.Infrastructure;

public static class DatabaseConfiguration
{
    public static WebApplicationBuilder AddDatabase(this WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("Database")
            ?? throw new InvalidOperationException("Database connection string not found");

        builder.Services.AddMarten(opts =>
        {
            opts.Connection(connectionString);

            // Configure event store
            opts.Events.StreamIdentity = StreamIdentity.AsString;

            // Auto-create schema in development
            if (builder.Environment.IsDevelopment())
            {
                opts.AutoCreateSchemaObjects = AutoCreate.CreateOrUpdate;
            }


        }).UseLightweightSessions()
          .IntegrateWithWolverine();

        return builder;
    }
}

