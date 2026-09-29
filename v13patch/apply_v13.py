from pathlib import Path

p = Path("CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt")
s = p.read_text()

# Imports para insets y flecha de volver.
if "import androidx.compose.foundation.layout.statusBarsPadding" not in s:
    s = s.replace(
        "import androidx.compose.foundation.layout.padding\n",
        "import androidx.compose.foundation.layout.padding\nimport androidx.compose.foundation.layout.statusBarsPadding\n",
        1
    )

if "import androidx.compose.material.icons.filled.ArrowBack" not in s:
    if "import androidx.compose.material.icons.filled.CameraAlt\n" in s:
        s = s.replace(
            "import androidx.compose.material.icons.filled.CameraAlt\n",
            "import androidx.compose.material.icons.filled.ArrowBack\nimport androidx.compose.material.icons.filled.CameraAlt\n",
            1
        )
    else:
        s = s.replace(
            "import androidx.compose.material.icons.Icons\n",
            "import androidx.compose.material.icons.Icons\nimport androidx.compose.material.icons.filled.ArrowBack\n",
            1
        )

old = '''                Surface(color = Color(0xFF111827)) {
                    Row(
                        Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 8.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column {
                            Text("CartaValor", fontSize = 21.sp, fontWeight = FontWeight.Bold, color = Color.White)
                            Text("V12 · Pokémon · USD / UYU", color = Color(0xFF9FB3CB), fontSize = 11.sp)
                        }
                        Spacer(Modifier.weight(1f))
                        if (screen == Screen.COLLECTION || screen == Screen.HISTORY || screen == Screen.ABOUT) {
                            TextButton(onClick = { screen = previousScreen }) {
                                Icon(Icons.Default.CameraAlt, null)
                                Spacer(Modifier.width(4.dp))
                                Text("Volver")
                            }
                        } else {
                            IconButton(onClick = ::openHistory) {
                                Icon(Icons.Default.History, "Historial", tint = Color(0xFFB8C4D8))
                            }
                            IconButton(onClick = ::openCollection) {
                                Icon(Icons.Default.CollectionsBookmark, "Mi colección", tint = Color(0xFFFFC928))
                            }
                            IconButton(onClick = ::openAbout) {
                                Icon(Icons.Default.Info, "Acerca de", tint = Color(0xFF7FC8FF))
                            }
                        }
                    }
                }'''

new = '''                Surface(color = Color(0xFF111827)) {
                    Row(
                        Modifier
                            .fillMaxWidth()
                            .statusBarsPadding()
                            .padding(horizontal = 12.dp, vertical = 10.dp),
                        verticalAlignment = Alignment.CenterVertically
                    ) {
                        Column(Modifier.weight(1f)) {
                            Text(
                                "CartaValor",
                                fontSize = 21.sp,
                                fontWeight = FontWeight.Bold,
                                color = Color.White,
                                maxLines = 1
                            )
                            Text(
                                "V13 · Pokémon · USD / UYU",
                                color = Color(0xFF9FB3CB),
                                fontSize = 11.sp,
                                maxLines = 1
                            )
                        }

                        if (screen != Screen.SCAN) {
                            IconButton(onClick = { screen = previousScreen }) {
                                Icon(
                                    Icons.Default.ArrowBack,
                                    contentDescription = "Volver",
                                    tint = Color(0xFFB8C4D8)
                                )
                            }
                            IconButton(onClick = ::resetScan) {
                                Icon(
                                    Icons.Default.CameraAlt,
                                    contentDescription = "Escanear",
                                    tint = Color(0xFFFFC928)
                                )
                            }
                        } else {
                            IconButton(onClick = ::openHistory) {
                                Icon(Icons.Default.History, "Historial", tint = Color(0xFFB8C4D8))
                            }
                            IconButton(onClick = ::openCollection) {
                                Icon(Icons.Default.CollectionsBookmark, "Mi colección", tint = Color(0xFFFFC928))
                            }
                            IconButton(onClick = ::openAbout) {
                                Icon(Icons.Default.Info, "Acerca de", tint = Color(0xFF7FC8FF))
                            }
                        }
                    }
                }'''

if old not in s:
    raise SystemExit("No se encontró la cabecera V12 esperada")

s = s.replace(old, new, 1)
p.write_text(s)

gradle = Path("CartaValor_V03/app/build.gradle.kts")
g = gradle.read_text()
g = g.replace("versionCode = 12", "versionCode = 13", 1)
g = g.replace('versionName = "12.0"', 'versionName = "13.0"', 1)
gradle.write_text(g)
