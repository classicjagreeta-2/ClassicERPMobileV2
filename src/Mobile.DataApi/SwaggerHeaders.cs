using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Mobile.DataApi;

// Face, consent and attendance calls need header x-app-user (the ERP user signed in on the phone); this shows it as an input field in Swagger.
public class AppUserHeader : IOperationFilter
{
    public void Apply(OpenApiOperation op, OperationFilterContext ctx)
    {
        var path = ctx.ApiDescription.RelativePath?.Trim('/') ?? "";
        if (!(path.StartsWith("mobile/v1/face-") || path.StartsWith("mobile/v1/import-eye"))) return;
        op.Parameters.Add(new OpenApiParameter
        {
            Name = "x-app-user", In = ParameterLocation.Header, Required = true,
            Description = "The ERP user signed in on the phone (must exist in the user-login master), e.g. AYAN",
            Schema = new OpenApiSchema { Type = "string" }
        });
    }
}
