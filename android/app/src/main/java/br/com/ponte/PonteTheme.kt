package br.com.ponte

import androidx.compose.foundation.Image
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.Alignment
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

val PonteInk = Color(0xFF24334D)
val PonteTeal = Color(0xFF003399)
val XpBlue = Color(0xFF0054E3)

@Composable
fun PonteTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = lightColorScheme(primary = Color(0xFF287E2D), onPrimary = Color.White,
            primaryContainer = Color(0xFFDCEAFF), onPrimaryContainer = PonteTeal,
            secondary = PonteTeal, background = Color(0xFFECE9D8), surface = Color.White,
            surfaceVariant = Color(0xFFD6DFF7), onSurface = PonteInk, onBackground = PonteInk,
            outline = Color(0xFF7F9DB9)),
        shapes = Shapes(extraSmall = RoundedCornerShape(3.dp), small = RoundedCornerShape(3.dp),
            medium = RoundedCornerShape(4.dp), large = RoundedCornerShape(6.dp), extraLarge = RoundedCornerShape(8.dp)),
        content = content
    )
}

@Composable
fun XpCaption(title: String, modifier: Modifier = Modifier) {
    Row(modifier.fillMaxWidth().background(Brush.verticalGradient(listOf(Color(0xFF559AF5), XpBlue, Color(0xFF003AB3))))
        .padding(horizontal = 12.dp, vertical = 10.dp), verticalAlignment = Alignment.CenterVertically) {
        DeviceGlyph(Modifier.size(32.dp))
        Text(title, color = Color.White, fontWeight = FontWeight.Bold, fontSize = 21.sp, modifier = Modifier.padding(start = 8.dp))
    }
}

@Composable
fun XpPanel(title: String, icon: Int? = null, content: @Composable ColumnScope.() -> Unit) {
    Surface(shape = RoundedCornerShape(4.dp), border = BorderStroke(1.dp, Color(0xFF91A9D1)), modifier = Modifier.fillMaxWidth()) {
        Column {
            Row(Modifier.fillMaxWidth().background(Brush.horizontalGradient(listOf(Color.White, Color(0xFFC7D7F1))))
                .padding(10.dp), verticalAlignment = Alignment.CenterVertically) {
                if (icon != null) Image(painterResource(icon), null, Modifier.size(32.dp).padding(end = 6.dp))
                Text(title, fontWeight = FontWeight.Bold, color = PonteTeal)
            }
            Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(8.dp), content = content)
        }
    }
}

@Composable
fun DeviceGlyph(modifier: Modifier = Modifier.size(56.dp), color: Color = PonteTeal, document: Boolean = false) {
    Image(painterResource(if (document) R.drawable.ponte_xp_folder else R.drawable.ponte_xp_pc), null, modifier)
}
