package com.vidikomcrew

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.content.Context
import android.content.Intent
import android.content.pm.ServiceInfo
import android.media.AudioAttributes
import android.media.AudioDeviceInfo
import android.media.AudioFocusRequest
import android.media.AudioManager
import android.net.wifi.WifiManager
import android.os.Build
import android.os.IBinder
import android.os.PowerManager
import androidx.core.app.NotificationCompat

class IntercomForegroundService : Service() {

    private var wakeLock: PowerManager.WakeLock? = null
    private var wifiLock: WifiManager.WifiLock? = null
    private var audioManager: AudioManager? = null
    private var audioFocusRequest: AudioFocusRequest? = null

    companion object {
        const val CHANNEL_ID = "vidikom_intercom_channel"
        const val CHANNEL_NAME = "Vidikom Intercom Comms"
        const val NOTIFICATION_ID = 9001

        const val ACTION_START = "ACTION_START"
        const val ACTION_STOP = "ACTION_STOP"
        const val ACTION_UPDATE = "ACTION_UPDATE"

        const val EXTRA_TITLE = "EXTRA_TITLE"
        const val EXTRA_MESSAGE = "EXTRA_MESSAGE"

        private const val WAKELOCK_TAG = "VidikomCrew::IntercomWakeLock"
        private const val WIFILOCK_TAG = "VidikomCrew::IntercomWifiLock"
        private const val WAKELOCK_TIMEOUT_MS = 24 * 60 * 60 * 1000L // 24-hour safety limit

        @Volatile
        var isRunning: Boolean = false
            private set

        fun setSpeakerphoneState(context: Context, enabled: Boolean): Boolean {
            val am = context.getSystemService(Context.AUDIO_SERVICE) as? AudioManager ?: return false
            return try {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                    if (enabled) {
                        val speaker = am.availableCommunicationDevices.firstOrNull {
                            it.type == AudioDeviceInfo.TYPE_BUILTIN_SPEAKER
                        }
                        if (speaker != null) {
                            am.setCommunicationDevice(speaker)
                        } else {
                            @Suppress("DEPRECATION")
                            am.isSpeakerphoneOn = true
                        }
                    } else {
                        am.clearCommunicationDevice()
                        @Suppress("DEPRECATION")
                        am.isSpeakerphoneOn = false
                    }
                } else {
                    @Suppress("DEPRECATION")
                    am.isSpeakerphoneOn = enabled
                }
                isSpeakerphoneStateOn(context)
            } catch (e: Exception) {
                false
            }
        }

        fun isSpeakerphoneStateOn(context: Context): Boolean {
            val am = context.getSystemService(Context.AUDIO_SERVICE) as? AudioManager ?: return false
            return if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                am.communicationDevice?.type == AudioDeviceInfo.TYPE_BUILTIN_SPEAKER || am.isSpeakerphoneOn
            } else {
                @Suppress("DEPRECATION")
                am.isSpeakerphoneOn
            }
        }
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
        acquireLocks()
        setupAudio()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val action = intent?.action ?: ACTION_START

        when (action) {
            ACTION_START -> {
                val title = intent?.getStringExtra(EXTRA_TITLE) ?: "Vidikom Intercom Active"
                val message = intent?.getStringExtra(EXTRA_MESSAGE) ?: "Comms connected • Screen lock safe"
                startForegroundWithNotification(title, message)
                isRunning = true
            }
            ACTION_UPDATE -> {
                val title = intent?.getStringExtra(EXTRA_TITLE) ?: "Vidikom Intercom Active"
                val message = intent?.getStringExtra(EXTRA_MESSAGE) ?: "Comms connected"
                if (isRunning) {
                    val notification = buildNotification(title, message)
                    val manager = getSystemService(Context.NOTIFICATION_SERVICE) as? NotificationManager
                    manager?.notify(NOTIFICATION_ID, notification)
                } else {
                    startForegroundWithNotification(title, message)
                    isRunning = true
                }
            }
            ACTION_STOP -> {
                stopForegroundAndSelf()
            }
        }

        return START_STICKY
    }

    private fun startForegroundWithNotification(title: String, message: String) {
        val notification = buildNotification(title, message)

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.UPSIDE_DOWN_CAKE) { // API 34+
            startForeground(
                NOTIFICATION_ID,
                notification,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE or ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK
            )
        } else if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) { // API 30+
            startForeground(
                NOTIFICATION_ID,
                notification,
                ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE
            )
        } else {
            startForeground(NOTIFICATION_ID, notification)
        }
    }

    private fun buildNotification(title: String, message: String): Notification {
        val launchIntent = packageManager.getLaunchIntentForPackage(packageName)?.apply {
            flags = Intent.FLAG_ACTIVITY_SINGLE_TOP or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }

        val pendingIntent = PendingIntent.getActivity(
            this,
            0,
            launchIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        return NotificationCompat.Builder(this, CHANNEL_ID)
            .setContentTitle(title)
            .setContentText(message)
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentIntent(pendingIntent)
            .setOngoing(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .setCategory(NotificationCompat.CATEGORY_SERVICE)
            .setAutoCancel(false)
            .build()
    }

    private fun createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            val channel = NotificationChannel(
                CHANNEL_ID,
                CHANNEL_NAME,
                NotificationManager.IMPORTANCE_LOW
            ).apply {
                description = "Background intercom audio and microphone service"
                setShowBadge(false)
            }
            val manager = getSystemService(NotificationManager::class.java)
            manager?.createNotificationChannel(channel)
        }
    }

    private fun acquireLocks() {
        try {
            if (wakeLock == null) {
                val powerManager = getSystemService(Context.POWER_SERVICE) as? PowerManager
                wakeLock = powerManager?.newWakeLock(
                    PowerManager.PARTIAL_WAKE_LOCK,
                    WAKELOCK_TAG
                )?.apply {
                    setReferenceCounted(false)
                }
            }
            wakeLock?.let {
                if (!it.isHeld) {
                    it.acquire(WAKELOCK_TIMEOUT_MS)
                }
            }

            if (wifiLock == null) {
                val wifiManager = applicationContext.getSystemService(Context.WIFI_SERVICE) as? WifiManager
                wifiLock = wifiManager?.createWifiLock(
                    WifiManager.WIFI_MODE_FULL_HIGH_PERF,
                    WIFILOCK_TAG
                )?.apply {
                    setReferenceCounted(false)
                }
            }
            wifiLock?.let {
                if (!it.isHeld) {
                    it.acquire()
                }
            }
        } catch (e: Exception) {
            // Log or handle gracefully
        }
    }

    private fun setupAudio() {
        try {
            audioManager = getSystemService(Context.AUDIO_SERVICE) as? AudioManager
            audioManager?.mode = AudioManager.MODE_IN_COMMUNICATION
            setSpeakerphoneState(this, true)

            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                val focusRequest = AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN)
                    .setAudioAttributes(
                        AudioAttributes.Builder()
                            .setUsage(AudioAttributes.USAGE_VOICE_COMMUNICATION)
                            .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH)
                            .build()
                    )
                    .setAcceptsDelayedFocusGain(true)
                    .setOnAudioFocusChangeListener { /* Audio focus change listener */ }
                    .build()
                audioFocusRequest = focusRequest
                audioManager?.requestAudioFocus(focusRequest)
            } else {
                @Suppress("DEPRECATION")
                audioManager?.requestAudioFocus(
                    null,
                    AudioManager.STREAM_VOICE_CALL,
                    AudioManager.AUDIOFOCUS_GAIN
                )
            }
        } catch (e: Exception) {
            // Handled safely
        }
    }

    private fun stopForegroundAndSelf() {
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.N) {
                stopForeground(STOP_FOREGROUND_REMOVE)
            } else {
                @Suppress("DEPRECATION")
                stopForeground(true)
            }
        } catch (e: Exception) {
            // Ignore
        }
        stopSelf()
    }

    override fun onDestroy() {
        super.onDestroy()
        isRunning = false

        // Release WakeLock
        try {
            wakeLock?.let {
                if (it.isHeld) {
                    it.release()
                }
            }
        } catch (e: Exception) {
            // Ignore
        }
        wakeLock = null

        // Release WifiLock
        try {
            wifiLock?.let {
                if (it.isHeld) {
                    it.release()
                }
            }
        } catch (e: Exception) {
            // Ignore
        }
        wifiLock = null

        // Abandon AudioFocus and restore normal mode
        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                audioFocusRequest?.let {
                    audioManager?.abandonAudioFocusRequest(it)
                }
                audioFocusRequest = null
            } else {
                @Suppress("DEPRECATION")
                audioManager?.abandonAudioFocus(null)
            }
            audioManager?.mode = AudioManager.MODE_NORMAL
        } catch (e: Exception) {
            // Ignore
        }
    }
}
