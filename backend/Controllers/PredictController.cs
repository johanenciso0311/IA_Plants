// ═══════════════════════════════════════════════════════════════════════════════
//  PredictController.cs  —  Controller HTTP para predicción de enfermedades
// ═══════════════════════════════════════════════════════════════════════════════
//
//  En ASP.NET Core, un CONTROLLER es una clase que agrupa endpoints HTTP
//  relacionados. Cada método público del controller puede mapear a una ruta
//  y un verbo HTTP (GET, POST, PUT, DELETE...).
//
//  FLUJO DE UNA PETICIÓN:
//    Cliente (Flutter) → HTTP → ASP.NET Core Pipeline → PredictController
//                                                             ↓
//                                                       TFLiteService
//                                                             ↓
//                                                    JSON response → Flutter
//
//  ¿Por qué separar Controller de Service?
//    • El controller solo sabe de HTTP: leer la petición, devolver respuestas.
//    • El service solo sabe de lógica de negocio: procesar imágenes, inferir.
//    • Si mañana cambias TFLite por otra IA, solo tocas TFLiteService,
//      no el controller. Esto es el principio de Responsabilidad Única (SRP).
//
// ═══════════════════════════════════════════════════════════════════════════════

using backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

// ── Atributo [ApiController] ──────────────────────────────────────────────────
//
// Le dice a ASP.NET Core que esta clase es un controller de API REST.
// Activa comportamientos automáticos muy útiles:
//   • Validación automática del modelo: si un parámetro es inválido, devuelve
//     400 Bad Request sin que tengas que escribir código de validación.
//   • Inferencia de fuente de parámetros: sabe si un parámetro viene del
//     body, la URL, el query string, etc., sin que tengas que indicarlo.
//   • Respuestas de error consistentes en formato ProblemDetails (RFC 7807).
//
[ApiController]

// ── Atributo [Route("api/[controller]")] ─────────────────────────────────────
//
// Define la ruta base para todos los endpoints de este controller.
// [controller] es un token especial que se reemplaza con el nombre de la clase
// SIN el sufijo "Controller". Entonces "PredictController" → "predict".
// La ruta completa base queda: /api/predict
//
// Esta convención es estándar en REST APIs: /api/{recurso}
//
[Route("api/[controller]")]
public class PredictController : ControllerBase
// ControllerBase es la clase base para controllers de API (sin vistas HTML).
// Hereda de ella nos da acceso a métodos como Ok(), BadRequest(), StatusCode()
// que crean respuestas HTTP con el código correcto de forma expresiva.
// (Si quisieras devolver vistas Razor/HTML usarías Controller en su lugar.)
{
    // ── Campos privados (dependencias) ────────────────────────────────────────
    //
    // En C# es convención nombrar los campos privados con guión bajo: _nombre.
    // Se declaran como "readonly" porque se asignan en el constructor y nunca
    // deben cambiar. Esto protege contra bugs donde accidentalmente reasignas
    // un servicio a mitad de la ejecución.
    //
    private readonly TFLiteService _tflite;
    private readonly ILogger<PredictController> _logger;

    // ── Constructor (Inyección de Dependencias) ───────────────────────────────
    //
    // ASP.NET Core crea automáticamente una instancia de PredictController
    // por cada petición HTTP. Para crearla, mira los parámetros del constructor
    // y los busca en el contenedor DI (registrado en Program.cs).
    //
    // Este patrón se llama CONSTRUCTOR INJECTION y es la forma recomendada
    // de recibir dependencias en C# porque:
    //   • Las dependencias son explícitas (se ven en la firma del constructor).
    //   • El objeto nunca existe en un estado inválido (sin sus dependencias).
    //   • Facilita el testing: en tests puedes pasar mocks de las dependencias.
    //
    // ILogger<PredictController> es el sistema de logging de .NET.
    // El tipo genérico <PredictController> le dice al logger qué categoría
    // usar en los logs (aparece como prefijo en la consola).
    //
    public PredictController(TFLiteService tflite, ILogger<PredictController> logger)
    {
        _tflite = tflite;
        _logger = logger;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  ENDPOINT: POST /api/predict
    // ═══════════════════════════════════════════════════════════════════════════
    //
    //  QUIÉN LO LLAMA: Flutter (main.dart → _analizarFoto) con multipart/form-data.
    //  QUÉ HACE: Recibe una imagen, la pasa al TFLiteService y devuelve el resultado.
    //
    //  LLAMADAS INTERNAS:
    //    Predecir() → TFLiteService.PredecirAsync()
    //
    // ── Atributo [HttpPost] ────────────────────────────────────────────────────
    //
    // Mapea este método al verbo HTTP POST en la ruta base (/api/predict).
    // POST se usa cuando envías datos al servidor para ser procesados.
    // GET sería para obtener datos sin enviar nada en el body.
    //
    [HttpPost]

    // ── Atributo [RequestSizeLimit] ────────────────────────────────────────────
    //
    // Limita el tamaño máximo del body de la petición.
    // 10 * 1024 * 1024 = 10 MB en bytes.
    // Sin este límite, alguien podría enviar archivos enormes y saturar la memoria.
    // Es una medida de seguridad básica para cualquier endpoint que reciba archivos.
    //
    [RequestSizeLimit(10 * 1024 * 1024)]

    // ── Firma del método ──────────────────────────────────────────────────────
    //
    // "async Task<IActionResult>" significa:
    //   • async     → El método puede usar "await" para operaciones asíncronas.
    //   • Task<>    → Es la versión asíncrona de un tipo de retorno (como Promise en JS).
    //   • IActionResult → Interfaz que representa cualquier respuesta HTTP:
    //                     Ok(200), BadRequest(400), NotFound(404), StatusCode(500)...
    //                     Usar la interfaz en vez de un tipo concreto da flexibilidad
    //                     para devolver distintos tipos de respuesta según el caso.
    //
    // "IFormFile imagen" → ASP.NET Core lee automáticamente el archivo del
    // multipart/form-data y lo expone como IFormFile. El nombre del parámetro
    // ("imagen") debe coincidir con el campo del formulario que envía Flutter.
    //
    public async Task<IActionResult> Predecir(IFormFile imagen)
    {
        // ── Validación de entrada ──────────────────────────────────────────────
        //
        // Siempre valida los datos que vienen del exterior antes de procesarlos.
        // "is null" es el operador de comprobación de nulidad moderno en C#
        // (preferido sobre "== null" porque no puede ser sobrecargado).
        //
        // "new { error = "..." }" crea un objeto anónimo que ASP.NET Core
        // serializa automáticamente a JSON: { "error": "Se requiere una imagen." }
        //
        if (imagen is null || imagen.Length == 0)
            return BadRequest(new { error = "Se requiere una imagen." });

        try
        {
            // ── Abrir el stream de la imagen ───────────────────────────────────
            //
            // "using var" garantiza que el stream se cierra y libera memoria
            // cuando salimos del bloque, aunque ocurra una excepción.
            // Esto es el patrón RAII de C# implementado con IDisposable.
            //
            // OpenReadStream() devuelve un Stream: una secuencia de bytes.
            // Es más eficiente que leer todo el archivo en memoria de golpe
            // con ReadAllBytes(), porque los bytes se procesan en trozos.
            //
            using var stream = imagen.OpenReadStream();

            // ── Llamar al servicio de inferencia ──────────────────────────────
            //
            // "await" cede el control al llamador mientras espera que la tarea
            // asíncrona termine. Esto libera el hilo HTTP para atender otras
            // peticiones mientras se procesa la imagen. Sin async/await, un
            // servidor con 10 peticiones simultáneas necesitaría 10 hilos;
            // con async puede manejarlas con muchos menos hilos.
            //
            // La deconstrucción de tupla "(string Clase, double Confianza)"
            // permite asignar los dos valores devueltos en una sola línea.
            // Es equivalente a: var resultado = await ...; var clase = resultado.Clase;
            //
            var (clase, confianza) = await _tflite.PredecirAsync(stream);

            // ── Logging estructurado ───────────────────────────────────────────
            //
            // LogInformation escribe en el nivel INFO (visible en consola por defecto).
            // Los {} son placeholders nombrados, NO format strings de C.
            // El logger los serializa correctamente para sistemas como Seq, ELK, etc.
            // NUNCA uses string interpolation ($"...") para logging porque pierdes
            // las propiedades estructuradas y hay riesgo de log injection.
            //
            _logger.LogInformation("Predicción: {Clase} ({Confianza}%)", clase, confianza);

            // ── Respuesta exitosa (HTTP 200) ───────────────────────────────────
            //
            // Ok() devuelve HTTP 200 con el objeto serializado como JSON.
            // "new { ... }" crea un objeto anónimo — C# infiere los tipos
            // de las propiedades automáticamente. Muy útil para respuestas
            // ad-hoc sin necesidad de crear una clase específica.
            //
            // La interpolación $"..." en el mensaje es para texto legible
            // por humanos, no para logs estructurados, así que aquí sí es apropiado.
            //
            return Ok(new
            {
                clase,              // equivalente a: clase = clase
                confianza,          // equivalente a: confianza = confianza
                mensaje = $"Se detectó: {clase} con {confianza}% de confianza."
            });
        }
        catch (Exception ex)
        {
            // ── Manejo de errores ──────────────────────────────────────────────
            //
            // Capturamos Exception (la base de todas las excepciones) para que
            // ningún error escape sin ser registrado y cause un 500 genérico.
            //
            // LogError incluye el objeto de excepción (ex) como primer argumento.
            // Esto serializa el stack trace completo en el log, invaluable para
            // depurar problemas en producción.
            //
            // StatusCode(500, ...) devuelve HTTP 500 Internal Server Error.
            // Incluimos ex.Message en la respuesta para facilitar el debugging.
            // En producción real, podrías querer ocultar ex.Message al cliente
            // y solo loguearlo internamente, por seguridad.
            //
            _logger.LogError(ex, "Error durante la predicción");
            return StatusCode(500, new { error = "Error procesando la imagen.", detalle = ex.Message });
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  ENDPOINT: GET /api/predict/health
    // ═══════════════════════════════════════════════════════════════════════════
    //
    //  QUIÉN LO LLAMA: Flutter (main.dart → _verificarBackend) al arrancar.
    //                  También el iniciar.bat para saber cuándo el backend está listo.
    //  QUÉ HACE: Confirma que el servidor está vivo y el modelo cargado.
    //
    //  El "health check" es un patrón estándar en microservicios y APIs REST.
    //  Permite que orquestadores (Docker, Kubernetes), scripts de inicio y
    //  clientes sepan si el servicio está disponible antes de enviar trabajo real.
    //
    // ── Atributo [HttpGet("health")] ──────────────────────────────────────────
    //
    // La ruta completa es /api/predict/health (ruta base + "health").
    // Al añadir un string al atributo, se agrega como subruta.
    //
    // El método es síncrono (no async) porque no hace ninguna operación
    // costosa: solo devuelve un objeto estático. No tiene sentido añadir
    // la sobrecarga de async/await para algo tan simple.
    //
    // La expresión "=>" (expression body) es una forma compacta de escribir
    // métodos de una sola línea. Es equivalente a { return Ok(...); }
    //
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok", modelo = "modelo_plantas.tflite" });
}
