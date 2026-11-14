using System.IO;
using UnityEditor;
using UnityEngine;

namespace ExpoTech.EditorTools
{
    /// <summary>
    /// Setup repetível da cena de teste do drone. Rode de novo depois de reimportar o URDF
    /// ou trocar a arte: ExpoTech > Setup cena de teste.
    /// </summary>
    public static class ExpoTechSceneSetup
    {
        const string PhysicsFolder = "Assets/ExpoTech/Physics";

        [MenuItem("ExpoTech/Setup cena de teste")]
        public static void Setup()
        {
            // Simulação continua rodando com o Unity sem foco (RViz/terminal do ROS na frente).
            PlayerSettings.runInBackground = true;
            // O template URP desliga o reload de domínio ao dar Play. O ROSConnection é um singleton
            // estático e não foi feito para sobreviver entre sessões: religa o reload.
            EditorSettings.enterPlayModeOptionsEnabled = false;

            var wheel = Material("Wheel", 1.0f, PhysicsMaterialCombine.Maximum);
            var caster = Material("Caster", 0.0f, PhysicsMaterialCombine.Minimum);
            var ground = Material("Ground", 0.8f, PhysicsMaterialCombine.Average);

            var floor = GameObject.Find("TestFloor");
            if (floor == null)
            {
                floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                floor.name = "TestFloor";
                floor.transform.localScale = new Vector3(2f, 1f, 2f); // 20 x 20 m
                Undo.RegisterCreatedObjectUndo(floor, "ExpoTech floor");
            }
            floor.GetComponent<Collider>().sharedMaterial = ground;

            var robot = GameObject.Find("expo_drone");
            if (robot != null)
            {
                Assign(robot, "left_wheel_link", wheel);
                Assign(robot, "right_wheel_link", wheel);
                Assign(robot, "caster_link", caster);
                RemoveImporterController(robot);
                PlaceOnFloor(robot);
            }

            var cam = Camera.main;
            if (cam != null && robot != null)
            {
                var baseLink = robot.transform.Find("base_link");
                var follow = cam.GetComponent<ExpoCameraFollow>() ?? Undo.AddComponent<ExpoCameraFollow>(cam.gameObject);
                follow.target = baseLink;
                cam.transform.position = baseLink.position + new Vector3(0f, 3f, -6f);
                cam.transform.LookAt(baseLink.position);
            }
            Debug.Log("[ExpoTech] Cena de teste configurada.");
        }

        static PhysicsMaterial Material(string name, float friction, PhysicsMaterialCombine combine)
        {
            Directory.CreateDirectory(PhysicsFolder);
            string path = $"{PhysicsFolder}/{name}.physicMaterial";
            var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (mat == null)
            {
                mat = new PhysicsMaterial(name);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.dynamicFriction = friction;
            mat.staticFriction = friction;
            mat.frictionCombine = combine;
            mat.bounciness = 0f;
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        /// <summary>
        /// O URDF Importer adiciona um Controller de demonstração que reescreve stiffness/damping
        /// de todas as juntas no Start (prende as rodas) e lê o Input antigo (exceção com o Input System).
        /// </summary>
        static void RemoveImporterController(GameObject robot)
        {
            foreach (var c in robot.GetComponents<MonoBehaviour>())
                if (c != null && c.GetType().FullName == "Unity.Robotics.UrdfImporter.Control.Controller")
                    Undo.DestroyObjectImmediate(c);
        }

        /// <summary>Apoia o robô no chão (y = 0): a parte mais baixa das colisões fica 5 mm acima.</summary>
        static void PlaceOnFloor(GameObject robot)
        {
            var cols = robot.GetComponentsInChildren<Collider>();
            if (cols.Length == 0) return;
            float minY = float.MaxValue;
            foreach (var c in cols) minY = Mathf.Min(minY, c.bounds.min.y);
            Undo.RecordObject(robot.transform, "ExpoTech place robot");
            robot.transform.position += Vector3.up * (0.005f - minY);
        }

        static void Assign(GameObject robot, string linkName, PhysicsMaterial mat)
        {
            foreach (var t in robot.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != linkName) continue;
                var collisions = t.Find("Collisions");
                if (collisions == null) return;
                foreach (var c in collisions.GetComponentsInChildren<Collider>(true))
                {
                    Undo.RecordObject(c, "ExpoTech physics material");
                    c.sharedMaterial = mat;
                }
                return;
            }
        }
    }
}
