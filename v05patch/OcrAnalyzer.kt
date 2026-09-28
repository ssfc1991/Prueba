package uy.impulsa.cartavalor

import android.os.SystemClock
import androidx.annotation.OptIn
import androidx.camera.core.ExperimentalGetImage
import androidx.camera.core.ImageAnalysis
import androidx.camera.core.ImageProxy
import com.google.mlkit.vision.common.InputImage
import com.google.mlkit.vision.text.TextRecognition
import com.google.mlkit.vision.text.latin.TextRecognizerOptions
import java.util.concurrent.atomic.AtomicBoolean

class OcrAnalyzer(
    private val onGuess: (ScanGuess) -> Unit
) : ImageAnalysis.Analyzer {
    private val recognizer = TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS)
    private val busy = AtomicBoolean(false)
    private var lastRun = 0L

    @OptIn(ExperimentalGetImage::class)
    override fun analyze(imageProxy: ImageProxy) {
        val now = SystemClock.elapsedRealtime()
        if (busy.get() || now - lastRun < 900) {
            imageProxy.close()
            return
        }
        val mediaImage = imageProxy.image
        if (mediaImage == null) {
            imageProxy.close()
            return
        }
        busy.set(true)
        lastRun = now
        val input = InputImage.fromMediaImage(mediaImage, imageProxy.imageInfo.rotationDegrees)
        recognizer.process(input)
            .addOnSuccessListener { result ->
                onGuess(ScanTextParser.parse(result.textBlocks.flatMap { it.lines }.map { it.text }, result.text))
            }
            .addOnCompleteListener {
                busy.set(false)
                imageProxy.close()
            }
    }

    fun close() = recognizer.close()
}

object ScanTextParser {
    fun parse(linesRaw: List<String>, raw: String): ScanGuess {
        val lines = linesRaw.map { it.trim() }.filter { it.isNotBlank() }
        val fractionPatterns = listOf(
            Regex("(?i)\\b([A-Z]{0,4}[- ]?\\d{1,3}[a-z]?)\\s*[/／]\\s*(\\d{2,3})\\b"),
            Regex("(?i)\\b(\\d{1,3}[a-z]?)\\s*(?:de|of)\\s*(\\d{2,3})\\b")
        )
        val fraction = lines.asSequence().mapNotNull { line ->
            fractionPatterns.asSequence().mapNotNull { it.find(line) }.firstOrNull()
        }.firstOrNull()

        val number = fraction?.groupValues?.getOrNull(1)?.replace(" ", "")?.replace("-", "") ?: ""
        val total = fraction?.groupValues?.getOrNull(2) ?: ""

        val blocked = listOf(
            "pokemon", "pokémon", "basic", "básico", "stage", "fase", "hp", "pv",
            "trainer", "entrenador", "energy", "energía", "weakness", "resistance",
            "retreat", "debilidad", "resistencia", "ilustrador", "illustrator"
        )
        val name = lines.take(24)
            .map { cleanNameCandidate(it) }
            .filter { clean ->
                val lower = clean.lowercase()
                clean.length in 3..28 &&
                    clean.count { it.isDigit() } <= 1 &&
                    blocked.none { lower == it || lower.startsWith("$it ") } &&
                    clean.any { it.isLetter() } &&
                    !clean.contains('/') &&
                    !Regex("(?i)\\b(?:hp|pv)\\s*\\d+").containsMatchIn(clean)
            }
            .maxByOrNull { scoreNameCandidate(it) }
            .orEmpty()

        return ScanGuess(number = number, printedTotal = total, nameHint = name, rawText = raw)
    }

    fun fallbackHints(raw: String, currentName: String): List<String> {
        val blockedWords = setOf(
            "pokemon", "pokémon", "basic", "básico", "stage", "fase", "hp", "pv", "energy", "energía",
            "weakness", "resistance", "retreat", "debilidad", "resistencia", "illustrator", "ilustrador",
            "damage", "daño", "card", "carta", "attack", "ataque", "coste", "cost"
        )
        val hints = linkedSetOf<String>()
        cleanNameCandidate(currentName).takeIf { it.length >= 3 }?.let(hints::add)
        raw.lines().take(28).forEach { line ->
            val clean = cleanNameCandidate(line)
            if (clean.length in 3..28 && clean.any { it.isLetter() } && clean.count { it.isDigit() } <= 1) {
                val lower = clean.lowercase()
                if (blockedWords.none { lower == it || lower.startsWith("$it ") }) hints += clean
            }
            Regex("[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]{4,20}").findAll(line).forEach { match ->
                val word = repairOcrWord(match.value)
                if (word.lowercase() !in blockedWords) hints += word
            }
        }
        return hints.take(8)
    }

    private fun cleanNameCandidate(text: String): String = text
        .replace(Regex("(?i)\\b(?:HP|PV)\\s*\\d+\\b"), " ")
        .replace(Regex("[^A-Za-zÁÉÍÓÚÜÑáéíóúüñ0-9' .-]"), " ")
        .replace(Regex("\\s+"), " ")
        .trim()

    private fun repairOcrWord(word: String): String {
        if (word.length < 4) return word
        return word.replace('1', 'i').replace('0', 'o')
    }

    private fun scoreNameCandidate(text: String): Int {
        var score = 0
        if (text.length in 4..16) score += 12
        if (text.split(' ').size <= 3) score += 8
        if (text.firstOrNull()?.isUpperCase() == true) score += 5
        score -= text.count { it.isDigit() } * 8
        if (Regex("(?i)\\b(?:ex|gx|v|max|star)\\b").containsMatchIn(text)) score += 2
        return score
    }
}
