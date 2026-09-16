package com.vidikomcrew

import android.speech.tts.TextToSpeech
import android.speech.tts.UtteranceProgressListener
import com.facebook.react.bridge.Promise
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.bridge.ReactContextBaseJavaModule
import com.facebook.react.bridge.ReactMethod
import java.util.Locale

class DirectorVoiceModule(private val reactContext: ReactApplicationContext) :
    ReactContextBaseJavaModule(reactContext), TextToSpeech.OnInitListener {

    private var tts: TextToSpeech? = null
    private var isInitialized = false
    private var pendingSpeech: String? = null

    init {
        try {
            tts = TextToSpeech(reactContext, this)
        } catch (e: Exception) {
            e.printStackTrace()
        }
    }

    override fun getName(): String = "DirectorVoice"

    override fun onInit(status: Int) {
        if (status == TextToSpeech.SUCCESS) {
            tts?.language = Locale.US
            tts?.setPitch(0.95f) // Crisp, authoritative broadcast director pitch
            tts?.setSpeechRate(1.08f) // Fast, professional broadcast cadence
            isInitialized = true

            // If a speech was requested before init finished, execute it now
            pendingSpeech?.let { text ->
                speakInternal(text)
                pendingSpeech = null
            }
        }
    }

    private fun speakInternal(text: String) {
        val utteranceId = "director_cue_${System.currentTimeMillis()}"
        tts?.speak(text, TextToSpeech.QUEUE_FLUSH, null, utteranceId)
    }

    @ReactMethod
    fun speak(text: String, promise: Promise) {
        try {
            if (!isInitialized || tts == null) {
                pendingSpeech = text
                if (tts == null) {
                    tts = TextToSpeech(reactContext, this)
                }
                promise.resolve(true)
                return
            }
            speakInternal(text)
            promise.resolve(true)
        } catch (e: Exception) {
            promise.reject("TTS_SPEAK_ERROR", e.message, e)
        }
    }

    @ReactMethod
    fun stop(promise: Promise) {
        try {
            tts?.stop()
            pendingSpeech = null
            promise.resolve(true)
        } catch (e: Exception) {
            promise.reject("TTS_STOP_ERROR", e.message, e)
        }
    }

    @ReactMethod
    fun isAvailable(promise: Promise) {
        promise.resolve(isInitialized)
    }
}
