"""Original Ponte UI artwork, generated with Blender; no Windows assets copied.

Run with Blender --background --factory-startup --python this_file.py.
Only a new process/scenes and project-owned output paths are modified.
"""
import bpy
import math
import ast
import re
import shutil
from pathlib import Path
from datetime import datetime
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "design" / "xp" / ("render-" + datetime.now().strftime("%Y%m%d-%H%M%S"))
OUT.mkdir(parents=True, exist_ok=False)
ASSETS = ROOT / "windows" / "Assets" / "Xp"
ANDROID = ROOT / "android" / "app" / "src" / "main" / "res" / "drawable-nodpi"
ASSETS.mkdir(parents=True, exist_ok=True)
ANDROID.mkdir(parents=True, exist_ok=True)

def enum_by_name(owner, prop, label):
    items = owner.bl_rna.properties[prop].enum_items
    match = next((i.identifier for i in items if i.name.lower() == label.lower()), None)
    if match is None:
        # Some context-sensitive OCIO enums expose only NONE in RNA. Blender's
        # validation error reports the actual legal identifiers without changing
        # the value. Choose from that list, never assume a version's identifiers.
        try:
            setattr(owner, prop, "__PONTE_ENUM_PROBE__")
        except (TypeError, ValueError) as error:
            legal = re.search(r"not found in (\(.*\))", str(error))
            values = ast.literal_eval(legal.group(1)) if legal else []
            match = next((v for v in values if v.lower() == label.lower()), None)
        if match is None:
            raise RuntimeError(f"{prop}: {label!r} unavailable; found {[(i.identifier, i.name) for i in items]}")
    return match

def rgb(value):
    values = [int(value[i:i+2], 16) / 255 for i in (0, 2, 4)]
    return tuple(v / 12.92 if v <= .04045 else ((v + .055) / 1.055) ** 2.4 for v in values) + (1,)

def material(name, color, flat=False):
    mat = bpy.data.materials.new("PonteXP_" + name)
    mat.use_nodes = True
    node = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    if flat:
        output = next(n for n in mat.node_tree.nodes if n.type == "OUTPUT_MATERIAL")
        mat.node_tree.nodes.remove(node)
        emission = mat.node_tree.nodes.new(bpy.types.ShaderNodeEmission.bl_rna.identifier)
        emission.inputs[0].default_value = rgb(color)
        mat.node_tree.links.new(emission.outputs[0], output.inputs[0])
    else:
        next(s for s in node.inputs if s.identifier == "Base Color").default_value = rgb(color)
        next(s for s in node.inputs if s.identifier == "Roughness").default_value = .29
    return mat

COLORS = {k: material(k, v, True) for k, v in {
    "blue":"0054E3", "blue_light":"4595F4", "navy":"003399", "cream":"ECE9D8",
    "white":"FFFFFF", "ink":"24334D", "muted":"4C5D74", "panel":"D6DFF7",
    "line":"9AAEC8", "green":"287E2D", "yellow":"FFF5CC", "red":"D94428",
    "button":"F7F5EC", "shadow":"466EAB", "back":"1E3F78",
}.items()}

def add_object(scene, obj):
    scene.collection.objects.link(obj)
    return obj

def box(scene, name, loc, size, mat, bevel=0):
    mesh = bpy.data.meshes.new(name)
    x,y,z = (v/2 for v in size)
    mesh.from_pydata([(-x,-y,-z),(-x,-y,z),(-x,y,-z),(-x,y,z),(x,-y,-z),(x,-y,z),(x,y,-z),(x,y,z)], [], [(0,4,6,2),(1,3,7,5),(0,1,5,4),(2,6,7,3),(0,2,3,1),(4,5,7,6)])
    mesh.update()
    obj = add_object(scene, bpy.data.objects.new(name, mesh)); obj.location = loc
    obj.data.materials.append(mat)
    if bevel:
        kind = next(i.identifier for i in bpy.types.Modifier.bl_rna.properties["type"].enum_items if i.name == "Bevel")
        mod = obj.modifiers.new("Soft edges", kind); mod.width = bevel; mod.segments = 3
    return obj

def setup(scene, width, height, transparent=False):
    print("Scene render engine:", scene.render.engine)
    scene.render.resolution_x = width; scene.render.resolution_y = height; scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = enum_by_name(scene.render.image_settings, "file_format", "PNG")
    scene.render.image_settings.color_mode = enum_by_name(scene.render.image_settings, "color_mode", "RGBA")
    scene.render.film_transparent = transparent
    scene.view_settings.view_transform = enum_by_name(scene.view_settings, "view_transform", "Standard")
    scene.world = bpy.data.worlds.new(scene.name + " world"); scene.world.use_nodes = True
    background = next(n for n in scene.world.node_tree.nodes if n.type == "BACKGROUND")
    background.inputs[0].default_value = (.15,.18,.25,1); background.inputs[1].default_value = .4

def camera(scene, name, position, target, ortho):
    data = bpy.data.cameras.new(name); obj = add_object(scene, bpy.data.objects.new(name, data))
    data.type = enum_by_name(data, "type", "Orthographic")
    obj.location = position; obj.rotation_euler = (Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
    data.ortho_scale = ortho; scene.camera = obj

def render_icon(kind):
    scene = bpy.data.scenes.new("Ponte XP - icon " + kind); setup(scene, 256, 256, True)
    blue = material(kind+" bezel", "2879CD"); silver = material(kind+" metal", "CBD9E8")
    glass = material(kind+" display", "79C7FA"); dark = material(kind+" frame", "174C85")
    if kind == "pc":
        box(scene,"Monitor body",(0,0,1.15),(2.1,.28,1.5),blue,.09)
        box(scene,"Screen inset",(0,-.17,1.19),(1.78,.05,1.15),dark,.025)
        box(scene,"Screen",(0,-.205,1.19),(1.65,.04,1.03),glass,.02)
        box(scene,"Stand",(0,.03,.35),(.35,.35,.55),silver,.04)
        box(scene,"Foot",(0,-.03,.075),(1.1,.65,.15),blue,.06)
    elif kind == "phone":
        box(scene,"Phone body",(0,0,1.1),(1.05,.27,2.2),blue,.09)
        box(scene,"Phone screen",(0,-.16,1.18),(.83,.05,1.68),glass,.025)
        box(scene,"Home key",(0,-.19,.2),(.24,.035,.09),silver,.025)
        box(scene,"Speaker",(0,-.17,2.04),(.28,.035,.05),dark,.01)
    else:
        gold = material("folder gold", "D6A325"); yellow = material("folder front", "FFDA68")
        box(scene,"Folder back",(0,.1,.8),(2,.2,1.45),gold,.06)
        box(scene,"Folder tab",(-.52,.1,1.57),(.85,.2,.26),gold,.04)
        front = box(scene,"Folder front",(0,-.15,.66),(2.12,.16,1.2),yellow,.06); front.rotation_euler.x = math.radians(-8)
    camera(scene,"Icon camera",(3.2,-6.5,3.1),(0,0,1),3.2)
    for name, position, power, size in [("Key",(-3,-4,6),450,4),("Fill",(3,-1,4),260,3)]:
        light = bpy.data.lights.new(kind+name, enum_by_name(bpy.types.Light,"type","Area")); light.energy=power; light.shape=enum_by_name(light,"shape","Disk"); light.size=size
        obj=add_object(scene,bpy.data.objects.new(kind+name,light)); obj.location=position; obj.rotation_euler=(Vector((0,0,1))-obj.location).to_track_quat('-Z','Y').to_euler()
    destination = ASSETS / (kind+".png")
    scene.render.filepath=str(destination); bpy.ops.render.render(write_still=True, scene=scene.name)
    shutil.copyfile(destination, ANDROID / ("ponte_xp_"+kind+".png"))
    return scene

for kind in ("pc", "phone", "folder"):
    render_icon(kind)

scene = bpy.data.scenes.new("Ponte XP - Desktop and Android"); setup(scene, 1600, 1000)
SCALE = 100
font_path = Path("C:/Windows/Fonts/tahoma.ttf")
font = bpy.data.fonts.load(str(font_path)) if font_path.exists() else None

def rect(name, x, y, w, h, color, z=.055):
    return box(scene, name, ((x+w/2-800)/SCALE,(500-y-h/2)/SCALE,z), (w/SCALE,h/SCALE,.015), COLORS[color])

def text(value, x, y, size=16, color="ink", z=.12):
    kind = next(i.identifier for i in bpy.types.BlendDataCurves.bl_rna.functions["new"].parameters["type"].enum_items if i.name == "Text")
    curve=bpy.data.curves.new("Label "+value[:25], kind)
    curve.body=value; curve.size=size/SCALE
    if font: curve.font=font
    obj=add_object(scene,bpy.data.objects.new("Text "+value[:25],curve)); obj.location=((x-800)/SCALE,(500-y-size)/SCALE,z); curve.materials.append(COLORS[color]); return obj

def button(label,x,y,w,h=38,primary=False):
    rect(label+" border",x,y,w,h,"navy",.05); rect(label+" face",x+1,y+1,w-2,h-2,"green" if primary else "button",.065)
    rect(label+" shine",x+2,y+2,w-4,1,"white",.08); text(label,x+12,y+8,15,"white" if primary else "ink")

def window(title,x,y,w,h):
    rect(title+" shadow",x+6,y+7,w,h,"back",0)
    rect(title+" border",x,y,w,h,"blue",.02); rect(title+" body",x+3,y+34,w-6,h-37,"cream",.04)
    for i in range(34):
        # Original Luna-inspired strip gradient, not a Windows bitmap.
        shade="blue_light" if i < 8 else "blue"
        rect(title+" caption "+str(i),x+3,y+i+2,w-6,1,shade,.06)
    text(title,x+13,y+6,19,"white")
    for j,label in enumerate(("_","□","×")):
        rect(title+label,x+w-89+j*27,y+5,23,23,"red" if j==2 else "blue_light",.09); text(label,x+w-84+j*27,y+5,17,"white",.14)

rect("Design backdrop",0,0,1600,1000,"back",-.04)
text("PONTE / CLÁSSICO XP",56,22,27,"white")
text("Três tarefas. Menos passos. Mesmas permissões.",57,58,17,"panel")
window("Ponte — Central de conexões",55,107,1060,770)
rect("Desktop sidebar",65,151,220,675,"panel")
rect("Sidebar heading",76,163,198,32,"white",.07); text("O que você quer fazer?",87,170,16,"navy")
button("Controlar",78,212,194,42,True); button("Arquivos",78,262,194,42); button("Conectar telefone",78,312,194,42)
text("Controle do PC",87,398,17,"navy"); text("Autorização: desligada",87,435,14)
button("Autorizar telefone",78,474,194); button("Parar / revogar",78,521,194)
text("Dica",87,610,17,"navy"); text("Pareie uma vez. Depois,",87,649,14); text("escolha a direção do controle.",87,672,14)
text("Controlar seus dispositivos",310,165,25,"navy")
text("Selecione o telefone pareado e escolha uma ação.",311,205,16,"muted")
rect("Device panel",308,251,776,130,"line"); rect("Device panel inside",309,252,774,128,"white",.07)
text("Telefone pareado",332,272,17); text("Meu Android",332,312,24,"navy")
rect("PC task",309,412,375,253,"white"); rect("Android task",704,412,380,253,"white")
text("PC → telefone",332,438,21,"navy"); text("Tela, teclado e áudio do PC.",332,481,16)
button("Autorizar controle do PC",330,543,332,46,True)
text("Android → computador",727,438,21,"navy"); text("Abra o visualizador no Windows.",727,481,16)
button("Abrir tela do Android",725,543,335,46)
rect("Consent note",309,700,775,76,"yellow"); text("Você decide quando compartilhar.",328,711,17,"navy"); text("Revogue a qualquer momento. Nenhuma permissão é ativada sozinha.",328,742,14)
rect("Desktop status",61,841,1048,29,"button"); text("Conexão local • identidade TLS verificada",77,848,13,"muted")
window("Ponte",1170,149,374,704)
text("Meu computador",1192,207,22,"navy"); text("Pareado • rede local",1193,247,15,"muted")
button("Início",1190,291,101); button("Arquivos",1298,291,112); button("Conexão",1417,291,107)
rect("Phone task PC",1189,354,336,166,"white"); text("Usar o computador",1206,371,21,"navy"); text("Tela, mouse e teclado.",1207,407,15)
button("Controlar PC",1205,449,304,46,True)
rect("Phone task Android",1189,545,336,205,"white"); text("Mostrar meu Android",1206,561,21,"navy"); text("Autorize a captura no telefone.",1207,602,15)
button("Compartilhar tela",1205,645,304,46); text("Controle opcional via Acessibilidade.",1205,713,13,"muted")
text("Conectar • escolher • usar",1193,789,15,"navy")
text("Ícones 3D próprios • tema inspirado no XP • aplicativo continua nativo",58,930,18,"white")
camera(scene,"UI overview camera",(0,0,25),(0,0,0),16)
scene.render.filepath = str(OUT / "ponte-xp.png")
bpy.ops.render.render(write_still=True, scene=scene.name)
if bpy.context.window: bpy.context.window.scene=scene
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / "ponte-xp.blend"))
print("PONTE_XP_OUTPUT=" + str(OUT))
