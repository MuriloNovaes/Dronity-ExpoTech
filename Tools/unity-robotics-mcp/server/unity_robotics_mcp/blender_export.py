
from __future__ import annotations

BLENDER_CONVENTIONS = """
# Convenções Blender -> URDF (Unity Robotics MCP)

1. Monte o robô como uma hierarquia de parentesco (Ctrl+P > Object (Keep Transform)).
   O objeto sem pai é o link raiz (ex.: base_link). Cada objeto = 1 link.
2. Posicione a ORIGEM de cada peça no centro da junta (Set Origin > Origin to 3D Cursor).
   A origem do filho é o ponto de rotação da junta.
3. No painel Object Properties > Custom Properties do FILHO, adicione:
   - joint_type: "revolute" | "continuous" | "prismatic" | "fixed" (padrão fixed)
   - joint_axis: "0 0 1" (eixo no frame local do filho; veja as setas do objeto)
   - joint_lower / joint_upper: graus (revolute) ou metros (prismatic)
   - joint_effort: torque/força máxima (ex.: 50)
   - joint_velocity: velocidade máxima (ex.: 2.0)
   - mass: massa em kg (ex.: 2.5)
4. Use nomes sem espaços (base_link, shoulder_link, wheel_left...). Eles viram os nomes
   dos links/joints no ROS (joint = "<filho>_joint").
5. Aplique escala é opcional: o exportador "assa" a escala no mesh do link.
6. Unidades: 1 unidade Blender = 1 metro. Robôs em mm precisam de escala 0.001.
""".strip()

_SCRIPT = r'''
import bpy, math, os
from mathutils import Matrix

ROBOT_NAME = __ROBOT_NAME__
OUTPUT_DIR = __OUTPUT_DIR__
ROOT_OBJECT = __ROOT_OBJECT__      # None = usa a seleção / coleção ativa
EXPORT_FBX = __EXPORT_FBX__
DEFAULT_MASS = __DEFAULT_MASS__

pkg = ROBOT_NAME + "_description"
robot_dir = os.path.join(bpy.path.abspath(OUTPUT_DIR), ROBOT_NAME)
mesh_dir = os.path.join(robot_dir, pkg, "meshes")
fbx_dir = os.path.join(robot_dir, "fbx_visuals")
os.makedirs(mesh_dir, exist_ok=True)
if EXPORT_FBX:
    os.makedirs(fbx_dir, exist_ok=True)

if bpy.context.object and bpy.context.object.mode != "OBJECT":
    bpy.ops.object.mode_set(mode="OBJECT")

def collect():
    if ROOT_OBJECT:
        root = bpy.data.objects[ROOT_OBJECT]
        return [root] + list(root.children_recursive)
    sel = list(bpy.context.selected_objects)
    if sel:
        return sel
    return list(bpy.context.view_layer.active_layer_collection.collection.all_objects)

objs = [o for o in collect() if o.type in {"MESH", "EMPTY"}]
names = {o.name for o in objs}
clean = lambda n: "".join(c if c.isalnum() or c in "_-" else "_" for c in n)

def frame(o):
    loc, rot, _ = o.matrix_world.decompose()
    return Matrix.Translation(loc) @ rot.to_matrix().to_4x4()

def fmt(v):
    return " ".join("%.6g" % x for x in v)

def export_selected(o, path, kind):
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    bpy.context.view_layer.objects.active = o
    if kind == "stl":
        if hasattr(bpy.ops.wm, "stl_export"):
            bpy.ops.wm.stl_export(filepath=path, export_selected_objects=True, apply_modifiers=True)
        else:
            bpy.ops.export_mesh.stl(filepath=path, use_selection=True, use_mesh_modifiers=True)
    else:
        bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"},
                                 apply_scale_options="FBX_SCALE_ALL", path_mode="COPY",
                                 embed_textures=True, use_mesh_modifiers=True)

def color_of(o):
    if o.type != "MESH" or not o.data.materials or o.data.materials[0] is None:
        return None
    m = o.data.materials[0]
    c = list(m.diffuse_color)
    if m.use_nodes:
        bsdf = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf:
            c = list(bsdf.inputs["Base Color"].default_value)
    return clean(m.name), c

# Frames e dimensões são lidos ANTES de exportar: mover um pai temporariamente
# altera o matrix_world avaliado dos filhos.
bpy.context.view_layer.update()
frames = {o.name: frame(o) for o in objs}
dims = {o.name: o.dimensions.copy() for o in objs}

links, joints, report = [], [], []
roots = [o for o in objs if o.parent is None or o.parent.name not in names]
root_name = clean(roots[0].name) if len(roots) == 1 else "base_link"
if len(roots) != 1:
    links.append('  <link name="base_link"/>')

for o in objs:
    ln = clean(o.name)
    mass = float(o.get("mass", DEFAULT_MASS if o.type == "MESH" else 0.01))
    d = dims[o.name]
    sx, sy, sz = max(d.x, 1e-3), max(d.y, 1e-3), max(d.z, 1e-3)
    ixx, iyy, izz = (mass / 12.0 * (sy*sy + sz*sz), mass / 12.0 * (sx*sx + sz*sz), mass / 12.0 * (sx*sx + sy*sy))
    xml = ['  <link name="%s">' % ln,
           '    <inertial><origin xyz="0 0 0" rpy="0 0 0"/><mass value="%.6g"/>' % mass,
           '      <inertia ixx="%.6g" ixy="0" ixz="0" iyy="%.6g" iyz="0" izz="%.6g"/></inertial>' % (ixx, iyy, izz)]
    if o.type == "MESH":
        saved = o.matrix_world.copy()
        _, _, scale = saved.decompose()
        o.matrix_world = Matrix.Diagonal((scale.x, scale.y, scale.z, 1.0))
        export_selected(o, os.path.join(mesh_dir, ln + ".stl"), "stl")
        if EXPORT_FBX:
            export_selected(o, os.path.join(fbx_dir, ln + ".fbx"), "fbx")
        o.matrix_world = saved
        bpy.context.view_layer.update()
        uri = "package://%s/meshes/%s.stl" % (pkg, ln)
        mat = color_of(o)
        mat_xml = ('<material name="%s"><color rgba="%s"/></material>' % (mat[0], fmt(mat[1]))) if mat else ""
        xml.append('    <visual><origin xyz="0 0 0" rpy="0 0 0"/><geometry><mesh filename="%s"/></geometry>%s</visual>' % (uri, mat_xml))
        xml.append('    <collision><origin xyz="0 0 0" rpy="0 0 0"/><geometry><mesh filename="%s"/></geometry></collision>' % uri)
    xml.append("  </link>")
    links.append("\n".join(xml))

    is_root = o in roots
    if is_root and len(roots) == 1:
        continue
    parent_name = "base_link" if is_root else clean(o.parent.name)
    rel = frames[o.name] if is_root else frames[o.parent.name].inverted() @ frames[o.name]
    loc, rot, _ = rel.decompose()
    rpy = rot.to_euler("XYZ")
    jtype = str(o.get("joint_type", "fixed"))
    j = ['  <joint name="%s_joint" type="%s">' % (ln, jtype),
         '    <parent link="%s"/><child link="%s"/>' % (parent_name, ln),
         '    <origin xyz="%s" rpy="%s"/>' % (fmt(loc), fmt(rpy))]
    if jtype != "fixed":
        j.append('    <axis xyz="%s"/>' % str(o.get("joint_axis", "0 0 1")))
        effort, vel = float(o.get("joint_effort", 50)), float(o.get("joint_velocity", 2.0))
        if jtype == "revolute":
            lo, hi = math.radians(float(o.get("joint_lower", -180))), math.radians(float(o.get("joint_upper", 180)))
            j.append('    <limit lower="%.6g" upper="%.6g" effort="%g" velocity="%g"/>' % (lo, hi, effort, vel))
        elif jtype == "prismatic":
            lo, hi = float(o.get("joint_lower", 0)), float(o.get("joint_upper", 0.1))
            j.append('    <limit lower="%.6g" upper="%.6g" effort="%g" velocity="%g"/>' % (lo, hi, effort, vel))
        else:
            j.append('    <limit effort="%g" velocity="%g"/>' % (effort, vel))
        j.append('    <dynamics damping="0.1" friction="0.0"/>')
    j.append("  </joint>")
    joints.append("\n".join(j))
    report.append("%s -> %s (%s)" % (parent_name, ln, jtype))

urdf_path = os.path.join(robot_dir, ROBOT_NAME + ".urdf")
with open(urdf_path, "w", encoding="utf-8") as f:
    f.write('<?xml version="1.0"?>\n<robot name="%s">\n%s\n%s\n</robot>\n' % (ROBOT_NAME, "\n".join(links), "\n".join(joints)))

result = {"urdf": urdf_path, "root_link": root_name, "links": len(links), "joints": report,
          "fbx_visuals": fbx_dir if EXPORT_FBX else None}
print(result)
result
'''


def build_blender_export_script(
    robot_name: str,
    output_dir: str,
    root_object: str | None = None,
    export_fbx_visuals: bool = True,
    default_mass: float = 1.0,
) -> str:
    return (
        _SCRIPT.replace("__ROBOT_NAME__", repr(robot_name))
        .replace("__OUTPUT_DIR__", repr(output_dir))
        .replace("__ROOT_OBJECT__", repr(root_object))
        .replace("__EXPORT_FBX__", repr(bool(export_fbx_visuals)))
        .replace("__DEFAULT_MASS__", repr(float(default_mass)))
        .strip()
    )
