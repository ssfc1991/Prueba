from pathlib import Path

root = Path("CartaValor_V03")
main = root / "app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt"
s = main.read_text()

# Imports needed by V14.
if "import androidx.compose.material3.AlertDialog" not in s:
    s = s.replace("import androidx.compose.material3.Button\n", "import androidx.compose.material3.AlertDialog\nimport androidx.compose.material3.Button\n", 1)
if "import androidx.core.content.FileProvider" not in s:
    s = s.replace("import androidx.core.content.ContextCompat\n", "import androidx.core.content.ContextCompat\nimport androidx.core.content.FileProvider\n", 1)

# V14 state and file picker.
state_anchor = '''    var loading by remember { mutableStateOf(false) }
    var message by remember { mutableStateOf<String?>(null) }
'''
state_repl = '''    var loading by remember { mutableStateOf(false) }
    var message by remember { mutableStateOf<String?>(null) }
    var pendingImport by remember { mutableStateOf<List<CollectionItem>?>(null) }
    var pendingImportSource by remember { mutableStateOf<String?>(null) }

    val importCollectionLauncher = rememberLauncherForActivityResult(
        ActivityResultContracts.OpenDocument()
    ) { uri ->
        if (uri != null) {
            runCatching {
                val raw = context.contentResolver.openInputStream(uri)
                    ?.bufferedReader()
                    ?.use { it.readText() }
                    ?: error("No pude abrir el archivo seleccionado.")
                CollectionTransfer.importJson(raw)
            }.onSuccess { imported ->
                pendingImport = imported.items
                pendingImportSource = imported.sourceAppVersion
            }.onFailure { error ->
                message = error.message ?: "No pude importar la colección."
            }
        }
    }
'''
if state_anchor not in s:
    raise SystemExit("No se encontró el bloque de estado principal.")
s = s.replace(state_anchor, state_repl, 1)

# Add export/import actions before openCollection.
action_anchor = '''    fun openCollection() {
'''
actions = '''    fun exportCollection() {
        if (collection.isEmpty()) {
            message = "Tu colección está vacía; no hay nada para compartir."
            return
        }
        runCatching {
            val dir = File(context.cacheDir, "shared_collections").apply { mkdirs() }
            val stamp = SimpleDateFormat("yyyyMMdd_HHmm", Locale.US).format(Date())
            val file = File(dir, "CartaValor_coleccion_$stamp.cvcollection")
            file.writeText(CollectionTransfer.exportJson(collection, "14.0"))

            val uri = FileProvider.getUriForFile(
                context,
                "${context.packageName}.fileprovider",
                file
            )
            val intent = android.content.Intent(android.content.Intent.ACTION_SEND).apply {
                type = "application/json"
                putExtra(android.content.Intent.EXTRA_STREAM, uri)
                addFlags(android.content.Intent.FLAG_GRANT_READ_URI_PERMISSION)
            }
            context.startActivity(
                android.content.Intent.createChooser(intent, "Compartir colección CartaValor")
            )
        }.onFailure { error ->
            message = error.message ?: "No pude preparar la colección para compartir."
        }
    }

    fun importCollection() {
        importCollectionLauncher.launch(
            arrayOf("application/json", "text/plain", "application/octet-stream")
        )
    }

    fun openCollection() {
'''
if action_anchor not in s:
    raise SystemExit("No se encontró openCollection.")
s = s.replace(action_anchor, actions, 1)

# Wire actions into CollectionScreen.
call_old = '''                    Screen.COLLECTION -> CollectionScreen(
                        collectionItems = collection,
                        onScan = ::resetScan,
                        onQuantity = { item, q -> collection = store.setQuantity(item.key, q) },
                        onRemove = { item -> collection = store.remove(item.key) },
                        onRefresh = ::refreshCollectionPrices
                    )
'''
call_new = '''                    Screen.COLLECTION -> CollectionScreen(
                        collectionItems = collection,
                        onScan = ::resetScan,
                        onQuantity = { item, q -> collection = store.setQuantity(item.key, q) },
                        onRemove = { item -> collection = store.remove(item.key) },
                        onRefresh = ::refreshCollectionPrices,
                        onExport = ::exportCollection,
                        onImport = ::importCollection
                    )
'''
if call_old not in s:
    raise SystemExit("No se encontró llamada CollectionScreen.")
s = s.replace(call_old, call_new, 1)

# Add import confirmation dialog before loading overlay.
dialog_anchor = '''                if (loading) {
'''
dialog = '''                pendingImport?.let { imported ->
                    AlertDialog(
                        onDismissRequest = {
                            pendingImport = null
                            pendingImportSource = null
                        },
                        title = { Text("Importar colección") },
                        text = {
                            Text(
                                buildString {
                                    append("El archivo contiene ")
                                    append(imported.sumOf { it.quantity })
                                    append(" carta")
                                    if (imported.sumOf { it.quantity } != 1) append("s")
                                    append(" en ")
                                    append(imported.size)
                                    append(" variante")
                                    if (imported.size != 1) append("s")
                                    append(".")
                                    pendingImportSource?.let { append("\nCreado con CartaValor $it.") }
                                    append("\n\nPodés combinarla con tu colección o reemplazar la actual.")
                                }
                            )
                        },
                        confirmButton = {
                            TextButton(
                                onClick = {
                                    val merged = CollectionTransfer.merge(collection, imported)
                                    store.replaceAll(merged)
                                    collection = merged
                                    pendingImport = null
                                    pendingImportSource = null
                                    message = "Colecciones combinadas sin crear filas duplicadas."
                                }
                            ) { Text("Combinar") }
                        },
                        dismissButton = {
                            Row {
                                TextButton(
                                    onClick = {
                                        store.replaceAll(imported)
                                        collection = imported
                                        pendingImport = null
                                        pendingImportSource = null
                                        message = "Colección reemplazada correctamente."
                                    }
                                ) { Text("Reemplazar") }
                                TextButton(
                                    onClick = {
                                        pendingImport = null
                                        pendingImportSource = null
                                    }
                                ) { Text("Cancelar") }
                            }
                        }
                    )
                }

                if (loading) {
'''
if dialog_anchor not in s:
    raise SystemExit("No se encontró overlay loading.")
s = s.replace(dialog_anchor, dialog, 1)

# Extend CollectionScreen signature.
sig_old = '''private fun CollectionScreen(
    collectionItems: List<CollectionItem>,
    onScan: () -> Unit,
    onQuantity: (CollectionItem, Int) -> Unit,
    onRemove: (CollectionItem) -> Unit,
    onRefresh: () -> Unit
) {
'''
sig_new = '''private fun CollectionScreen(
    collectionItems: List<CollectionItem>,
    onScan: () -> Unit,
    onQuantity: (CollectionItem, Int) -> Unit,
    onRemove: (CollectionItem) -> Unit,
    onRefresh: () -> Unit,
    onExport: () -> Unit,
    onImport: () -> Unit
) {
'''
if sig_old not in s:
    raise SystemExit("No se encontró firma CollectionScreen.")
s = s.replace(sig_old, sig_new, 1)

# Empty collection: allow import.
empty_button = '''                        Button(onClick = onScan) {
                            Icon(Icons.Default.CameraAlt, null)
                            Spacer(Modifier.width(8.dp))
                            Text("ESCANEAR MI PRIMERA CARTA")
                        }
'''
empty_repl = empty_button + '''                        Spacer(Modifier.height(8.dp))
                        OutlinedButton(onClick = onImport) {
                            Text("IMPORTAR COLECCIÓN")
                        }
'''
if empty_button not in s:
    raise SystemExit("No se encontró botón inicial de colección.")
s = s.replace(empty_button, empty_repl, 1)

# Non-empty collection: share/import controls.
buttons_old = '''                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            Button(onClick = onRefresh, modifier = Modifier.weight(1f)) {
                                Icon(Icons.Default.Refresh, null)
                                Spacer(Modifier.width(6.dp))
                                Text("Actualizar")
                            }
                            OutlinedButton(onClick = onScan, modifier = Modifier.weight(1f)) {
                                Icon(Icons.Default.CameraAlt, null)
                                Spacer(Modifier.width(6.dp))
                                Text("Escanear")
                            }
                        }
'''
buttons_new = buttons_old + '''                        Spacer(Modifier.height(8.dp))
                        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                            OutlinedButton(onClick = onExport, modifier = Modifier.weight(1f)) {
                                Text("Compartir")
                            }
                            OutlinedButton(onClick = onImport, modifier = Modifier.weight(1f)) {
                                Text("Importar")
                            }
                        }
'''
if buttons_old not in s:
    raise SystemExit("No se encontró bloque de botones de colección.")
s = s.replace(buttons_old, buttons_new, 1)

# Version labels.
s = s.replace("V13 · Pokémon · USD / UYU", "V14 · Pokémon · USD / UYU", 1)
s = s.replace('"12.0"', '"14.0"', 1)
main.write_text(s)

# Manifest: icon + FileProvider, no new runtime permission.
manifest = root / "app/src/main/AndroidManifest.xml"
m = manifest.read_text()
m = m.replace(
    'android:allowBackup="true"\n        android:label="CartaValor"',
    'android:allowBackup="true"\n        android:icon="@mipmap/ic_launcher"\n        android:roundIcon="@mipmap/ic_launcher"\n        android:label="CartaValor"',
    1
)
activity_close = '''        </activity>
    </application>
'''
provider = '''        </activity>

        <provider
            android:name="androidx.core.content.FileProvider"
            android:authorities="${applicationId}.fileprovider"
            android:exported="false"
            android:grantUriPermissions="true">
            <meta-data
                android:name="android.support.FILE_PROVIDER_PATHS"
                android:resource="@xml/file_paths" />
        </provider>
    </application>
'''
if activity_close not in m:
    raise SystemExit("No se encontró cierre de activity en manifest.")
m = m.replace(activity_close, provider, 1)
manifest.write_text(m)

# FileProvider paths.
xml_dir = root / "app/src/main/res/xml"
xml_dir.mkdir(parents=True, exist_ok=True)
(xml_dir / "file_paths.xml").write_text(
    '<?xml version="1.0" encoding="utf-8"?>\n'
    '<paths xmlns:android="http://schemas.android.com/apk/res/android">\n'
    '    <cache-path name="shared_collections" path="shared_collections/" />\n'
    '</paths>\n'
)

# Selected icon.
mipmap = root / "app/src/main/res/mipmap-nodpi"
mipmap.mkdir(parents=True, exist_ok=True)
icon_src = Path("v14patch/ic_launcher.webp")
(mipmap / "ic_launcher.webp").write_bytes(icon_src.read_bytes())

# Version bump.
gradle = root / "app/build.gradle.kts"
g = gradle.read_text()
g = g.replace("versionCode = 13", "versionCode = 14", 1)
g = g.replace('versionName = "13.0"', 'versionName = "14.0"', 1)
gradle.write_text(g)

# Minimal V14 changelog notes inside project.
readme = root / "README.md"
r = readme.read_text()
r += "\n\n## V14\n- Nuevo icono de CartaValor.\n- Exportar/compartir colección como .cvcollection.\n- Importar colección con validación.\n- Combinar sin filas duplicadas o reemplazar colección.\n- Sin permisos Android nuevos.\n"
readme.write_text(r)
