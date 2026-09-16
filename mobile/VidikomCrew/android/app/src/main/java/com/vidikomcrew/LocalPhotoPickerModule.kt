package com.vidikomcrew

import android.app.Activity
import android.content.Intent
import android.net.Uri
import android.provider.MediaStore
import androidx.core.content.FileProvider
import com.facebook.react.bridge.*
import java.io.File
import java.io.FileOutputStream

class LocalPhotoPickerModule(private val reactContext: ReactApplicationContext) :
    ReactContextBaseJavaModule(reactContext), ActivityEventListener {

    companion object {
        const val NAME = "LocalPhotoPicker"
        private const val REQUEST_PICK_IMAGE = 4101
        private const val REQUEST_CAPTURE_IMAGE = 4102
    }

    private var pickPromise: Promise? = null
    private var currentCaptureFile: File? = null

    init {
        reactContext.addActivityEventListener(this)
    }

    override fun getName(): String = NAME

    @ReactMethod
    fun pickImageFromStorage(promise: Promise) {
        val activity = reactContext.currentActivity
        if (activity == null) {
            promise.reject("NO_ACTIVITY", "Activity doesn't exist")
            return
        }

        if (pickPromise != null) {
            promise.reject("PENDING_TASK", "A photo selection is already in progress")
            return
        }

        pickPromise = promise

        try {
            val intent = Intent(Intent.ACTION_GET_CONTENT).apply {
                type = "image/*"
                addCategory(Intent.CATEGORY_OPENABLE)
                putExtra(
                    Intent.EXTRA_MIME_TYPES,
                    arrayOf("image/jpeg", "image/png", "image/webp", "image/heic", "image/jpg")
                )
            }
            val chooser = Intent.createChooser(intent, "Select Photo from Storage")
            activity.startActivityForResult(chooser, REQUEST_PICK_IMAGE)
        } catch (e: Exception) {
            pickPromise = null
            promise.reject("PICK_ERROR", "Failed to open photo picker: ${e.message}", e)
        }
    }

    @ReactMethod
    fun capturePhotoWithCamera(promise: Promise) {
        val activity = reactContext.currentActivity
        if (activity == null) {
            promise.reject("NO_ACTIVITY", "Activity doesn't exist")
            return
        }

        if (pickPromise != null) {
            promise.reject("PENDING_TASK", "A photo operation is already in progress")
            return
        }

        pickPromise = promise

        try {
            val captureDir = File(reactContext.cacheDir, "camera_photos").apply { mkdirs() }
            val photoFile = File(captureDir, "camera_${System.currentTimeMillis()}.jpg")
            currentCaptureFile = photoFile

            val authority = "${reactContext.packageName}.fileprovider"
            val photoUri = FileProvider.getUriForFile(reactContext, authority, photoFile)

            val intent = Intent(MediaStore.ACTION_IMAGE_CAPTURE).apply {
                putExtra(MediaStore.EXTRA_OUTPUT, photoUri)
                addFlags(Intent.FLAG_GRANT_WRITE_URI_PERMISSION or Intent.FLAG_GRANT_READ_URI_PERMISSION)
            }
            activity.startActivityForResult(intent, REQUEST_CAPTURE_IMAGE)
        } catch (e: Exception) {
            pickPromise = null
            currentCaptureFile = null
            promise.reject("CAMERA_ERROR", "Failed to launch camera: ${e.message}", e)
        }
    }

    override fun onActivityResult(activity: Activity, requestCode: Int, resultCode: Int, data: Intent?) {
        if (requestCode == REQUEST_PICK_IMAGE) {
            handlePickResult(resultCode, data)
        } else if (requestCode == REQUEST_CAPTURE_IMAGE) {
            handleCaptureResult(resultCode)
        }
    }

    private fun handlePickResult(resultCode: Int, data: Intent?) {
        val promise = pickPromise ?: return
        pickPromise = null

        if (resultCode != Activity.RESULT_OK || data?.data == null) {
            promise.resolve(null)
            return
        }

        val sourceUri: Uri = data.data!!
        try {
            val destDir = File(reactContext.cacheDir, "picked_photos").apply { mkdirs() }
            val fileName = "venue_${System.currentTimeMillis()}.jpg"
            val destFile = File(destDir, fileName)

            reactContext.contentResolver.openInputStream(sourceUri)?.use { input ->
                FileOutputStream(destFile).use { output ->
                    input.copyTo(output)
                }
            }

            val result = Arguments.createMap().apply {
                putString("uri", "file://${destFile.absolutePath}")
                putString("path", destFile.absolutePath)
                putString("fileName", fileName)
                putDouble("fileSize", destFile.length().toDouble())
                putString("type", reactContext.contentResolver.getType(sourceUri) ?: "image/jpeg")
            }
            promise.resolve(result)
        } catch (e: Exception) {
            promise.reject("SAVE_ERROR", "Failed to cache selected photo: ${e.message}", e)
        }
    }

    private fun handleCaptureResult(resultCode: Int) {
        val promise = pickPromise ?: return
        pickPromise = null
        val file = currentCaptureFile
        currentCaptureFile = null

        if (resultCode != Activity.RESULT_OK || file == null || !file.exists() || file.length() == 0L) {
            if (file != null && file.exists() && file.length() == 0L) {
                file.delete()
            }
            promise.resolve(null)
            return
        }

        val result = Arguments.createMap().apply {
            putString("uri", "file://${file.absolutePath}")
            putString("path", file.absolutePath)
            putString("fileName", file.name)
            putDouble("fileSize", file.length().toDouble())
            putString("type", "image/jpeg")
        }
        promise.resolve(result)
    }

    override fun onNewIntent(intent: Intent) {
        // No-op
    }
}
