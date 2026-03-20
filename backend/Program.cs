// ═══════════════════════════════════════════════════════════════════════════════
//  Program.cs  —  Punto de entrada de la aplicación ASP.NET Core
// ═══════════════════════════════════════════════════════════════════════════════
//
//  En C# moderno (.NET 6+) existe el patrón "Top-Level Statements":
//  no hace falta escribir "class Program { static void Main(...) { ... } }".
//  El compilador lo genera automáticamente. Todo lo que escribas aquí es el
//  cuerpo de Main, lo que hace el código más limpio y directo.
//
//  Este archivo hace DOS cosas:
//    1. CONSTRUIR la app → registrar servicios, configurar opciones.
//    2. EJECUTAR la app  → activar middleware y escuchar peticiones HTTP.
//
// ═══════════════════════════════════════════════════════════════════════════════

using backend.Services;

// ── FASE 1: BUILDER ───────────────────────────────────────────────────────────
//
// WebApplication.CreateBuilder(args) crea un "constructor" de la aplicación.
// args son los argumentos de la línea de comandos (ej: --urls http://...).
// El builder nos da acceso al contenedor de inyección de dependencias (DI),
// la configuración (appsettings.json, variables de entorno) y el logging.
//
var builder = WebApplication.CreateBuilder(args);

// ── Registro de Controllers ───────────────────────────────────────────────────
//
// AddControllers() le dice a ASP.NET Core:
//   "Escanea los ensamblados en busca de clases que hereden de ControllerBase
//    y regístralas para manejar peticiones HTTP."
//
// Sin esta línea, PredictController nunca recibiría ninguna petición.
// Usa el patrón MVC (Model-View-Controller), pero solo la parte Controller
// porque esta es una Web API (sin vistas HTML).
//
builder.Services.AddControllers();

// ── Registro de TFLiteService como Singleton ──────────────────────────────────
//
// En C# existe el concepto de "Lifetimes" (tiempos de vida) para servicios:
//
//   • AddSingleton   → Se crea UNA sola instancia para toda la vida de la app.
//                      Todos los controllers que lo pidan reciben EL MISMO objeto.
//
//   • AddScoped      → Se crea una instancia por cada petición HTTP.
//                      Perfecto para conexiones a base de datos (DbContext).
//
//   • AddTransient   → Se crea una instancia nueva cada vez que se pide.
//                      Para servicios livianos y sin estado.
//
// ¿Por qué Singleton para TFLiteService?
//   Cargar un modelo de IA es una operación MUY costosa (leer disco, reservar
//   memoria nativa, compilar el grafo de operaciones). Si lo creáramos por cada
//   petición HTTP, la app sería inusablemente lenta. Al ser Singleton, se carga
//   UNA vez al arrancar y queda en memoria para siempre.
//
// La lambda "_ =>" recibe el IServiceProvider (el contenedor DI) pero no lo
// usamos aquí porque TFLiteService no depende de otros servicios registrados;
// solo necesita rutas de archivo. Por eso el parámetro se ignora con _.
//
builder.Services.AddSingleton<TFLiteService>(_ =>
{
    // AppContext.BaseDirectory devuelve la carpeta donde está el ejecutable.
    // Es más confiable que Directory.GetCurrentDirectory() porque no cambia
    // según desde dónde se ejecute la app.
    string baseDir = AppContext.BaseDirectory;

    // Path.Combine construye rutas de forma segura y multiplataforma,
    // usando el separador correcto (\ en Windows, / en Linux/Mac).
    string modelPath  = Path.Combine(baseDir, "assets", "modelo_plantas.tflite");
    string clasesPath = Path.Combine(baseDir, "assets", "clases.txt");

    // Creamos manualmente la instancia porque necesitamos pasar argumentos
    // al constructor que no son servicios registrados (son paths de archivo).
    return new TFLiteService(modelPath, clasesPath);
});

// ── Configuración de CORS ─────────────────────────────────────────────────────
//
// CORS (Cross-Origin Resource Sharing) es un mecanismo de seguridad del
// navegador que bloquea peticiones HTTP hechas desde un origen diferente
// al del servidor. Por ejemplo, Flutter Web corriendo en localhost:8080 NO
// puede llamar a localhost:5000 sin que el servidor lo permita explícitamente.
//
// AllowAnyOrigin()  → Acepta peticiones de cualquier dominio.
// AllowAnyHeader()  → Acepta cualquier cabecera HTTP (Authorization, Content-Type, etc.).
// AllowAnyMethod()  → Acepta GET, POST, PUT, DELETE, etc.
//
// Para producción se debería restringir el origen:
//   policy.WithOrigins("https://mi-app.com")
// Pero para desarrollo local, permitir todo es lo más cómodo.
//
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

// ── FASE 2: BUILD ─────────────────────────────────────────────────────────────
//
// Build() finaliza la configuración y crea el objeto WebApplication.
// A partir de aquí ya no podemos registrar más servicios.
// Ahora configuramos el PIPELINE HTTP: la cadena de middleware que procesa
// cada petición en orden de registro.
//
var app = builder.Build();

// ── Middleware: CORS ──────────────────────────────────────────────────────────
//
// app.Use* registra middleware en el pipeline. El orden IMPORTA:
// cada petición pasa por los middleware en el orden en que están registrados.
//
// UseCors() debe ir ANTES de MapControllers() para que las cabeceras CORS
// se añadan antes de que el controller procese la petición. Si lo pones
// después, las peticiones del navegador fallarán con error de CORS.
//
app.UseCors();

// ── Middleware: Controllers ───────────────────────────────────────────────────
//
// MapControllers() conecta las rutas HTTP con los métodos de los controllers.
// Internamente lee los atributos [Route], [HttpGet], [HttpPost], etc. que
// decoramos en PredictController y crea un mapa de enrutamiento.
//
app.MapControllers();

// ── Precalentamiento del servicio (Warm-up) ───────────────────────────────────
//
// Los Singletons en ASP.NET Core son LAZY por defecto: se crean la primera vez
// que alguien los pide, no al arrancar. Esto significa que la primera petición
// HTTP sería lenta porque tendría que cargar el modelo de IA.
//
// Para evitarlo, forzamos la creación del servicio ANTES de aceptar peticiones.
// Así el modelo ya está en memoria cuando llegue la primera foto.
//
// ¿Por qué un "scope"? Aunque el servicio es Singleton, el contenedor DI
// requiere un scope para resolver dependencias correctamente en esta fase.
// "using" garantiza que el scope se libera al salir del bloque (RAII en C#).
//
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<TFLiteService>();

// ── FASE 3: RUN ───────────────────────────────────────────────────────────────
//
// app.Run() bloquea el hilo principal y empieza a escuchar peticiones HTTP.
// La URL viene de --urls en la línea de comandos, de appsettings.json,
// o del launchSettings.json cuando se ejecuta con "dotnet run".
// Solo termina cuando el proceso recibe una señal de cierre (Ctrl+C, SIGTERM).
//
app.Run();
