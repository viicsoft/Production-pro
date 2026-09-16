package com.vidikomcrew

import android.content.Intent
import androidx.core.content.ContextCompat
import com.facebook.react.bridge.Promise
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.bridge.ReactContextBaseJavaModule
import com.facebook.react.bridge.ReactMethod

class IntercomServiceModule(private val reactContext: ReactApplicationContext) :
    ReactContextBaseJavaModule(reactContext) {

    override fun getName(): String = "IntercomService"

    @ReactMethod
    fun startService(title: String, message: String, promise: Promise) {
        try {
            val intent = Intent(reactContext, IntercomForegroundService::class.java).apply {
                action = IntercomForegroundService.ACTION_START
                putExtra(IntercomForegroundService.EXTRA_TITLE, title)
                putExtra(IntercomForegroundService.EXTRA_MESSAGE, message)
            }
            ContextCompat.startForegroundService(reactContext, intent)
            promise.resolve(true)
        } catch (e: Exception) {
            promise.reject("START_SERVICE_ERROR", e.message, e)
        }
    }

    @ReactMethod
    fun updateNotification(title: String, message: String, promise: Promise) {
        try {
            val intent = Intent(reactContext, IntercomForegroundService::class.java).apply {
                action = IntercomForegroundService.ACTION_UPDATE
                putExtra(IntercomForegroundService.EXTRA_TITLE, title)
                putExtra(IntercomForegroundService.EXTRA_MESSAGE, message)
            }
            reactContext.startService(intent)
            promise.resolve(true)
        } catch (e: Exception) {
            promise.reject("UPDATE_NOTIFICATION_ERROR", e.message, e)
        }
    }

    @ReactMethod
    fun stopService(promise: Promise) {
        try {
            val intent = Intent(reactContext, IntercomForegroundService::class.java).apply {
                action = IntercomForegroundService.ACTION_STOP
            }
            reactContext.startService(intent)
            promise.resolve(true)
        } catch (e: Exception) {
            promise.reject("STOP_SERVICE_ERROR", e.message, e)
        }
    }

    @ReactMethod
    fun isRunning(promise: Promise) {
        try {
            promise.resolve(IntercomForegroundService.isRunning)
        } catch (e: Exception) {
            promise.reject("IS_RUNNING_ERROR", e.message, e)
        }
    }

    @ReactMethod
    fun isServiceRunning(promise: Promise) {
        // Defensive alias for isRunning
        isRunning(promise)
    }

    @ReactMethod
    fun setSpeakerphone(enabled: Boolean, promise: Promise) {
        try {
            val result = IntercomForegroundService.setSpeakerphoneState(reactContext, enabled)
            promise.resolve(result)
        } catch (e: Exception) {
            promise.reject("AUDIO_ERROR", e.message, e)
        }
    }
}
