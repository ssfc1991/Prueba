package uy.impulsa.cartavalor

import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.graphics.ColorMatrix
import android.graphics.ColorMatrixColorFilter
import android.graphics.Paint
import android.graphics.Canvas
import android.net.Uri
import com.google.mlkit.vision.common.InputImage
import com.google.mlkit.vision.text.TextRecognition
import com.google.mlkit.vision.text.latin.TextRecognizerOptions

class PhotoOcrProcessor {
    private val recognizer = TextRecognition.getClient(TextRecognizerOptions.DEFAULT_OPTIONS)

    fun analyze(
        context: Context,
        uri: Uri,
        onSuccess: (ScanGuess) -> Unit,
        onFailure: (Throwable) -> Unit
    ) {
        val bitmap = runCatching { loadBitmap(context, uri) }
            .getOrElse { onFailure(it); return }
            ?: run { onFailure(IllegalStateException("No pude abrir la fotografía.")); return }

        val passes = buildPasses(bitmap)
        val allLines = mutableListOf<String>()
        val rawParts = mutableListOf<String>()
        processPass(passes, 0, allLines, rawParts, onSuccess, onFailure)
    }

    private fun processPass(
        passes: List<Bitmap>,
        index: Int,
        lines: MutableList<String>,
        rawParts: MutableList<String>,
        onSuccess: (ScanGuess) -> Unit,
        onFailure: (Throwable) -> Unit
    ) {
        if (index >= passes.size) {
            passes.forEach { if (!it.isRecycled) it.recycle() }
            onSuccess(ScanTextParser.parse(lines.distinct(), rawParts.joinToString("\n")))
            return
        }

        recognizer.process(InputImage.fromBitmap(passes[index], 0))
            .addOnSuccessListener { result ->
                lines += result.textBlocks.flatMap { it.lines }.map { it.text }
                if (result.text.isNotBlank()) rawParts += result.text
                processPass(passes, index + 1, lines, rawParts, onSuccess, onFailure)
            }
            .addOnFailureListener { error ->
                if (index + 1 < passes.size) {
                    processPass(passes, index + 1, lines, rawParts, onSuccess, onFailure)
                } else if (lines.isNotEmpty()) {
                    passes.forEach { if (!it.isRecycled) it.recycle() }
                    onSuccess(ScanTextParser.parse(lines.distinct(), rawParts.joinToString("\n")))
                } else {
                    passes.forEach { if (!it.isRecycled) it.recycle() }
                    onFailure(error)
                }
            }
    }

    private fun loadBitmap(context: Context, uri: Uri): Bitmap? {
        return if (uri.scheme == "file") {
            uri.path?.let(BitmapFactory::decodeFile)
        } else {
            context.contentResolver.openInputStream(uri)?.use(BitmapFactory::decodeStream)
        }
    }

    private fun buildPasses(source: Bitmap): List<Bitmap> {
        val oriented = downscale(source, 1800)
        if (oriented !== source) source.recycle()

        val topHeight = (oriented.height * 0.38f).toInt().coerceAtLeast(1)
        val top = Bitmap.createBitmap(oriented, 0, 0, oriented.width, topHeight)
        val centerWidth = (oriented.width * 0.88f).toInt().coerceAtLeast(1)
        val x = ((oriented.width - centerWidth) / 2).coerceAtLeast(0)
        val centeredTop = Bitmap.createBitmap(top, x.coerceAtMost(top.width - 1), 0, centerWidth.coerceAtMost(top.width - x), top.height)
        top.recycle()

        val enhancedTop = contrastCopy(centeredTop, 1.65f)
        return listOf(centeredTop, enhancedTop, oriented)
    }

    private fun downscale(bitmap: Bitmap, maxSide: Int): Bitmap {
        val largest = maxOf(bitmap.width, bitmap.height)
        if (largest <= maxSide) return bitmap
        val scale = maxSide.toFloat() / largest
        return Bitmap.createScaledBitmap(
            bitmap,
            (bitmap.width * scale).toInt().coerceAtLeast(1),
            (bitmap.height * scale).toInt().coerceAtLeast(1),
            true
        )
    }

    private fun contrastCopy(source: Bitmap, contrast: Float): Bitmap {
        val result = Bitmap.createBitmap(source.width, source.height, Bitmap.Config.ARGB_8888)
        val translate = (-0.5f * contrast + 0.5f) * 255f
        val matrix = ColorMatrix(floatArrayOf(
            contrast, 0f, 0f, 0f, translate,
            0f, contrast, 0f, 0f, translate,
            0f, 0f, contrast, 0f, translate,
            0f, 0f, 0f, 1f, 0f
        ))
        val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { colorFilter = ColorMatrixColorFilter(matrix) }
        Canvas(result).drawBitmap(source, 0f, 0f, paint)
        return result
    }

    fun close() = recognizer.close()
}
