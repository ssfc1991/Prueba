from pathlib import Path

p = Path("CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/CardRepository.kt")
s = p.read_text()

old = '''            if (name.isNotBlank()) {
                searchLanguage(lang, name, "").forEach { combined.putIfAbsent(it.id, it) }
            }'''
new = '''            if (name.isNotBlank()) {
                for (variant in queryVariants(name)) {
                    searchLanguage(lang, variant, "").forEach { combined.putIfAbsent(it.id, it) }
                }
            }'''
if old not in s:
    raise SystemExit("No se encontró búsqueda principal por nombre")
s = s.replace(old, new, 1)

old = '''            for (hint in hints) {
                for (lang in listOf("es", "en")) {
                    runCatching { searchLanguage(lang, hint, "") }
                        .getOrDefault(emptyList())
                        .forEach { combined.putIfAbsent(it.id, it) }
                }
                if (combined.size >= 40) break
            }'''
new = '''            for (hint in hints) {
                for (variant in queryVariants(hint)) {
                    for (lang in listOf("es", "en")) {
                        runCatching { searchLanguage(lang, variant, "") }
                            .getOrDefault(emptyList())
                            .forEach { combined.putIfAbsent(it.id, it) }
                    }
                }
                if (combined.size >= 40) break
            }'''
if old not in s:
    raise SystemExit("No se encontró fallback por hints")
s = s.replace(old, new, 1)

old = '''        for (corrected in correctedNames) {
            for (lang in listOf("es", "en")) {
                runCatching { searchLanguage(lang, corrected, "") }
                    .getOrDefault(emptyList())
                    .forEach { combined.putIfAbsent(it.id, it) }
            }
            if (combined.size >= 50) break
        }'''
new = '''        for (corrected in correctedNames) {
            for (variant in queryVariants(corrected)) {
                for (lang in listOf("es", "en")) {
                    runCatching { searchLanguage(lang, variant, "") }
                        .getOrDefault(emptyList())
                        .forEach { combined.putIfAbsent(it.id, it) }
                }
            }
            if (combined.size >= 50) break
        }'''
if old not in s:
    raise SystemExit("No se encontró búsqueda de nombres corregidos")
s = s.replace(old, new, 1)

anchor = '''    private fun similarity(candidate: String, hint: String): Int {'''
helper = r'''    private fun queryVariants(raw: String): List<String> {
        val trimmed = raw.trim().replace(Regex("\\s+"), " ")
        if (trimmed.isBlank()) return emptyList()

        val fixedSuffix = trimmed.replace(
            Regex("(?i)([A-Za-zÁÉÍÓÚÜÑáéíóúüñ])((?:VMAX|VSTAR|EX|GX|V))$"),
            "$1 $2"
        )
        val baseName = fixedSuffix.replace(
            Regex("(?i)\\s+(?:VMAX|VSTAR|EX|GX|V)$"),
            ""
        ).trim()

        return linkedSetOf<String>().apply {
            add(trimmed)
            add(fixedSuffix)
            if (baseName.length >= 3) add(baseName)
        }.filter { it.isNotBlank() }
    }

'''
if anchor not in s:
    raise SystemExit("No se encontró ancla similarity")
s = s.replace(anchor, helper + anchor, 1)

p.write_text(s)

main = Path("CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt")
m = main.read_text()
m = m.replace("V09 · Pokémon · USD / UYU", "V10 · Pokémon · USD / UYU", 1)
main.write_text(m)

gradle = Path("CartaValor_V03/app/build.gradle.kts")
g = gradle.read_text()
g = g.replace("versionCode = 9", "versionCode = 10", 1)
g = g.replace('versionName = "9.0"', 'versionName = "10.0"', 1)
gradle.write_text(g)
