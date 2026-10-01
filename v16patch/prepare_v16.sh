#!/usr/bin/env bash
set -euo pipefail

cat \
  build-input/parts/part00.bin \
  build-input/parts/part01.bin \
  build-input/parts/part02.bin \
  build-input/parts/part03.bin \
  build-input/parts/part04.bin \
  build-input/parts/part05.bin \
  build-input/parts/part06.bin \
  build-input/parts/part07.bin \
  build-input/parts/part08.bin \
  build-input/parts/part09.bin \
  build-input/parts/part10.bin \
  > build-input/CartaValor_V03_source.zip

unzip -t build-input/CartaValor_V03_source.zip
rm -rf CartaValor_V03
unzip -q build-input/CartaValor_V03_source.zip -d .

cd CartaValor_V03
patch -p1 < ../v04patch/v04.patch
sed -i '/import androidx.compose.foundation.layout.weight/d' app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt
cp ../v04patch/VisualSimilarityMatcher.kt app/src/main/java/uy/impulsa/cartavalor/VisualSimilarityMatcher.kt
cd ..

cp v05patch/OcrAnalyzer.kt CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/OcrAnalyzer.kt
cp v05patch/CardRepository.kt CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/CardRepository.kt
sed -i 's/val found = repo.search(name, n)/val found = repo.searchWithFallback(name, n, guess.rawText)/' CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt
sed -i 's/V04 · Pokémon · USD \/ UYU/V05 · Pokémon · USD \/ UYU/' CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt
sed -i 's/No encontré candidatos para comparar. Probá corregir el nombre o número y volver a buscar./No pude identificar la carta automáticamente. Probá otra foto con menos reflejo o escribí el nombre si lo conocés./' CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt

cp v06patch/PhotoOcrProcessor.kt CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/PhotoOcrProcessor.kt
python3 v06patch/apply_v06.py

cp v07patch/PhotoOcrProcessor.kt CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/PhotoOcrProcessor.kt
sed -i 's/V06 · Pokémon · USD \/ UYU/V07 · Pokémon · USD \/ UYU/' CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt
sed -i 's/versionCode = 6/versionCode = 7/' CartaValor_V03/app/build.gradle.kts
sed -i 's/versionName = "6.0"/versionName = "7.0"/' CartaValor_V03/app/build.gradle.kts

cp v08patch/PokemonNameCorrector.kt CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/PokemonNameCorrector.kt
cp v08patch/CardRepository.kt CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/CardRepository.kt
sed -i 's/V07 · Pokémon · USD \/ UYU/V08 · Pokémon · USD \/ UYU/' CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt
sed -i 's/versionCode = 7/versionCode = 8/' CartaValor_V03/app/build.gradle.kts
sed -i 's/versionName = "7.0"/versionName = "8.0"/' CartaValor_V03/app/build.gradle.kts

python3 v09patch/apply_v09.py
python3 v10patch/apply_v10.py
python3 v11patch/apply_v11.py
python3 v12patch/apply_v12.py
python3 v13patch/apply_v13.py

cat v14patch/iconpart_00.b64 v14patch/iconpart_01.b64 v14patch/iconpart_02.b64 v14patch/iconpart_03.b64 v14patch/iconpart_04.b64 | base64 -d > v14patch/ic_launcher.webp
echo "c22ea5ee3ada735d3776054ac73031b18af95d3797c44e47e9d6c2bbf24795b5  v14patch/ic_launcher.webp" | sha256sum -c -
cp v14patch/CollectionTransfer.kt CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/CollectionTransfer.kt
python3 v14patch/apply_v14.py
python3 v15patch/apply_v15.py
python3 v16patch/apply_v16.py

grep -E 'versionCode|versionName' CartaValor_V03/app/build.gradle.kts
grep -n 'V16 · Pokémon' CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt
grep -n 'No estoy seguro' CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt
grep -n 'NO significa que la carta de tu foto' CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt
grep -n 'AGREGAR ESTA VARIANTE A MI COLECCIÓN' CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt
grep -n 'Exportar colección' CartaValor_V03/app/src/main/java/uy/impulsa/cartavalor/MainActivity.kt
test -s CartaValor_V03/app/src/main/res/mipmap-anydpi-v26/ic_launcher.xml
