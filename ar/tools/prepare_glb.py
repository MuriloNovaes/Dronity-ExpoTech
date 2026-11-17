"""Gera ar/models/expo_drone.glb (versão web do drone) a partir do Drone.glb exportado do Blender.

- Remove o chão da cena do Blender (malhas com material "Floor").
- Centraliza o drone em X/Z e apoia a base das rodas em Y = 0 (o plano do marcador no AR.js).
- Mantém materiais e emissão dos LEDs.
Uso: python ar/tools/prepare_glb.py
"""

import os
import sys

import numpy as np
import pygltflib
import trimesh

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SRC = os.path.join(ROOT, "RobotDescription", "expo_drone_source", "Drone.glb")
DST = os.path.join(ROOT, "ar", "models", "expo_drone.glb")

gltf = pygltflib.GLTF2().load(SRC)
floor_mat = [i for i, m in enumerate(gltf.materials) if (m.name or "").lower() == "floor"]
floor_meshes = {i for i, m in enumerate(gltf.meshes) if any(p.material in floor_mat for p in m.primitives)}
floor_nodes = {i for i, n in enumerate(gltf.nodes) if n.mesh in floor_meshes}

scene = gltf.scenes[gltf.scene or 0]
kept = [n for n in scene.nodes if n not in floor_nodes]
print(f"chão removido: nós {sorted(floor_nodes)} ({[gltf.nodes[i].name for i in floor_nodes]})")

# Bounds reais (com as transformações dos nós) só do que ficou.
tm = trimesh.load(SRC, force="scene")
floor_names = {gltf.nodes[i].name for i in floor_nodes}
bounds = []
for node_name in tm.graph.nodes_geometry:
    if node_name in floor_names:
        continue
    transform, geom = tm.graph[node_name]
    mesh = tm.geometry[geom].copy()
    mesh.apply_transform(transform)
    bounds.append(mesh.bounds)
bmin = np.min([b[0] for b in bounds], axis=0)
bmax = np.max([b[1] for b in bounds], axis=0)
size = bmax - bmin
center = (bmin + bmax) / 2
print(f"tamanho do drone (m): {np.round(size, 3)} | centro {np.round(center, 3)} | base y {bmin[1]:.3f}")

root = pygltflib.Node(name="ExpoDrone", children=kept,
                      translation=[float(-center[0]), float(-bmin[1]), float(-center[2])])
gltf.nodes.append(root)
scene.nodes = [len(gltf.nodes) - 1]
gltf.asset.generator = "ExpoTech prepare_glb.py"

os.makedirs(os.path.dirname(DST), exist_ok=True)
gltf.save_binary(DST)
print(f"salvo {DST} ({os.path.getsize(DST) / 1024:.0f} KB)")

check = trimesh.load(DST, force="scene")
print("bounds finais:", np.round(check.bounds, 3).tolist())
sys.exit(0)
