using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Scalar.AspNetCore;
using System.Net.Mime;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Wolverine.Http.FluentValidation;
using Wolverine.SignalR;

namespace Application.Infrastructure;

public static class EndpointsConfiguration
{
    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddEndpointsModule()
        {
            builder.Services.AddOpenApi(options =>
            {
                options.ShouldInclude = _ => true;



                options.AddOperationTransformer<ReadableOperationTransformer>();
                options.AddSchemaTransformer<XmlDocumentationSchemaTransformer>();
                options.AddDocumentTransformer((document, _, _) =>
                {
                    document.Info.Title = "Today I Learn Backend API";
                    document.Info.Description = "Learn, Learn, Learn!";

                    document.Components ??= new();

                    // Keep only simple tags that were set by ReadableOperationTransformer
                    if (document.Tags != null)
                    {
                        document.Tags = document.Tags.Where(tag => tag is not { Name: null } && !tag.Name.Contains('.'))
                            .ToHashSet();
                    }

                    return Task.CompletedTask;
                });
            });

            builder.Services.AddCors(options =>
            {
                var origins = builder.Configuration
                    .GetRequiredSection("CORS")
                    .GetRequiredSection("AllowedOrigins")
                    .Value!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                options.AddPolicy("AllowSpecificOrigin",
                    builder => builder
                        .WithOrigins(origins)
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials()
                        .WithExposedHeaders(HeaderNames.ContentDisposition)
                        .SetIsOriginAllowedToAllowWildcardSubdomains());
            });

            builder.Services.AddHealthChecks();
            return builder;
        }

        public WebApplicationBuilder AddForwardHeadersConfigs()
        {
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders =
                    ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownIPNetworks.Clear();
                options.KnownProxies.Clear();
            });
            return builder;
        }
    }


    public static WebApplication ConfigureEndpointsModule(this WebApplication webApplication)
    {
        webApplication.MapOpenApi();
        webApplication.UseForwardedHeaders();
        webApplication.MapScalarApiReference(options =>
        {
            options.WithTitle("API Specification");
        });

        webApplication.UseExceptionHandler(appBuilder =>
        {
            appBuilder.Run(async context =>
            {
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.ContentType = MediaTypeNames.Text.Plain;
                var responseBody = string.Empty;

                if (webApplication.Environment.IsDevelopment())
                {
                    var exceptionHandler = context.Features.Get<IExceptionHandlerFeature>();
                    responseBody = $"{exceptionHandler!.Error.Message} at {exceptionHandler.Error.StackTrace}";
                }

                await context.Response.WriteAsync(responseBody);
            });
        });

        webApplication.UseRouting()
            .UseCors("AllowSpecificOrigin")
            //.UseAuthorization()
            .UseEndpoints(endpoints =>
            {
                endpoints.MapHealthChecks("/health/ready", new()
                {
                    Predicate = check => check.Tags.Contains("postgres") ||
                                         check.Tags.Contains("applifetime") ||
                                         check.Tags.Contains("daemon")
                });

                endpoints.MapHealthChecks("/health/live", new() { Predicate = _ => false });
            });

        webApplication.MapWolverineEndpoints(opts =>
        {
            //opts.RequireAuthorizeOnAll();
            opts.UseFluentValidationProblemDetailMiddleware();
            opts.WarmUpRoutes = RouteWarmup.Eager;
        });

        webApplication.MapWolverineSignalRHub("/hubs/werewolf");

        return webApplication;
    }
}

public class ReadableOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        operation.Tags = new HashSet<OpenApiTagReference>();

        var path = context.Description.RelativePath?.TrimStart('/');
        if (!string.IsNullOrEmpty(path))
        {
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments is ["api", "v1", _, ..])
            {
                var groupName = segments[2];

                groupName = char.ToUpperInvariant(groupName[0]) + groupName[1..];
                operation.Tags.Add(new(groupName));
            }
            else
            {
                operation.Tags.Add(new(segments[0]));
            }
        }

        // We added a custom summary since the default summaries don't contain spaces so don't replace it
        if (!string.IsNullOrEmpty(operation.Summary) && operation.Summary.Contains(' '))
        {
            return Task.CompletedTask;
        }

        var method = context.Description.HttpMethod is { }
            ? $"{context.Description.HttpMethod.ToUpperInvariant()} "
            : "";
        operation.Summary = $"{method}/{path}";


        return Task.CompletedTask;
    }
}

public class XmlDocumentationSchemaTransformer : IOpenApiSchemaTransformer
{
    private static readonly Lazy<XDocument?> XmlDoc = new(LoadXmlDocumentation);

    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var xmlDoc = XmlDoc.Value;
        if (xmlDoc == null)
        {
            return Task.CompletedTask;
        }

        var type = context.JsonTypeInfo.Type;

        // Add type description
        var typeSummary = GetXmlDocumentation(xmlDoc, type);
        if (!string.IsNullOrEmpty(typeSummary))
        {
            schema.Description = typeSummary;
        }

        // Add property descriptions
        if (schema.Properties == null)
        {
            return Task.CompletedTask;
        }

        foreach (var property in schema.Properties)
        {
            var propertyInfo = type.GetProperty(property.Key,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (propertyInfo == null)
            {
                continue;
            }

            var propertyDoc = GetXmlDocumentation(xmlDoc, propertyInfo);
            if (!string.IsNullOrEmpty(propertyDoc))
            {
                property.Value.Description = propertyDoc;
            }
        }

        return Task.CompletedTask;
    }

    private static XDocument? LoadXmlDocumentation()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var xmlPath = Path.Combine(Path.GetDirectoryName(assembly.Location) ?? "",
                $"{assembly.GetName().Name}.xml");

            if (File.Exists(xmlPath))
            {
                return XDocument.Load(xmlPath);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load XML documentation: {ex.Message}");
        }

        return null;
    }

    private static string? GetXmlDocumentation(XDocument xmlDoc, Type type)
    {
        var memberName = $"T:{type.FullName}";
        return GetXmlDocumentationByMemberName(xmlDoc, memberName);
    }

    private static string? GetXmlDocumentation(XDocument xmlDoc, PropertyInfo propertyInfo)
    {
        var memberName = $"P:{propertyInfo.DeclaringType?.FullName}.{propertyInfo.Name}";
        return GetXmlDocumentationByMemberName(xmlDoc, memberName);
    }

    private static string? GetXmlDocumentationByMemberName(XDocument xmlDoc, string memberName)
    {
        var element = xmlDoc.Descendants("member")
            .FirstOrDefault(m => m.Attribute("name")?.Value == memberName);

        var summary = element?.Element("summary");
        return summary?.Value.Trim();
    }
}
