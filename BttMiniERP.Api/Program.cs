var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.MapOpenApi();

app.MapGet("/health", () => Results.Ok(new HealthResponse("Healthy")))
    .WithName("GetHealth")
    .WithSummary("Kiểm tra trạng thái hoạt động của API.");

app.Run();

public sealed record HealthResponse(string Status);

public partial class Program;
