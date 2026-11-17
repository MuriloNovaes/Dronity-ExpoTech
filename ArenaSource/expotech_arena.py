"""
ExpoTech - Gerador do cenario "Centro de Treinamento Tatico" (inspirado em R6 Siege)
------------------------------------------------------------------------------------
Rode no Blender (aba Scripting > Open > Run Script) ou via MCP.
O script e idempotente: apaga e recria SOMENTE a colecao "ExpoTech_Arena".
O drone e qualquer outro objeto do arquivo ficam intactos.

Regras de projeto (tiradas da apresentacao do ExpoTech):
  - Drone com ~2,8 m de largura, rodas de 0,55 m de raio, pulo medido de 2,30 m.
  - Passagens com no minimo 3,5 m (slide 13). O script VALIDA isso no fim.
  - Obstaculos BAIXOS (pulaveis): ate 1,4 m. Obstaculos ALTOS (desviar): >= 2,9 m.
    Nada entre 1,5 m e 2,8 m, para o assistente nao ficar na zona ambigua do pulo.
  - Sem teto e sem vergas nas portas: camera de topo (tecla C) continua funcionando
    e o drone nao bate a cabeca no pulo.
  - Duas camadas, igual ao drone: malha visual (VISUAL) + caixas simples (COLLISION,
    objetos "COL_*"). Na Unity, COL_* viram BoxCollider e perdem o renderer.
  - Materiais so com cor/rugosidade/emissao: e o que o FBX leva para a URP.
"""

import bpy
import bmesh
import math
import os
from mathutils import Vector

ROOT_NAME = "ExpoTech_Arena"
MIN_GAP = 3.5          # passagem minima (m)
TOUCH_TOL = 0.12       # abaixo disso consideramos "encostado" (sem vao)
SPAWN = (-13.5, -10.0)  # ponto de partida do drone (Blender XY)
EXPORT = True          # exporta FBX ao final
EXPORT_DIR = None      # None = pasta do .blend (ou home se o arquivo nao foi salvo)

# ----------------------------------------------------------------------------
# Limpeza (somente a colecao do cenario)
# ----------------------------------------------------------------------------
def purge_root():
    root = bpy.data.collections.get(ROOT_NAME)
    if not root:
        return
    def rec(c):
        for ch in list(c.children):
            rec(ch)
        for o in list(c.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.collections.remove(c)
    rec(root)
    for m in list(bpy.data.meshes):
        if m.users == 0:
            bpy.data.meshes.remove(m)

purge_root()

root = bpy.data.collections.new(ROOT_NAME)
bpy.context.scene.collection.children.link(root)
def sub(name):
    c = bpy.data.collections.new(name)
    root.children.link(c)
    return c
C_VIS = sub("Arena_VISUAL")
C_COL = sub("Arena_COLLISION")
C_MRK = sub("Arena_MARKERS")

# ----------------------------------------------------------------------------
# Materiais (paleta R6: concreto, compensado, metal, verde militar, amarelo de fita)
# ----------------------------------------------------------------------------
def mat(name, rgb, rough=0.8, metal=0.0, emit=None, strength=0.0):
    full = "AR_" + name
    m = bpy.data.materials.get(full) or bpy.data.materials.new(full)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*rgb, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if emit:
        key = "Emission Color" if "Emission Color" in b.inputs else "Emission"
        b.inputs[key].default_value = (*emit, 1)
        b.inputs["Emission Strength"].default_value = strength
    m.diffuse_color = (*rgb, 1)
    return m

M = {
    "floor":    mat("Concreto_Piso", (0.20, 0.20, 0.21), 0.9),
    "concrete": mat("Concreto_Parede", (0.42, 0.41, 0.39), 0.9),
    "plywood":  mat("Compensado", (0.55, 0.40, 0.24), 0.75),
    "stud":     mat("Madeira_Escura", (0.30, 0.20, 0.11), 0.8),
    "metal":    mat("Metal_Reforco", (0.16, 0.17, 0.18), 0.45, 0.8),
    "steel":    mat("Aco", (0.35, 0.36, 0.37), 0.4, 0.9),
    "olive":    mat("Verde_Militar", (0.17, 0.21, 0.12), 0.7),
    "cont_or":  mat("Container_Laranja", (0.55, 0.20, 0.06), 0.65, 0.4),
    "cont_bl":  mat("Container_Azul", (0.07, 0.16, 0.30), 0.65, 0.4),
    "sand":     mat("Saco_Areia", (0.42, 0.37, 0.24), 0.95),
    "crate":    mat("Caixa_Madeira", (0.50, 0.36, 0.20), 0.8),
    "black":    mat("Preto", (0.03, 0.03, 0.035), 0.6),
    "yellow":   mat("Fita_Amarela", (0.85, 0.62, 0.03), 0.6),
    "blue":     mat("Spawn_Azul", (0.05, 0.15, 0.55), 0.6),
    "glass":    mat("Vidro_Escuro", (0.02, 0.03, 0.04), 0.1, 0.5),
    "red_led":  mat("LED_Vermelho", (0.6, 0.02, 0.01), 0.4, 0, (1.0, 0.05, 0.02), 8.0),
    "green_led":mat("LED_Verde", (0.02, 0.4, 0.1), 0.4, 0, (0.1, 1.0, 0.3), 5.0),
    "lamp":     mat("Lampada", (0.9, 0.9, 0.85), 0.3, 0, (1.0, 0.95, 0.85), 12.0),
    "screen":   mat("Tela_Briefing", (0.02, 0.06, 0.08), 0.3, 0, (0.15, 0.55, 0.8), 3.0),
}

# ----------------------------------------------------------------------------
# Geometria
# ----------------------------------------------------------------------------
COLLIDERS = []  # (nome, minx, miny, maxx, maxy, altura)

def _box_mesh(name, dims):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * dims[0], v.co.y * dims[1], v.co.z * dims[2] + dims[2] / 2))
    bm.to_mesh(me)
    bm.free()
    return me

def vis_box(name, dims, xy, z0=0.0, m=None, bevel=0.03, rot_z=0.0, coll=C_VIS, parent=None):
    """Caixa visual com base em z0, centrada em xy."""
    me = _box_mesh(name, dims)
    o = bpy.data.objects.new(name, me)
    o.location = (xy[0], xy[1], z0)
    o.rotation_euler[2] = rot_z
    if m:
        me.materials.append(m)
    if bevel > 0:
        md = o.modifiers.new("Bevel", "BEVEL")
        md.width = bevel
        md.segments = 2
        md.limit_method = "ANGLE"
    coll.objects.link(o)
    if parent:
        o.parent = parent
        o.matrix_parent_inverse = parent.matrix_world.inverted()
    return o

def col_box(name, dims, xy, z0=0.0):
    """Colisor simples (alinhado aos eixos). Registra para validacao."""
    me = _box_mesh("COL_" + name, dims)
    o = bpy.data.objects.new("COL_" + name, me)
    o.location = (xy[0], xy[1], z0)
    o.display_type = "WIRE"
    o.hide_render = True
    o["expotech_collider"] = True
    C_COL.objects.link(o)
    COLLIDERS.append((name, xy[0] - dims[0] / 2, xy[1] - dims[1] / 2,
                      xy[0] + dims[0] / 2, xy[1] + dims[1] / 2, z0 + dims[2]))
    return o

def solid(name, dims, xy, m, z0=0.0, bevel=0.03):
    """Visual + colisor com as mesmas medidas."""
    v = vis_box(name, dims, xy, z0, m, bevel)
    col_box(name, dims, xy, z0)
    return v

def cylinder(name, r, h, xy, m, z0=0.0, verts=20):
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=verts, radius1=r, radius2=r, depth=h)
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, h / 2))
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    o.location = (xy[0], xy[1], z0)
    me.materials.append(m)
    for p in me.polygons:
        p.use_smooth = len(p.vertices) == 4
    C_VIS.objects.link(o)
    return o

def decal(name, dims, xy, m, rot_z=0.0):
    """Faixa no chao (sem colisao)."""
    return vis_box(name, (dims[0], dims[1], 0.01), xy, 0.004, m, 0, rot_z)

# ----------------------------------------------------------------------------
# Paredes (alinhadas aos eixos)
# ----------------------------------------------------------------------------
WALL_H = 4.0
def wall_x(name, x, y0, y1, t, m, studs=False):
    """Parede vertical em X fixo, de y0 a y1."""
    L = y1 - y0
    cy = (y0 + y1) / 2
    v = solid(name, (t, L, WALL_H), (x, cy), m, bevel=0.02)
    if studs:   # montantes de madeira dos dois lados (cara de "kill house")
        n = max(2, int(L / 2.0) + 1)
        for i in range(n):
            yy = y0 + 0.08 + i * (L - 0.16) / (n - 1)
            for side in (-1, 1):
                vis_box(f"{name}_mont{i}{'a' if side < 0 else 'b'}", (0.06, 0.12, WALL_H),
                        (x + side * (t / 2 + 0.03), yy), 0, M["stud"], 0.01)
        for side in (-1, 1):   # rodape e travessa
            for zz in (0.0, 2.6):
                vis_box(f"{name}_trav{zz}{side}", (0.05, L, 0.15),
                        (x + side * (t / 2 + 0.025), cy), zz, M["stud"], 0.01)
    return v

def wall_y(name, y, x0, x1, t, m, studs=False):
    L = x1 - x0
    cx = (x0 + x1) / 2
    v = solid(name, (L, t, WALL_H), (cx, y), m, bevel=0.02)
    if studs:
        n = max(2, int(L / 2.0) + 1)
        for i in range(n):
            xx = x0 + 0.08 + i * (L - 0.16) / (n - 1)
            for side in (-1, 1):
                vis_box(f"{name}_mont{i}{'a' if side < 0 else 'b'}", (0.12, 0.06, WALL_H),
                        (xx, y + side * (t / 2 + 0.03)), 0, M["stud"], 0.01)
        for side in (-1, 1):
            for zz in (0.0, 2.6):
                vis_box(f"{name}_trav{zz}{side}", (L, 0.05, 0.15),
                        (cx, y + side * (t / 2 + 0.025)), zz, M["stud"], 0.01)
    return v

# --- Piso ---------------------------------------------------------------------
W, D = 36.0, 28.0   # area util: x -18..18, y -14..14
vis_box("Piso", (W + 1.6, D + 1.6, 0.2), (0, 0), -0.2, M["floor"], 0)
col_box("Piso", (W + 1.6, D + 1.6, 0.2), (0, 0), -0.2)
COLLIDERS.pop()  # o piso nao entra na validacao de vaos

# Juntas de dilatacao no piso (grade de 4 m)
for i, x in enumerate(range(-16, 17, 4)):
    decal(f"Junta_X{i}", (0.04, D), (x, 0), M["black"])
for i, y in enumerate(range(-12, 13, 4)):
    decal(f"Junta_Y{i}", (W, 0.04), (0, y), M["black"])

# --- Perimetro (concreto) -----------------------------------------------------
PT = 0.4
wall_y("Parede_Sul", -14 - PT / 2, -18 - PT, 18 + PT, PT, M["concrete"])
wall_y("Parede_Norte", 14 + PT / 2, -18 - PT, 18 + PT, PT, M["concrete"])
wall_x("Parede_Oeste", -18 - PT / 2, -14, 14, PT, M["concrete"])
wall_x("Parede_Leste", 18 + PT / 2, -14, 14, PT, M["concrete"])

# --- Paredes internas (compensado com montantes) ------------------------------
IT = 0.3
# x = -4 : portas em y [-8.5,-4] e [4, 8.5]
wall_x("Int_A1", -4, -14, -8.5, IT, M["plywood"], True)
wall_x("Int_A2", -4, -4, 4, IT, M["plywood"], True)
wall_x("Int_A3", -4, 8.5, 14, IT, M["plywood"], True)
# x = +4 : portas em y [-6.5, -2] (deposito) e [8, 12.5] (objetivo)
wall_x("Int_B1", 4, -14, -6.5, IT, M["plywood"], True)
wall_x("Int_B2", 4, -2, 2.5, IT, M["plywood"], True)
wall_x("Int_B2_Reforcada", 4, 2.5, 8, IT, M["metal"])         # parede reforcada (R6)
wall_x("Int_B3", 4, 12.5, 14, IT, M["plywood"], True)
# y = 0 lado oeste: porta em x [-13, -8.5]
wall_y("Int_C1", 0, -18, -13, IT, M["plywood"], True)
wall_y("Int_C2", 0, -8.5, -4, IT, M["plywood"], True)
# y = 0 lado leste: porta em x [9, 13.5]
wall_y("Int_D1", 0, 4, 9, IT, M["plywood"], True)
wall_y("Int_D2", 0, 13.5, 18, IT, M["plywood"], True)

# Nervuras da parede reforcada (so visual, salientes 3 cm)
for i in range(6):
    yy = 2.95 + i * 0.98
    for side in (-1, 1):
        vis_box(f"Reforco_Nervura{i}{side}", (0.04, 0.12, WALL_H - 0.2),
                (4 + side * (IT / 2 + 0.02), yy), 0.1, M["steel"], 0.01)
for side in (-1, 1):
    for zz in (0.8, 2.0, 3.2):
        vis_box(f"Reforco_Barra{zz}{side}", (0.05, 5.3, 0.18),
                (4 + side * (IT / 2 + 0.025), 5.25), zz, M["steel"], 0.01)

# Batentes das portas (dentro da espessura da parede, sem verga)
def door_posts(name, axis, fixed, a0, a1, t):
    for k, a in enumerate((a0, a1)):
        if axis == "x":
            vis_box(f"Batente_{name}{k}", (t + 0.08, 0.12, WALL_H), (fixed, a), 0, M["steel"], 0.01)
        else:
            vis_box(f"Batente_{name}{k}", (0.12, t + 0.08, WALL_H), (a, fixed), 0, M["steel"], 0.01)
door_posts("A_S", "x", -4, -8.5, -4, IT)
door_posts("A_N", "x", -4, 4, 8.5, IT)
door_posts("B_S", "x", 4, -6.5, -2, IT)
door_posts("B_N", "x", 4, 8, 12.5, IT)
door_posts("C", "y", 0, -13, -8.5, IT)
door_posts("D", "y", 0, 9, 13.5, IT)

# ----------------------------------------------------------------------------
# Props
# ----------------------------------------------------------------------------
def crate(name, xy, s=1.2):
    vis_box(name + "_Tampa", (s * 0.94, s * 0.94, s * 0.94), xy, s * 0.03, M["crate"], 0.01)
    fr = vis_box(name + "_Moldura", (s, s, s), xy, 0, M["stud"], 0)
    w = fr.modifiers.new("Frame", "WIREFRAME")
    w.thickness = 0.09
    w.use_even_offset = True
    fr.modifiers.remove(fr.modifiers["Bevel"]) if "Bevel" in fr.modifiers else None

def crate_group(name, xy, nx, ny, s=1.2):
    """Grupo de caixas encostadas; 1 colisor envolvendo o grupo."""
    x0 = xy[0] - nx * s / 2 + s / 2
    y0 = xy[1] - ny * s / 2 + s / 2
    for i in range(nx):
        for j in range(ny):
            crate(f"{name}_{i}{j}", (x0 + i * s, y0 + j * s), s)
    col_box(name, (nx * s, ny * s, s), xy)

def sandbag_wall(name, xy, length, along="x", layers=3):
    bag_l, bag_w, bag_h = 0.75, 0.45, 0.28
    n = int(length / bag_l)
    for k in range(layers):
        off = (bag_l / 2) * (k % 2)
        cnt = n - (k % 2)
        for i in range(cnt):
            a = -length / 2 + bag_l / 2 + off + i * bag_l
            for row in (-1, 1) if k < layers - 1 else (0,):
                b = row * bag_w * 0.5
                p = (xy[0] + a, xy[1] + b) if along == "x" else (xy[0] + b, xy[1] + a)
                d = (bag_l * 0.98, bag_w, bag_h) if along == "x" else (bag_w, bag_l * 0.98, bag_h)
                o = vis_box(f"{name}_{k}_{i}_{row}", d, p, k * bag_h * 0.95, M["sand"], 0.1)
                o.modifiers["Bevel"].segments = 3
    h = layers * bag_h * 0.95 + 0.05
    dims = (length, bag_w * 2, h) if along == "x" else (bag_w * 2, length, h)
    col_box(name, dims, xy)

def barrel(name, xy):
    cylinder(name, 0.45, 1.0, xy, M["olive"])
    for zz in (0.25, 0.72):
        cylinder(name + f"_aro{zz}", 0.465, 0.04, xy, M["black"], zz)
    cylinder(name + "_tampa", 0.42, 0.01, xy, M["black"], 1.0)

def container(name, xy, along, m):
    L, Wd, H = 6.1, 2.44, 2.9       # high cube: alto o bastante para "desviar"
    dims = (L, Wd, H) if along == "x" else (Wd, L, H)
    vis_box(name, dims, xy, 0, m, 0.05)
    # corrugado nas laterais longas
    n = 16
    for i in range(n):
        a = -L / 2 + 0.3 + i * (L - 0.6) / (n - 1)
        for side in (-1, 1):
            if along == "x":
                vis_box(f"{name}_rib{i}{side}", (0.12, 0.06, H - 0.3),
                        (xy[0] + a, xy[1] + side * (Wd / 2 + 0.02)), 0.15, m, 0.01)
            else:
                vis_box(f"{name}_rib{i}{side}", (0.06, 0.12, H - 0.3),
                        (xy[0] + side * (Wd / 2 + 0.02), xy[1] + a), 0.15, m, 0.01)
    # cantoneiras
    for sx in (-1, 1):
        for sy in (-1, 1):
            vis_box(f"{name}_canto{sx}{sy}", (0.22, 0.22, H + 0.02),
                    (xy[0] + sx * (dims[0] / 2 - 0.1), xy[1] + sy * (dims[1] / 2 - 0.1)),
                    0, M["black"], 0.01)
    col_box(name, dims, xy)

def shield(name, xy, facing_y=True):
    """Escudo deslocavel (deployable shield) - obstaculo baixo e fino."""
    w, t, h = 1.6, 0.12, 1.2
    d = (w, t, h) if facing_y else (t, w, h)
    vis_box(name, d, xy, 0.15, M["metal"], 0.03)
    vd = (0.7, t + 0.02, 0.18) if facing_y else (t + 0.02, 0.7, 0.18)
    vis_box(name + "_Visor", vd, xy, 1.0, M["glass"], 0.01)
    for s in (-1, 1):
        p = (xy[0] + s * 0.6, xy[1]) if facing_y else (xy[0], xy[1] + s * 0.6)
        ld = (0.08, 0.5, 0.15) if facing_y else (0.5, 0.08, 0.15)
        vis_box(name + f"_Pe{s}", ld, p, 0, M["black"], 0.01)
    col_box(name, (w, 0.5, h + 0.15) if facing_y else (0.5, w, h + 0.15), xy)

def flood_light(name, xy):
    vis_box(name + "_Poste", (0.18, 0.18, 5.0), xy, 0, M["black"], 0.01)
    vis_box(name + "_Refletor", (0.6, 0.6, 0.35), xy, 5.0, M["black"], 0.02)
    vis_box(name + "_Lampada", (0.5, 0.5, 0.02), xy, 4.99, M["lamp"], 0)
    col_box(name, (0.18, 0.18, 5.0), xy)

def hazard_square(name, cx, cy, sx, sy, t=0.15):
    decal(name + "_S", (sx, t), (cx, cy - sy / 2), M["yellow"])
    decal(name + "_N", (sx, t), (cx, cy + sy / 2), M["yellow"])
    decal(name + "_O", (t, sy), (cx - sx / 2, cy), M["yellow"])
    decal(name + "_L", (t, sy), (cx + sx / 2, cy), M["yellow"])

# ---- Sala 1: GARAGEM / spawn (SO) -------------------------------------------
crate_group("Garagem_Caixas", (-5.35, -13.4), 2, 1)          # canto SE, encostado
sandbag_wall("Garagem_Sacos", (-10.0, -6.0), 3.2, "x")       # baixo: pula
solid("Garagem_Prateleira", (1.0, 4.0, 3.5), (-17.5, -2.15), M["steel"], bevel=0.02)  # alto: desvia
for k in range(3):   # prateleiras visuais
    vis_box(f"Garagem_Prat_Nivel{k}", (1.04, 4.04, 0.06), (-17.5, -2.15), 0.6 + k * 1.1, M["black"], 0)
# spawn
sp = cylinder("Spawn_Marca", 2.4, 0.012, SPAWN, M["blue"], 0.004, 40)
decal("Spawn_Seta", (0.3, 2.0), (SPAWN[0], SPAWN[1] + 1.2), M["yellow"])

# ---- Sala 2: BRIEFING (NO) ---------------------------------------------------
solid("Briefing_Mesa", (4.0, 2.0, 1.0), (-11.0, 7.0), M["olive"])
vis_box("Briefing_Mesa_Tampo", (4.1, 2.1, 0.06), (-11.0, 7.0), 1.0, M["black"], 0.01)
solid("Briefing_Armarios", (6.0, 0.8, 3.2), (-15.0, 13.6), M["olive"], bevel=0.02)
for i in range(8):
    vis_box(f"Armario_Divisao{i}", (0.03, 0.04, 3.1), (-17.6 + i * 0.75, 13.18), 0.05, M["black"], 0)
vis_box("Briefing_Tela", (0.04, 3.2, 1.8), (-17.98, 7.0), 1.4, M["screen"], 0)

# ---- Corredor central ---------------------------------------------------------
for i, yy in enumerate((-13.5, 13.5)):
    decal(f"Corredor_Faixa{i}", (7.7, 0.25), (0, yy), M["yellow"])
for i in range(7):   # setas no chao
    decal(f"Corredor_Seta{i}", (0.2, 1.2), (0, -10.5 + i * 3.5), M["yellow"])

# ---- Sala 3: OBJETIVO / bomba (NE) -------------------------------------------
solid("Objetivo_Mesa", (2.4, 1.4, 1.0), (11.0, 9.5), M["steel"])
vis_box("Bomba", (0.8, 0.5, 0.4), (11.0, 9.5), 1.0, M["black"], 0.03)
vis_box("Bomba_LED", (0.5, 0.52, 0.06), (11.0, 9.5), 1.25, M["red_led"], 0)
cylinder("Bomba_Cilindro1", 0.09, 0.45, (10.6, 9.5), M["olive"], 1.0)
cylinder("Bomba_Cilindro2", 0.09, 0.45, (11.4, 9.5), M["olive"], 1.0)
hazard_square("Objetivo_Area", 11.0, 9.5, 6.0, 4.4)
solid("Objetivo_Servidores", (0.9, 7.0, 3.2), (17.55, 10.5), M["black"], bevel=0.02)
for i in range(7):
    for j in range(5):
        m = M["green_led"] if (i + j) % 3 else M["red_led"]
        vis_box(f"Servidor_LED{i}{j}", (0.02, 0.5, 0.04), (17.09, 7.5 + i * 1.0), 0.5 + j * 0.55, m, 0)
shield("Objetivo_Escudo", (11.0, 4.2), facing_y=True)

# ---- Sala 4: DEPOSITO / containers (SE) ---------------------------------------
container("Container_A", (14.95, -12.78), "x", M["cont_or"])
container("Container_B", (5.37, -10.95), "y", M["cont_bl"])
crate_group("Deposito_Caixas", (11.3, -6.5), 2, 1)
for i, p in enumerate(((17.5, -0.65), (16.6, -0.65), (17.5, -1.55))):
    barrel(f"Barril{i}", p)
col_box("Barris", (1.8, 1.8, 1.0), (17.05, -1.1))
hazard_square("Deposito_Carga", 11.3, -6.5, 4.4, 3.2, 0.12)

# ---- Refletores nos cantos ----------------------------------------------------
for i, p in enumerate(((-17.8, -13.8), (17.8, -13.8), (-17.8, 13.8), (17.8, 13.8))):
    flood_light(f"Refletor{i}", p)

# ---- Marcadores (empties exportados para a Unity) -----------------------------
e = bpy.data.objects.new("SPAWN_Drone", None)
e.location = (SPAWN[0], SPAWN[1], 0.6)
e.empty_display_type = "SINGLE_ARROW"
e.rotation_euler = (math.radians(-90), 0, 0)  # seta aponta +Y (norte)
C_MRK.objects.link(e)
for name, p in (("OBJ_Bomba", (11.0, 9.5)), ("ROOM_Garagem", (-11, -7)), ("ROOM_Briefing", (-11, 7)),
                ("ROOM_Corredor", (0, 0)), ("ROOM_Objetivo", (11, 7)), ("ROOM_Deposito", (11, -7))):
    o = bpy.data.objects.new(name, None)
    o.location = (*p, 0)
    o.empty_display_type = "PLAIN_AXES"
    C_MRK.objects.link(o)

# ----------------------------------------------------------------------------
# Validacao: vaos entre colisores e regra de alturas
# ----------------------------------------------------------------------------
def gap(a, b):
    dx = max(0.0, a[1] - b[3], b[1] - a[3])
    dy = max(0.0, a[2] - b[4], b[2] - a[4])
    return math.hypot(dx, dy)

problems = []
for i in range(len(COLLIDERS)):
    for j in range(i + 1, len(COLLIDERS)):
        walls = ("Parede", "Int_")
        if COLLIDERS[i][0].startswith(walls) and COLLIDERS[j][0].startswith(walls):
            continue  # paredes conectadas formam cantos; portas tem 4,5 m por construcao
        g = gap(COLLIDERS[i], COLLIDERS[j])
        if TOUCH_TOL < g < MIN_GAP:
            problems.append(f"VAO {g:.2f} m entre {COLLIDERS[i][0]} e {COLLIDERS[j][0]}")

ambig = [f"ALTURA ambigua {c[5]:.2f} m em {c[0]}" for c in COLLIDERS if 1.5 < c[5] < 2.8]

sp_clear = min(gap((None, SPAWN[0], SPAWN[1], SPAWN[0], SPAWN[1]), c) for c in COLLIDERS)
if sp_clear < MIN_GAP:
    problems.append(f"SPAWN com folga de {sp_clear:.2f} m (< {MIN_GAP})")

low = [c[0] for c in COLLIDERS if c[5] <= 1.5]
high = [c[0] for c in COLLIDERS if c[5] >= 2.8 and not c[0].startswith(("Parede", "Int_", "Refletor"))]

# ----------------------------------------------------------------------------
# Exportacao FBX (Unity: Y para cima, -Z para frente, escala aplicada)
# ----------------------------------------------------------------------------
exported = None
if EXPORT:
    out = EXPORT_DIR or (os.path.dirname(bpy.data.filepath) if bpy.data.filepath else os.path.expanduser("~"))
    exported = os.path.join(out, "ExpoTech_Arena.fbx")
    for o in bpy.context.view_layer.objects:
        o.select_set(False)
    objs = list(C_VIS.objects) + list(C_COL.objects) + list(C_MRK.objects)
    for o in objs:
        o.hide_set(False)
        o.select_set(True)
    bpy.ops.export_scene.fbx(
        filepath=exported, use_selection=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True,
        use_mesh_modifiers=True, mesh_smooth_type="FACE", object_types={"MESH", "EMPTY"},
        add_leaf_bones=False, path_mode="AUTO")
    for o in objs:
        o.select_set(False)

# Colisores escondidos na viewport depois de exportar (ficam no arquivo)
C_COL.hide_viewport = False
for o in C_COL.objects:
    o.hide_set(True)

tris = sum(sum(len(p.vertices) - 2 for p in o.evaluated_get(bpy.context.evaluated_depsgraph_get()).data.polygons)
           for o in C_VIS.objects if o.type == "MESH")

result = {
    "objetos_visuais": len(C_VIS.objects),
    "colisores": len(C_COL.objects),
    "triangulos_aprox": tris,
    "folga_spawn_m": round(sp_clear, 2),
    "problemas_vao": problems,
    "alturas_ambiguas": ambig,
    "obstaculos_baixos_pulaveis": low,
    "obstaculos_altos_desviar": high,
    "fbx": exported,
}
print(result)
