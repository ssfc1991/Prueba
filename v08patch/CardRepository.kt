package uy.impulsa.cartavalor

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.HttpUrl.Companion.toHttpUrl
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONArray
import org.json.JSONObject
import java.io.IOException
import java.text.Normalizer

class CardRepository {
    private val client = OkHttpClient()
    private val tcgdexBase = "https://api.tcgdex.net/v2"
    private val nameCorrector = PokemonNameCorrector(client)

    suspend fun search(name: String, number: String): List<CardCandidate> = withContext(Dispatchers.IO) {
        if (name.isBlank() && number.isBlank()) {
            throw IllegalArgumentException("Ingresá al menos el nombre o el número de la carta.")
        }

        val combined = linkedMapOf<String, CardCandidate>()
        for (lang in listOf("es", "en")) {
            if (number.isNotBlank()) {
                searchLanguage(lang, "", number).forEach { combined.putIfAbsent(it.id, it) }
            }
            if (name.isNotBlank()) {
                searchLanguage(lang, name, "").forEach { combined.putIfAbsent(it.id, it) }
            }
        }

        combined.values
            .sortedWith(
                compareByDescending<CardCandidate> {
                    if (number.isNotBlank() && it.number.equals(number.trim(), true)) 1 else 0
                }
                    .thenByDescending { similarity(it.name, name) }
                    .thenBy { it.name }
            )
            .take(50)
    }

    suspend fun searchWithFallback(
        name: String,
        number: String,
        rawText: String
    ): List<CardCandidate> = withContext(Dispatchers.IO) {
        val combined = linkedMapOf<String, CardCandidate>()

        // 1) Número exacto y nombre OCR tal como fueron leídos.
        runCatching { search(name, number) }
            .getOrDefault(emptyList())
            .forEach { combined.putIfAbsent(it.id, it) }

        val hints = ScanTextParser.fallbackHints(rawText, name)

        // 2) Palabras alternativas extraídas del OCR.
        if (combined.size < 12) {
            for (hint in hints) {
                for (lang in listOf("es", "en")) {
                    runCatching { searchLanguage(lang, hint, "") }
                        .getOrDefault(emptyList())
                        .forEach { combined.putIfAbsent(it.id, it) }
                }
                if (combined.size >= 40) break
            }
        }

        // 3) Corrección aproximada del nombre Pokémon.
        // El OCR puede leer Pinslr/Pinsirr/etc. La lista de especies de PokéAPI
        // sirve como diccionario; no se envía la foto ni datos personales.
        val correctedNames = if (combined.size < 18) {
            nameCorrector.suggest((listOf(name) + hints).filter { it.isNotBlank() })
        } else {
            emptyList()
        }

        for (corrected in correctedNames) {
            for (lang in listOf("es", "en")) {
                runCatching { searchLanguage(lang, corrected, "") }
                    .getOrDefault(emptyList())
                    .forEach { combined.putIfAbsent(it.id, it) }
            }
            if (combined.size >= 50) break
        }

        val rankingHints = (listOf(name) + hints + correctedNames)
            .filter { it.isNotBlank() }
            .distinct()

        combined.values
            .sortedWith(
                compareByDescending<CardCandidate> {
                    if (number.isNotBlank() && it.number.equals(number.trim(), true)) 1 else 0
                }
                    .thenByDescending { candidate ->
                        rankingHints.maxOfOrNull { hint -> similarity(candidate.name, hint) } ?: 0
                    }
                    .thenBy { it.name }
            )
            .take(50)
    }

    suspend fun valuation(candidate: CardCandidate): CardValuation = withContext(Dispatchers.IO) {
        val full = fetchCard(candidate.id, "es") ?: fetchCard(candidate.id, "en")
            ?: throw IOException("No pude cargar los datos completos de esta carta.")
        val card = parseFullCandidate(full)
        val prices = parseTcgPlayerPrices(full.optJSONObject("pricing")?.optJSONObject("tcgplayer"))
        val uyuRate = fetchRate("usd", "uyu")
        CardValuation(card, prices, uyuRate)
    }

    private fun searchLanguage(lang: String, name: String, number: String): List<CardCandidate> {
        val urlBuilder = "$tcgdexBase/$lang/cards".toHttpUrl().newBuilder()
        if (number.isNotBlank()) {
            urlBuilder.addQueryParameter("localId", "eq:${number.trim()}")
        } else if (name.isNotBlank()) {
            urlBuilder.addQueryParameter("name", name.trim())
            urlBuilder.addQueryParameter("pagination:page", "1")
            urlBuilder.addQueryParameter("pagination:itemsPerPage", "50")
        }
        val array = executeArray(urlBuilder.build().toString())
        val cards = mutableListOf<CardCandidate>()
        for (i in 0 until array.length()) {
            val obj = array.optJSONObject(i) ?: continue
            val id = obj.optString("id")
            if (id.isBlank()) continue
            val imageBase = obj.optString("image").takeIf { it.isNotBlank() }
            cards += CardCandidate(
                id = id,
                name = obj.optString("name", "Carta"),
                number = obj.optString("localId"),
                imageUrl = imageBase?.let { "$it/low.webp" }
            )
        }
        return cards
    }

    private fun fetchCard(id: String, lang: String): JSONObject? {
        val request = Request.Builder().url("$tcgdexBase/$lang/cards/$id").build()
        return client.newCall(request).execute().use { response ->
            if (response.code == 404) return@use null
            if (!response.isSuccessful) throw IOException("Error ${response.code} al consultar TCGdex.")
            JSONObject(response.body?.string().orEmpty())
        }
    }

    private fun parseFullCandidate(obj: JSONObject): CardCandidate {
        val set = obj.optJSONObject("set")
        val imageBase = obj.optString("image").takeIf { it.isNotBlank() }
        return CardCandidate(
            id = obj.optString("id"),
            name = obj.optString("name", "Carta"),
            number = obj.optString("localId"),
            setName = set?.optString("name").orEmpty(),
            rarity = obj.optString("rarity").takeIf { it.isNotBlank() },
            imageUrl = imageBase?.let { "$it/high.webp" }
        )
    }

    private fun parseTcgPlayerPrices(tcg: JSONObject?): List<VariantPrice> {
        if (tcg == null) return emptyList()
        val unit = tcg.optString("unit", "USD")
        if (!unit.equals("USD", ignoreCase = true)) return emptyList()

        val updated = tcg.optString("updated").takeIf { it.isNotBlank() }
        val result = mutableListOf<VariantPrice>()
        val keys = tcg.keys()
        while (keys.hasNext()) {
            val key = keys.next()
            if (key == "updated" || key == "unit") continue
            val p = tcg.optJSONObject(key) ?: continue
            val market = numberOrNull(p, "marketPrice")
            val low = numberOrNull(p, "lowPrice")
            val mid = numberOrNull(p, "midPrice")
            val high = numberOrNull(p, "highPrice")
            if (market == null && low == null && mid == null && high == null) continue
            result += VariantPrice(
                variant = key,
                label = variantLabel(key),
                marketUsd = market,
                lowUsd = low,
                midUsd = mid,
                highUsd = high,
                updated = updated
            )
        }
        return result.sortedBy { variantOrder(it.variant) }
    }

    private fun numberOrNull(obj: JSONObject, key: String): Double? {
        if (!obj.has(key) || obj.isNull(key)) return null
        val d = obj.optDouble(key, Double.NaN)
        return d.takeIf { it.isFinite() && it >= 0 }
    }

    private fun variantLabel(key: String): String = when (key.lowercase()) {
        "normal" -> "Normal"
        "holo", "holofoil" -> "Holográfica"
        "reverse", "reverse-holofoil", "reverseholofoil" -> "Reverse Holo"
        "1st-edition", "1stedition" -> "1.ª edición"
        "1st-edition-holofoil", "1steditionholofoil" -> "1.ª edición Holo"
        "unlimited" -> "Unlimited"
        "unlimited-holofoil" -> "Unlimited Holo"
        else -> key.replace('-', ' ').replaceFirstChar { it.uppercase() }
    }

    private fun variantOrder(key: String): Int = when (key.lowercase()) {
        "normal" -> 0
        "holo", "holofoil" -> 1
        "reverse", "reverse-holofoil", "reverseholofoil" -> 2
        "1st-edition", "1stedition" -> 3
        "1st-edition-holofoil", "1steditionholofoil" -> 4
        else -> 9
    }

    private fun fetchRate(base: String, quote: String): Double? {
        val request = Request.Builder().url("https://api.frankfurter.dev/v2/rate/$base/$quote").build()
        return runCatching {
            client.newCall(request).execute().use { response ->
                if (!response.isSuccessful) return@use null
                val obj = JSONObject(response.body?.string().orEmpty())
                obj.optDouble("rate", Double.NaN).takeIf { it.isFinite() && it > 0 }
            }
        }.getOrNull()
    }

    private fun executeArray(url: String): JSONArray {
        val request = Request.Builder().url(url).build()
        client.newCall(request).execute().use { response ->
            val text = response.body?.string().orEmpty()
            if (!response.isSuccessful) throw IOException("Error ${response.code} al buscar cartas.")
            return JSONArray(text)
        }
    }

    private fun similarity(candidate: String, hint: String): Int {
        if (hint.isBlank()) return 0
        val a = normalize(candidate)
        val b = normalize(hint)
        if (a == b) return 100
        if (a.contains(b) || b.contains(a)) return 80

        val aw = a.split(" ").filter { it.length > 1 }.toSet()
        val bw = b.split(" ").filter { it.length > 1 }.toSet()
        val wordScore = aw.intersect(bw).size * 12

        val editScore = fuzzySimilarity(a, b)
        return maxOf(wordScore, editScore)
    }

    private fun fuzzySimilarity(a: String, b: String): Int {
        if (a.isBlank() || b.isBlank()) return 0
        val distance = levenshtein(a, b)
        val maxLen = maxOf(a.length, b.length).coerceAtLeast(1)
        return ((1.0 - distance.toDouble() / maxLen) * 100).toInt().coerceIn(0, 100)
    }

    private fun levenshtein(a: String, b: String): Int {
        var previous = IntArray(b.length + 1) { it }
        for (i in a.indices) {
            val current = IntArray(b.length + 1)
            current[0] = i + 1
            for (j in b.indices) {
                val cost = if (a[i] == b[j]) 0 else 1
                current[j + 1] = minOf(
                    current[j] + 1,
                    previous[j + 1] + 1,
                    previous[j] + cost
                )
            }
            previous = current
        }
        return previous[b.length]
    }

    private fun normalize(text: String): String = Normalizer.normalize(
        text.lowercase(),
        Normalizer.Form.NFD
    )
        .replace(Regex("\\p{Mn}+"), "")
        .replace(Regex("[^a-z0-9 ]"), " ")
        .replace(Regex("\\s+"), " ")
        .trim()
}
