using backend.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Servicios ─────────────────────────────────────────────────────────────────
builder.Services.AddControllers();

// Registrar TFLiteService como singleton (el modelo solo se carga una vez)
builder.Services.AddSingleton<TFLiteService>(_ =>
{
    string baseDir = AppContext.BaseDirectory;
    string modelPath  = Path.Combine(baseDir, "assets", "modelo_plantas.tflite");
    string clasesPath = Path.Combine(baseDir, "assets", "clases.txt");
    return new TFLiteService(modelPath, clasesPath);
});

// CORS para que Flutter Web pueda consumir la API
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

// ── Pipeline ──────────────────────────────────────────────────────────────────
app.UseCors();
app.MapControllers();

// Precalentar el servicio al arrancar
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<TFLiteService>();

app.Run();
