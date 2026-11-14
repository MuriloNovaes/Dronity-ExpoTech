"""Remonta o expo_drone na cena aberta do Unity, do URDF até os sensores, pela ponte do MCP.

Use depois de mudar o URDF ou a arte (Drone.fbx). Precisa do Unity aberto no ExpoTech, FORA do Play Mode,
e de um Python com o pacote `mcp` (o venv do servidor em Tools/unity-robotics-mcp/server):
    python Tools/rebuild_expo_drone.py
"""

import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "Tools" / "unity-robotics-mcp" / "server"))
from unity_robotics_mcp import server as mcp  # noqa: E402

URDF = str(ROOT / "RobotDescription" / "expo_drone" / "expo_drone.urdf")
R = "expo_drone"
BASE = f"{R}/base_link"

COMPONENTS = [
    (R, "ExpoTech.ExpoDroneDriveController", {
        "leftWheel": f"{BASE}/left_wheel_link", "rightWheel": f"{BASE}/right_wheel_link",
        "wheelRadius": 0.55, "trackWidth": 2.136, "wheelDamping": 60, "wheelForceLimit": 120,
        "maxLinear": 3.0, "maxAngular": 2.0, "maxLinearAcceleration": 1.0, "maxAngularAcceleration": 2.0,
        "keyboardLinear": 2.5, "keyboardAngular": 1.5}),
    (R, "ExpoTech.ExpoDroneJump", {
        "wheels": [f"{BASE}/left_wheel_link", f"{BASE}/right_wheel_link"], "wheelRadius": 0.55, "jumpHeight": 2.5}),
    (R, "ExpoTech.ExpoDroneSmartAvoidance", {
        "jump": R, "drive": R, "baseBody": BASE, "statusLeds": f"{BASE}/Visuals/Art_LEDs"}),
    (R, "ExpoTech.ExpoDroneAutopilot", {"lidar": f"{BASE}/lidar_link", "baseBody": BASE}),
    (R, "ExpoTech.ExpoDroneReset", {}),
    (R, "ExpoTech.ExpoDroneControlPanel", {}),
    (R, "ExpoTech.ExpoDroneOdometryPublisher", {"baseBody": BASE}),
    (R, "ExpoDroneJointStatePublisher", {"robotRoot": R}),
    (f"{BASE}/camera_link", "UnityEngine.Camera", {"enabled": False, "fieldOfView": 70, "nearClipPlane": 0.05}),
    (f"{BASE}/camera_link", "ExpoDroneCameraPublisher", {
        "sensorCamera": f"{BASE}/camera_link", "width": 640, "height": 480, "frameId": "camera_link"}),
    (f"{BASE}/camera_link", "ExpoTech.ExpoDroneRangeSensor", {"robotRoot": R}),
    (f"{BASE}/lidar_link", "ExpoTech.ExpoDroneLidar", {"robotRoot": R}),
    (f"{BASE}/imu_link", "ExpoTech.ExpoDroneImu", {}),
]


def step(label, fn, *args, **kwargs):
    result = fn(*args, **kwargs)
    print(f"[ok] {label}")
    return result


def main():
    status = mcp.unity_status()
    if status["isPlaying"]:
        raise SystemExit("Saia do Play Mode antes de remontar o robô.")
    step("remover robô atual", mcp.unity_execute_menu_item, "ExpoTech/Drone final/1. Remover robô atual")
    report = step("importar URDF", mcp.unity_import_urdf, URDF)
    step("aplicar arte do FBX", mcp.unity_execute_menu_item, "ExpoTech/Drone final/3. Aplicar arte do FBX")
    step("rodas por velocidade", mcp.unity_configure_joints, R, name_filter=".*wheel.*", stiffness=0, damping=60,
         force_limit=120, joint_friction=0.05, solver_iterations=16)
    step("amortecimento do corpo", mcp.unity_configure_joints, R, name_filter="^base_link$", angular_damping=1.5)
    for obj, comp, fields in COMPONENTS:
        r = mcp.unity_add_component(obj, comp, fields)
        if r["unknownFields"]:
            raise SystemExit(f"{comp}: campos desconhecidos {r['unknownFields']}")
        print(f"[ok] {comp.split('.')[-1]} em {obj}")
    step("setup da cena", mcp.unity_execute_menu_item, "ExpoTech/Setup cena de teste")
    time.sleep(1)
    step("salvar cena", mcp.unity_save_scene)
    print(f"\nRobô: {report['bodies']} corpos, {report['totalMass']:.1f} kg. Avisos:")
    for w in mcp.unity_inspect_robot(R)["warnings"]:
        print("  -", w)


if __name__ == "__main__":
    main()
