using UnityEngine;

namespace ExpoTech
{
    /// <summary>
    /// Painel de controle na tela (canto superior direito) para a demonstração:
    /// andar sozinho / manual, pular, sensores automáticos e modo da câmera.
    /// </summary>
    public class ExpoDroneControlPanel : MonoBehaviour
    {
        public ExpoDroneDriveController drive;
        public ExpoDroneAutopilot autopilot;
        public ExpoDroneSmartAvoidance avoidance;
        public ExpoDroneJump jump;
        public ExpoCameraFollow cameraFollow;
        public ExpoDroneReset reset;
        public bool showHelp = true;

        GUIStyle box, button, title, text;

        void Start()
        {
            if (drive == null) drive = GetComponent<ExpoDroneDriveController>();
            if (autopilot == null) autopilot = GetComponent<ExpoDroneAutopilot>();
            if (avoidance == null) avoidance = GetComponent<ExpoDroneSmartAvoidance>();
            if (jump == null) jump = GetComponent<ExpoDroneJump>();
            if (reset == null) reset = GetComponent<ExpoDroneReset>();
            if (cameraFollow == null && Camera.main != null) cameraFollow = Camera.main.GetComponent<ExpoCameraFollow>();
        }

        void Styles()
        {
            if (box != null) return;
            box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 10, 10) };
            button = new GUIStyle(GUI.skin.button) { fontSize = 15, fixedHeight = 34 };
            title = new GUIStyle(GUI.skin.label) { fontSize = 17, fontStyle = FontStyle.Bold };
            title.normal.textColor = Color.white;
            text = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            text.normal.textColor = new Color(0.9f, 0.92f, 0.95f);
        }

        void OnGUI()
        {
            Styles();
            const float w = 300f;
            GUILayout.BeginArea(new Rect(Screen.width - w - 12f, 12f, w, Screen.height - 24f));
            GUILayout.BeginVertical(box);
            GUILayout.Label("EXPO DRONE", title);

            bool auto = autopilot != null && autopilot.active;
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = auto ? new Color(1f, 0.45f, 0.35f) : new Color(0.4f, 0.9f, 0.5f);
            if (autopilot != null && GUILayout.Button(auto ? "■  Parar modo autônomo  (M)" : "▶  Andar sozinho  (M)", button))
                autopilot.Toggle();
            GUI.backgroundColor = prev;

            if (jump != null && GUILayout.Button("Pular  (Espaço)", button)) jump.RequestJump();
            if (reset != null && GUILayout.Button("Reposicionar no início  (R)", button)) reset.ResetPose();

            if (avoidance != null)
            {
                bool on = avoidance.avoidanceEnabled;
                GUI.backgroundColor = on ? new Color(0.45f, 0.75f, 1f) : Color.gray;
                if (GUILayout.Button(on ? "Sensores automáticos: LIGADOS  (T)" : "Sensores automáticos: DESLIGADOS  (T)", button))
                    avoidance.avoidanceEnabled = !on;
                GUI.backgroundColor = prev;
            }

            if (cameraFollow != null && GUILayout.Button($"Câmera: {cameraFollow.mode}  (C)", button))
                cameraFollow.ToggleMode();

            GUILayout.Space(6f);
            string mode = auto ? "Autônomo" : "Manual";
            string source = drive != null ? drive.CommandSource : "-";
            float speed = drive != null ? drive.CommandLinear : 0f;
            GUILayout.Label($"Modo: {mode}   ·   comando: {source}", text);
            GUILayout.Label($"Velocidade comandada: {speed:0.0} m/s", text);
            if (auto) GUILayout.Label($"Piloto: {autopilot.State}", text);
            if (avoidance != null) GUILayout.Label($"Sensores: {avoidance.State}", text);

            if (showHelp)
            {
                GUILayout.Space(6f);
                GUILayout.Label("Manual: WASD / setas ou analógico do controle.\nEspaço ou botão A pula. Tecla ou botão assume o controle mesmo no modo autônomo.", text);
            }
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
    }
}
