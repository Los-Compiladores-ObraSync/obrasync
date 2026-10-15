var builder = WebApplication.CreateBuilder(args);

// Registrar controladores y Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "ObraSync - Servicio de Usuarios y Obras",
        Version = "v1"
    });
});

var app = builder.Build();

// Habilitar Swagger siempre en desarrollo
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ObraSync Usuarios API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseAuthorization();
app.MapControllers();

// Endpoint mínimo de prueba
app.MapGet("/weatherforecast", () => new[] { "OK" });

app.Run();