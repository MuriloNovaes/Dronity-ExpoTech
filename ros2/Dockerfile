# ROS 2 Humble + ROS-TCP-Endpoint (ponte com o Unity) + robot_state_publisher
FROM ros:humble-ros-base
SHELL ["/bin/bash", "-c"]

RUN apt-get update && apt-get install -y --no-install-recommends \
        git python3-colcon-common-extensions ros-humble-robot-state-publisher \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /ros2_ws
RUN git clone -b main-ros2 --depth 1 https://github.com/Unity-Technologies/ROS-TCP-Endpoint.git src/ros_tcp_endpoint \
    && source /opt/ros/humble/setup.bash \
    && colcon build

COPY entrypoint.sh /entrypoint.sh
RUN sed -i 's/\r$//' /entrypoint.sh && chmod +x /entrypoint.sh
ENTRYPOINT ["/entrypoint.sh"]
