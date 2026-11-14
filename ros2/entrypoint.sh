#!/bin/bash
# Sobe o robot_state_publisher (TFs a partir do URDF + /joint_states) e o ROS-TCP-Endpoint.
set -e
source /opt/ros/humble/setup.bash
source /ros2_ws/install/setup.bash

# O URDF vai por arquivo de parâmetros: passado com -p, o XML (":" e comentários) quebra o parser.
{
    echo "robot_state_publisher:"
    echo "  ros__parameters:"
    echo "    robot_description: |"
    sed 's/^/      /' /robot/expo_drone.urdf
} > /tmp/robot_state_publisher.yaml

ros2 run robot_state_publisher robot_state_publisher \
    --ros-args --params-file /tmp/robot_state_publisher.yaml &

exec ros2 run ros_tcp_endpoint default_server_endpoint \
    --ros-args -p ROS_IP:=0.0.0.0 -p ROS_TCP_PORT:=10000
