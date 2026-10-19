package com.vidikomcrew

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.bluetooth.BluetoothA2dp
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothHeadset
import android.bluetooth.BluetoothProfile
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.ServiceInfo
import android.media.AudioAttributes
import android.media.AudioDeviceCallback
import android.media.AudioDeviceInfo
import android.media.AudioFocusRequest
import android.media.AudioManager
import android.net.wifi.WifiManager
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.os.PowerManager
import android.util.Log
import androidx.core.app.NotificationCompat

class IntercomForegroundService : Service() {

    private var wakeLock: PowerManager.WakeLock? = null
    private var wifiLock: WifiManager.WifiLock? = null
    private var audioManager: AudioManager? = null
    private var audioFocusRequest: AudioFocusRequest? = null
    private val mainHandler = Handler(Looper.getMainLooper())
    private var isReceiverRegistered = false
    private var lastScoRetryTimestamp = 0L

    private val audioDeviceCallback = object : AudioDeviceCallback() {
        override fun onAudioDevicesAdded(addedDevices: Array<out AudioDeviceInfo>?) {
            super.onAudioDevicesAdded(addedDevices)
            scheduleRouteRefresh("onAudioDevicesAdded")
        }

        override fun onAudioDevicesRemoved(removedDevices: Array<out AudioDeviceInfo>?) {
            super.onAudioDevicesRemoved(removedDevices)
            scheduleRouteRefresh("onAudioDevicesRemoved")
        }
    }

    private val audioRouteReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context?, intent: Intent?) {
            val action = intent?.action ?: return
            when (action) {
                BluetoothHeadset.ACTION_CONNECTION_STATE_CHANGED,
                BluetoothA2dp.ACTION_CONNECTION_STATE_CHANGED,
                Intent.ACTION_HEADSET_PLUG,
                AudioManager.ACTION_AUDIO_BECOMING_NOISY -> {
                    scheduleRouteRefresh(action)
                }
                BluetoothHeadset.ACTION_AUDIO_STATE_CHANGED,
                AudioManager.ACTION_SCO_AUDIO_STATE_UPDATED -> {
                    val scoState = intent.getIntExtra(
                        AudioManager.EXTRA_SCO_AUDIO_STATE,
                        AudioManager.SCO_AUDIO_STATE_ERROR
                    )
                    if (scoState == AudioManager.SCO_AUDIO_STATE_CONNECTED) {
                        // Ensure speakerphone remains off once SCO is active
                        val am = audioManager
                        if (am != null) {
                            @Suppress("DEPRECATION")
                            am.isSpeakerphoneOn = false
                        }
                    } else if (scoState == AudioManager.SCO_AUDIO_STATE_DISCONNECTED) {
                        val now = System.currentTimeMillis()
                        if (now - lastScoRetryTimestamp > 3000L) {
                            lastScoRetryTimestamp = now
                            scheduleRouteRefresh("SCO_DISCONNECTED_CHECK")
                        }
                    }
                }
            }
        }
    }

    companion object {
        private const val TAG = "VidikomAudioRouter"
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
        private const val WAKELOCK_TIMEOUT_MS = 96 * 60 * 60 * 1000L // 96-hour continuous production limit (>72h)
        private const val STREAM_BLUETOOTH_SCO = 6

        @Volatile
        var isRunning: Boolean = false
            private set

        private fun isBluetoothCommunicationType(type: Int): Boolean {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                if (type == AudioDeviceInfo.TYPE_BLE_HEADSET || type == AudioDeviceInfo.TYPE_BLE_SPEAKER) {
                    return true
                }
            }
            return type == AudioDeviceInfo.TYPE_BLUETOOTH_SCO ||
                type == AudioDeviceInfo.TYPE_HEARING_AID
        }

        private fun isAnyBluetoothDeviceType(type: Int): Boolean {
            return isBluetoothCommunicationType(type) || type == AudioDeviceInfo.TYPE_BLUETOOTH_A2DP
        }

        private fun isWiredHeadsetType(type: Int): Boolean {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O && type == AudioDeviceInfo.TYPE_USB_HEADSET) {
                return true
            }
            return type == AudioDeviceInfo.TYPE_WIRED_HEADSET ||
                type == AudioDeviceInfo.TYPE_WIRED_HEADPHONES ||
                type == AudioDeviceInfo.TYPE_USB_DEVICE
        }

        private fun hasBluetoothProfileConnected(): Boolean {
            return try {
                val adapter = BluetoothAdapter.getDefaultAdapter() ?: return false
                if (!adapter.isEnabled) return false
                val hfp = adapter.getProfileConnectionState(BluetoothProfile.HEADSET) == BluetoothProfile.STATE_CONNECTED
                val a2dp = adapter.getProfileConnectionState(BluetoothProfile.A2DP) == BluetoothProfile.STATE_CONNECTED
                hfp || a2dp
            } catch (_: SecurityException) {
                false
            } catch (_: Exception) {
                false
            }
        }

        /**
         * Smart Audio Router for broadcast crew intercom:
         * Priority 1: Bluetooth Headset / Earbuds (BLE Headset, Bluetooth SCO/HFP, Hearing Aid, A2DP)
         * Priority 2: Wired / USB Headset
         * Priority 3: Built-in Speakerphone (unless forceEarpiece == true)
         */
        fun applySmartAudioRoute(context: Context, forceEarpiece: Boolean = false): Boolean {
            val am = context.getSystemService(Context.AUDIO_SERVICE) as? AudioManager ?: return false
            return try {
                if (am.mode != AudioManager.MODE_IN_COMMUNICATION) {
                    am.mode = AudioManager.MODE_IN_COMMUNICATION
                }
                am.isMicrophoneMute = false

                val allDevices = am.getDevices(AudioManager.GET_DEVICES_ALL)
                val commDevices = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                    am.availableCommunicationDevices
                } else {
                    emptyList()
                }

                val hasBtDevice = allDevices.any { isAnyBluetoothDeviceType(it.type) } ||
                    commDevices.any { isBluetoothCommunicationType(it.type) } ||
                    hasBluetoothProfileConnected()

                val hasWiredHeadset = allDevices.any { isWiredHeadsetType(it.type) } ||
                    commDevices.any { isWiredHeadsetType(it.type) }

                if (hasBtDevice) {
                    // 1. Release built-in speakerphone immediately so audio never leaks to phone speaker
                    @Suppress("DEPRECATION")
                    am.isSpeakerphoneOn = false

                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                        val btCommDevice = commDevices.firstOrNull { it.type == AudioDeviceInfo.TYPE_BLE_HEADSET }
                            ?: commDevices.firstOrNull { it.type == AudioDeviceInfo.TYPE_BLUETOOTH_SCO }
                            ?: commDevices.firstOrNull { it.type == AudioDeviceInfo.TYPE_HEARING_AID }
                            ?: commDevices.firstOrNull { it.type == AudioDeviceInfo.TYPE_BLE_SPEAKER }

                        if (btCommDevice != null) {
                            if (am.communicationDevice?.id != btCommDevice.id) {
                                am.setCommunicationDevice(btCommDevice)
                                Log.i(TAG, "Routed intercom communication device to Bluetooth (type=${btCommDevice.type})")
                            }
                        } else if (am.communicationDevice?.type == AudioDeviceInfo.TYPE_BUILTIN_SPEAKER) {
                            // Clear speaker lock immediately while HFP finishes its connection handshake
                            am.clearCommunicationDevice()
                            Log.i(TAG, "Cleared speaker communication lock for incoming Bluetooth headset")
                        }
                    }

                    // Start Bluetooth SCO for HFP voice channel
                    try {
                        @Suppress("DEPRECATION")
                        if (!am.isBluetoothScoOn) {
                            am.startBluetoothSco()
                            am.isBluetoothScoOn = true
                        }
                    } catch (e: Exception) {
                        Log.w(TAG, "startBluetoothSco warning: ${e.message}")
                    }

                    // Ensure Bluetooth SCO & Voice Call volumes are clearly audible
                    ensureAudibleStreamVolume(am, AudioManager.STREAM_VOICE_CALL)
                    ensureAudibleStreamVolume(am, STREAM_BLUETOOTH_SCO)
                    true
                } else if (hasWiredHeadset) {
                    // 2. Wired / USB headset connected
                    stopBluetoothScoSafely(am)
                    @Suppress("DEPRECATION")
                    am.isSpeakerphoneOn = false

                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                        val wiredCommDevice = commDevices.firstOrNull { isWiredHeadsetType(it.type) }
                        if (wiredCommDevice != null) {
                            if (am.communicationDevice?.id != wiredCommDevice.id) {
                                am.setCommunicationDevice(wiredCommDevice)
                                Log.i(TAG, "Routed intercom communication device to Wired/USB headset (type=${wiredCommDevice.type})")
                            }
                        } else if (am.communicationDevice?.type == AudioDeviceInfo.TYPE_BUILTIN_SPEAKER) {
                            am.clearCommunicationDevice()
                        }
                    }
                    ensureAudibleStreamVolume(am, AudioManager.STREAM_VOICE_CALL)
                    true
                } else {
                    // 3. No external headset connected -> use built-in speakerphone (or earpiece if requested)
                    stopBluetoothScoSafely(am)

                    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                        if (!forceEarpiece) {
                            val speaker = commDevices.firstOrNull {
                                it.type == AudioDeviceInfo.TYPE_BUILTIN_SPEAKER
                            }
                            if (speaker != null) {
                                if (am.communicationDevice?.id != speaker.id) {
                                    am.setCommunicationDevice(speaker)
                                }
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
                        am.isSpeakerphoneOn = !forceEarpiece
                    }
                    ensureAudibleStreamVolume(am, AudioManager.STREAM_VOICE_CALL)
                    !forceEarpiece
                }
            } catch (e: Exception) {
                Log.w(TAG, "applySmartAudioRoute error: ${e.message}")
                false
            }
        }

        private fun stopBluetoothScoSafely(am: AudioManager) {
            try {
                @Suppress("DEPRECATION")
                if (am.isBluetoothScoOn) {
                    am.isBluetoothScoOn = false
                    am.stopBluetoothSco()
                }
            } catch (_: Exception) {}
        }

        private fun ensureAudibleStreamVolume(am: AudioManager, streamType: Int) {
            try {
                val maxVol = am.getStreamMaxVolume(streamType)
                val curVol = am.getStreamVolume(streamType)
                if (maxVol > 0 && curVol < (maxVol * 0.75).toInt()) {
                    am.setStreamVolume(streamType, (maxVol * 0.85).toInt(), 0)
                }
            } catch (_: Exception) {}
        }

        fun setSpeakerphoneState(context: Context, enabled: Boolean): Boolean {
            return applySmartAudioRoute(context, forceEarpiece = !enabled)
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

    private val lockRenewalRunnable = object : Runnable {
        override fun run() {
            if (isRunning) {
                acquireLocks()
                mainHandler.postDelayed(this, 15 * 60 * 1000L)
            }
        }
    }

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        createNotificationChannel()
        acquireLocks()
        setupAudio()
        registerAudioRouteObservers()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        val action = intent?.action ?: ACTION_START

        when (action) {
            ACTION_START -> {
                val title = intent?.getStringExtra(EXTRA_TITLE) ?: "Vidikom Intercom Active"
                val message = intent?.getStringExtra(EXTRA_MESSAGE) ?: "Comms connected • Screen lock safe"
                acquireLocks()
                startForegroundWithNotification(title, message)
                isRunning = true
                mainHandler.removeCallbacks(lockRenewalRunnable)
                mainHandler.postDelayed(lockRenewalRunnable, 15 * 60 * 1000L)
                scheduleRouteRefresh("ACTION_START")
            }
            ACTION_UPDATE -> {
                val title = intent?.getStringExtra(EXTRA_TITLE) ?: "Vidikom Intercom Active"
                val message = intent?.getStringExtra(EXTRA_MESSAGE) ?: "Comms connected"
                acquireLocks()
                if (isRunning) {
                    val notification = buildNotification(title, message)
                    val manager = getSystemService(Context.NOTIFICATION_SERVICE) as? NotificationManager
                    manager?.notify(NOTIFICATION_ID, notification)
                } else {
                    startForegroundWithNotification(title, message)
                    isRunning = true
                    mainHandler.removeCallbacks(lockRenewalRunnable)
                    mainHandler.postDelayed(lockRenewalRunnable, 15 * 60 * 1000L)
                }
                scheduleRouteRefresh("ACTION_UPDATE")
            }
            ACTION_STOP -> {
                mainHandler.removeCallbacks(lockRenewalRunnable)
                stopForegroundAndSelf()
            }
        }

        return START_STICKY
    }

    private fun scheduleRouteRefresh(reason: String) {
        Log.d(TAG, "Scheduling smart audio route refresh ($reason)")
        mainHandler.post {
            applySmartAudioRoute(this@IntercomForegroundService, forceEarpiece = false)
        }
        // Follow-up passes handle the ~2s delay between Bluetooth A2DP connection and HFP/SCO readiness
        for (delayMs in longArrayOf(350L, 1000L, 2200L, 3500L)) {
            mainHandler.postDelayed({
                if (isRunning) {
                    applySmartAudioRoute(this@IntercomForegroundService, forceEarpiece = false)
                }
            }, delayMs)
        }
    }

    private fun registerAudioRouteObservers() {
        try {
            audioManager?.registerAudioDeviceCallback(audioDeviceCallback, mainHandler)
        } catch (_: Exception) {}

        if (!isReceiverRegistered) {
            try {
                val filter = IntentFilter().apply {
                    addAction(BluetoothHeadset.ACTION_CONNECTION_STATE_CHANGED)
                    addAction(BluetoothHeadset.ACTION_AUDIO_STATE_CHANGED)
                    addAction(BluetoothA2dp.ACTION_CONNECTION_STATE_CHANGED)
                    addAction(AudioManager.ACTION_SCO_AUDIO_STATE_UPDATED)
                    addAction(Intent.ACTION_HEADSET_PLUG)
                    addAction(AudioManager.ACTION_AUDIO_BECOMING_NOISY)
                }
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
                    registerReceiver(audioRouteReceiver, filter, Context.RECEIVER_EXPORTED)
                } else {
                    registerReceiver(audioRouteReceiver, filter)
                }
                isReceiverRegistered = true
            } catch (_: Exception) {}
        }
    }

    private fun unregisterAudioRouteObservers() {
        mainHandler.removeCallbacksAndMessages(null)
        try {
            audioManager?.unregisterAudioDeviceCallback(audioDeviceCallback)
        } catch (_: Exception) {}

        if (isReceiverRegistered) {
            try {
                unregisterReceiver(audioRouteReceiver)
            } catch (_: Exception) {}
            isReceiverRegistered = false
        }
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
                val lockMode = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                    WifiManager.WIFI_MODE_FULL_LOW_LATENCY
                } else {
                    @Suppress("DEPRECATION")
                    WifiManager.WIFI_MODE_FULL_HIGH_PERF
                }
                wifiLock = wifiManager?.createWifiLock(
                    lockMode,
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
            audioManager?.isMicrophoneMute = false

            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                val focusRequest = AudioFocusRequest.Builder(AudioManager.AUDIOFOCUS_GAIN)
                    .setAudioAttributes(
                        AudioAttributes.Builder()
                            .setUsage(AudioAttributes.USAGE_VOICE_COMMUNICATION)
                            .setContentType(AudioAttributes.CONTENT_TYPE_SPEECH)
                            .build()
                    )
                    .setAcceptsDelayedFocusGain(true)
                    .setOnAudioFocusChangeListener { focusChange ->
                        if (focusChange == AudioManager.AUDIOFOCUS_GAIN) {
                            scheduleRouteRefresh("AUDIOFOCUS_GAIN")
                        }
                    }
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

            // Route to Bluetooth headset if connected, else wired headset, else speakerphone
            scheduleRouteRefresh("setupAudio")
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
        unregisterAudioRouteObservers()

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

        // Stop Bluetooth SCO, clear communication device override, abandon AudioFocus, and restore normal mode
        try {
            audioManager?.let { am ->
                stopBluetoothScoSafely(am)
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
                    am.clearCommunicationDevice()
                }
            }
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
