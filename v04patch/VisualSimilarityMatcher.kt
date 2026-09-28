package uy.impulsa.cartavalor

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.net.Uri
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import okhttp3.OkHttpClient
import okhttp3.Request
import java.io.IOException
import kotlin.math.roundToInt
import kotlin.math.sqrt

class VisualSimilarityMatcher(private val context: Context) {
    private val client = OkHttpClient()

    suspend fun rank(photoUri: String, candidates: List<CardCandidate>, limit: Int = 24): List<CardCandidate> =
        withContext(Dispatchers.IO) {
            val source = loadLocalBitmap(photoUri) ?: return@withContext candidates
            val sourcePrepared = prepare(source, cropCaptured = true)
            source.recycle()

            val ranked = candidates.take(limit).map { candidate ->
                val score = candidate.imageUrl?.let { url ->
                    runCatching {
                        val official = downloadBitmap(url) ?: return@runCatching null
                        val prepared = prepare(official, cropCaptured = false)
                        official.recycle()
                        val value = compare(sourcePrepared, prepared)
                        prepared.recycle()
                        value
                    }.getOrNull()
                }
                candidate.copy(visualScore = score)
            }.sortedWith(
                compareByDescending<CardCandidate> { it.visualScore ?: -1 }
                    .thenBy { it.name }
            )

            sourcePrepared.recycle()
            ranked + candidates.drop(limit)
        }

    private fun loadLocalBitmap(uriText: String): Bitmap? {
        val uri = Uri.parse(uriText)
        return if (uri.scheme == "file") {
            uri.path?.let(BitmapFactory::decodeFile)
        } else {
            context.contentResolver.openInputStream(uri)?.use(BitmapFactory::decodeStream)
        }
    }

    private fun downloadBitmap(url: String): Bitmap? {
        val request = Request.Builder().url(url).build()
        return client.newCall(request).execute().use { response ->
            if (!response.isSuccessful) throw IOException("Imagen oficial ${response.code}")
            val bytes = response.body?.bytes() ?: return@use null
            BitmapFactory.decodeByteArray(bytes, 0, bytes.size)
        }
    }

    private fun prepare(bitmap: Bitmap, cropCaptured: Boolean): Bitmap {
        var working = bitmap
        if (cropCaptured) {
            val cropW = (bitmap.width * 0.78f).roundToInt().coerceAtLeast(1)
            val cropH = (bitmap.height * 0.78f).roundToInt().coerceAtLeast(1)
            val x = ((bitmap.width - cropW) / 2).coerceAtLeast(0)
            val y = ((bitmap.height - cropH) / 2).coerceAtLeast(0)
            working = Bitmap.createBitmap(bitmap, x, y, cropW, cropH)
        }

        val targetRatio = 0.72f
        val ratio = working.width.toFloat() / working.height.toFloat()
        val cropped = if (ratio > targetRatio) {
            val newW = (working.height * targetRatio).roundToInt().coerceAtLeast(1)
            Bitmap.createBitmap(working, (working.width - newW) / 2, 0, newW, working.height)
        } else {
            val newH = (working.width / targetRatio).roundToInt().coerceAtLeast(1)
            Bitmap.createBitmap(working, 0, (working.height - newH) / 2, working.width, newH)
        }
        if (working !== bitmap && working !== cropped) working.recycle()
        val scaled = Bitmap.createScaledBitmap(cropped, 36, 50, true)
        if (cropped !== bitmap && cropped !== scaled) cropped.recycle()
        return scaled
    }

    private fun compare(a: Bitmap, b: Bitmap): Int {
        val luminance = luminanceCorrelation(a, b)
        val hash = differenceHashSimilarity(a, b)
        val color = colorHistogramSimilarity(a, b)
        return (luminance * 0.55 + hash * 0.25 + color * 0.20)
            .roundToInt().coerceIn(0, 100)
    }

    private fun luminanceCorrelation(a: Bitmap, b: Bitmap): Double {
        val av = luminanceVector(a)
        val bv = luminanceVector(b)
        val meanA = av.average()
        val meanB = bv.average()
        var dot = 0.0
        var aa = 0.0
        var bb = 0.0
        for (i in av.indices) {
            val x = av[i] - meanA
            val y = bv[i] - meanB
            dot += x * y
            aa += x * x
            bb += y * y
        }
        if (aa <= 1e-9 || bb <= 1e-9) return 0.0
        val corr = dot / sqrt(aa * bb)
        return ((corr + 1.0) / 2.0 * 100.0).coerceIn(0.0, 100.0)
    }

    private fun luminanceVector(bitmap: Bitmap): DoubleArray {
        val out = DoubleArray(bitmap.width * bitmap.height)
        var p = 0
        for (y in 0 until bitmap.height) for (x in 0 until bitmap.width) {
            val c = bitmap.getPixel(x, y)
            val r = (c shr 16) and 0xff
            val g = (c shr 8) and 0xff
            val b = c and 0xff
            out[p++] = 0.299 * r + 0.587 * g + 0.114 * b
        }
        return out
    }

    private fun differenceHashSimilarity(a: Bitmap, b: Bitmap): Double {
        val ah = dHash(a)
        val bh = dHash(b)
        val distance = java.lang.Long.bitCount(ah xor bh)
        return (1.0 - distance / 64.0) * 100.0
    }

    private fun dHash(bitmap: Bitmap): Long {
        val tiny = Bitmap.createScaledBitmap(bitmap, 9, 8, true)
        var hash = 0L
        var bit = 0
        for (y in 0 until 8) for (x in 0 until 8) {
            val left = gray(tiny.getPixel(x, y))
            val right = gray(tiny.getPixel(x + 1, y))
            if (left > right) hash = hash or (1L shl bit)
            bit++
        }
        tiny.recycle()
        return hash
    }

    private fun gray(c: Int): Int {
        val r = (c shr 16) and 0xff
        val g = (c shr 8) and 0xff
        val b = c and 0xff
        return (r * 30 + g * 59 + b * 11) / 100
    }

    private fun colorHistogramSimilarity(a: Bitmap, b: Bitmap): Double {
        val ha = histogram(a)
        val hb = histogram(b)
        var dot = 0.0
        var aa = 0.0
        var bb = 0.0
        for (i in ha.indices) {
            dot += ha[i] * hb[i]
            aa += ha[i] * ha[i]
            bb += hb[i] * hb[i]
        }
        if (aa <= 1e-9 || bb <= 1e-9) return 0.0
        return (dot / sqrt(aa * bb) * 100.0).coerceIn(0.0, 100.0)
    }

    private fun histogram(bitmap: Bitmap): DoubleArray {
        val hist = DoubleArray(64)
        for (y in 0 until bitmap.height step 2) for (x in 0 until bitmap.width step 2) {
            val c = bitmap.getPixel(x, y)
            val r = ((c shr 16) and 0xff) / 64
            val g = ((c shr 8) and 0xff) / 64
            val b = (c and 0xff) / 64
            hist[r * 16 + g * 4 + b] += 1.0
        }
        return hist
    }
}
