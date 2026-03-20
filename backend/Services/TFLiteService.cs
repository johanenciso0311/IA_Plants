// ═══════════════════════════════════════════════════════════════════════════════
//  TFLiteService.cs  —  Servicio de inferencia con TensorFlow Lite
// ═══════════════════════════════════════════════════════════════════════════════
//
//  Este archivo es el núcleo del backend: toda la inteligencia artificial vive
//  aquí. Se comunica con una DLL de C (tensorflowlite_c.dll) usando P/Invoke,
//  procesa imágenes y devuelve la enfermedad detectada.
//
//  CONCEPTOS CLAVE DE C# QUE APRENDERÁS AQUÍ:
//    • P/Invoke              → llamar a código C/C++ desde C#
//    • IntPtr                → puntero de memoria nativa
//    • IDisposable + using   → liberar recursos no administrados (memoria nativa)
//    • async/await           → operaciones asíncronas sin bloquear hilos
//    • Tuplas                → devolver múltiples valores de un método
//    • Lambdas               → funciones anónimas inline
//    • LINQ                  → consultas sobre colecciones (Where, Max, etc.)
//
//  FLUJO DE DATOS:
//    Stream (imagen) → PrepararInputAsync() → float[150528] (224×224×3 píxeles)
//                   → TFLite C API         → float[15]     (probabilidades)
//                   → FormatearClase()     → string        (nombre legible)
//
// ═══════════════════════════════════════════════════════════════════════════════

using SixLabors.ImageSharp;           // Librería de procesamiento de imágenes
using SixLabors.ImageSharp.PixelFormats; // Rgb24: estructura de píxel RGB de 24 bits
using SixLabors.ImageSharp.Processing;  // Mutate, Resize
using System.Runtime.InteropServices;   // DllImport, IntPtr

namespace backend.Services;

// ═══════════════════════════════════════════════════════════════════════════════
//  CLASE: TFLiteService
// ═══════════════════════════════════════════════════════════════════════════════
//
//  Responsabilidades:
//    1. Cargar el modelo TFLite una sola vez al construirse (costoso).
//    2. Procesar imágenes y ejecutar inferencia (rápido, reutilizable).
//    3. Liberar memoria nativa cuando ya no se necesita (crítico).
//
//  ¿Por qué implementa IDisposable?
//    La DLL de TFLite reserva memoria fuera del heap administrado de .NET.
//    El Garbage Collector (GC) de C# NO puede liberar esa memoria porque
//    no sabe que existe. Si no la liberamos manualmente, hay un memory leak.
//    IDisposable es el contrato estándar de C# para decir:
//    "Este objeto tiene recursos que hay que liberar explícitamente."
//    Quien lo use puede escribir: using var svc = new TFLiteService(...);
//    y el Dispose() se llama automáticamente al salir del scope.
//
public class TFLiteService : IDisposable
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  SECCIÓN 1: P/INVOKE — PUENTE ENTRE C# Y LA DLL DE C
    // ═══════════════════════════════════════════════════════════════════════════
    //
    //  P/Invoke (Platform Invocation Services) es el mecanismo de C# para llamar
    //  a funciones de DLLs escritas en C o C++. Es como un "traductor" entre
    //  el mundo administrado de .NET y el mundo nativo de C.
    //
    //  El proceso:
    //    1. Declaras la firma de la función C en C# con [DllImport].
    //    2. .NET busca la DLL en el directorio del ejecutable.
    //    3. Al llamar al método, .NET hace el marshaling (conversión de tipos)
    //       y transfiere el control a la función C nativa.
    //
    //  ¿Por qué "static extern"?
    //    • static  → No necesita instancia de la clase para existir (son solo
    //                wrappers de funciones globales de la DLL).
    //    • extern  → La implementación está FUERA de C# (en la DLL nativa).
    //
    //  El nombre del método DEBE coincidir exactamente con la función exportada
    //  en la DLL. TFLite C API exporta nombres como "TfLiteModelCreateFromFile".
    //
    const string DLL = "tensorflowlite_c"; // Nombre sin extensión (.dll la añade Windows)

    // ── Funciones de gestión del MODELO ───────────────────────────────────────
    //
    // IntPtr es un tipo de C# que representa un puntero de memoria nativa.
    // En C, "TfLiteModel*" es un puntero a una estructura opaca. Como C#
    // no puede usar punteros tipados fuera de bloques "unsafe", usamos IntPtr
    // que simplemente guarda una dirección de memoria sin saber qué hay ahí.
    //
    // TfLiteModelCreateFromFile(path) → lee el archivo .tflite del disco y
    //   lo parsea en memoria nativa. Devuelve un puntero al modelo, o cero
    //   (IntPtr.Zero) si falla. Usada en: constructor TFLiteService().
    //
    [DllImport(DLL)] static extern IntPtr TfLiteModelCreateFromFile(string modelPath);

    // TfLiteModelDelete(model) → libera la memoria del modelo.
    //   Usada en: Dispose()
    //
    [DllImport(DLL)] static extern void TfLiteModelDelete(IntPtr model);

    // ── Funciones de OPCIONES del intérprete ──────────────────────────────────
    //
    // Las opciones permiten configurar el intérprete antes de crearlo.
    // Es el patrón Builder aplicado en C: primero creas opciones, las
    // configuras, y luego las pasas al constructor del intérprete.
    //
    // TfLiteInterpreterOptionsCreate() → crea el objeto de opciones.
    //   Usada en: constructor TFLiteService()
    //
    [DllImport(DLL)] static extern IntPtr TfLiteInterpreterOptionsCreate();

    // TfLiteInterpreterOptionsSetNumThreads(options, n) → cuántos hilos del
    //   CPU puede usar TFLite para procesar en paralelo. Más hilos = más rápido
    //   en modelos grandes, pero consume más CPU. Usada en: constructor.
    //
    [DllImport(DLL)] static extern void TfLiteInterpreterOptionsSetNumThreads(IntPtr options, int numThreads);

    // TfLiteInterpreterOptionsDelete(options) → libera las opciones.
    //   Usada en: Dispose()
    //
    [DllImport(DLL)] static extern void TfLiteInterpreterOptionsDelete(IntPtr options);

    // ── Funciones del INTÉRPRETE ───────────────────────────────────────────────
    //
    // El intérprete es el motor que ejecuta el modelo. Toma el modelo y las
    // opciones, reserva memoria para los tensores de entrada/salida, y
    // expone métodos para correr la inferencia.
    //
    // TfLiteInterpreterCreate(model, options) → crea el intérprete.
    //   Usada en: constructor TFLiteService()
    //
    [DllImport(DLL)] static extern IntPtr TfLiteInterpreterCreate(IntPtr model, IntPtr options);

    // TfLiteInterpreterDelete(interpreter) → libera el intérprete.
    //   Usada en: Dispose()
    //
    [DllImport(DLL)] static extern void TfLiteInterpreterDelete(IntPtr interpreter);

    // TfLiteInterpreterAllocateTensors(interpreter) → reserva memoria para los
    //   tensores de entrada y salida según la arquitectura del modelo.
    //   Devuelve 0 si OK, distinto de 0 si hay error.
    //   Usada en: constructor TFLiteService()
    //
    [DllImport(DLL)] static extern int TfLiteInterpreterAllocateTensors(IntPtr interpreter);

    // TfLiteInterpreterInvoke(interpreter) → EJECUTA LA INFERENCIA.
    //   Lee los datos del tensor de entrada, corre todas las capas de la red
    //   neuronal, y escribe los resultados en el tensor de salida.
    //   Devuelve 0 si OK. Usada en: PredecirAsync()
    //
    [DllImport(DLL)] static extern int TfLiteInterpreterInvoke(IntPtr interpreter);

    // ── Funciones de TENSORES ─────────────────────────────────────────────────
    //
    // Un tensor es un array multidimensional. El modelo tiene tensores de
    // entrada (donde pones los píxeles) y de salida (donde lee las probabilidades).
    //
    // TfLiteInterpreterGetInputTensor(interpreter, index) → devuelve un puntero
    //   al tensor de entrada número "index". Nuestro modelo tiene solo uno (index=0).
    //   Usada en: PredecirAsync()
    //
    [DllImport(DLL)] static extern IntPtr TfLiteInterpreterGetInputTensor(IntPtr interpreter, int inputIndex);

    // TfLiteInterpreterGetOutputTensor(interpreter, index) → devuelve un puntero
    //   al tensor de salida. Nuestro modelo tiene uno (index=0) con 15 valores.
    //   Usada en: PredecirAsync()
    //
    [DllImport(DLL)] static extern IntPtr TfLiteInterpreterGetOutputTensor(IntPtr interpreter, int outputIndex);

    // TfLiteTensorCopyFromBuffer(tensor, data, sizeInBytes) → copia datos desde
    //   un array de C# al tensor nativo. El tamaño debe ser en BYTES, no en
    //   número de elementos (de ahí el sizeof(float)).
    //   Usada en: PredecirAsync()
    //
    [DllImport(DLL)] static extern int TfLiteTensorCopyFromBuffer(IntPtr tensor, float[] inputData, int inputDataSize);

    // TfLiteTensorCopyToBuffer(tensor, data, sizeInBytes) → copia el resultado
    //   del tensor nativo de vuelta a un array de C#.
    //   Usada en: PredecirAsync()
    //
    [DllImport(DLL)] static extern int TfLiteTensorCopyToBuffer(IntPtr tensor, float[] outputData, int outputDataSize);

    // ═══════════════════════════════════════════════════════════════════════════
    //  SECCIÓN 2: ESTADO INTERNO DE LA CLASE
    // ═══════════════════════════════════════════════════════════════════════════
    //
    //  "private readonly" combina dos modificadores:
    //    • private  → solo accesible dentro de esta clase (encapsulación).
    //    • readonly → solo se puede asignar en el constructor. Garantiza que
    //                 los punteros no cambien accidentalmente durante la vida
    //                 del objeto (inmutabilidad después de la construcción).
    //
    private readonly IntPtr _model;       // Puntero al modelo cargado en memoria nativa
    private readonly IntPtr _interpreter; // Puntero al intérprete de TFLite
    private readonly IntPtr _options;     // Puntero a las opciones del intérprete
    private readonly string[] _clases;    // Nombres de las 15 clases de enfermedades
    private bool _disposed;               // Bandera para evitar liberar memoria dos veces

    // Constante para el tamaño de imagen que espera el modelo.
    // "const" en C# es un valor conocido en tiempo de compilación, incrustado
    // directamente en el código generado (más eficiente que readonly para tipos primitivos).
    const int IMG_SIZE = 224;

    // ═══════════════════════════════════════════════════════════════════════════
    //  CONSTRUCTOR
    // ═══════════════════════════════════════════════════════════════════════════
    //
    //  El constructor inicializa TODO lo necesario para la inferencia:
    //    1. Lee las clases del archivo de texto.
    //    2. Carga el modelo TFLite desde disco.
    //    3. Crea el intérprete y le asigna los tensores.
    //
    //  Si cualquier paso falla, lanza una excepción. Esto garantiza que
    //  si el constructor termina sin excepción, el objeto está 100% listo
    //  para usarse. Nunca existirá un TFLiteService "a medias".
    //
    //  QUIÉN LO LLAMA: Program.cs → builder.Services.AddSingleton<TFLiteService>
    //  UNA SOLA VEZ al arrancar la aplicación.
    //
    public TFLiteService(string modelPath, string clasesPath)
    {
        // ── Validación defensiva ───────────────────────────────────────────────
        //
        // "Fail fast": si los archivos no existen, mejor lanzar un error claro
        // AHORA que obtener un NullReferenceException críptico más adelante.
        // FileNotFoundException es más descriptiva que una excepción genérica.
        //
        if (!File.Exists(modelPath))
            throw new FileNotFoundException($"Modelo no encontrado: {modelPath}");
        if (!File.Exists(clasesPath))
            throw new FileNotFoundException($"Clases no encontradas: {clasesPath}");

        // ── Leer las clases ────────────────────────────────────────────────────
        //
        // File.ReadAllLines lee todas las líneas del archivo de texto.
        // .Where(l => !string.IsNullOrWhiteSpace(l)) filtra líneas vacías
        //   usando LINQ. La lambda "l => ..." es una función anónima donde
        //   "l" es cada línea y "=>" separa el parámetro del cuerpo.
        // .ToArray() materializa el resultado de LINQ en un array en memoria.
        //
        _clases = File.ReadAllLines(clasesPath)
                      .Where(l => !string.IsNullOrWhiteSpace(l))
                      .ToArray();

        // ── Cargar el modelo TFLite ────────────────────────────────────────────
        //
        // TfLiteModelCreateFromFile devuelve IntPtr.Zero (equivalente a NULL en C)
        // si no puede leer el archivo o si el formato es inválido.
        // Comprobamos esto explícitamente porque P/Invoke no lanza excepciones
        // automáticamente cuando una función C devuelve error.
        //
        _model = TfLiteModelCreateFromFile(modelPath);
        if (_model == IntPtr.Zero)
            throw new InvalidOperationException("No se pudo cargar el modelo TFLite.");

        // ── Configurar el intérprete ───────────────────────────────────────────
        //
        // Environment.ProcessorCount devuelve el número de núcleos lógicos del CPU.
        // Usar todos los núcleos para el modelo maximiza el rendimiento.
        // En un servidor con 8 núcleos, TFLite puede paralelizar las
        // multiplicaciones de matrices de la red neuronal.
        //
        _options = TfLiteInterpreterOptionsCreate();
        TfLiteInterpreterOptionsSetNumThreads(_options, Environment.ProcessorCount);

        _interpreter = TfLiteInterpreterCreate(_model, _options);
        if (_interpreter == IntPtr.Zero)
            throw new InvalidOperationException("No se pudo crear el intérprete TFLite.");

        // ── Asignar tensores ───────────────────────────────────────────────────
        //
        // AllocateTensors reserva la memoria para los buffers de entrada y salida
        // según la forma (shape) definida en el modelo: [1, 224, 224, 3] entrada
        // y [1, 15] salida. Devuelve 0 si OK, cualquier otro valor es error.
        //
        if (TfLiteInterpreterAllocateTensors(_interpreter) != 0)
            throw new InvalidOperationException("Error al asignar tensores.");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  MÉTODO PÚBLICO: PredecirAsync
    // ═══════════════════════════════════════════════════════════════════════════
    //
    //  Orquesta todo el proceso de inferencia: preparar datos → ejecutar modelo
    //  → interpretar resultados.
    //
    //  QUIÉN LO LLAMA: PredictController.Predecir() tras recibir una petición POST.
    //
    //  PARÁMETRO:
    //    imageStream → Stream con los bytes de la imagen (cualquier formato:
    //                  JPEG, PNG, BMP, WEBP). ImageSharp se encarga de decodificarlo.
    //
    //  RETORNA:
    //    (string Clase, double Confianza) → TUPLA con nombre de enfermedad y % de confianza.
    //    Las tuplas son una forma de devolver múltiples valores sin crear una clase.
    //    El llamador puede deconstruirlas: var (clase, confianza) = await PredecirAsync(...)
    //
    //  LLAMA A:
    //    PrepararInputAsync() → convierte la imagen a array de floats
    //    TFLite C API        → ejecuta la inferencia
    //    FormatearClase()    → convierte el nombre interno a texto legible
    //
    public async Task<(string Clase, double Confianza)> PredecirAsync(Stream imageStream)
    {
        // Convertir imagen a tensor de entrada (array de floats normalizados)
        float[] input = await PrepararInputAsync(imageStream);

        // Crear el buffer de salida con tantas posiciones como clases hay (15)
        float[] output = new float[_clases.Length];

        // Obtener referencias a los tensores de entrada y salida del intérprete
        IntPtr inputTensor  = TfLiteInterpreterGetInputTensor(_interpreter, 0);
        IntPtr outputTensor = TfLiteInterpreterGetOutputTensor(_interpreter, 0);

        // ── Tamaño en bytes ────────────────────────────────────────────────────
        //
        // La API de C trabaja con bytes, no con número de elementos.
        // sizeof(float) = 4 bytes (un float de 32 bits ocupa 4 bytes).
        // Para 150.528 floats → 150.528 × 4 = 602.112 bytes ≈ 588 KB.
        // "sizeof" en C# es seguro para tipos primitivos sin necesitar "unsafe".
        //
        int inputBytes  = input.Length  * sizeof(float);
        int outputBytes = output.Length * sizeof(float);

        // ── Copiar datos al tensor de entrada ─────────────────────────────────
        //
        // El marshaling de P/Invoke maneja automáticamente la conversión de
        // float[] de C# a float* de C, fijando el array en memoria para que
        // el GC no lo mueva mientras la DLL lo está leyendo.
        //
        if (TfLiteTensorCopyFromBuffer(inputTensor, input, inputBytes) != 0)
            throw new InvalidOperationException("Error copiando datos de entrada al tensor.");

        // ── Ejecutar la inferencia ────────────────────────────────────────────
        //
        // Este es el paso más costoso: la red neuronal procesa los 150.528 floats
        // a través de todas sus capas (convoluciones, activaciones, etc.) y
        // produce 15 números que suman ~1.0 (probabilidades softmax).
        //
        if (TfLiteInterpreterInvoke(_interpreter) != 0)
            throw new InvalidOperationException("Error durante la inferencia.");

        // ── Leer los resultados ───────────────────────────────────────────────
        //
        // Copiamos los 15 floats del tensor nativo de vuelta a nuestro array.
        // Cada float es la probabilidad de que la imagen sea de esa clase.
        // El modelo garantiza que la suma de todos es ≈ 1.0 (distribución softmax).
        //
        if (TfLiteTensorCopyToBuffer(outputTensor, output, outputBytes) != 0)
            throw new InvalidOperationException("Error copiando datos de salida del tensor.");

        // ── Encontrar la clase ganadora ───────────────────────────────────────
        //
        // output.Max() usa LINQ para encontrar el valor más alto del array.
        // Array.IndexOf busca la posición de ese valor máximo (argmax).
        // Math.Round redondea la confianza a 1 decimal (ej: 95.3%).
        //
        int maxIdx        = Array.IndexOf(output, output.Max());
        double confianza  = Math.Round(output[maxIdx] * 100, 1);
        string clase      = FormatearClase(_clases[maxIdx]);

        // Devolvemos una TUPLA con nombre y confianza.
        // El llamador decide cómo usar cada valor.
        return (clase, confianza);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  MÉTODO PRIVADO: PrepararInputAsync
    // ═══════════════════════════════════════════════════════════════════════════
    //
    //  Convierte una imagen de cualquier tamaño y formato al array de floats
    //  que espera el modelo: [224 × 224 × 3] valores entre 0.0 y 1.0.
    //
    //  ¿Por qué "private static"?
    //    • private → Solo se usa dentro de TFLiteService. No forma parte
    //                de la API pública de la clase.
    //    • static  → No accede a ningún campo de instancia (_model, _interpreter...).
    //                Marcarla static hace explícito que es una función pura:
    //                misma entrada → misma salida, sin efectos secundarios.
    //
    //  QUIÉN LO LLAMA: PredecirAsync()
    //
    private static async Task<float[]> PrepararInputAsync(Stream imageStream)
    {
        // ── Cargar y redimensionar la imagen ──────────────────────────────────
        //
        // Image.LoadAsync<Rgb24> lee el stream y decodifica el formato (JPEG, PNG...).
        // El parámetro genérico <Rgb24> le dice a ImageSharp en qué formato de
        // píxel queremos trabajar: 3 bytes por píxel (R, G, B), sin canal alpha.
        // Esto simplifica el procesamiento porque no hay que manejar transparencia.
        //
        // "using" garantiza que la imagen se libera de memoria al salir del método,
        // ya que Image<> implementa IDisposable y puede ocupar varios MB.
        //
        using Image<Rgb24> img = await Image.LoadAsync<Rgb24>(imageStream);

        // Mutate() aplica operaciones de transformación al objeto de imagen.
        // Resize(224, 224) cambia las dimensiones al tamaño exacto que espera
        // el modelo, usando interpolación bilineal por defecto.
        // La lambda "x => x.Resize(...)" es el fluent builder de ImageSharp.
        //
        img.Mutate(x => x.Resize(IMG_SIZE, IMG_SIZE));

        // ── Convertir píxeles a array de floats ───────────────────────────────
        //
        // El modelo espera los datos en formato "channel-last" (NHWC):
        //   [batch, height, width, channels] → [1, 224, 224, 3]
        // El batch siempre es 1 (procesamos una imagen a la vez).
        // Aplanamos las 3 dimensiones en un solo array 1D de 224×224×3 = 150.528 floats.
        //
        float[] data = new float[IMG_SIZE * IMG_SIZE * 3];
        int i = 0;

        // ProcessPixelRows es la forma eficiente de iterar píxeles en ImageSharp.
        // Evita copias innecesarias dando acceso directo a las filas de píxeles
        // en memoria (Span<Rgb24>), lo que es significativamente más rápido que
        // llamar a GetPixel(x, y) por cada píxel.
        //
        img.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < IMG_SIZE; y++)
            {
                // GetRowSpan devuelve un Span<Rgb24>: una vista directa en memoria
                // de la fila y-ésima, sin hacer copia. Span<T> es una estructura
                // de C# para trabajar con porciones de memoria de forma segura.
                //
                var row = accessor.GetRowSpan(y);

                for (int x = 0; x < IMG_SIZE; x++)
                {
                    // Normalizamos cada canal de 0-255 a 0.0-1.0 dividiendo por 255.
                    // La "f" al final de 255f indica que es un literal float
                    // (sin ella sería int/int = entero, perdiendo los decimales).
                    // Los modelos de visión por computadora esperan valores normalizados
                    // porque facilita el entrenamiento con descenso de gradiente.
                    //
                    data[i++] = row[x].R / 255f;
                    data[i++] = row[x].G / 255f;
                    data[i++] = row[x].B / 255f;
                }
            }
        });

        return data;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  MÉTODO PRIVADO: FormatearClase
    // ═══════════════════════════════════════════════════════════════════════════
    //
    //  Convierte el nombre interno del dataset (con guiones bajos) a texto
    //  legible para el usuario.
    //
    //  Ejemplos:
    //    "Tomato___Early_blight"             → "Tomato — Early blight"
    //    "Pepper__bell___Bacterial_spot"     → "Pepper  bell — Bacterial spot"
    //    "Potato___healthy"                  → "Potato — healthy"
    //
    //  ¿Por qué "private static"?
    //    Es una función de transformación pura (no toca estado de la clase).
    //    Marcarla static deja claro que no tiene efectos secundarios.
    //
    //  QUIÉN LO LLAMA: PredecirAsync()
    //
    //  La sintaxis "=>" (expression body method) es una forma compacta válida
    //  cuando el cuerpo del método es una sola expresión. Es equivalente a:
    //    { return raw.Replace("___", " — ").Replace("__", " ").Replace("_", " "); }
    //
    private static string FormatearClase(string raw)
        => raw.Replace("___", " — ").Replace("__", " ").Replace("_", " ");
    // IMPORTANTE: el orden de los Replace importa. Primero reemplazamos "___"
    // antes de "__" y "_", porque si empezamos por "_" también destrozaríamos "___".

    // ═══════════════════════════════════════════════════════════════════════════
    //  MÉTODO PÚBLICO: Dispose (implementación de IDisposable)
    // ═══════════════════════════════════════════════════════════════════════════
    //
    //  Libera TODA la memoria nativa reservada por la DLL de TFLite.
    //  Si no llamamos a estas funciones, la memoria permanece ocupada hasta
    //  que el proceso termine (memory leak).
    //
    //  ¿Cuándo se llama Dispose()?
    //    • Automáticamente cuando se usa: using var svc = new TFLiteService(...)
    //    • Automáticamente cuando el contenedor DI destruye el Singleton al cerrar la app.
    //    • Manualmente: svc.Dispose()
    //
    //  QUIÉN LO LLAMA: El framework ASP.NET Core al apagar el servidor.
    //
    public void Dispose()
    {
        // ── Protección contra doble liberación ────────────────────────────────
        //
        // Sin esta comprobación, si alguien llama Dispose() dos veces,
        // intentaríamos liberar memoria ya liberada → crash o comportamiento
        // indefinido en C. El flag _disposed previene esto.
        //
        if (_disposed) return;

        // Liberamos en orden INVERSO al que fueron creados para evitar
        // referencias colgantes: primero el intérprete (que usa el modelo y opciones),
        // luego las opciones, y por último el modelo.
        //
        // Comprobamos que el puntero no sea cero antes de llamar Delete,
        // porque si el constructor falló a mitad, algunos punteros pueden
        // ser IntPtr.Zero (null nativo) y llamar Delete sobre ellos crashearía.
        //
        if (_interpreter != IntPtr.Zero) TfLiteInterpreterDelete(_interpreter);
        if (_options     != IntPtr.Zero) TfLiteInterpreterOptionsDelete(_options);
        if (_model       != IntPtr.Zero) TfLiteModelDelete(_model);

        _disposed = true;

        // ── GC.SuppressFinalize ───────────────────────────────────────────────
        //
        // Le dice al Garbage Collector que NO llame al finalizador (~TFLiteService)
        // de esta instancia porque ya liberamos todo. Esto es una optimización:
        // los objetos con finalizador pendiente tienen un ciclo extra de GC.
        // En este código no tenemos finalizador, pero es buena práctica incluirlo
        // por si alguien lo añade en el futuro.
        //
        GC.SuppressFinalize(this);
    }
}
