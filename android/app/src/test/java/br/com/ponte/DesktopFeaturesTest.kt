package br.com.ponte

import org.junit.Assert.*
import org.junit.Test

class DesktopFeaturesTest {
    @Test fun wakePacketHasSixFfBytesAndSixteenMacCopies() {
        val packet = LanNetwork.magicPacket("001122AABBCC")
        assertEquals(102, packet.size)
        assertTrue(packet.take(6).all { it == 0xff.toByte() })
        repeat(16) { assertArrayEquals(byteArrayOf(0, 0x11, 0x22, 0xaa.toByte(), 0xbb.toByte(), 0xcc.toByte()), packet.copyOfRange(6 + it * 6, 12 + it * 6)) }
    }
    @Test fun wakeTargetsSurviveEncryptedPayloadSerializationAndHostChanges() {
        val connection = Connection("192.168.1.2", 47831, "A".repeat(64), "secret", wakeTargets = listOf(WakeTarget("001122334455", "192.168.1.255")))
        assertEquals(connection, Connection.fromJson(connection.json()))
        assertEquals(connection.wakeTargets, connection.copy(host = "192.168.1.3").wakeTargets)
    }
    @Test(expected = IllegalArgumentException::class) fun malformedMacIsRejected() { LanNetwork.magicPacket("not a MAC") }
    @Test fun qualityDowngradesAndRequiresThreeHealthyWindowsToUpgrade() {
        val policy = AdaptiveQuality()
        assertEquals("balanced", policy.next("sharp", 5))
        assertEquals("fluid", policy.next("balanced", 7))
        assertEquals("fluid", policy.next("fluid", 15))
        assertEquals("fluid", policy.next("fluid", 15))
        assertEquals("balanced", policy.next("fluid", 15))
        assertEquals("balanced", policy.next("balanced", 11))
    }
}
