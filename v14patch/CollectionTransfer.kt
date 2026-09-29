package uy.impulsa.cartavalor

import org.json.JSONArray
import org.json.JSONObject

data class CollectionImportResult(
    val items: List<CollectionItem>,
    val sourceVersion: Int,
    val sourceAppVersion: String?
)

object CollectionTransfer {
    private const val FORMAT = "CartaValorCollection"
    private const val FORMAT_VERSION = 1

    fun exportJson(items: List<CollectionItem>, appVersion: String): String {
        val array = JSONArray()
        items.forEach { array.put(encode(it)) }

        return JSONObject().apply {
            put("format", FORMAT)
            put("formatVersion", FORMAT_VERSION)
            put("appVersion", appVersion)
            put("exportedAt", System.currentTimeMillis())
            put("itemCount", items.size)
            put("items", array)
        }.toString(2)
    }

    fun importJson(raw: String): CollectionImportResult {
        val root = JSONObject(raw)
        val format = root.optString("format")
        require(format == FORMAT) { "El archivo no pertenece a CartaValor." }

        val formatVersion = root.optInt("formatVersion", -1)
        require(formatVersion in 1..FORMAT_VERSION) {
            "Versión de archivo no compatible: $formatVersion."
        }

        val array = root.optJSONArray("items")
            ?: throw IllegalArgumentException("El archivo no contiene una colección válida.")

        val items = buildList {
            for (i in 0 until array.length()) {
                val obj = array.optJSONObject(i) ?: continue
                decode(obj)?.let(::add)
            }
        }

        require(items.isNotEmpty() || array.length() == 0) {
            "No pude leer las cartas del archivo."
        }

        return CollectionImportResult(
            items = items,
            sourceVersion = formatVersion,
            sourceAppVersion = root.optString("appVersion").takeIf { it.isNotBlank() }
        )
    }

    fun merge(existing: List<CollectionItem>, incoming: List<CollectionItem>): List<CollectionItem> {
        val merged = linkedMapOf<String, CollectionItem>()
        existing.forEach { merged[it.key] = it }

        incoming.forEach { item ->
            val current = merged[item.key]
            merged[item.key] = if (current == null) {
                item
            } else {
                current.copy(
                    quantity = current.quantity + item.quantity,
                    savedAt = minOf(current.savedAt, item.savedAt)
                )
            }
        }

        return merged.values.toList()
    }

    private fun encode(item: CollectionItem) = JSONObject().apply {
        put("cardId", item.card.id)
        put("name", item.card.name)
        put("number", item.card.number)
        put("setName", item.card.setName)
        put("rarity", item.card.rarity ?: JSONObject.NULL)
        put("imageUrl", item.card.imageUrl ?: JSONObject.NULL)
        put("variant", item.variant)
        put("variantLabel", item.variantLabel)
        put("unitUsd", item.unitUsd)
        put("uyuRate", item.uyuRate ?: JSONObject.NULL)
        put("quantity", item.quantity)
        put("condition", item.condition.code)
        put("priceUpdated", item.priceUpdated ?: JSONObject.NULL)
        put("savedAt", item.savedAt)
    }

    private fun decode(obj: JSONObject): CollectionItem? {
        val id = obj.optString("cardId")
        val variant = obj.optString("variant")
        val unitUsd = obj.optDouble("unitUsd", Double.NaN)
        if (id.isBlank() || variant.isBlank() || !unitUsd.isFinite() || unitUsd < 0) return null

        val card = CardCandidate(
            id = id,
            name = obj.optString("name", "Carta"),
            number = obj.optString("number"),
            setName = obj.optString("setName"),
            rarity = nullableString(obj, "rarity"),
            imageUrl = nullableString(obj, "imageUrl")
        )

        val rate = obj.optDouble("uyuRate", Double.NaN).takeIf { it.isFinite() && it > 0 }

        return CollectionItem(
            card = card,
            variant = variant,
            variantLabel = obj.optString("variantLabel", variant),
            unitUsd = unitUsd,
            uyuRate = rate,
            quantity = obj.optInt("quantity", 1).coerceIn(1, 9999),
            condition = CardCondition.fromCode(nullableString(obj, "condition")),
            priceUpdated = nullableString(obj, "priceUpdated"),
            savedAt = obj.optLong("savedAt", System.currentTimeMillis())
        )
    }

    private fun nullableString(obj: JSONObject, key: String): String? {
        if (!obj.has(key) || obj.isNull(key)) return null
        return obj.optString(key).takeIf { it.isNotBlank() }
    }
}
