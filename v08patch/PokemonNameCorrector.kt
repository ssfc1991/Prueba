package uy.impulsa.cartavalor

import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONObject
import java.text.Normalizer
import kotlin.math.max

class PokemonNameCorrector(
    private val client: OkHttpClient
) {
    @Volatile
    private var cachedNames: List<String>? = null

    fun suggest(rawHints: List<String>, maxSuggestions: Int = 6): List<String> {
        val names = loadNames()
        if (names.isEmpty()) return emptyList()

        val bases = rawHints
            .flatMap { extractBaseCandidates(it) }
            .map(::normalize)
            .filter { it.length >= 3 }
            .distinct()
            .take(12)

        if (bases.isEmpty()) return emptyList()

        return names.asSequence()
            .map { canonical ->
                val normalized = normalize(canonical)
                val score = bases.maxOf { hint -> fuzzyScore(hint, normalized) }
                canonical to score
            }
            .filter { (_, score) -> score >= 62 }
            .sortedByDescending { it.second }
            .map { it.first }
            .distinct()
            .take(maxSuggestions)
            .toList()
    }

    private fun loadNames(): List<String> {
        cachedNames?.let { return it }
        return synchronized(this) {
            cachedNames?.let { return@synchronized it }

            val request = Request.Builder()
                .url("https://pokeapi.co/api/v2/pokemon-species?limit=2000")
                .build()

            val loaded = runCatching {
                client.newCall(request).execute().use { response ->
                    if (!response.isSuccessful) return@use emptyList<String>()
                    val obj = JSONObject(response.body?.string().orEmpty())
                    val array = obj.optJSONArray("results") ?: return@use emptyList<String>()
                    buildList {
                        for (i in 0 until array.length()) {
                            val name = array.optJSONObject(i)?.optString("name").orEmpty()
                            if (name.isNotBlank()) add(name.replace('-', ' '))
                        }
                    }
                }
            }.getOrDefault(emptyList())

            cachedNames = loaded
            loaded
        }
    }

    private fun extractBaseCandidates(text: String): List<String> {
        val cleaned = text
            .replace(Regex("(?i)\\b(?:ex|gx|v|max|vmax|vstar|star|lv\\.?x|break)\\b"), " ")
            .replace(Regex("[^A-Za-zÁÉÍÓÚÜÑáéíóúüñ0-9' .-]"), " ")
            .replace(Regex("\\s+"), " ")
            .trim()

        if (cleaned.isBlank()) return emptyList()

        val words = cleaned.split(' ').filter { it.length >= 3 }
        val result = linkedSetOf<String>()
        result += cleaned
        words.forEach { result += it }
        if (words.size >= 2) {
            words.windowed(2).forEach { result += it.joinToString(" ") }
        }
        return result.toList()
    }

    private fun fuzzyScore(a: String, b: String): Int {
        if (a == b) return 100
        if (a.startsWith(b) || b.startsWith(a)) return 92
        if (a.contains(b) || b.contains(a)) return 86

        val distance = levenshtein(a, b)
        val maxLen = max(a.length, b.length).coerceAtLeast(1)
        val ratio = 1.0 - distance.toDouble() / maxLen

        val lengthBonus = when {
            a.length <= 5 && distance <= 1 -> 10
            a.length <= 8 && distance <= 2 -> 7
            a.length > 8 && distance <= 3 -> 5
            else -> 0
        }
        return (ratio * 100).toInt() + lengthBonus
    }

    private fun levenshtein(a: String, b: String): Int {
        if (a == b) return 0
        if (a.isEmpty()) return b.length
        if (b.isEmpty()) return a.length

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
