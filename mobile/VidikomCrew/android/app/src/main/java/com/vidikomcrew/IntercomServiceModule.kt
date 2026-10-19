package com.vidikomcrew

import android.content.Intent
import androidx.core.content.ContextCompat
import com.facebook.react.bridge.Arguments
import com.facebook.react.bridge.Promise
import com.facebook.react.bridge.ReactApplicationContext
import com.facebook.react.bridge.ReactContextBaseJavaModule
import com.facebook.react.bridge.ReactMethod
import com.facebook.react.bridge.WritableMap
import org.json.JSONObject
import java.io.BufferedReader
import java.io.InputStreamReader
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.HttpURLConnection
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.net.Socket
import java.net.URL
import java.util.concurrent.CountDownLatch
import java.util.concurrent.Executors
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

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

    /**
     * Discovers an active Vidikom / CuePilot Director on the same local network (Wi-Fi / LAN / Hotspot)
     * with zero internet dependency:
     * 1. Sends UDP broadcast probes ("DISCOVER_VIDIKOM") to port 41234 across all local interfaces.
     * 2. Performs a fast parallel TCP/HTTP scan on port 8080 across the device's active /24 subnet(s).
     */
    @ReactMethod
    fun discoverLanDirector(hintIp: String?, promise: Promise) {
        Thread {
            try {
                // 0. If hintIp is a valid LAN IP, probe it first immediately (150ms fast-path)
                val cleanHint = hintIp?.trim() ?: ""
                if (cleanHint.matches(Regex("^\\d+\\.\\d+\\.\\d+\\.\\d+$")) && cleanHint != "127.0.0.1") {
                    val fastHit = probeHttpActiveRoom(cleanHint, 400)
                    if (fastHit != null) {
                        promise.resolve(fastHit)
                        return@Thread
                    }
                }

                // 1. Collect all local IPv4 subnets on this Android device
                val subnets = LinkedHashSet<String>()
                try {
                    val interfaces = NetworkInterface.getNetworkInterfaces()
                    while (interfaces != null && interfaces.hasMoreElements()) {
                        val nif = interfaces.nextElement()
                        if (!nif.isUp || nif.isLoopback) continue
                        val addrs = nif.inetAddresses
                        while (addrs.hasMoreElements()) {
                            val addr = addrs.nextElement()
                            if (addr is Inet4Address && !addr.isLoopbackAddress) {
                                val ip = addr.hostAddress ?: continue
                                val parts = ip.split(".")
                                if (parts.size == 4 && parts[0] != "169") {
                                    subnets.add("${parts[0]}.${parts[1]}.${parts[2]}")
                                }
                            }
                        }
                    }
                } catch (_: Exception) {}

                if (cleanHint.matches(Regex("^\\d+\\.\\d+\\.\\d+\\.\\d+$"))) {
                    val parts = cleanHint.split(".")
                    if (parts.size == 4 && parts[0] != "127") {
                        subnets.add("${parts[0]}.${parts[1]}.${parts[2]}")
                    }
                }
                if (subnets.isEmpty()) {
                    subnets.add("192.168.1")
                    subnets.add("192.168.0")
                }

                // 2. Try fast UDP broadcast discovery on port 41234 (300ms)
                val udpHit = probeUdpLanBeacon(subnets)
                if (udpHit != null) {
                    promise.resolve(udpHit)
                    return@Thread
                }

                // 3. Fast parallel TCP + HTTP /api/room/active scan across all active subnets
                val foundRef = AtomicReference<WritableMap?>(null)
                val candidateIps = ArrayList<String>(subnets.size * 254)
                for (subnet in subnets) {
                    for (host in 1..254) {
                        candidateIps.add("$subnet.$host")
                    }
                }

                val pool = Executors.newFixedThreadPool(48)
                val latch = CountDownLatch(candidateIps.size)

                for (ip in candidateIps) {
                    pool.execute {
                        try {
                            if (foundRef.get() == null) {
                                val res = probeHttpActiveRoom(ip, 380)
                                if (res != null && foundRef.compareAndSet(null, res)) {
                                    // Signal early completion
                                    while (latch.count > 0) {
                                        latch.countDown()
                                    }
                                }
                            }
                        } catch (_: Exception) {
                        } finally {
                            latch.countDown()
                        }
                    }
                }

                latch.await(2800, TimeUnit.MILLISECONDS)
                pool.shutdownNow()

                val winner = foundRef.get()
                if (winner != null) {
                    promise.resolve(winner)
                } else {
                    promise.resolve(null)
                }
            } catch (e: Exception) {
                promise.resolve(null)
            }
        }.start()
    }

    private fun probeUdpLanBeacon(subnets: Set<String>): WritableMap? {
        var socket: DatagramSocket? = null
        return try {
            socket = DatagramSocket()
            socket.broadcast = true
            socket.soTimeout = 300
            val probeBytes = "DISCOVER_VIDIKOM".toByteArray(Charsets.UTF_8)

            try {
                val bcast = InetAddress.getByName("255.255.255.255")
                socket.send(DatagramPacket(probeBytes, probeBytes.size, bcast, 41234))
            } catch (_: Exception) {}

            for (subnet in subnets) {
                try {
                    val subBcast = InetAddress.getByName("$subnet.255")
                    socket.send(DatagramPacket(probeBytes, probeBytes.size, subBcast, 41234))
                } catch (_: Exception) {}
            }

            val buf = ByteArray(2048)
            val packet = DatagramPacket(buf, buf.size)
            socket.receive(packet)
            val text = String(packet.data, 0, packet.length, Charsets.UTF_8)
            val json = JSONObject(text)
            if (json.optBoolean("active", false) && json.optString("roomId").isNotEmpty()) {
                val senderIp = packet.address?.hostAddress ?: json.optString("serverIp")
                Arguments.createMap().apply {
                    putBoolean("active", true)
                    putString("serverIp", senderIp)
                    putString("roomId", json.optString("roomId").replace(" ", ""))
                    putString("pin", json.optString("pin", ""))
                    putBoolean("requiresPin", json.optBoolean("requiresPin", true))
                    putInt("directorPort", json.optInt("directorPort", 8080))
                    putInt("voicePort", json.optInt("voicePort", 8080))
                    putBoolean("isCloudRelay", false)
                    putString("productionName", json.optString("productionName", "Live Production"))
                    putString("directorName", json.optString("directorName", "Director"))
                }
            } else {
                null
            }
        } catch (_: Exception) {
            null
        } finally {
            try {
                socket?.close()
            } catch (_: Exception) {}
        }
    }

    private fun probeHttpActiveRoom(ip: String, timeoutMs: Int): WritableMap? {
        // Fast TCP connect check first
        val sock = Socket()
        try {
            sock.connect(InetSocketAddress(ip, 8080), timeoutMs)
        } catch (_: Exception) {
            return null
        } finally {
            try {
                sock.close()
            } catch (_: Exception) {}
        }

        // TCP port 8080 is open; query /api/room/active
        var conn: HttpURLConnection? = null
        return try {
            val url = URL("http://$ip:8080/api/room/active")
            conn = (url.openConnection() as HttpURLConnection).apply {
                connectTimeout = 600
                readTimeout = 600
                requestMethod = "GET"
            }
            if (conn.responseCode == 200) {
                val reader = BufferedReader(InputStreamReader(conn.inputStream))
                val body = reader.readText()
                reader.close()
                val json = JSONObject(body)
                val roomId = json.optString("roomId", "").replace(" ", "").trim()
                if (json.optBoolean("active", false) && roomId.isNotEmpty()) {
                    Arguments.createMap().apply {
                        putBoolean("active", true)
                        putString("serverIp", ip)
                        putString("roomId", roomId)
                        putString("pin", json.optString("pin", "").trim())
                        putBoolean("requiresPin", json.optBoolean("requiresPin", true))
                        putInt("directorPort", 8080)
                        putInt("voicePort", 8080)
                        putBoolean("isCloudRelay", false)
                        putString("productionName", json.optString("productionName", "Live Production"))
                        putString("directorName", json.optString("directorName", "Director"))
                    }
                } else {
                    null
                }
            } else {
                null
            }
        } catch (_: Exception) {
            null
        } finally {
            try {
                conn?.disconnect()
            } catch (_: Exception) {}
        }
    }
}
