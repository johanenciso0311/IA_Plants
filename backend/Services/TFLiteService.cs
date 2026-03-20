using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Runtime.InteropServices;

namespace backend.Services;

/// <summary>
/// Servicio de inferencia usando TensorFlow Lite via P/Invoke.
/// Carga el modelo .tflite y realiza predicciones sobre imágenes 224x224 RGB.
/// </summary>
public class TFLiteService : IDisposable
{
    // ── P/Invoke: TFLite C API ────────────────────────────────────────────────
    const string DLL = "tensorflowlite_c";

    [DllImport(DLL)] static extern IntPtr TfLiteModelCreateFromFile(string modelPath);
    [DllImport(DLL)] static extern void   TfLiteModelDelete(IntPtr model);

    [DllImport(DLL)] static extern IntPtr TfLiteInterpreterOptionsCreate();
    [DllImport(DLL)] static extern void   TfLiteInterpreterOptionsSetNumThreads(IntPtr options, int numThreads);
    [DllImport(DLL)] static extern void   TfLiteInterpreterOptionsDelete(IntPtr options);

    [DllImport(DLL)] static extern IntPtr TfLiteInterpreterCreate(IntPtr model, IntPtr options);
    [DllImport(DLL)] static extern void   TfLiteInterpreterDelete(IntPtr interpreter);
    [DllImport(DLL)] static extern int    TfLiteInterpreterAllocateTensors(IntPtr interpreter);
    [DllImport(DLL)] static extern int    TfLiteInterpreterInvoke(IntPtr interpreter);

    [DllImport(DLL)] static extern IntPtr TfLiteInterpreterGetInputTensor(IntPtr interpreter, int inputIndex);
    [DllImport(DLL)] static extern IntPtr TfLiteInterpreterGetOutputTensor(IntPtr interpreter, int outputIndex);
    [DllImport(DLL)] static extern int    TfLiteTensorCopyFromBuffer(IntPtr tensor, float[] inputData, int inputDataSize);
    [DllImport(DLL)] static extern int    TfLiteTensorCopyToBuffer(IntPtr tensor, float[] outputData, int outputDataSize);

    // ── Estado interno ────────────────────────────────────────────────────────
    private readonly IntPtr _model;
    private readonly IntPtr _interpreter;
    private readonly IntPtr _options;
    private readonly string[] _clases;
    private bool _disposed;

    const int IMG_SIZE = 224;

    public TFLiteService(string modelPath, string clasesPath)
    {
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Modelo no encontrado: {modelPath}");
        if (!File.Exists(clasesPath))
            throw new FileNotFoundException($"Clases no encontradas: {clasesPath}");

        _clases = File.ReadAllLines(clasesPath)
                      .Where(l => !string.IsNullOrWhiteSpace(l))
                      .ToArray();

        _model = TfLiteModelCreateFromFile(modelPath);
        if (_model == IntPtr.Zero)
            throw new InvalidOperationException("No se pudo cargar el modelo TFLite.");

        _options = TfLiteInterpreterOptionsCreate();
        TfLiteInterpreterOptionsSetNumThreads(_options, Environment.ProcessorCount);

        _interpreter = TfLiteInterpreterCreate(_model, _options);
        if (_interpreter == IntPtr.Zero)
            throw new InvalidOperationException("No se pudo crear el intérprete TFLite.");

        if (TfLiteInterpreterAllocateTensors(_interpreter) != 0)
            throw new InvalidOperationException("Error al asignar tensores.");
    }

    /// <summary>
    /// Ejecuta inferencia sobre un stream de imagen.
    /// </summary>
    /// <param name="imageStream">Stream con la imagen (cualquier formato soportado por ImageSharp).</param>
    /// <returns>Clase predicha y nivel de confianza (0-100).</returns>
    public async Task<(string Clase, double Confianza)> PredecirAsync(Stream imageStream)
    {
        float[] input = await PrepararInputAsync(imageStream);
        float[] output = new float[_clases.Length];

        IntPtr inputTensor  = TfLiteInterpreterGetInputTensor(_interpreter, 0);
        IntPtr outputTensor = TfLiteInterpreterGetOutputTensor(_interpreter, 0);

        // Reason: el tamaño del buffer debe ser bytes = floats × sizeof(float)
        int inputBytes  = input.Length  * sizeof(float);
        int outputBytes = output.Length * sizeof(float);

        if (TfLiteTensorCopyFromBuffer(inputTensor, input, inputBytes) != 0)
            throw new InvalidOperationException("Error copiando datos de entrada al tensor.");

        if (TfLiteInterpreterInvoke(_interpreter) != 0)
            throw new InvalidOperationException("Error durante la inferencia.");

        if (TfLiteTensorCopyToBuffer(outputTensor, output, outputBytes) != 0)
            throw new InvalidOperationException("Error copiando datos de salida del tensor.");

        int maxIdx = Array.IndexOf(output, output.Max());
        double confianza = Math.Round(output[maxIdx] * 100, 1);
        string clase = FormatearClase(_clases[maxIdx]);

        return (clase, confianza);
    }

    /// <summary>
    /// Redimensiona la imagen a 224×224, normaliza a [0,1] y devuelve array float para el tensor.
    /// </summary>
    private static async Task<float[]> PrepararInputAsync(Stream imageStream)
    {
        using Image<Rgb24> img = await Image.LoadAsync<Rgb24>(imageStream);
        img.Mutate(x => x.Resize(IMG_SIZE, IMG_SIZE));

        float[] data = new float[IMG_SIZE * IMG_SIZE * 3];
        int i = 0;
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < IMG_SIZE; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < IMG_SIZE; x++)
                {
                    data[i++] = row[x].R / 255f;
                    data[i++] = row[x].G / 255f;
                    data[i++] = row[x].B / 255f;
                }
            }
        });

        return data;
    }

    /// <summary>
    /// Convierte "Tomato___Early_blight" → "Tomato — Early blight".
    /// </summary>
    private static string FormatearClase(string raw)
        => raw.Replace("___", " — ").Replace("__", " ").Replace("_", " ");

    public void Dispose()
    {
        if (_disposed) return;
        if (_interpreter != IntPtr.Zero) TfLiteInterpreterDelete(_interpreter);
        if (_options     != IntPtr.Zero) TfLiteInterpreterOptionsDelete(_options);
        if (_model       != IntPtr.Zero) TfLiteModelDelete(_model);
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
