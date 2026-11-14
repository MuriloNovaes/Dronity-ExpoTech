# Dronity · ExpoTech

Drone de reconhecimento tático (tema inspirado no universo de Rainbow Six) simulado na **Unity 6** com física real e integrado ao **ROS 2 Humble**.

- Arte modelada no Blender, montada sobre um robô físico (ArticulationBody: rodas com motor, peso e pulo por impulso).
- Sensores no ROS 2: LiDAR 360°, IMU, ultrassom frontal, câmera, contato com o chão, odometria e TF.
- Assistente inteligente que pula obstáculos baixos e desvia dos altos; modo autônomo guiado pelo LiDAR.
- Jogável à mão (teclado ou controle) com painel na tela.

## Requisitos

- Unity **6000.6.7f1** (URP)
- Docker Desktop (para o ROS 2)
- Git (a Unity baixa os pacotes do Robotics Hub direto do GitHub)

## Como rodar

1. **ROS 2:** na pasta `ros2`, rode `docker compose up -d` (a primeira vez constrói a imagem).
2. **Unity:** no Unity Hub, *Add > Add project from disk* e escolha esta pasta. Abra a cena `Assets/Scenes/ExpoTech_Arena.unity` e dê Play.
3. O HUD do ROS (canto superior esquerdo) fica verde quando conecta em `127.0.0.1:10000`.

Mantenha a janela da Unity em foco: sem foco o Play Mode pausa.

## Controles

| Ação | Teclado / controle | ROS 2 |
|---|---|---|
| Dirigir | WASD / setas · analógico | `/cmd_vel` |
| Andar sozinho | M · Start | `/expo_drone/autopilot_enable` |
| Pular | Espaço · botão A | `/expo_drone/jump` |
| Sensores automáticos | T | `/expo_drone/avoidance_enable` |
| Câmera seguir / topo | C | — |
| Reposicionar no início | R | `/expo_drone/reset` |

Comandos e tópicos do ROS 2: veja [`ros2/README.md`](ros2/README.md).

## Estrutura

| Pasta | Conteúdo |
|---|---|
| `Assets/ExpoTech` | scripts do drone, editor, arte (FBX), prefab e materiais de física |
| `Assets/Scenes` | `ExpoTech_Arena` (principal) e `ExpoTech_DroneTest` (chão plano) |
| `RobotDescription/expo_drone` | URDF do drone (fonte única, lido pela Unity e pelo ROS) |
| `RobotDescription/expo_drone_source` | arquivos originais do Blender e scripts originais |
| `ros2` | container ROS 2 Humble + ROS-TCP-Endpoint + robot_state_publisher; endpoint de teste |
| `Tools/unity-robotics-mcp` | servidor MCP e ponte da Unity usados para configurar o projeto |
| `Tools/rebuild_expo_drone.py` | remonta o drone na cena depois de mudar o URDF ou a arte |
| `Docs` | apresentação (PDF) e o gerador dela |
