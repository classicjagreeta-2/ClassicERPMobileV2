using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Mobile.Gateway;

// The provision call reads its login from HEADERS; this makes Swagger show them as input fields.
public class ProvisionHeaders : IOperationFilter
{
    public void Apply(OpenApiOperation op, OperationFilterContext ctx)
    {
        if (ctx.ApiDescription.RelativePath?.Trim('/') != "gateway/v1/provision") return;
        foreach (var (name, text) in new[]
        {
            ("x-username", "Company login (e.g. CORETEAM)"),
            ("x-password", "Company password"),
            ("x-api-key", "Company API key (generated in MobileKeyManager)")
        })
            op.Parameters.Add(new OpenApiParameter { Name = name, In = ParameterLocation.Header, Required = true, Description = text, Schema = new OpenApiSchema { Type = "string" } });
        op.Summary = "Get a token (and the config for the phone) with the company login, password and API key";
    }
}
