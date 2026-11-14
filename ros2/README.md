# ExpoTech — lado ROS 2

Container ROS 2 Humble com o **ROS-TCP-Endpoint** (ponte com o Unity, porta 10000) e o
**robot_state_publisher** lendo o mesmo URDF do Unity (`../RobotDescription/expo_drone`).

## Subir

Docker Desktop precisa estar rodando (engine Linux / WSL 2).

```powershell
cd C:\dev\ExpoTech\ros2
docker compose up --build -d
docker compose logs -f
```

No Unity: abra `Assets/Scenes/ExpoTech_DroneTest.unity` e dê Play. O HUD do ROSConnection
(canto superior esquerdo) fica verde quando conecta em `127.0.0.1:10000`.

## Testar

```powershell
docker exec -it expotech_ros2 bash
source /opt/ros/humble/setup.bash
ros2 topic list
ros2 topic hz /odom
ros2 topic pub -r 10 /cmd_vel geometry_msgs/msg/Twist "{linear: {x: 0.3}, angular: {z: 0.0}}"
```

| Tópico | Tipo | Direção | Taxa medida |
|---|---|---|---|
| `/cmd_vel` | geometry_msgs/Twist | ROS → Unity | — |
| `/expo_drone/jump` | std_msgs/Empty | ROS → Unity | — |
| `/expo_drone/avoidance_enable` | std_msgs/Bool | ROS → Unity | — |
| `/expo_drone/autopilot_enable` | std_msgs/Bool | ROS → Unity | — |
| `/expo_drone/reset` | std_msgs/Empty | ROS → Unity | — |
| `/expo_drone/autopilot_state` | std_msgs/String | Unity → ROS | na mudança + 1 Hz |
| `/odom` | nav_msgs/Odometry | Unity → ROS | ~30 Hz |
| `/tf` (odom → base_link) | tf2_msgs/TFMessage | Unity → ROS | ~30 Hz |
| `/joint_states` | sensor_msgs/JointState | Unity → ROS | ~27 Hz |
| `/scan` (LiDAR 360°, 20 m) | sensor_msgs/LaserScan | Unity → ROS | ~8 Hz |
| `/imu` | sensor_msgs/Imu | Unity → ROS | ~31 Hz |
| `/expo_drone/range_front` (ultrassom) | sensor_msgs/Range | Unity → ROS | ~16 Hz |
| `/expo_drone/grounded` | std_msgs/Bool | Unity → ROS | ~10 Hz |
| `/expo_drone/avoidance_state` | std_msgs/String | Unity → ROS | na mudança + 2 Hz |
| `/expo_drone/camera/image/compressed` | sensor_msgs/CompressedImage | Unity → ROS | ~10 Hz |

Pular e ligar/desligar o assistente de desvio (use `-w 1` para esperar o Unity assinar o tópico):

```bash
ros2 topic pub -w 1 --once /expo_drone/jump std_msgs/msg/Empty '{}'
ros2 topic pub -w 1 --once /expo_drone/avoidance_enable std_msgs/msg/Bool '{data: false}'
ros2 topic pub -w 1 --once /expo_drone/autopilot_enable std_msgs/msg/Bool '{data: true}'
ros2 topic pub -w 1 --once /expo_drone/reset std_msgs/msg/Empty '{}'
```

No Unity, os mesmos comandos estão no painel (canto superior direito) e no teclado:
M = andar sozinho, Espaço = pular, T = sensores, C = câmera, R = reposicionar.

## Testar sem ROS

`tools/fake_endpoint.py` imita o endpoint (protocolo ROS 2): lista o que o Unity registra,
mede as taxas, envia `/cmd_vel` e decodifica `/odom`.

```powershell
python tools\fake_endpoint.py --seconds 9 --drive
```

Rode e dê Play no Unity em seguida. Não rode junto com o container (ambos usam a porta 10000).

## Observações

- O Unity precisa continuar simulando sem foco: `Run In Background` já está ligado pelo menu
  ExpoTech > Setup cena de teste. Se o Play Mode congelar com o Unity em segundo plano, deixe a
  janela do Unity em foco (veja o relatório).
- O timestamp das mensagens usa o relógio do sistema. Para `use_sim_time`, marque `useSimTime`
  nos componentes de odometria, joint states e câmera.
