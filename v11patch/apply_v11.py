from pathlib import Path

p = Path("CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt")
s = p.read_text()

# Import para abrir el correo.
if "import android.content.Intent" not in s:
    s = s.replace("import android.Manifest\n", "import android.Manifest\nimport android.content.Intent\n", 1)

# Ícono de información.
if "import androidx.compose.material.icons.filled.Info" not in s:
    anchor = "import androidx.compose.material.icons.filled.History\n"
    if anchor in s:
        s = s.replace(anchor, anchor + "import androidx.compose.material.icons.filled.Info\n", 1)
    else:
        s = s.replace("import androidx.compose.material.icons.Icons\n", "import androidx.compose.material.icons.Icons\nimport androidx.compose.material.icons.filled.Info\n", 1)

old = "private enum class Screen { SCAN, RESULTS, DETAIL, COLLECTION, HISTORY }"
new = "private enum class Screen { SCAN, RESULTS, DETAIL, COLLECTION, HISTORY, ABOUT }"
if old not in s:
    raise SystemExit("No se encontró enum Screen")
s = s.replace(old, new, 1)

anchor = '''    fun openHistory() {
        if (screen != Screen.HISTORY) previousScreen = screen
        history = historyStore.load()
        screen = Screen.HISTORY
    }

'''
addition = anchor + '''    fun openAbout() {
        if (screen != Screen.ABOUT) previousScreen = screen
        screen = Screen.ABOUT
    }

'''
if anchor not in s:
    raise SystemExit("No se encontró openHistory")
s = s.replace(anchor, addition, 1)

s = s.replace(
    "if (screen == Screen.COLLECTION || screen == Screen.HISTORY) {",
    "if (screen == Screen.COLLECTION || screen == Screen.HISTORY || screen == Screen.ABOUT) {",
    1
)

collection_button = '''                            IconButton(onClick = ::openCollection) {
                                Icon(Icons.Default.CollectionsBookmark, "Mi colección", tint = Color(0xFFFFC928))
                            }
'''
about_button = collection_button + '''                            IconButton(onClick = ::openAbout) {
                                Icon(Icons.Default.Info, "Acerca de", tint = Color(0xFF7FC8FF))
                            }
'''
if collection_button not in s:
    raise SystemExit("No se encontró botón Mi colección")
s = s.replace(collection_button, about_button, 1)

history_block_end = '''                    Screen.HISTORY -> HistoryScreen(
                        items = history,
                        onOpen = { item -> openCard(item.card, saveHistory = false, photoUri = item.capturedPhotoUri) },
                        onScan = ::resetScan,
                        onClear = {
                            historyStore.clear()
                            history = emptyList()
                            message = "Historial borrado."
                        }
                    )
'''
about_case = history_block_end + '''
                    Screen.ABOUT -> AboutScreen()
'''
if history_block_end not in s:
    raise SystemExit("No se encontró bloque HISTORY")
s = s.replace(history_block_end, about_case, 1)

# Insertar pantalla Acerca antes de ScanScreen.
marker = "@Composable\nprivate fun ScanScreen("
if marker not in s:
    raise SystemExit("No se encontró ScanScreen")

about_screen = r'''@Composable
private fun AboutScreen() {
    val context = LocalContext.current
    val email = "silviosebastian1991@gmail.com"

    LazyColumn(
        modifier = Modifier.fillMaxSize().padding(horizontal = 20.dp),
        contentPadding = PaddingValues(vertical = 24.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp)
    ) {
        item {
            Text(
                "Acerca de CartaValor",
                color = Color.White,
                fontSize = 28.sp,
                fontWeight = FontWeight.Bold
            )
            Spacer(Modifier.height(6.dp))
            Text(
                "Información del creador y del proyecto.",
                color = Color(0xFFAAB7CA),
                fontSize = 15.sp
            )
        }

        item {
            Surface(
                color = Color(0xFF15233C),
                shape = RoundedCornerShape(22.dp),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(Modifier.padding(22.dp)) {
                    Text(
                        "Silvio Fagúndez",
                        color = Color(0xFFFFC928),
                        fontSize = 24.sp,
                        fontWeight = FontWeight.Bold
                    )
                    Spacer(Modifier.height(6.dp))
                    Text(
                        "Creador y desarrollador de CartaValor",
                        color = Color.White,
                        fontSize = 16.sp,
                        fontWeight = FontWeight.SemiBold
                    )
                    Spacer(Modifier.height(14.dp))
                    Text(
                        "CartaValor nació como una herramienta práctica para identificar cartas Pokémon, comparar coincidencias visuales, organizar una colección y consultar valores orientativos.",
                        color = Color(0xFFD7E0ED),
                        fontSize = 15.sp,
                        lineHeight = 22.sp
                    )
                }
            }
        }

        item {
            Surface(
                color = Color(0xFF111827),
                shape = RoundedCornerShape(18.dp),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(Modifier.padding(18.dp)) {
                    Text(
                        "Contacto",
                        color = Color.White,
                        fontSize = 18.sp,
                        fontWeight = FontWeight.Bold
                    )
                    Spacer(Modifier.height(8.dp))
                    Text(email, color = Color(0xFF7FC8FF), fontSize = 15.sp)
                    Spacer(Modifier.height(12.dp))
                    Button(
                        onClick = {
                            val intent = Intent(
                                Intent.ACTION_SENDTO,
                                Uri.parse("mailto:$email")
                            ).apply {
                                putExtra(Intent.EXTRA_SUBJECT, "Contacto desde CartaValor")
                            }
                            runCatching { context.startActivity(intent) }
                        }
                    ) {
                        Text("Contactar por correo", fontWeight = FontWeight.Bold)
                    }
                }
            }
        }

        item {
            Surface(
                color = Color(0xFF111827),
                shape = RoundedCornerShape(18.dp),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(Modifier.padding(18.dp)) {
                    Text(
                        "CartaValor · Versión 11.0",
                        color = Color.White,
                        fontWeight = FontWeight.Bold
                    )
                    Spacer(Modifier.height(8.dp))
                    Text(
                        "Proyecto independiente. Los nombres, imágenes y marcas de Pokémon pertenecen a sus respectivos propietarios.",
                        color = Color(0xFF9FB3CB),
                        fontSize = 13.sp,
                        lineHeight = 19.sp
                    )
                }
            }
        }
    }
}

'''
s = s.replace(marker, about_screen + marker, 1)

s = s.replace("V10 · Pokémon · USD / UYU", "V11 · Pokémon · USD / UYU", 1)
p.write_text(s)

gradle = Path("CartaValor_V03/app/build.gradle.kts")
g = gradle.read_text()
g = g.replace("versionCode = 10", "versionCode = 11", 1)
g = g.replace('versionName = "10.0"', 'versionName = "11.0"', 1)
gradle.write_text(g)
