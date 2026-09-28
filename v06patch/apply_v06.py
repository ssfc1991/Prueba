from pathlib import Path

p = Path("CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt")
s = p.read_text()

old = """            cameraSelector = CameraSelector.DEFAULT_BACK_CAMERA
            setEnabledUseCases(CameraController.IMAGE_ANALYSIS or CameraController.IMAGE_CAPTURE)"""
new = """            cameraSelector = CameraSelector.DEFAULT_BACK_CAMERA
            setTapToFocusEnabled(true)
            setPinchToZoomEnabled(true)
            setEnabledUseCases(CameraController.IMAGE_ANALYSIS or CameraController.IMAGE_CAPTURE)"""
if old not in s:
    raise SystemExit("No se encontró configuración CameraController")
s = s.replace(old, new, 1)

old = """        controller.bindToLifecycle(lifecycleOwner)
        onDispose {"""
new = """        controller.bindToLifecycle(lifecycleOwner)
        runCatching { controller.setZoomRatio(1.20f) }
        onDispose {"""
if old not in s:
    raise SystemExit("No se encontró bindToLifecycle")
s = s.replace(old, new, 1)

old = """.fillMaxWidth(0.78f)
                .aspectRatio(0.72f)
                .border(3.dp, Color(0xFFFFC928), RoundedCornerShape(16.dp))"""
new = """.fillMaxWidth(0.92f)
                .aspectRatio(0.72f)
                .border(4.dp, Color(0xFFFFC928), RoundedCornerShape(18.dp))"""
if old not in s:
    raise SystemExit("No se encontró marco de cámara")
s = s.replace(old, new, 1)

marker = "onClick = ::takePhoto"
idx = s.find(marker)
if idx < 0:
    raise SystemExit("No se encontró botón de captura")
button_start = s.rfind("        Button(", 0, idx)
if button_start < 0:
    raise SystemExit("No se encontró inicio del botón")

label = '''        Text(
            "Acercá la carta hasta llenar el marco · tocá sobre el nombre para enfocar",
            color = Color.White,
            fontSize = 12.sp,
            fontWeight = FontWeight.SemiBold,
            modifier = Modifier.align(Alignment.TopCenter)
                .padding(12.dp)
                .background(Color(0xAA000000), RoundedCornerShape(10.dp))
                .padding(horizontal = 10.dp, vertical = 6.dp)
        )
'''
s = s[:button_start] + label + s[button_start:]
s = s.replace("V05 · Pokémon · USD / UYU", "V06 · Pokémon · USD / UYU", 1)
p.write_text(s)

gradle = Path("CartaValor_V03/app/build.gradle.kts")
g = gradle.read_text()
g = g.replace("versionCode = 4", "versionCode = 6", 1)
g = g.replace('versionName = "4.0"', 'versionName = "6.0"', 1)
gradle.write_text(g)
