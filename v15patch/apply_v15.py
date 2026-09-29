from pathlib import Path

root = Path("CartaValor_V03")
main = root / "app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt"
s = main.read_text()

# V15: exportación real con selector de archivo (Storage Access Framework).
state_anchor = '''    var pendingImport by remember { mutableStateOf<List<CollectionItem>?>(null) }
    var pendingImportSource by remember { mutableStateOf<String?>(null) }

    val importCollectionLauncher = rememberLauncherForActivityResult(
'''
state_repl = '''    var pendingImport by remember { mutableStateOf<List<CollectionItem>?>(null) }
    var pendingImportSource by remember { mutableStateOf<String?>(null) }
    var pendingExportJson by remember { mutableStateOf<String?>(null) }

    val exportCollectionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.CreateDocument("application/json")
    ) { uri ->
        val payload = pendingExportJson
        pendingExportJson = null
        if (uri != null && payload != null) {
            runCatching {
                context.contentResolver.openOutputStream(uri)
                    ?.bufferedWriter()
                    ?.use { it.write(payload) }
                    ?: error("No pude crear el archivo de exportación.")
            }.onSuccess {
                message = "Colección exportada correctamente."
            }.onFailure { error ->
                message = error.message ?: "No pude exportar la colección."
            }
        }
    }

    val importCollectionLauncher = rememberLauncherForActivityResult(
'''
if state_anchor not in s:
    raise SystemExit("No se encontró bloque de importación V14.")
s = s.replace(state_anchor, state_repl, 1)

# Reemplazar acción de compartir por exportación real.
start = s.index('''    fun exportCollection() {''')
end = s.index('''    fun importCollection() {''', start)
export_fn = '''    fun exportCollection() {
        if (collection.isEmpty()) {
            message = "Tu colección está vacía; no hay nada para exportar."
            return
        }

        val stamp = SimpleDateFormat("yyyyMMdd_HHmm", Locale.US).format(Date())
        pendingExportJson = CollectionTransfer.exportJson(collection, "15.0")
        exportCollectionLauncher.launch("CartaValor_coleccion_$stamp.cvcollection")
    }

'''
s = s[:start] + export_fn + s[end:]

# Texto de botones visible y explícito.
s = s.replace('Text("Compartir")', 'Text("Exportar colección")', 1)
s = s.replace('Text("Importar")', 'Text("Importar colección")', 1)
s = s.replace('Text("IMPORTAR COLECCIÓN")', 'Text("IMPORTAR COLECCIÓN")', 1)

# Versión visible.
s = s.replace("V14 · Pokémon · USD / UYU", "V15 · Pokémon · USD / UYU", 1)
s = s.replace('"14.0"', '"15.0"', 1)
main.write_text(s)

# Manifest: roundIcon separado.
manifest = root / "app/src/main/AndroidManifest.xml"
m = manifest.read_text()
m = m.replace(
    'android:roundIcon="@mipmap/ic_launcher"',
    'android:roundIcon="@mipmap/ic_launcher_round"',
    1
)
manifest.write_text(m)

# Icono legacy: conservar imagen elegida exacta.
legacy_dir = root / "app/src/main/res/mipmap-nodpi"
legacy_dir.mkdir(parents=True, exist_ok=True)
icon = Path("v14patch/ic_launcher.webp").read_bytes()
(legacy_dir / "ic_launcher.webp").write_bytes(icon)
(legacy_dir / "ic_launcher_round.webp").write_bytes(icon)

# Adaptive icon (Android 8+).
drawable_dir = root / "app/src/main/res/drawable-nodpi"
drawable_dir.mkdir(parents=True, exist_ok=True)
(drawable_dir / "ic_launcher_foreground.webp").write_bytes(icon)

values_dir = root / "app/src/main/res/values"
colors = values_dir / "colors.xml"
colors.write_text('''<?xml version="1.0" encoding="utf-8"?>
<resources>
    <color name="ic_launcher_background">#071A38</color>
</resources>
''')

adaptive_dir = root / "app/src/main/res/mipmap-anydpi-v26"
adaptive_dir.mkdir(parents=True, exist_ok=True)
adaptive_xml = '''<?xml version="1.0" encoding="utf-8"?>
<adaptive-icon xmlns:android="http://schemas.android.com/apk/res/android">
    <background android:drawable="@color/ic_launcher_background" />
    <foreground android:drawable="@drawable/ic_launcher_foreground" />
</adaptive-icon>
'''
(adaptive_dir / "ic_launcher.xml").write_text(adaptive_xml)
(adaptive_dir / "ic_launcher_round.xml").write_text(adaptive_xml)

# Version Android.
gradle = root / "app/build.gradle.kts"
g = gradle.read_text()
g = g.replace("versionCode = 14", "versionCode = 15", 1)
g = g.replace('versionName = "14.0"', 'versionName = "15.0"', 1)
gradle.write_text(g)

# README.
readme = root / "README.md"
r = readme.read_text()
r += """
## V15
- Icono launcher corregido con adaptive icon para Android 8+.
- Icono redondo configurado.
- Botón visible Exportar colección.
- Exportación .cvcollection con selector de ubicación.
- Importación .cvcollection con Combinar/Reemplazar.
"""
readme.write_text(r)
