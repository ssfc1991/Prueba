from pathlib import Path

p = Path("CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt")
s = p.read_text()

start = s.index("@Composable\nprivate fun AboutScreen()")
end = s.index("@Composable\nprivate fun ScanScreen(", start)

replacement = r'''@Composable
private fun AboutScreen() {
    val context = LocalContext.current
    val email = "silviosebastian1991@gmail.com"

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(horizontal = 18.dp, vertical = 22.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp)
    ) {
        item {
            Column(
                modifier = Modifier.fillMaxWidth(),
                horizontalAlignment = Alignment.CenterHorizontally
            ) {
                Surface(
                    color = Color(0xFFFFC928),
                    shape = RoundedCornerShape(100.dp),
                    modifier = Modifier.size(92.dp)
                ) {
                    Box(contentAlignment = Alignment.Center) {
                        Text(
                            "SF",
                            color = Color(0xFF0B1220),
                            fontSize = 34.sp,
                            fontWeight = FontWeight.ExtraBold
                        )
                    }
                }
                Spacer(Modifier.height(14.dp))
                Text(
                    "Silvio Fagúndez",
                    color = Color.White,
                    fontSize = 26.sp,
                    fontWeight = FontWeight.Bold
                )
                Spacer(Modifier.height(4.dp))
                Text(
                    "Creador y desarrollador de CartaValor",
                    color = Color(0xFF9FB3CB),
                    fontSize = 15.sp
                )
            }
        }

        item {
            Surface(
                color = Color(0xFF15233C),
                shape = RoundedCornerShape(22.dp),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(Modifier.padding(20.dp)) {
                    Text(
                        "Sobre CartaValor",
                        color = Color(0xFFFFC928),
                        fontSize = 19.sp,
                        fontWeight = FontWeight.Bold
                    )
                    Spacer(Modifier.height(10.dp))
                    Text(
                        "CartaValor fue creada para facilitar la identificación de cartas Pokémon, comparar coincidencias visuales, consultar valores orientativos y organizar una colección personal desde el teléfono.",
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
                shape = RoundedCornerShape(20.dp),
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
                    Text(
                        email,
                        color = Color(0xFF7FC8FF),
                        fontSize = 15.sp,
                        fontWeight = FontWeight.Medium
                    )
                    Spacer(Modifier.height(14.dp))
                    Button(
                        onClick = {
                            val intent = Intent(
                                Intent.ACTION_SENDTO,
                                Uri.parse("mailto:$email")
                            ).apply {
                                putExtra(Intent.EXTRA_SUBJECT, "Contacto desde CartaValor")
                            }
                            runCatching { context.startActivity(intent) }
                        },
                        modifier = Modifier.fillMaxWidth()
                    ) {
                        Text("Enviar correo", fontWeight = FontWeight.Bold)
                    }
                }
            }
        }

        item {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(12.dp)
            ) {
                Surface(
                    color = Color(0xFF111827),
                    shape = RoundedCornerShape(18.dp),
                    modifier = Modifier.weight(1f)
                ) {
                    Column(Modifier.padding(16.dp)) {
                        Text(
                            "Versión",
                            color = Color(0xFF9FB3CB),
                            fontSize = 12.sp
                        )
                        Spacer(Modifier.height(4.dp))
                        Text(
                            "12.0",
                            color = Color.White,
                            fontSize = 20.sp,
                            fontWeight = FontWeight.Bold
                        )
                    }
                }

                Surface(
                    color = Color(0xFF111827),
                    shape = RoundedCornerShape(18.dp),
                    modifier = Modifier.weight(1f)
                ) {
                    Column(Modifier.padding(16.dp)) {
                        Text(
                            "Proyecto",
                            color = Color(0xFF9FB3CB),
                            fontSize = 12.sp
                        )
                        Spacer(Modifier.height(4.dp))
                        Text(
                            "Independiente",
                            color = Color.White,
                            fontSize = 16.sp,
                            fontWeight = FontWeight.Bold
                        )
                    }
                }
            }
        }

        item {
            Surface(
                color = Color(0xFF0F172A),
                shape = RoundedCornerShape(18.dp),
                modifier = Modifier.fillMaxWidth()
            ) {
                Column(Modifier.padding(17.dp)) {
                    Text(
                        "Privacidad",
                        color = Color.White,
                        fontSize = 16.sp,
                        fontWeight = FontWeight.Bold
                    )
                    Spacer(Modifier.height(7.dp))
                    Text(
                        "Las fotografías utilizadas para reconocer cartas se procesan dentro del flujo de la aplicación. CartaValor no requiere crear una cuenta para usar sus funciones principales.",
                        color = Color(0xFFB8C4D8),
                        fontSize = 13.sp,
                        lineHeight = 19.sp
                    )
                }
            }
        }

        item {
            Text(
                "CartaValor es un proyecto independiente. Pokémon y sus marcas, nombres e imágenes pertenecen a sus respectivos propietarios.",
                color = Color(0xFF7D8CA3),
                fontSize = 12.sp,
                lineHeight = 18.sp,
                modifier = Modifier.fillMaxWidth().padding(horizontal = 6.dp, vertical = 4.dp)
            )
        }
    }
}

'''

s = s[:start] + replacement + s[end:]
s = s.replace("V11 · Pokémon · USD / UYU", "V12 · Pokémon · USD / UYU", 1)
p.write_text(s)

gradle = Path("CartaValor_V03/app/build.gradle.kts")
g = gradle.read_text()
g = g.replace("versionCode = 11", "versionCode = 12", 1)
g = g.replace('versionName = "11.0"', 'versionName = "12.0"', 1)
gradle.write_text(g)
