from pathlib import Path

root = Path("CartaValor_V03")
main = root / "app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt"
s = main.read_text()

start = s.index("@Composable\nprivate fun DetailScreen(")
end = s.index("@Composable\nprivate fun PhotoComparison", start)

replacement = r'''@Composable
private fun DetailScreen(
    value: CardValuation?,
    capturedPhotoUri: String?,
    onBack: () -> Unit,
    onAdd: (CardValuation, VariantPrice, CardCondition) -> Unit
) {
    val usd = NumberFormat.getCurrencyInstance(Locale.US)
    val uyu = NumberFormat.getCurrencyInstance(Locale("es", "UY"))
    var condition by remember(value?.card?.id) { mutableStateOf(CardCondition.NEAR_MINT) }
    var selectedVariant by remember(value?.card?.id) { mutableStateOf<String?>(null) }

    LazyColumn(
        Modifier.fillMaxSize(),
        contentPadding = PaddingValues(16.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp)
    ) {
        item { TextButton(onClick = onBack) { Text("← Volver") } }

        if (value == null) {
            item { Text("Sin datos.", color = Color.White) }
        } else {
            item {
                Text("Comparación visual", color = Color.White, fontSize = 22.sp, fontWeight = FontWeight.Bold)
                if (capturedPhotoUri != null) {
                    PhotoComparison(capturedPhotoUri, value.card.imageUrl)
                } else {
                    CardImage(value.card.imageUrl, Modifier.fillMaxWidth(0.48f).aspectRatio(0.72f))
                }
            }

            item {
                Card(colors = CardDefaults.cardColors(containerColor = Color(0xFF172238))) {
                    Column(Modifier.fillMaxWidth().padding(16.dp)) {
                        Text(value.card.name, color = Color.White, fontSize = 24.sp, fontWeight = FontWeight.Bold)
                        Text(
                            value.card.setName.ifBlank { value.card.id.substringBeforeLast('-') },
                            color = Color(0xFFFFD969),
                            fontSize = 16.sp
                        )
                        Text("N.º ${value.card.number}", color = Color(0xFFB7C3D5))
                        value.card.rarity?.let { Text(it, color = Color(0xFFB7C3D5)) }
                    }
                }
            }

            item {
                ConditionSelector(condition = condition, onCondition = { condition = it })
            }

            if (value.prices.isEmpty()) {
                item {
                    Card(colors = CardDefaults.cardColors(containerColor = Color(0xFF172238))) {
                        Column(Modifier.fillMaxWidth().padding(18.dp)) {
                            Text("Sin precio disponible", color = Color(0xFFFFD969), fontWeight = FontWeight.Bold)
                            Text(
                                "TCGPlayer no publica un precio USD para esta carta en este momento. La identificación sigue siendo válida; podés volver a consultar más adelante.",
                                color = Color.White
                            )
                        }
                    }
                }
            } else {
                item {
                    Card(colors = CardDefaults.cardColors(containerColor = Color(0xFF14213A))) {
                        Column(Modifier.fillMaxWidth().padding(16.dp)) {
                            Text(
                                "Variante física de tu carta",
                                color = Color.White,
                                fontSize = 19.sp,
                                fontWeight = FontWeight.Bold
                            )
                            Spacer(Modifier.height(6.dp))
                            Text(
                                "CartaValor puede saber qué variantes existen y sus precios, pero una sola foto no confirma de forma fiable si tu copia es Holo o Reverse Holo. Por eso no seleccionamos ninguna automáticamente.",
                                color = Color(0xFFB8C4D8),
                                fontSize = 13.sp,
                                lineHeight = 19.sp
                            )
                            Spacer(Modifier.height(12.dp))

                            LazyRow(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                                item {
                                    FilterChip(
                                        selected = selectedVariant == null,
                                        onClick = { selectedVariant = null },
                                        label = { Text("No estoy seguro") }
                                    )
                                }
                                items(value.prices, key = { it.variant }) { price ->
                                    FilterChip(
                                        selected = selectedVariant == price.variant,
                                        onClick = { selectedVariant = price.variant },
                                        label = { Text(price.label) }
                                    )
                                }
                            }

                            Spacer(Modifier.height(8.dp))
                            Text(
                                if (selectedVariant == null) {
                                    "Todavía no elegiste la variante física. Los precios de abajo son solo referencias posibles."
                                } else {
                                    "Usaré únicamente la variante elegida para mostrar el precio y guardarla en tu colección."
                                },
                                color = if (selectedVariant == null) Color(0xFFFFD969) else Color(0xFF64E38B),
                                fontSize = 12.sp,
                                fontWeight = FontWeight.SemiBold
                            )
                        }
                    }
                }

                item {
                    Text(
                        if (selectedVariant == null) "Precios de variantes disponibles" else "Precio de tu variante elegida",
                        color = Color.White,
                        fontSize = 22.sp,
                        fontWeight = FontWeight.Bold
                    )
                }

                val pricesToShow = if (selectedVariant == null) {
                    value.prices
                } else {
                    value.prices.filter { it.variant == selectedVariant }
                }

                items(pricesToShow, key = { it.variant }) { p ->
                    Card(colors = CardDefaults.cardColors(containerColor = Color(0xFF172238))) {
                        Column(Modifier.fillMaxWidth().padding(18.dp)) {
                            Text(
                                if (selectedVariant == null) {
                                    "VARIANTE DISPONIBLE: ${p.label.uppercase()}"
                                } else {
                                    "TU VARIANTE: ${p.label.uppercase()}"
                                },
                                color = Color(0xFFFFD969),
                                fontWeight = FontWeight.Bold
                            )

                            if (selectedVariant == null) {
                                Text(
                                    "Disponible en la base de precios; esto NO significa que la carta de tu foto tenga este acabado.",
                                    color = Color(0xFFFFB4AB),
                                    fontSize = 12.sp
                                )
                                Spacer(Modifier.height(8.dp))
                            }

                            p.bestUsd?.let { amount ->
                                Text(
                                    usd.format(amount),
                                    color = Color(0xFF64E38B),
                                    fontSize = 30.sp,
                                    fontWeight = FontWeight.ExtraBold
                                )
                                value.uyu(amount)?.let {
                                    Text(uyu.format(it), color = Color.White, fontSize = 23.sp, fontWeight = FontWeight.Bold)
                                }
                            }

                            if (p.marketUsd != null) {
                                Text("Mercado: ${usd.format(p.marketUsd)}", color = Color(0xFFB7C3D5))
                            }
                            if (p.lowUsd != null && p.highUsd != null) {
                                Text("Rango: ${usd.format(p.lowUsd)} – ${usd.format(p.highUsd)}", color = Color(0xFFB7C3D5))
                            }
                            p.updated?.let {
                                Text("Actualización de precio: $it", color = Color(0xFF91A4BC), fontSize = 12.sp)
                            }
                            Text("Estado elegido: ${condition.label}", color = Color(0xFFB7C3D5), fontSize = 12.sp)
                            Text("Fuente: TCGPlayer vía TCGdex", color = Color(0xFF91A4BC), fontSize = 12.sp)

                            if (p.bestUsd != null && selectedVariant == p.variant) {
                                Spacer(Modifier.height(12.dp))
                                Button(
                                    onClick = { onAdd(value, p, condition) },
                                    modifier = Modifier.fillMaxWidth()
                                ) {
                                    Icon(Icons.Default.CollectionsBookmark, null)
                                    Spacer(Modifier.width(8.dp))
                                    Text("AGREGAR ESTA VARIANTE A MI COLECCIÓN")
                                }
                            } else if (selectedVariant == null) {
                                Spacer(Modifier.height(10.dp))
                                Text(
                                    "Elegí arriba qué variante tiene tu carta antes de guardarla.",
                                    color = Color(0xFF9FB3CB),
                                    fontSize = 12.sp
                                )
                            }
                        }
                    }
                }
            }

            item {
                Text(
                    "El valor real depende del estado físico, idioma, variante, edición y autenticidad. El precio mostrado es orientativo.",
                    color = Color(0xFFAAB7CA),
                    fontSize = 13.sp
                )
            }
        }
    }
}

'''

s = s[:start] + replacement + s[end:]
s = s.replace("V15 · Pokémon · USD / UYU", "V16 · Pokémon · USD / UYU", 1)
s = s.replace('"15.0"', '"16.0"')
main.write_text(s)

gradle = root / "app/build.gradle.kts"
g = gradle.read_text()
g = g.replace("versionCode = 15", "versionCode = 16", 1)
g = g.replace('versionName = "15.0"', 'versionName = "16.0"', 1)
gradle.write_text(g)

readme = root / "README.md"
r = readme.read_text()
r += """

## V16
- La app ya no asume automáticamente que la carta física es Holo, Reverse Holo o Normal.
- La variante empieza en No estoy seguro.
- Las variantes de precio se muestran como posibilidades de la base de datos, no como detección visual confirmada.
- Solo la variante elegida por el usuario puede guardarse en Mi colección.
"""
readme.write_text(r)
