using BttMiniERP.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BttMiniERP.Api.Common.Authorization.BearerOpenApiTransformer>());
builder.Services.AddPersistence(builder.Configuration, builder.Environment.EnvironmentName);
builder.Services.AddScoped<BttMiniERP.Api.Services.CategoryService>();
builder.Services.AddScoped<BttMiniERP.Api.Services.ItemService>();
BttMiniERP.Api.Common.Authorization.SecurityRegistration.AddApplicationSecurity(builder.Services, builder.Configuration);

var app = builder.Build();

if (args.Contains("--seed-identity=true", StringComparer.Ordinal))
{
    IdentitySeeder.ValidateTarget(builder.Configuration, builder.Environment.EnvironmentName, args);
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().RunAsync(builder.Configuration, CancellationToken.None);
    return;
}

app.UseExceptionHandler();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapOpenApi().AllowAnonymous();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new HealthResponse("Healthy")))
    .AllowAnonymous().WithName("GetHealth")
    .WithSummary("Kiểm tra trạng thái hoạt động của API.");

app.Run();

public sealed record HealthResponse(string Status);

public partial class Program;
