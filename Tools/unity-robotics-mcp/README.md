# Unity Robotics MCP

Servidor MCP especialista em **Unity Robotics Hub** (URDF Importer, ROS-TCP-Connector,
ArticulationBody) com uma ponte para o Unity Editor e um exportador **Blender → URDF**.

```
Claude / cliente MCP ──stdio──> servidor Python (server/)
                                   │  ferramentas offline: validate_urdf, blender_export_script, guias
                                   └─TCP 127.0.0.1:8765──> Unity Editor (unity-package/com.robotics.mcp-bridge)
                                                             importa URDF, ajusta juntas, ROSConnection, scripts…
Blender ──script bpy gerado──> <robo>.urdf + meshes STL + FBX por link
```

## 1. Instalar o servidor MCP (Python 3.10+)

> No Windows, mantenha a pasta do projeto num caminho **curto** (ex.: `C:\dev\unity-robotics-mcp`).
> Caminhos muito longos fazem DLLs do Python (cryptography) falharem ao carregar.

```powershell
cd C:\dev\unity-robotics-mcp\server
python -m venv .venv
.venv\Scripts\python -m pip install -e .
```

## 2. Instalar a ponte no projeto Unity

Unity Hub → abra o projeto do robô → **Window > Package Manager > + > Install package from disk…**
→ selecione `unity-package/com.robotics.mcp-bridge/package.json`.

Depois abra **Tools > Robotics MCP**: deve aparecer `Listening em 127.0.0.1:8765`.
Os pacotes do Robotics Hub podem ser instalados pelo próprio MCP (`unity_install_robotics_packages`).

## 3. Registrar no cliente MCP

**Claude Code** (terminal):

```bash
claude mcp add unity-robotics -- C:\dev\unity-robotics-mcp\server\.venv\Scripts\python.exe -m unity_robotics_mcp
```

**Claude Desktop / outros** (`claude_desktop_config.json` ou `.mcp.json`):

```json
{
  "mcpServers": {
    "unity-robotics": {
      "command": "C:\\dev\\unity-robotics-mcp\\server\\.venv\\Scripts\\python.exe",
      "args": ["-m", "unity_robotics_mcp"],
      "env": { "UNITY_MCP_PORT": "8765" }
    }
  }
}
```

## Ferramentas

| Grupo | Ferramenta | O que faz |
|---|---|---|
| Projeto | `unity_status` | versão, pacotes, protocolo ROS, compilação e erros |
| | `unity_install_robotics_packages` | instala ROS-TCP-Connector, URDF Importer, Visualizations |
| | `unity_set_ros_protocol` | ROS1 ↔ ROS2 (símbolo `ROS2`) |
| | `unity_console`, `unity_play_mode`, `unity_execute_menu_item`, `unity_save_scene` | operação do Editor |
| Cena | `unity_get_hierarchy`, `unity_set_transform`, `unity_add_component`, `unity_create_prefab` | manipulação genérica |
| Robô | `unity_import_urdf` | importa URDF (copia para `Assets/URDF` se estiver fora) |
| | `unity_inspect_robot` | relatório das juntas + avisos de especialista |
| | `unity_configure_joints` | stiffness / damping / forceLimit / driveType por regex |
| | `unity_set_physics` | fixedDeltaTime, iterações do solver, gravidade |
| | `unity_import_files`, `unity_attach_blender_visual` | traz FBX do Blender e alinha sozinho ao link |
| ROS | `unity_setup_ros_connection` | cria/configura o `ROSConnection` |
| | `unity_generate_ros_messages` | .msg/.srv/.action → C# |
| | `unity_create_robot_script` | `joint_state_publisher`, `joint_command_subscriber`, `diff_drive_cmd_vel`, `camera_publisher` |
| | `unity_write_script` | qualquer script customizado |
| Offline | `validate_urdf` | árvore, limites, inércia, meshes `package://` |
| | `blender_urdf_conventions`, `blender_export_script` | Blender → URDF + STL + FBX |
| | `ros_endpoint_setup`, `robotics_guide` | lado ROS e guias (tuning, troubleshooting) |

Também expõe os resources `robotics://guide/*` e o prompt `setup_robot_from_blender`.

## Fluxo Blender → Unity → ROS

1. **Blender**: monte o robô como hierarquia de parentesco; origem de cada peça no centro da
   junta; propriedades customizadas no filho (`joint_type`, `joint_axis`, `joint_lower`,
   `joint_upper`, `joint_effort`, `joint_velocity`, `mass`). Detalhes: `blender_urdf_conventions`.
2. Peça ao Claude: *"gere o script de exportação do robô `meu_robo` para `C:/dev/export`"* e
   rode no Blender (aba Scripting, ou pelo MCP do Blender).
3. *"valide o URDF e importe no Unity"* → `validate_urdf` + `unity_import_urdf`.
4. *"ajuste as juntas para um braço de 6 eixos e aplique os visuais FBX"* →
   `unity_configure_joints`, `unity_import_files`, `unity_attach_blender_visual` por link.
5. *"prepare ROS 2 com joint states e comando de juntas"* → `unity_set_ros_protocol`,
   `unity_setup_ros_connection`, `unity_create_robot_script`, `unity_add_component`,
   `ros_endpoint_setup`.

## Exemplo e teste rápido

`examples/demo_arm` é um braço de 2 juntas (URDF + STL) e `examples/demo_msgs` uma mensagem
customizada. Com o Unity aberto e a ponte escutando:

```powershell
cd C:\dev\unity-robotics-mcp\server
.venv\Scripts\python ..\examples\e2e_smoke_test.py C:\dev\unity-robotics-mcp\examples
```

O script importa o braço, ajusta as juntas, cria o ROSConnection, gera a mensagem, cria os
4 scripts de template e adiciona os componentes ao robô.

## Notas

- A ponte escuta só em `127.0.0.1`. Para mudar a porta: Tools > Robotics MCP e a variável
  `UNITY_MCP_PORT` no cliente.
- Durante a compilação o Unity recarrega o domínio e a ponte fica fora por alguns segundos;
  o cliente tenta de novo automaticamente por até 20 s.
- URDF Importer e ROS-TCP-Connector são acessados por reflexão: a ponte compila mesmo antes
  desses pacotes existirem no projeto.
