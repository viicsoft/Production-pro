package com.vidikomcrew

import com.facebook.react.bridge.*
import com.google.mlkit.vision.barcode.common.Barcode
import com.google.mlkit.vision.codescanner.GmsBarcodeScannerOptions
import com.google.mlkit.vision.codescanner.GmsBarcodeScanning

class QrScannerModule(private val reactContext: ReactApplicationContext) :
    ReactContextBaseJavaModule(reactContext) {

    companion object {
        const val NAME = "QrScanner"
    }

    override fun getName(): String = NAME

    @ReactMethod
    fun scanQrCode(promise: Promise) {
        val activity = reactContext.currentActivity
        if (activity == null) {
            promise.reject("NO_ACTIVITY", "Current activity is null")
            return
        }

        try {
            val options = GmsBarcodeScannerOptions.Builder()
                .setBarcodeFormats(Barcode.FORMAT_QR_CODE)
                .enableAutoZoom()
                .build()

            val scanner = GmsBarcodeScanning.getClient(activity, options)
            scanner.startScan()
                .addOnSuccessListener { barcode ->
                    val rawValue = barcode.rawValue ?: barcode.displayValue
                    if (!rawValue.isNullOrEmpty()) {
                        promise.resolve(rawValue)
                    } else {
                        promise.resolve(null)
                    }
                }
                .addOnCanceledListener {
                    promise.resolve(null)
                }
                .addOnFailureListener { e ->
                    promise.reject("SCAN_FAILED", e.message ?: "Failed to scan QR code", e)
                }
        } catch (e: Exception) {
            promise.reject("SCAN_ERROR", e.message ?: "Could not launch scanner", e)
        }
    }
}
