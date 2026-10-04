#!/usr/bin/env bash
set -euo pipefail

bash v16patch/prepare_v16.sh
python3 v17patch/apply_v17.py

grep -E 'versionCode|versionName' CartaValor_V03/app/build.gradle.kts
grep -n 'V17 · Pokémon' CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt
grep -n 'Pokémon TCG API (respaldo)' CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/CardRepository.kt
grep -n 'Fuente: ${p.sourceLabel}' CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt
grep -n 'fetchPokemonTcgFallbackPrices' CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/CardRepository.kt
