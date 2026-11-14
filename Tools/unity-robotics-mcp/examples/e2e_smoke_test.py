import json, sys, time
from unity_robotics_mcp import server as s
SAMPLE = sys.argv[1]
def show(label, value): print(f"== {label}:", json.dumps(value, ensure_ascii=False)[:900], flush=True)
def wait_ready(max_s=900):
    t0 = time.time()
    while time.time() - t0 < max_s:
        try:
            st = s.unity.call("ping", retry_for=0)
            if not st["isCompiling"]: return st
        except Exception: pass
        time.sleep(3)
    raise SystemExit("Unity não ficou pronto")

st = wait_ready(); show("status", st)
show("import_urdf", s.unity_import_urdf(SAMPLE + r"\demo_arm\demo_arm.urdf"))
show("hierarchy", s.unity_get_hierarchy("demo_arm", depth=2, include_components=True))
show("configure", s.unity_configure_joints("demo_arm", stiffness=10000, damping=100, force_limit=50, immovable_root=True, solver_iterations=20))
show("physics", s.unity_set_physics(fixed_timestep=0.01, solver_iterations=12, solver_velocity_iterations=4))
show("inspect", s.unity_inspect_robot("demo_arm"))
show("ros_conn", s.unity_setup_ros_connection("127.0.0.1", 10000))
show("import_obj", s.unity_import_files(SAMPLE + r"\visuals", "Assets/Models"))
show("attach", s.unity_attach_blender_visual("demo_arm/base_link/link1", "Assets/Models/visuals/link1_visual.obj"))
show("msgs", s.unity_generate_ros_messages(SAMPLE + r"\demo_msgs"))
for t in ["joint_state_publisher", "joint_command_subscriber", "diff_drive_cmd_vel", "camera_publisher"]:
    show("script " + t, s.unity_create_robot_script(t))
time.sleep(5); st = wait_ready(); show("compileErrors", st["compileErrors"])
show("add_jsp", s.unity_add_component("demo_arm", "JointStatePublisher", {"publishRateHz": 50, "robotRoot": "demo_arm"}))
show("add_cmd", s.unity_add_component("demo_arm", "JointCommandSubscriber", {}))
show("save", s.unity_execute_menu_item("File/Save Project"))
show("errors", s.unity_console(30, errors_only=True))
