import 'package:flutter/material.dart';
import 'package:image_picker/image_picker.dart';
import 'package:tflite_flutter/tflite_flutter.dart';
import 'package:image/image.dart' as img;
import 'package:flutter/services.dart';
import 'dart:io';

void main() => runApp(const RobotJardineroApp());

class RobotJardineroApp extends StatelessWidget {
  const RobotJardineroApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Robot Jardinero',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(seedColor: Colors.green),
        useMaterial3: true,
      ),
      home: const HomePage(),
    );
  }
}

class HomePage extends StatefulWidget {
  const HomePage({super.key});

  @override
  State<HomePage> createState() => _HomePageState();
}

class _HomePageState extends State<HomePage> {
  File? _imagen;
  String _resultado = '';
  String _confianza = '';
  bool _cargando = false;
  bool _modeloListo = false;
  Interpreter? _interpreter;
  List<String> _clases = [];

  @override
  void initState() {
    super.initState();
    _cargarModelo();
  }

  Future<void> _cargarModelo() async {
    try {
      _interpreter = await Interpreter.fromAsset(
        'assets/modelo_plantas.tflite',
      );
      final txt = await rootBundle.loadString('assets/clases.txt');
      _clases = txt.trim().split('\n');
      setState(() => _modeloListo = true);
      print('✓ Modelo cargado — ${_clases.length} clases');
    } catch (e) {
      print('❌ Error cargando modelo: $e');
    }
  }

  Future<void> _seleccionarFoto(ImageSource fuente) async {
    final picker = ImagePicker();
    final foto = await picker.pickImage(source: fuente);
    if (foto == null) return;

    setState(() {
      _imagen = File(foto.path);
      _cargando = true;
      _resultado = '';
      _confianza = '';
    });

    await _analizarFoto(File(foto.path));
  }

  Future<void> _analizarFoto(File archivo) async {
    if (!_modeloListo || _interpreter == null) {
      setState(() {
        _resultado = 'Modelo aún cargando, espera un momento';
        _cargando = false;
      });
      return;
    }

    final bytes = await archivo.readAsBytes();
    final original = img.decodeImage(bytes)!;
    final redimensionada = img.copyResize(
      original,
      width: 224,
      height: 224,
    );

    final input = List.generate(
      1,
      (_) => List.generate(
        224,
        (y) => List.generate(224, (x) {
          final pixel = redimensionada.getPixel(x, y);
          return [
            pixel.r / 255.0,
            pixel.g / 255.0,
            pixel.b / 255.0,
          ];
        }),
      ),
    );

    final output = List.filled(
      _clases.length,
      0.0,
    ).reshape([1, _clases.length]);

    _interpreter!.run(input, output);

    final probabilidades = List<double>.from(output[0]);
    double maxProb = 0;
    int maxIdx = 0;
    for (int i = 0; i < probabilidades.length; i++) {
      if (probabilidades[i] > maxProb) {
        maxProb = probabilidades[i];
        maxIdx = i;
      }
    }

    setState(() {
      _resultado = _clases[maxIdx]
          .replaceAll('___', ' — ')
          .replaceAll('_', ' ');
      _confianza = '${(maxProb * 100).toStringAsFixed(1)}%';
      _cargando = false;
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFF0d1f14),
      appBar: AppBar(
        backgroundColor: const Color(0xFF112019),
        title: const Text(
          '🌱 Robot Jardinero',
          style: TextStyle(
            color: Colors.white,
            fontWeight: FontWeight.bold,
          ),
        ),
      ),
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(24),
        child: Column(
          children: [
            // Imagen seleccionada
            Container(
              width: double.infinity,
              height: 280,
              decoration: BoxDecoration(
                color: const Color(0xFF162a1e),
                borderRadius: BorderRadius.circular(16),
                border: Border.all(color: const Color(0xFF1e4030)),
              ),
              child: _imagen == null
                  ? const Center(
                      child: Text(
                        '📷 Selecciona una foto de planta',
                        style: TextStyle(color: Colors.white54),
                      ),
                    )
                  : ClipRRect(
                      borderRadius: BorderRadius.circular(16),
                      child: Image.file(_imagen!, fit: BoxFit.cover),
                    ),
            ),

            const SizedBox(height: 20),

            // Botones
            Row(
              children: [
                Expanded(
                  child: ElevatedButton.icon(
                    onPressed: _modeloListo
                        ? () => _seleccionarFoto(ImageSource.camera)
                        : null,
                    icon: const Icon(Icons.camera_alt),
                    label: const Text('Cámara'),
                    style: ElevatedButton.styleFrom(
                      backgroundColor: const Color(0xFF2ECC71),
                      foregroundColor: Colors.black,
                      padding: const EdgeInsets.symmetric(vertical: 14),
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(12),
                      ),
                    ),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: ElevatedButton.icon(
                    onPressed: _modeloListo
                        ? () => _seleccionarFoto(ImageSource.gallery)
                        : null,
                    icon: const Icon(Icons.photo_library),
                    label: const Text('Galería'),
                    style: ElevatedButton.styleFrom(
                      backgroundColor: const Color(0xFF1e4030),
                      foregroundColor: Colors.white,
                      padding: const EdgeInsets.symmetric(vertical: 14),
                      shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(12),
                      ),
                    ),
                  ),
                ),
              ],
            ),

            const SizedBox(height: 24),

            // Estado del modelo
            if (!_modeloListo)
              const Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  SizedBox(
                    width: 16,
                    height: 16,
                    child: CircularProgressIndicator(
                      strokeWidth: 2,
                      color: Color(0xFF2ECC71),
                    ),
                  ),
                  SizedBox(width: 10),
                  Text(
                    'Cargando modelo...',
                    style: TextStyle(color: Colors.white54, fontSize: 13),
                  ),
                ],
              ),

            // Resultado
            if (_cargando)
              const CircularProgressIndicator(color: Color(0xFF2ECC71))
            else if (_resultado.isNotEmpty)
              Container(
                width: double.infinity,
                padding: const EdgeInsets.all(24),
                decoration: BoxDecoration(
                  color: const Color(0xFF162a1e),
                  borderRadius: BorderRadius.circular(16),
                  border: Border.all(
                    color: const Color(0xFF2ECC71).withOpacity(0.4),
                  ),
                ),
                child: Column(
                  children: [
                    const Text(
                      '🔬 RESULTADO DEL ANÁLISIS',
                      style: TextStyle(
                        color: Colors.white54,
                        fontSize: 11,
                        letterSpacing: 2,
                      ),
                    ),
                    const SizedBox(height: 12),
                    Text(
                      _resultado,
                      textAlign: TextAlign.center,
                      style: const TextStyle(
                        color: Colors.white,
                        fontSize: 20,
                        fontWeight: FontWeight.bold,
                      ),
                    ),
                    const SizedBox(height: 8),
                    Text(
                      _confianza,
                      style: const TextStyle(
                        color: Color(0xFF2ECC71),
                        fontSize: 36,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                    const Text(
                      'de confianza',
                      style: TextStyle(color: Colors.white38, fontSize: 12),
                    ),
                  ],
                ),
              ),
          ],
        ),
      ),
    );
  }
}