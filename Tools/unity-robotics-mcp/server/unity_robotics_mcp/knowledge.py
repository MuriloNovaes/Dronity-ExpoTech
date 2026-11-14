"""Conhecimento especialista exposto como instruções, resources e prompts do MCP."""

from __future__ import annotations

PACKAGES = {
    "ros-tcp-connector": (
        "com.unity.robotics.ros-tcp-connector",
        "https://github.com/Unity-Technologies/ROS-TCP-Connector.git?path=/com.unity.robotics.ros-tcp-connector",
    ),
    "urdf-importer": (
        "com.unity.robotics.urdf-importer",
        "https://github.com/Unity-Technologies/URDF-Importer.git?path=/com.unity.robotics.urdf-importer",
    ),
    "visualizations": (
        "com.unity.robotics.visualizations",
        "https://github.com/Unity-Technologies/ROS-TCP-Connector.git?path=/com.unity.robotics.visualizations",
    ),
}

INSTRUCTIONS = """
Você é um especialista em robótica com Unity Robotics Hub (URDF Importer, ROS-TCP-Connector,
Visualizations) e ROS/ROS 2. Ferramentas `unity_*` agem no Unity Editor aberto via a ponte
"Robotics MCP Bridge"; as demais funcionam offline.

Fluxo recomendado para um robô novo vindo do Blender:
1. unity_status -> confirma conexão e pacotes. Faltando pacote: unity_install_robotics_packages.
2. No Blender: blender_urdf_conventions + blender_export_script (rode o script no Blender,
   p.ex. pelo MCP do Blender) -> gera <robo>.urdf, meshes STL e FBX com materiais.
3. validate_urdf no arquivo gerado; corrija erros antes de importar.
4. unity_import_urdf (copia para Assets/URDF e importa como ArticulationBody).
5. unity_inspect_robot -> leia os avisos; unity_configure_joints para stiffness/damping/forceLimit;
   unity_set_physics (fixedDeltaTime 0.01-0.02, solver iterations >= 10 para braços).
6. Estética: unity_attach_blender_visual para cada link com o FBX do Blender (auto-alinha ao STL).
7. ROS: unity_set_ros_protocol, unity_setup_ros_connection, ros_endpoint_setup (lado ROS),
   unity_create_robot_script (joint_state_publisher, joint_command_subscriber, diff_drive_cmd_vel,
   camera_publisher) e depois unity_add_component quando a compilação terminar.
8. unity_play_mode enter + unity_console para verificar.

Regras de ouro:
- Unity é Y-up mão-esquerda; ROS é Z-up mão-direita (x frente, y esquerda). Use
  ROSGeometry (`.To<FLU>()` / `.From<FLU>()`) ao converter vetores/quaternions manualmente.
- ArticulationDrive: target em GRAUS (revolute) ou metros (prismatic); targetVelocity em graus/s.
  Mensagens ROS usam radianos: converta com Mathf.Rad2Deg.
- Controle de posição: stiffness alto (1e4-1e5), damping ~ 2*sqrt(stiffness*massa_efetiva)/10,
  forceLimit = effort do URDF. Controle de velocidade (rodas): stiffness 0, damping alto.
- Razão de massa entre links vizinhos > 10:1 deixa o PhysX instável; links muito leves (<0.01 kg)
  também. Base de braço fixo: root ArticulationBody com immovable = true.
- Depois de criar scripts, o Unity recompila e recarrega o domínio: espere unity_status mostrar
  isCompiling=false e sem compileErrors antes de adicionar componentes.
- Nunca edite assets gerados pelo URDF Importer à mão; reimporte o URDF.
""".strip()

GUIDES: dict[str, str] = {
    "workflow": INSTRUCTIONS,
    "articulation-tuning": """
# Ajuste de ArticulationBody (PhysX)

| Situação | stiffness | damping | forceLimit | Observações |
|---|---|---|---|---|
| Braço industrial (posição) | 1e4 – 1e5 | 1e2 – 1e3 | effort do URDF | solverIterations 20+, root immovable |
| Garra / dedos | 1e3 – 1e4 | 1e2 | 10 – 50 | aumente friction dos colliders |
| Roda (velocidade) | 0 | 1e2 – 1e3 | torque do motor | colliders de roda: use Capsule/Sphere ou Mesh convexa |
| Junta passiva | 0 | 0.1 – 5 | 0 | jointFriction simula atrito |

- Oscilação/tremedeira: reduza stiffness OU aumente damping; reduza Time.fixedDeltaTime (0.005-0.01).
- Robô "explode" ao dar play: colliders sobrepostos entre links pai/filho (o URDF Importer desliga
  colisão entre pai e filho, mas não entre netos), massas/inércias absurdas, ou inertia tensor zero.
- Robô afunda/escorrega: massa total realista, Physic Material com friction 0.8-1.0 nas rodas.
- driveType (Unity 2022.2+): Force (padrão, usa stiffness/damping), Target, Velocity, Acceleration.
- matchAnchors e anchorRotation vêm do URDF; não mexa a menos que o eixo esteja errado no URDF.
""".strip(),
    "ros-setup": """
# Lado ROS (ROS-TCP-Endpoint)

ROS 2 (Humble/Jazzy):
    cd ~/ros2_ws/src
    git clone -b main-ros2 https://github.com/Unity-Technologies/ROS-TCP-Endpoint.git
    cd .. && colcon build && source install/setup.bash
    ros2 run ros_tcp_endpoint default_server_endpoint --ros-args -p ROS_IP:=0.0.0.0 -p ROS_TCP_PORT:=10000

ROS 1 (Noetic):
    cd ~/catkin_ws/src && git clone https://github.com/Unity-Technologies/ROS-TCP-Endpoint.git
    cd .. && catkin_make && source devel/setup.bash
    roslaunch ros_tcp_endpoint endpoint.launch tcp_ip:=0.0.0.0 tcp_port:=10000

No Windows com WSL2: o Unity conecta no IP do WSL (`wsl hostname -I`) ou use
`networkingMode=mirrored` no .wslconfig e então 127.0.0.1 funciona.
Docker: publique a porta (-p 10000:10000) e use o IP do host no ROSConnection.

Mensagens customizadas: aponte unity_generate_ros_messages para a pasta msg/srv/action do pacote;
o código vai para Assets/RosMessages com namespace RosMessageTypes.<Pacote>.
""".strip(),
    "blender-to-unity": "",  # preenchido em server.py com BLENDER_CONVENTIONS
    "troubleshooting": """
# Problemas comuns

- "ROSConnection: connection failed" no HUD: endpoint não está rodando, IP/porta errados, firewall
  do Windows bloqueando a porta 10000, ou protocolo ROS1/ROS2 diferente do endpoint.
- Tópico aparece mas sem dados: o tipo da mensagem no Unity difere do ROS (ex.: ROS1 vs ROS2 build).
- Erro de compilação 'RosMessageTypes.X not found': gere as mensagens (unity_generate_ros_messages).
- Meshes rosa no URP/HDRP: materiais do URDF usam Standard; converta com
  Edit > Rendering > Materials > Convert ou reimporte o FBX com o shader do pipeline.
- Robô importado deitado: escolha axis 'z' no import se o URDF/mesh for Z-up e as meshes estiverem
  vindo rotacionadas (meshes DAE com Y_UP às vezes precisam disso).
- Juntas ao contrário: o eixo do URDF aponta para o outro lado; inverta joint_axis no Blender ou use
  invertLeft/invertRight no diff drive.
- A ponte MCP não responde: Tools > Robotics MCP > Restart; ela desliga durante compilação.
""".strip(),
}

PROMPTS = {
    "setup_robot_from_blender": (
        "Configure o robô '{robot_name}' modelado no Blender para Unity Robotics. Siga o fluxo das "
        "instruções do servidor: verifique o Unity, gere e rode o script de exportação no Blender "
        "(saída em '{export_dir}'), valide o URDF, importe, ajuste as juntas para um robô do tipo "
        "'{robot_type}', aplique os visuais FBX e prepare a comunicação ROS ({ros_version})."
    ),
}
