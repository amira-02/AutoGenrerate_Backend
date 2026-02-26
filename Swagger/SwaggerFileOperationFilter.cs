using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

public class SwaggerFileOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        // 🔹 Vérifier que OperationId n'est pas null
        if (!string.IsNullOrEmpty(operation.OperationId) &&
            operation.OperationId.ToLower().Contains("generate"))
        {
            operation.RequestBody = new OpenApiRequestBody
            {
                Content =
                {
                    ["multipart/form-data"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties =
                            {
                                ["prompt"] = new OpenApiSchema { Type = "string" },
                                ["jsonFile"] = new OpenApiSchema { Type = "string", Format = "binary" }
                            },
                            Required = new HashSet<string> { "prompt", "jsonFile" }
                        }
                    }
                }
            };
        }
    }
}