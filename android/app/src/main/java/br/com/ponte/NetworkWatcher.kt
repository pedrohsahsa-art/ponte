package br.com.ponte

import android.content.Context
import android.net.ConnectivityManager
import android.net.Network

// Only real default-network replacement/loss triggers a restart, not every
// capabilities callback. No requests are forced onto a stale Network object.
internal class DefaultNetworkState<T>(private var current: T?) {
    @Synchronized fun available(network: T): Boolean { if (current == network) return false; current = network; return true }
    @Synchronized fun lost(network: T): Boolean { if (current != network) return false; current = null; return true }
}

class NetworkWatcher(context: Context, private val changed: () -> Unit) : AutoCloseable {
    private val manager = context.getSystemService(ConnectivityManager::class.java)
    private val state = DefaultNetworkState(manager.activeNetwork)
    private val callback = object : ConnectivityManager.NetworkCallback() {
        override fun onAvailable(network: Network) { if (state.available(network)) changed() }
        override fun onLost(network: Network) { if (state.lost(network)) changed() }
    }
    init { manager.registerDefaultNetworkCallback(callback) }
    override fun close() { manager.unregisterNetworkCallback(callback) }
}
