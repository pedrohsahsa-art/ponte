. "$PSScriptRoot\env.ps1"
$blender = 'C:\Program Files (x86)\Steam\steamapps\common\Blender\blender.exe'
if (!(Test-Path $blender)) { throw 'Informe o caminho da instalacao do Blender neste script.' }
& $blender --background --factory-startup --python "$Root\design\xp\create_mockup.py"
if ($LASTEXITCODE -ne 0) { throw 'O Blender nao concluiu a referencia XP.' }
