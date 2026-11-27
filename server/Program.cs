using LabAlert;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ResultStore>();
builder.Services.AddHostedService<MllpListener>();

var app = builder.Build();
app.MapGet("/api/results", async (string? patient, string? unit, ResultStore store, CancellationToken ct) =>
    Results.Ok(await store.GetFlaggedAsync(patient, unit, ct)));
app.MapPost("/api/results/{id:long}/acknowledge", async (long id, ResultStore store, CancellationToken ct) =>
    await store.AcknowledgeAsync(id, ct) ? Results.NoContent() : Results.NotFound());
app.Run();
