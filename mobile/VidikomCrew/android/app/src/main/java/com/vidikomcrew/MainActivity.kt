package com.vidikomcrew

import android.Manifest
import android.content.pm.PackageManager
import android.media.AudioManager
import android.os.Build
import android.os.Bundle
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import com.facebook.react.ReactActivity
import com.facebook.react.ReactActivityDelegate
import com.facebook.react.defaults.DefaultNewArchitectureEntryPoint.fabricEnabled
import com.facebook.react.defaults.DefaultReactActivityDelegate

class MainActivity : ReactActivity() {

  companion object {
    private const val REQUEST_BLUETOOTH_CONNECT = 4102
  }

  private var requestedBluetoothPermission = false

  override fun onCreate(savedInstanceState: Bundle?) {
    super.onCreate(null)
    // Bind hardware volume buttons to Intercom Voice / Bluetooth SCO stream
    volumeControlStream = AudioManager.STREAM_VOICE_CALL
    ensureBluetoothConnectPermission()
  }

  override fun onResume() {
    super.onResume()
    if (IntercomForegroundService.isRunning) {
      IntercomForegroundService.applySmartAudioRoute(this, forceEarpiece = false)
    }
  }

  private fun ensureBluetoothConnectPermission() {
    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S && !requestedBluetoothPermission) {
      val granted = ContextCompat.checkSelfPermission(
        this,
        Manifest.permission.BLUETOOTH_CONNECT
      ) == PackageManager.PERMISSION_GRANTED

      if (!granted) {
        requestedBluetoothPermission = true
        try {
          ActivityCompat.requestPermissions(
            this,
            arrayOf(Manifest.permission.BLUETOOTH_CONNECT),
            REQUEST_BLUETOOTH_CONNECT
          )
        } catch (_: Exception) {}
      }
    }
  }

  override fun onRequestPermissionsResult(
    requestCode: Int,
    permissions: Array<out String>,
    grantResults: IntArray
  ) {
    super.onRequestPermissionsResult(requestCode, permissions, grantResults)
    if (IntercomForegroundService.isRunning) {
      IntercomForegroundService.applySmartAudioRoute(this, forceEarpiece = false)
    }
  }

  /**
   * Returns the name of the main component registered from JavaScript. This is used to schedule
   * rendering of the component.
   */
  override fun getMainComponentName(): String = "VidikomCrew"

  /**
   * Returns the instance of the [ReactActivityDelegate]. We use [DefaultReactActivityDelegate]
   * which allows you to enable New Architecture with a single boolean flags [fabricEnabled]
   */
  override fun createReactActivityDelegate(): ReactActivityDelegate =
      DefaultReactActivityDelegate(this, mainComponentName, fabricEnabled)
}
