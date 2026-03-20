import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:http/http.dart' as http;
import 'package:image_picker/image_picker.dart';

// URL base del backend ASP.NET Core
const String _backendUrl = 'http://localhost:5000/api/predict';

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
  bool _backendListo = false;
  String _errorConexion = '';

  @override
  void initState() {
    super.initState();
    _verificarBackend();
  }

  /// Comprueba que el backend esté disponible antes de habilitar los botones.
  Future<void> _verificarBackend() async {
    try {
      final res = await http
          .get(Uri.parse('http://localhost:5000/api/predict/health'))
          .timeout(const Duration(seconds: 5));
      if (res.statusCode == 200) {
        setState(() => _backendListo = true);
      } else {
        setState(() => _errorConexion = 'Backend respondió con error ${res.statusCode}');
      }
    } catch (_) {
      setState(() => _errorConexion = 'No se pudo conectar al backend.\nAsegúrate de que esté corriendo.');
    }
  }

  Future<void> _seleccionarFoto(ImageSource fuente) async {
    final picker = ImagePicker();
    final foto = await picker.pickImage(source: fuente, imageQuality: 85);
    if (foto == null) return;

    setState(() {
      _imagen = File(foto.path);
      _cargando = true;
      _resultado = '';
      _confianza = '';
    });

    await _analizarFoto(File(foto.path));
  }

  /// Envía la imagen al backend y muestra el resultado.
  Future<void> _analizarFoto(File archivo) async {
    try {
      final request = http.MultipartRequest('POST', Uri.parse(_backendUrl));
      request.files.add(await http.MultipartFile.fromPath('imagen', archivo.path));

      final streamed = await request.send().timeout(const Duration(seconds: 30));
      final res = await http.Response.fromStream(streamed);

      if (res.statusCode == 200) {
        final json = jsonDecode(res.body) as Map<String, dynamic>;
        setState(() {
          _resultado = json['clase'] as String;
          _confianza = '${json['confianza']}%';
        });
      } else {
        final json = jsonDecode(res.body) as Map<String, dynamic>;
        setState(() => _resultado = 'Error: ${json['error']}');
      }
    } catch (e) {
      setState(() => _resultado = 'Error de conexión: $e');
    } finally {
      setState(() => _cargando = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: const Color(0xFF0d1f14),
      appBar: AppBar(
        backgroundColor: const Color(0xFF112019),
        title: const Text(
          '🌱 Robot Jardinero',
          style: TextStyle(color: Colors.white, fontWeight: FontWeight.bold),
        ),
      ),
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(24),
        child: Column(
          children: [
            // ── Previsualización de imagen ──────────────────────────────────
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

            // ── Botones cámara / galería ────────────────────────────────────
            Row(
              children: [
                Expanded(
                  child: ElevatedButton.icon(
                    onPressed: _backendListo
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
                    onPressed: _backendListo
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

            // ── Estado del backend ──────────────────────────────────────────
            if (!_backendListo)
              _errorConexion.isEmpty
                  ? const Row(
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
                          'Conectando con el backend...',
                          style: TextStyle(color: Colors.white54, fontSize: 13),
                        ),
                      ],
                    )
                  : Container(
                      padding: const EdgeInsets.all(16),
                      decoration: BoxDecoration(
                        color: const Color(0xFF2a1010),
                        borderRadius: BorderRadius.circular(12),
                        border: Border.all(color: Colors.redAccent.withOpacity(0.4)),
                      ),
                      child: Column(
                        children: [
                          Text(
                            _errorConexion,
                            textAlign: TextAlign.center,
                            style: const TextStyle(color: Colors.redAccent, fontSize: 13),
                          ),
                          const SizedBox(height: 10),
                          ElevatedButton(
                            onPressed: () {
                              setState(() {
                                _backendListo = false;
                                _errorConexion = '';
                              });
                              _verificarBackend();
                            },
                            style: ElevatedButton.styleFrom(
                              backgroundColor: const Color(0xFF2ECC71),
                              foregroundColor: Colors.black,
                            ),
                            child: const Text('Reintentar conexión'),
                          ),
                        ],
                      ),
                    ),

            // ── Resultado ──────────────────────────────────────────────────
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
