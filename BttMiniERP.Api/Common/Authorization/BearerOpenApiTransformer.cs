using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace BttMiniERP.Api.Common.Authorization;

public sealed class BearerOpenApiTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" };
        foreach (var description in context.DescriptionGroups.SelectMany(group => group.Items))
        {
            if (description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()) continue;
            var path = "/" + description.RelativePath;
            if (!document.Paths.TryGetValue(path, out var item) || item.Operations == null) continue;
            foreach (var operation in item.Operations.Where(pair => string.Equals(pair.Key.Method, description.HttpMethod, StringComparison.OrdinalIgnoreCase)))
                operation.Value.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
        }
        return Task.CompletedTask;
    }
}
