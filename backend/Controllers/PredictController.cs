using backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PredictController : ControllerBase
{
    private readonly TFLiteService _tflite;
    private readonly ILogger<PredictController> _logger;

    public PredictController(TFLiteService tflite, ILogger<PredictController> logger)
    {
        _tflite = tflite;
        _logger = logger;
    }

    /// <summary>
    /// POST /api/predict
    /// Recibe una imagen (multipart/form-data) y devuelve la enfermedad detectada.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB máx
    public async Task<IActionResult> Predecir(IFormFile imagen)
    {
        if (imagen is null || imagen.Length == 0)
            return BadRequest(new { error = "Se requiere una imagen." });

        try
        {
            using var stream = imagen.OpenReadStream();
            var (clase, confianza) = await _tflite.PredecirAsync(stream);

            _logger.LogInformation("Predicción: {Clase} ({Confianza}%)", clase, confianza);

            return Ok(new
            {
                clase,
                confianza,
                mensaje = $"Se detectó: {clase} con {confianza}% de confianza."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error durante la predicción");
            return StatusCode(500, new { error = "Error procesando la imagen.", detalle = ex.Message });
        }
    }

    /// <summary>
    /// GET /api/predict/health
    /// Verifica que el servicio y el modelo están listos.
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health() => Ok(new { status = "ok", modelo = "modelo_plantas.tflite" });
}
