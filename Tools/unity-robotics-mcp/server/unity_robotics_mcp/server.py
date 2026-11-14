"""Servidor MCP especialista em Unity Robotics."""

from __future__ import annotations

import re
from typing import Any, Literal

try:  # SDK MCP 1.x
    from mcp.server.fastmcp import FastMCP
except ImportError:  # SDK MCP 2.x renomeou FastMCP para MCPServer
    from mcp.server import MCPServer as FastMCP

from .blender_export import BLENDER_CONVENTIONS, build_blender_export_script
from .bridge import ToolError, UnityBridge
from .knowledge import GUIDES, INSTRUCTIONS, PACKAGES, PROMPTS
from .templates import TEMPLATES, render_template
from .urdf_tools import validate_urdf as _validate_urdf

GUIDES["blender-to-unity"] = BLENDER_CONVENTIONS

mcp = FastMCP("unity-robotics", instructions=INSTRUCTIONS)
unity = UnityBridge()

Vec3 = list[float]


def _clean(params: dict[str, Any]) -> dict[str, Any]:
    return {k: v for k, v in params.items() if v is not None}


# --------------------------------------------------------------------------- Unity: projeto


@mcp.tool()
def unity_status() -> dict:
    """Estado do Unity Editor: versão, projeto, pacotes de robótica instalados, protocolo ROS,
    play mode, se está compilando e os últimos erros de compilação."""
    return unity.call("ping")


@mcp.tool()
def unity_install_robotics_packages(
    packages: list[Literal["ros-tcp-connector", "urdf-importer", "visualizations"]] | None = None,
) -> dict:
    """Instala os pacotes do Unity Robotics Hub via Package Manager (git URLs oficiais).
    Padrão: ros-tcp-connector + urdf-importer. O Unity vai recompilar depois."""
    chosen = packages or ["ros-tcp-connector", "urdf-importer"]
    return unity.call("install_packages", {"packages": [PACKAGES[p][1] for p in chosen]})


@mcp.tool()
def unity_set_ros_protocol(protocol: Literal["ros1", "ros2"]) -> dict:
    """Define o protocolo do ROS-TCP-Connector (símbolo de script ROS2). Recompila o projeto."""
    return unity.call("set_ros_protocol", {"protocol": protocol})


@mcp.tool()
def unity_console(count: int = 50, errors_only: bool = False) -> list:
    """Últimas mensagens do Console do Unity (inclui erros de compilação)."""
    return unity.call("get_console", {"count": count, "errorsOnly": errors_only})


@mcp.tool()
def unity_play_mode(action: Literal["enter", "exit", "pause", "unpause"]) -> dict:
    """Entra/sai/pausa o Play Mode para testar a simulação."""
    return unity.call("play_mode", {"action": action})


@mcp.tool()
def unity_execute_menu_item(menu_path: str) -> dict:
    """Executa um item de menu do Editor, ex.: 'File/Save', 'Robotics/ROS Settings'."""
    return unity.call("execute_menu", {"path": menu_path})


@mcp.tool()
def unity_save_scene(path: str | None = None) -> dict:
    """Salva a cena ativa. Cena nova (sem arquivo) exige path, ex.: 'Assets/Scenes/Robot.unity'."""
    return unity.call("save_scene", _clean({"path": path}))


# --------------------------------------------------------------------------- Unity: cena


@mcp.tool()
def unity_get_hierarchy(root: str | None = None, depth: int = 3, include_components: bool = True) -> list:
    """Hierarquia da cena (ou de um objeto por caminho 'Robo/base_link'), com componentes."""
    return unity.call("get_hierarchy", _clean({"root": root, "depth": depth, "includeComponents": include_components}))


@mcp.tool()
def unity_set_transform(
    object_path: str,
    position: Vec3 | None = None,
    rotation_euler: Vec3 | None = None,
    scale: Vec3 | None = None,
    local: bool = False,
) -> dict:
    """Move/rotaciona/escala um GameObject (coordenadas Unity: Y para cima)."""
    return unity.call(
        "set_transform",
        _clean({"object": object_path, "position": position, "rotation": rotation_euler, "scale": scale, "local": local}),
    )


@mcp.tool()
def unity_add_component(object_path: str, component_type: str, fields: dict[str, Any] | None = None) -> dict:
    """Adiciona um componente (nome curto ou completo, ex.: 'JointStatePublisher') e define campos
    públicos. Referências a objetos/componentes aceitam o caminho do GameObject como string.
    Se o componente já existir, só atualiza os campos."""
    return unity.call("add_component", {"object": object_path, "type": component_type, "fields": fields or {}})


@mcp.tool()
def unity_create_prefab(object_path: str, asset_path: str) -> dict:
    """Salva um GameObject da cena como prefab, ex.: 'Assets/Prefabs/MeuRobo.prefab'."""
    return unity.call("create_prefab", {"object": object_path, "path": asset_path})


# --------------------------------------------------------------------------- Unity: robótica


@mcp.tool()
def unity_import_urdf(
    urdf_path: str,
    axis: Literal["y", "z"] = "y",
    convex_method: Literal["vhacd", "unity"] = "vhacd",
    remove_demo_controller: bool = True,
) -> dict:
    """Importa um URDF com o URDF Importer, criando a hierarquia de ArticulationBody.
    Se o arquivo estiver fora de Assets, a pasta inteira (urdf + meshes) é copiada para
    Assets/URDF/<pasta>. axis='z' para meshes Z-up que chegam deitadas.
    remove_demo_controller tira o Controller de teclado que o importer adiciona (ele reescreve
    os drives de todas as juntas no Start e quebra com o Input System)."""
    return unity.call(
        "import_urdf",
        {"path": urdf_path, "axis": axis, "convexMethod": convex_method, "removeDemoController": remove_demo_controller},
        timeout=600,
    )


@mcp.tool()
def unity_inspect_robot(root: str) -> dict:
    """Relatório do robô: cada ArticulationBody (tipo de junta, limites, drive, massa, colliders)
    e avisos de especialista (forceLimit zero, razão de massa, root não fixo, etc.)."""
    return unity.call("inspect_robot", {"root": root})


@mcp.tool()
def unity_configure_joints(
    root: str,
    name_filter: str = ".*",
    stiffness: float | None = None,
    damping: float | None = None,
    force_limit: float | None = None,
    joint_friction: float | None = None,
    drive_type: Literal["force", "acceleration", "target", "velocity"] | None = None,
    linear_damping: float | None = None,
    angular_damping: float | None = None,
    use_gravity: bool | None = None,
    immovable_root: bool | None = None,
    solver_iterations: int | None = None,
) -> dict:
    """Ajusta em lote os drives das juntas cujo nome casa com `name_filter` (regex), ex.:
    '.*wheel.*'. Veja o resource robotics://guide/articulation-tuning para valores típicos."""
    try:
        re.compile(name_filter)
    except re.error as exc:
        raise ToolError(f"name_filter não é uma regex válida: {exc}") from exc
    return unity.call(
        "configure_joints",
        _clean(
            {
                "root": root,
                "nameFilter": name_filter,
                "stiffness": stiffness,
                "damping": damping,
                "forceLimit": force_limit,
                "jointFriction": joint_friction,
                "driveType": drive_type,
                "linearDamping": linear_damping,
                "angularDamping": angular_damping,
                "useGravity": use_gravity,
                "immovableRoot": immovable_root,
                "solverIterations": solver_iterations,
            }
        ),
    )


@mcp.tool()
def unity_set_physics(
    fixed_timestep: float | None = None,
    solver_iterations: int | None = None,
    solver_velocity_iterations: int | None = None,
    gravity: Vec3 | None = None,
    default_contact_offset: float | None = None,
) -> dict:
    """Configura a física global (Time.fixedDeltaTime, iterações do solver, gravidade)."""
    return unity.call(
        "set_physics",
        _clean(
            {
                "fixedTimestep": fixed_timestep,
                "solverIterations": solver_iterations,
                "solverVelocityIterations": solver_velocity_iterations,
                "gravity": gravity,
                "contactOffset": default_contact_offset,
            }
        ),
    )


@mcp.tool()
def unity_import_files(source_path: str, destination: str = "Assets/Imported") -> dict:
    """Copia um arquivo ou pasta do disco (ex.: FBX exportados do Blender) para dentro de Assets."""
    return unity.call("import_files", {"source": source_path, "destination": destination}, timeout=300)


@mcp.tool()
def unity_attach_blender_visual(
    link_path: str,
    model_asset_path: str,
    align_to_existing: bool = True,
    hide_existing: bool = True,
) -> dict:
    """Coloca o modelo do Blender (FBX/GLB já em Assets) como visual do link do robô.
    Com align_to_existing, testa as 24 rotações de 90° e a escala para casar com o mesh STL
    importado do URDF; depois esconde os renderers antigos (colisões não são alteradas)."""
    return unity.call(
        "attach_visual",
        {"link": link_path, "asset": model_asset_path, "align": align_to_existing, "hideExisting": hide_existing},
    )


@mcp.tool()
def unity_setup_ros_connection(
    ros_ip: str = "127.0.0.1",
    ros_port: int = 10000,
    show_hud: bool = True,
    connect_on_start: bool = True,
) -> dict:
    """Cria/atualiza o ROSConnection na cena com IP/porta do ROS-TCP-Endpoint."""
    return unity.call(
        "setup_ros_connection",
        {"ip": ros_ip, "port": ros_port, "showHud": show_hud, "connectOnStart": connect_on_start},
    )


@mcp.tool()
def unity_generate_ros_messages(path: str, output_path: str = "Assets/RosMessages") -> dict:
    """Gera classes C# a partir de .msg/.srv/.action (arquivo ou pasta de um pacote ROS)."""
    return unity.call("generate_messages", {"path": path, "outputPath": output_path}, timeout=300)


@mcp.tool()
def unity_create_robot_script(
    template: Literal["joint_state_publisher", "joint_command_subscriber", "diff_drive_cmd_vel", "camera_publisher"],
    class_name: str | None = None,
    topic: str | None = None,
    rate_hz: float | None = None,
    folder: str = "Assets/Robotics/Scripts",
) -> dict:
    """Cria um script C# pronto de integração ROS a partir de um template e importa no projeto.
    Depois espere a compilação (unity_status) e use unity_add_component."""
    name = class_name or "".join(p.capitalize() for p in template.split("_"))
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", name):
        raise ToolError(f"Nome de classe inválido: {name}")
    code = render_template(template, name, topic, rate_hz)
    result = unity.call("write_script", {"path": f"{folder.rstrip('/')}/{name}.cs", "content": code})
    return {**result, "class": name, "about": TEMPLATES[template]["description"]}


@mcp.tool()
def unity_write_script(asset_path: str, content: str) -> dict:
    """Escreve qualquer arquivo de código/texto em Assets (ex.: um controlador customizado) e importa."""
    if not asset_path.startswith("Assets/"):
        raise ToolError("asset_path deve começar com 'Assets/'")
    return unity.call("write_script", {"path": asset_path, "content": content})


# --------------------------------------------------------------------------- Offline


@mcp.tool()
def validate_urdf(urdf: str, check_meshes: bool = True) -> dict:
    """Valida um URDF (caminho ou XML): árvore, juntas, limites, massas/inércias, formatos de mesh
    suportados pelo URDF Importer e se os arquivos package:// existem."""
    return _validate_urdf(urdf, check_meshes)


@mcp.tool()
def blender_urdf_conventions() -> str:
    """Como montar a hierarquia e as propriedades customizadas no Blender para exportar URDF."""
    return BLENDER_CONVENTIONS


@mcp.tool()
def blender_export_script(
    robot_name: str,
    output_dir: str,
    root_object: str | None = None,
    export_fbx_visuals: bool = True,
    default_mass: float = 1.0,
) -> str:
    """Gera um script Python (bpy) que exporta o robô do Blender para URDF + meshes STL
    (geometria/colisão) + FBX por link (estética). Rode no Blender (Scripting ou MCP do Blender).
    Sem root_object, usa os objetos selecionados ou a coleção ativa."""
    if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_\-]*", robot_name):
        raise ToolError("robot_name deve ser um identificador sem espaços (ex.: meu_robo)")
    return build_blender_export_script(robot_name, output_dir, root_object, export_fbx_visuals, default_mass)


@mcp.tool()
def ros_endpoint_setup(ros_version: Literal["ros1", "ros2"] = "ros2") -> str:
    """Comandos para subir o ROS-TCP-Endpoint no lado ROS (nativo, WSL2 ou Docker)."""
    drop = "ROS 1" if ros_version == "ros2" else "ROS 2"
    return "\n\n".join(b for b in GUIDES["ros-setup"].split("\n\n") if not b.startswith(drop))


@mcp.tool()
def robotics_guide(topic: Literal["workflow", "articulation-tuning", "ros-setup", "blender-to-unity", "troubleshooting"]) -> str:
    """Guia especialista sobre um tema de Unity Robotics."""
    return GUIDES[topic]


# --------------------------------------------------------------------------- Resources e prompts


def _register_guide(name: str) -> None:
    @mcp.resource(f"robotics://guide/{name}", name=name, mime_type="text/markdown")
    def _guide() -> str:
        return GUIDES[name]


for _name in GUIDES:
    _register_guide(_name)


@mcp.prompt()
def setup_robot_from_blender(
    robot_name: str,
    export_dir: str,
    robot_type: str = "braço robótico",
    ros_version: str = "ROS 2",
) -> str:
    """Passo a passo completo: Blender -> URDF -> Unity -> ROS."""
    return PROMPTS["setup_robot_from_blender"].format(
        robot_name=robot_name, export_dir=export_dir, robot_type=robot_type, ros_version=ros_version
    )


def main() -> None:
    mcp.run()


if __name__ == "__main__":
    main()
