"""Validação e resumo de URDF, sem depender do Unity nem do ROS."""

from __future__ import annotations

import os
import xml.etree.ElementTree as ET
from dataclasses import dataclass, field

JOINT_TYPES = {"revolute", "continuous", "prismatic", "fixed", "floating", "planar"}
UNITY_MESH_FORMATS = {".stl", ".dae", ".obj"}


@dataclass
class UrdfReport:
    robot_name: str = ""
    root_link: str = ""
    links: list[str] = field(default_factory=list)
    joints: list[dict] = field(default_factory=list)
    total_mass: float = 0.0
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)

    def as_dict(self) -> dict:
        return {
            "valid": not self.errors,
            "robot_name": self.robot_name,
            "root_link": self.root_link,
            "link_count": len(self.links),
            "joint_count": len(self.joints),
            "total_mass_kg": round(self.total_mass, 4),
            "joints": self.joints,
            "errors": self.errors,
            "warnings": self.warnings,
        }


def _resolve_mesh(filename: str, urdf_dir: str) -> str:
    """Resolve package://pkg/path do jeito que o URDF Importer faz: relativo à pasta do URDF."""
    if filename.startswith("package://"):
        rel = filename[len("package://"):]
        # O importer procura a partir da pasta do .urdf e de seus pais.
        parts = rel.split("/", 1)
        candidates = [os.path.join(urdf_dir, rel)]
        if len(parts) == 2:
            candidates.append(os.path.join(urdf_dir, parts[1]))
            candidates.append(os.path.join(os.path.dirname(urdf_dir), rel))
        for c in candidates:
            if os.path.exists(c):
                return c
        return candidates[0]
    if filename.startswith("file://"):
        return filename[len("file://"):]
    return filename if os.path.isabs(filename) else os.path.join(urdf_dir, filename)


def validate_urdf(source: str, check_meshes: bool = True) -> dict:
    """`source` pode ser o caminho de um .urdf ou o próprio XML."""
    report = UrdfReport()
    urdf_dir = ""
    try:
        if source.lstrip().startswith("<"):
            root = ET.fromstring(source)
            check_meshes = False
        else:
            urdf_dir = os.path.dirname(os.path.abspath(source))
            root = ET.parse(source).getroot()
    except (ET.ParseError, OSError) as exc:
        report.errors.append(f"Não foi possível ler o URDF: {exc}")
        return report.as_dict()

    if root.tag != "robot":
        report.errors.append(f"Elemento raiz deve ser <robot>, encontrado <{root.tag}>.")
        return report.as_dict()
    report.robot_name = root.get("name", "")
    if not report.robot_name:
        report.warnings.append("<robot> sem atributo name.")

    link_names: set[str] = set()
    for link in root.findall("link"):
        name = link.get("name")
        if not name:
            report.errors.append("<link> sem name.")
            continue
        if name in link_names:
            report.errors.append(f"Link duplicado: '{name}'.")
        link_names.add(name)
        report.links.append(name)

        inertial = link.find("inertial")
        if inertial is None:
            if link.find("visual") is not None or link.find("collision") is not None:
                report.warnings.append(
                    f"Link '{name}' sem <inertial>: o Unity usará massa 1 kg e inércia automática."
                )
        else:
            mass_el = inertial.find("mass")
            mass = float(mass_el.get("value", "0")) if mass_el is not None else 0.0
            if mass <= 0:
                report.errors.append(f"Link '{name}' com massa <= 0.")
            report.total_mass += mass
            inertia = inertial.find("inertia")
            if inertia is not None:
                ixx, iyy, izz = (float(inertia.get(k, "0")) for k in ("ixx", "iyy", "izz"))
                if min(ixx, iyy, izz) <= 0:
                    report.warnings.append(f"Link '{name}' com inércia diagonal <= 0.")
                elif ixx + iyy < izz or ixx + izz < iyy or iyy + izz < ixx:
                    report.warnings.append(
                        f"Link '{name}' viola a desigualdade triangular da inércia (fisicamente impossível; o PhysX pode explodir)."
                    )

        for kind in ("visual", "collision"):
            for el in link.findall(kind):
                mesh = el.find("geometry/mesh")
                if mesh is None:
                    continue
                fn = mesh.get("filename", "")
                ext = os.path.splitext(fn)[1].lower()
                if ext not in UNITY_MESH_FORMATS:
                    report.warnings.append(
                        f"Link '{name}' ({kind}) usa '{ext}': o URDF Importer só aceita {sorted(UNITY_MESH_FORMATS)}."
                    )
                if check_meshes and fn:
                    path = _resolve_mesh(fn, urdf_dir)
                    if not os.path.exists(path):
                        report.errors.append(f"Mesh não encontrada para '{name}' ({kind}): {fn} -> {path}")
        if link.find("collision") is None and link.find("visual") is not None:
            report.warnings.append(f"Link '{name}' sem <collision>: não vai colidir no Unity.")

    children: dict[str, str] = {}
    joint_names: set[str] = set()
    for joint in root.findall("joint"):
        name = joint.get("name", "")
        jtype = joint.get("type", "")
        parent_el, child_el = joint.find("parent"), joint.find("child")
        parent = parent_el.get("link") if parent_el is not None else None
        child = child_el.get("link") if child_el is not None else None
        info = {"name": name, "type": jtype, "parent": parent, "child": child}

        if name in joint_names:
            report.errors.append(f"Joint duplicada: '{name}'.")
        joint_names.add(name)
        if jtype not in JOINT_TYPES:
            report.errors.append(f"Joint '{name}' com tipo inválido '{jtype}'.")
        if jtype in ("floating", "planar"):
            report.warnings.append(
                f"Joint '{name}' é '{jtype}': o URDF Importer não suporta bem; vira joint fixa/livre no Unity."
            )
        for role, link in (("parent", parent), ("child", child)):
            if not link:
                report.errors.append(f"Joint '{name}' sem <{role}>.")
            elif link not in link_names:
                report.errors.append(f"Joint '{name}' referencia {role} inexistente '{link}'.")
        if child:
            if child in children:
                report.errors.append(f"Link '{child}' tem mais de um pai ('{children[child]}' e '{parent}').")
            children[child] = parent or ""

        axis = joint.find("axis")
        if axis is not None:
            info["axis"] = axis.get("xyz", "1 0 0")
            vals = [float(v) for v in info["axis"].split()]
            if len(vals) != 3 or all(abs(v) < 1e-9 for v in vals):
                report.errors.append(f"Joint '{name}' com eixo inválido '{info['axis']}'.")
        elif jtype in ("revolute", "continuous", "prismatic"):
            info["axis"] = "1 0 0 (padrão)"

        limit = joint.find("limit")
        if jtype in ("revolute", "prismatic"):
            if limit is None:
                report.errors.append(f"Joint '{name}' ({jtype}) exige <limit lower upper effort velocity>.")
            else:
                lo, hi = float(limit.get("lower", "0")), float(limit.get("upper", "0"))
                info["limits"] = [lo, hi]
                if lo >= hi:
                    report.warnings.append(f"Joint '{name}' com lower >= upper ({lo} >= {hi}).")
                if float(limit.get("effort", "0")) <= 0:
                    report.warnings.append(
                        f"Joint '{name}' com effort 0: o drive do ArticulationBody não terá força (forceLimit)."
                    )
        report.joints.append(info)

    roots = [l for l in report.links if l not in children]
    if len(roots) != 1:
        report.errors.append(f"A árvore deve ter exatamente 1 link raiz, encontrados {len(roots)}: {roots}.")
    else:
        report.root_link = roots[0]
        # detecta ciclos/links desconectados
        seen, stack = set(), [roots[0]]
        kids: dict[str, list[str]] = {}
        for c, p in children.items():
            kids.setdefault(p, []).append(c)
        while stack:
            cur = stack.pop()
            if cur in seen:
                report.errors.append(f"Ciclo detectado em '{cur}'.")
                break
            seen.add(cur)
            stack.extend(kids.get(cur, []))
        orphan = set(report.links) - seen
        if orphan:
            report.errors.append(f"Links desconectados da raiz: {sorted(orphan)}.")

    if report.root_link in ("world", "map"):
        report.warnings.append(
            "Link raiz é 'world': no Unity marque o ArticulationBody raiz como Immovable em vez de usar um link world."
        )
    return report.as_dict()
