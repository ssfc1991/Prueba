from pathlib import Path

root = Path("CartaValor_V03")
models = root / "app/src/main/java/uy/impulsa/cartavalor/Models.kt"
repo = root / "app/src/main/java/uy/impulsa/cartavalor/CardRepository.kt"
main = root / "app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt"
gradle = root / "app/build.gradle.kts"
readme = root / "README.md"

# 1) Marcar la fuente de cada precio sin romper constructores existentes.
s = models.read_text()
old = '''data class VariantPrice(\n    val variant: String,\n    val label: String,\n    val marketUsd: Double?,\n    val lowUsd: Double?,\n    val midUsd: Double?,\n    val highUsd: Double?,\n    val updated: String?\n) {'''
new = '''data class VariantPrice(\n    val variant: String,\n    val label: String,\n    val marketUsd: Double?,\n    val lowUsd: Double?,\n    val midUsd: Double?,\n    val highUsd: Double?,\n    val updated: String?,\n    val sourceLabel: String = "TCGPlayer vía TCGdex"\n) {'''
if old not in s:
    raise SystemExit("No se encontró VariantPrice")
s = s.replace(old, new, 1)
models.write_text(s)

# 2) Segundo intento de precio con Pokémon TCG API cuando TCGdex no trae pricing.
s = repo.read_text()
old = '''    suspend fun valuation(candidate: CardCandidate): CardValuation = withContext(Dispatchers.IO) {\n        val full = fetchCard(candidate.id, "es") ?: fetchCard(candidate.id, "en")\n            ?: throw IOException("No pude cargar los datos completos de esta carta.")\n        val card = parseFullCandidate(full)\n        val prices = parseTcgPlayerPrices(full.optJSONObject("pricing")?.optJSONObject("tcgplayer"))\n        val uyuRate = fetchRate("usd", "uyu")\n        CardValuation(card, prices, uyuRate)\n    }'''
new = '''    suspend fun valuation(candidate: CardCandidate): CardValuation = withContext(Dispatchers.IO) {\n        val full = fetchCard(candidate.id, "es") ?: fetchCard(candidate.id, "en")\n            ?: throw IOException("No pude cargar los datos completos de esta carta.")\n        val card = parseFullCandidate(full)\n        val primaryPrices = parseTcgPlayerPrices(full.optJSONObject("pricing")?.optJSONObject("tcgplayer"))\n        val prices = if (primaryPrices.isNotEmpty()) {\n            primaryPrices\n        } else {\n            fetchPokemonTcgFallbackPrices(card)\n        }\n        val uyuRate = fetchRate("usd", "uyu")\n        CardValuation(card, prices, uyuRate)\n    }'''
if old not in s:
    raise SystemExit("No se encontró valuation")
s = s.replace(old, new, 1)

anchor = '''    private fun fetchRate(base: String, quote: String): Double? {'''
helper = r'''    private fun fetchPokemonTcgFallbackPrices(card: CardCandidate): List<VariantPrice> {
        val byId = runCatching { fetchPokemonTcgCard(card.id) }.getOrNull()
        if (byId != null) {
            val prices = parsePokemonTcgPrices(byId)
            if (prices.isNotEmpty()) return prices
        }

        val safeName = card.name.replace("\\", "\\\\").replace("\"", "\\\"")
        val safeNumber = card.number.replace("\\", "\\\\").replace("\"", "\\\"")
        val query = buildString {
            append("name:\"")
            append(safeName)
            append("\"")
            if (safeNumber.isNotBlank()) {
                append(" number:\"")
                append(safeNumber)
                append("\"")
            }
        }

        val url = "https://api.pokemontcg.io/v2/cards".toHttpUrl().newBuilder()
            .addQueryParameter("q", query)
            .addQueryParameter("pageSize", "20")
            .addQueryParameter("select", "id,name,number,set,tcgplayer")
            .build()

        val request = Request.Builder().url(url).build()
        return runCatching {
            client.newCall(request).execute().use { response ->
                if (!response.isSuccessful) return@use emptyList()
                val root = JSONObject(response.body?.string().orEmpty())
                val data = root.optJSONArray("data") ?: return@use emptyList()
                val best = (0 until data.length())
                    .mapNotNull { data.optJSONObject(it) }
                    .maxByOrNull { candidate -> fallbackMatchScore(candidate, card) }
                    ?: return@use emptyList()
                if (fallbackMatchScore(best, card) < 120) return@use emptyList()
                parsePokemonTcgPrices(best)
            }
        }.getOrDefault(emptyList())
    }

    private fun fetchPokemonTcgCard(id: String): JSONObject? {
        if (id.isBlank()) return null
        val request = Request.Builder()
            .url("https://api.pokemontcg.io/v2/cards/${id}")
            .build()
        return client.newCall(request).execute().use { response ->
            if (response.code == 404) return@use null
            if (!response.isSuccessful) return@use null
            JSONObject(response.body?.string().orEmpty()).optJSONObject("data")
        }
    }

    private fun fallbackMatchScore(candidate: JSONObject, card: CardCandidate): Int {
        var score = 0
        val candidateName = candidate.optString("name")
        val candidateNumber = candidate.optString("number")
        val candidateSet = candidate.optJSONObject("set")?.optString("name").orEmpty()

        if (candidateNumber.equals(card.number, ignoreCase = true)) score += 100
        if (normalize(candidateName) == normalize(card.name)) score += 80
        else score += similarity(candidateName, card.name) / 2
        if (candidateSet.isNotBlank() && card.setName.isNotBlank()) {
            score += similarity(candidateSet, card.setName) / 5
        }
        return score
    }

    private fun parsePokemonTcgPrices(card: JSONObject): List<VariantPrice> {
        val tcgplayer = card.optJSONObject("tcgplayer") ?: return emptyList()
        val prices = tcgplayer.optJSONObject("prices") ?: return emptyList()
        val updated = tcgplayer.optString("updatedAt").takeIf { it.isNotBlank() }
        val result = mutableListOf<VariantPrice>()
        val keys = prices.keys()
        while (keys.hasNext()) {
            val key = keys.next()
            val p = prices.optJSONObject(key) ?: continue
            val market = numberOrNull(p, "market")
            val low = numberOrNull(p, "low")
            val mid = numberOrNull(p, "mid")
            val high = numberOrNull(p, "high")
            if (market == null && low == null && mid == null && high == null) continue
            result += VariantPrice(
                variant = key,
                label = variantLabel(key),
                marketUsd = market,
                lowUsd = low,
                midUsd = mid,
                highUsd = high,
                updated = updated,
                sourceLabel = "TCGPlayer vía Pokémon TCG API (respaldo)"
            )
        }
        return result.sortedBy { variantOrder(it.variant) }
    }

'''
if anchor not in s:
    raise SystemExit("No se encontró fetchRate")
s = s.replace(anchor, helper + anchor, 1)
repo.write_text(s)

# 3) UI: indicar fuente real y explicar que se intentaron dos fuentes.
s = main.read_text()
s = s.replace(
    '"TCGPlayer no publica un precio USD para esta carta en este momento. La identificación sigue siendo válida; podés volver a consultar más adelante."',
    '"No encontré un precio USD después de consultar TCGdex y la fuente secundaria de precios. La identificación sigue siendo válida; podés volver a consultar más adelante."',
    1
)
s = s.replace(
    'Text("Fuente: TCGPlayer vía TCGdex", color = Color(0xFF91A4BC), fontSize = 12.sp)',
    'Text("Fuente: ${p.sourceLabel}", color = Color(0xFF91A4BC), fontSize = 12.sp)',
    1
)
needle = '''                            p.updated?.let {\n                                Text("Actualización de precio: $it", color = Color(0xFF91A4BC), fontSize = 12.sp)\n                            }'''
insert = needle + '''\n                            if (p.sourceLabel.contains("respaldo", ignoreCase = true)) {\n                                Text(\n                                    "Precio recuperado automáticamente con la fuente secundaria porque TCGdex no devolvió valor para esta carta.",\n                                    color = Color(0xFF64E38B),\n                                    fontSize = 12.sp\n                                )\n                            }'''
if needle not in s:
    raise SystemExit("No se encontró bloque updated")
s = s.replace(needle, insert, 1)
s = s.replace("V16 · Pokémon · USD / UYU", "V17 · Pokémon · USD / UYU", 1)
s = s.replace('"16.0"', '"17.0"')
main.write_text(s)

# 4) Versión.
g = gradle.read_text()
g = g.replace("versionCode = 16", "versionCode = 17", 1)
g = g.replace('versionName = "16.0"', 'versionName = "17.0"', 1)
gradle.write_text(g)

r = readme.read_text()
r += """

## V17
- Segundo intento automático de precio cuando TCGdex identifica la carta pero no devuelve pricing.
- El respaldo consulta Pokémon TCG API por ID o por nombre + número y toma sus datos TCGplayer.
- La fuente del precio se muestra en pantalla.
- Si ambas fuentes fallan, se mantiene la identificación y se informa que no hay precio disponible.
"""
readme.write_text(r)
