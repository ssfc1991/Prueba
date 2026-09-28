from pathlib import Path

p = Path("CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt")
s = p.read_text()

start = s.index("@Composable\nprivate fun CameraBox(")
end = s.index("@Composable\nprivate fun CapturedPhotoPreview", start)

replacement = r'''@Composable
private fun CameraBox(
    onGuess: (ScanGuess) -> Unit,
    onPhotoCaptured: (String) -> Unit,
    onError: (String) -> Unit
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val executor = remember { Executors.newSingleThreadExecutor() }

    val acceptLiveOcr = remember { java.util.concurrent.atomic.AtomicBoolean(true) }
    var capturing by remember { mutableStateOf(false) }
    var candidateName by remember { mutableStateOf("") }
    var candidateRepeats by remember { mutableStateOf(0) }
    var stableName by remember { mutableStateOf("") }
    var stableGuess by remember { mutableStateOf(ScanGuess()) }

    fun normalizedName(value: String): String = value
        .lowercase()
        .replace(Regex("[^a-záéíóúüñ0-9 ]"), "")
        .replace(Regex("\\s+"), " ")
        .trim()

    fun mergeLiveGuess(incoming: ScanGuess) {
        if (!acceptLiveOcr.get() || capturing) return

        val incomingName = incoming.nameHint.trim()
        if (incomingName.isNotBlank() && stableName.isBlank()) {
            val currentNorm = normalizedName(incomingName)
            val candidateNorm = normalizedName(candidateName)
            if (currentNorm.isNotBlank() && currentNorm == candidateNorm) {
                candidateRepeats += 1
            } else {
                candidateName = incomingName
                candidateRepeats = 1
            }

            // El nombre visible solo se fija cuando la cámara repite la misma lectura.
            // Una vez fijado no se cambia por otra lectura en vivo.
            if (candidateRepeats >= 2) {
                stableName = incomingName
            }
        }

        val merged = ScanGuess(
            number = incoming.number.ifBlank { stableGuess.number },
            printedTotal = incoming.printedTotal.ifBlank { stableGuess.printedTotal },
            nameHint = stableName.ifBlank { stableGuess.nameHint },
            rawText = if (incoming.rawText.isNotBlank()) incoming.rawText else stableGuess.rawText
        )

        if (merged != stableGuess) {
            stableGuess = merged
            onGuess(merged)
        }
    }

    val analyzer = remember { OcrAnalyzer(::mergeLiveGuess) }
    val photoOcr = remember { PhotoOcrProcessor() }

    val controller = remember {
        LifecycleCameraController(context).apply {
            cameraSelector = CameraSelector.DEFAULT_BACK_CAMERA
            setTapToFocusEnabled(true)
            setPinchToZoomEnabled(true)
            setEnabledUseCases(CameraController.IMAGE_ANALYSIS or CameraController.IMAGE_CAPTURE)
        }
    }

    DisposableEffect(lifecycleOwner) {
        acceptLiveOcr.set(true)
        controller.setImageAnalysisAnalyzer(executor, analyzer)
        controller.bindToLifecycle(lifecycleOwner)
        runCatching { controller.setZoomRatio(1.20f) }

        onDispose {
            acceptLiveOcr.set(false)
            controller.clearImageAnalysisAnalyzer()
            analyzer.close()
            photoOcr.close()
            executor.shutdown()
            controller.unbind()
        }
    }

    fun takePhoto() {
        if (capturing) return

        // Congelar inmediatamente el OCR en vivo. Esto impide que un resultado
        // atrasado pise el nombre correcto obtenido desde la foto fija.
        capturing = true
        acceptLiveOcr.set(false)
        controller.clearImageAnalysisAnalyzer()

        val dir = File(context.filesDir, "scan_photos").apply { mkdirs() }
        val file = File(dir, "scan_${System.currentTimeMillis()}.jpg")
        val output = ImageCapture.OutputFileOptions.Builder(file).build()

        controller.takePicture(
            output,
            ContextCompat.getMainExecutor(context),
            object : ImageCapture.OnImageSavedCallback {
                override fun onImageSaved(outputFileResults: ImageCapture.OutputFileResults) {
                    val uri = Uri.fromFile(file)
                    photoOcr.analyze(
                        context = context,
                        uri = uri,
                        onSuccess = { parsed ->
                            val finalGuess = ScanGuess(
                                number = parsed.number.ifBlank { stableGuess.number },
                                printedTotal = parsed.printedTotal.ifBlank { stableGuess.printedTotal },
                                nameHint = parsed.nameHint.ifBlank { stableName.ifBlank { stableGuess.nameHint } },
                                rawText = parsed.rawText.ifBlank { stableGuess.rawText }
                            )

                            stableGuess = finalGuess
                            if (finalGuess.nameHint.isNotBlank()) stableName = finalGuess.nameHint
                            onGuess(finalGuess)
                            onPhotoCaptured(uri.toString())
                            capturing = false
                        },
                        onFailure = {
                            val fallback = stableGuess.copy(nameHint = stableName.ifBlank { stableGuess.nameHint })
                            onGuess(fallback)
                            onPhotoCaptured(uri.toString())
                            onError("La foto se guardó, pero el OCR fijo no pudo leer texto. Mantengo la última lectura estable para que puedas corregirla manualmente.")
                            capturing = false
                        }
                    )
                }

                override fun onError(exception: ImageCaptureException) {
                    file.delete()
                    capturing = false
                    acceptLiveOcr.set(true)
                    controller.setImageAnalysisAnalyzer(executor, analyzer)
                    onError("No se pudo tomar la foto. Probá nuevamente.")
                }
            }
        )
    }

    Box(
        Modifier.fillMaxWidth().aspectRatio(0.72f)
            .background(Color.Black, RoundedCornerShape(18.dp))
    ) {
        AndroidView(
            factory = {
                PreviewView(it).apply {
                    this.controller = controller
                    scaleType = PreviewView.ScaleType.FILL_CENTER
                }
            },
            modifier = Modifier.fillMaxSize()
        )
        Box(
            Modifier.align(Alignment.Center)
                .fillMaxWidth(0.92f)
                .aspectRatio(0.72f)
                .border(4.dp, Color(0xFFFFC928), RoundedCornerShape(18.dp))
        )
        Text(
            if (stableName.isBlank()) {
                "Acercá la carta hasta llenar el marco · tocá sobre el nombre para enfocar"
            } else {
                "Nombre estabilizado: $stableName"
            },
            color = Color.White,
            fontSize = 12.sp,
            fontWeight = FontWeight.SemiBold,
            modifier = Modifier.align(Alignment.TopCenter)
                .padding(12.dp)
                .background(Color(0xAA000000), RoundedCornerShape(10.dp))
                .padding(horizontal = 10.dp, vertical = 6.dp)
        )
        Button(
            onClick = ::takePhoto,
            enabled = !capturing,
            modifier = Modifier.align(Alignment.BottomCenter).padding(14.dp)
        ) {
            if (capturing) {
                CircularProgressIndicator(Modifier.size(20.dp), strokeWidth = 2.dp)
            } else {
                Icon(Icons.Default.PhotoCamera, null)
            }
            Spacer(Modifier.width(8.dp))
            Text(if (capturing) "PROCESANDO…" else "TOMAR FOTO", fontWeight = FontWeight.Bold)
        }
    }
}

'''

s = s[:start] + replacement + s[end:]
s = s.replace("V08 · Pokémon · USD / UYU", "V09 · Pokémon · USD / UYU", 1)
p.write_text(s)

gradle = Path("CartaValor_V03/app/build.gradle.kts")
g = gradle.read_text()
g = g.replace("versionCode = 8", "versionCode = 9", 1)
g = g.replace('versionName = "8.0"', 'versionName = "9.0"', 1)
gradle.write_text(g)
