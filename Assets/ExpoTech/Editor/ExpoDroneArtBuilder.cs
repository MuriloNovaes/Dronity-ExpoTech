using System.IO;
using UnityEditor;
using UnityEngine;

namespace ExpoTech.EditorTools
{
    /// <summary>
    /// Monta a arte do Blender (Drone.fbx) sobre o robô físico importado do URDF.
    /// Fluxo: 1) Remover robô atual  2) importar o URDF (MCP unity_import_urdf)  3) Aplicar arte do FBX
    /// 4) ExpoTech > Setup cena de teste.
    /// </summary>
    public static class ExpoDroneArtBuilder
    {
        const string ModelPath = ExpoModelProbe.ModelPath;
        const string MaterialFolder = "Assets/ExpoTech/Art/Drone/Materials";

        [MenuItem("ExpoTech/Drone final/1. Remover robô atual")]
        public static void RemoveRobot()
        {
            GameObject robot;
            while ((robot = GameObject.Find("expo_drone")) != null)
                Undo.DestroyObjectImmediate(robot);
            Debug.Log("[ExpoTech] Robô removido da cena.");
        }

        [MenuItem("ExpoTech/Drone final/3. Aplicar arte do FBX")]
        public static void ApplyArt()
        {
            var robot = GameObject.Find("expo_drone");
            if (robot == null) { Debug.LogError("[ExpoTech] expo_drone não está na cena. Importe o URDF antes."); return; }

            ExtractMaterials();
            ConfigureLedMaterial();

            var baseLink = FindLink(robot, "base_link");
            var leftWheel = FindLink(robot, "left_wheel_link");
            var rightWheel = FindLink(robot, "right_wheel_link");

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);

            // No FBX o eixo das rodas fica em X, na altura do pivô das rodas; a frente é +Z,
            // igual ao base_link importado (ROS x = Unity z). Alinha o centro do eixo ao base_link.
            float axleHeight = inst.transform.Find("Wheel_L").localPosition.y;
            inst.transform.rotation = baseLink.rotation;
            inst.transform.position = baseLink.position - baseLink.rotation * new Vector3(0f, axleHeight, 0f);

            Attach(inst, "Drone_Body", baseLink, "Art_Body");
            Attach(inst, "LEDs", baseLink, "Art_LEDs");
            // Com a frente em +Z, o "Wheel_R" do Blender (x negativo) é a roda ESQUERDA do robô.
            Attach(inst, "Wheel_R", leftWheel, "Art_Wheel");
            Attach(inst, "Wheel_L", rightWheel, "Art_Wheel");
            Object.DestroyImmediate(inst);

            // Esconde as primitivas do URDF (inclusive dos sensores, que ficariam rosa no URP).
            // A colisão (Collisions) não muda.
            foreach (var r in robot.GetComponentsInChildren<MeshRenderer>(true))
                if (!IsArt(r.transform) && r.GetComponentInParent<Collider>() == null)
                    r.enabled = false;

            Debug.Log("[ExpoTech] Arte do FBX aplicada ao expo_drone.");
        }

        static Transform FindLink(GameObject robot, string name)
        {
            foreach (var t in robot.GetComponentsInChildren<Transform>(true))
                if (t.name == name && t.GetComponent<ArticulationBody>() != null) return t;
            throw new System.Exception($"Link {name} não encontrado");
        }

        static void Attach(GameObject inst, string partName, Transform link, string newName)
        {
            var part = inst.transform.Find(partName);
            var visuals = link.Find("Visuals") ?? link;
            var old = visuals.Find(newName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            part.SetParent(visuals, true);
            part.name = newName;
            Undo.RegisterCreatedObjectUndo(part.gameObject, "ExpoTech art");
        }

        static bool IsArt(Transform t)
        {
            for (; t != null; t = t.parent)
                if (t.name.StartsWith("Art_")) return true;
            return false;
        }

        static void ExtractMaterials()
        {
            Directory.CreateDirectory(MaterialFolder);
            bool extracted = false;
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
            {
                if (obj is not Material m) continue;
                string dest = $"{MaterialFolder}/{m.name}.mat";
                if (File.Exists(dest)) continue;
                string err = AssetDatabase.ExtractAsset(m, dest);
                if (!string.IsNullOrEmpty(err)) Debug.LogWarning($"[ExpoTech] {m.name}: {err}");
                extracted = true;
            }
            if (!extracted) return;
            AssetDatabase.WriteImportSettingsIfDirty(ModelPath);
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceUpdate);
        }

        static void ConfigureLedMaterial()
        {
            var led = AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/LED.mat");
            if (led == null) return;
            led.EnableKeyword("_EMISSION");
            led.SetColor("_EmissionColor", new Color(1f, 0.1f, 0.05f) * 4f);
            led.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(led);
            AssetDatabase.SaveAssets();
        }
    }
}
