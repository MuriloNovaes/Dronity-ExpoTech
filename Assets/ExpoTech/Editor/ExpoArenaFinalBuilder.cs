using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ExpoTech.EditorTools
{
    /// <summary>
    /// Monta a cena ExpoTech_Final com a arena do espaço real (ArenaSource/expotech_arena.py -> ExpoTech_Arena.fbx):
    /// copia a cena da arena tática, troca o cenário, configura os colisores COL_*, coloca o drone no
    /// SPAWN_Drone virado para o norte da arena e acende os refletores. Pode ser rodado de novo
    /// depois de reexportar o FBX. Grava um relatório em Library/arena_final_report.txt.
    /// </summary>
    public static class ExpoArenaFinalBuilder
    {
        const string ArenaFbx = "Assets/ExpoTech/Art/Arena/ExpoTech_Arena.fbx";
        const string SourceScene = "Assets/Scenes/ExpoTech_Arena.unity";
        const string FinalScene = "Assets/Scenes/ExpoTech_Final.unity";
        const string ArenaName = "ExpoTech_ArenaFinal";

        [MenuItem("ExpoTech/Arena final/Montar cena ExpoTech_Final")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) { Debug.LogError("[ExpoTech] Saia do Play Mode antes de montar a arena."); return; }
            EditorSceneManager.SaveOpenScenes();
            if (!File.Exists(FinalScene)) AssetDatabase.CopyAsset(SourceScene, FinalScene);
            var scene = EditorSceneManager.OpenScene(FinalScene, OpenSceneMode.Single);

            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "TacticalArena" || root.name == "TestFloor" || root.name == ArenaName)
                    Object.DestroyImmediate(root);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ArenaFbx);
            if (prefab == null) { Debug.LogError($"[ExpoTech] {ArenaFbx} não encontrado."); return; }
            var arena = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            PrefabUtility.UnpackPrefabInstance(arena, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            arena.name = ArenaName;
            arena.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var ground = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>("Assets/ExpoTech/Physics/Ground.physicMaterial");
            int colliders = ConfigureColliders(arena, ground, out float floorTop);

            var report = new StringBuilder();
            var spawn = Find(arena, "SPAWN_Drone");
            var garagem = Find(arena, "ROOM_Garagem");
            var briefing = Find(arena, "ROOM_Briefing");
            Vector3 north = Vector3.forward;
            if (garagem != null && briefing != null)
            {
                north = briefing.position - garagem.position;
                north.y = 0f;
                north.Normalize();
            }

            var robot = GameObject.Find("expo_drone");
            if (robot != null && spawn != null)
            {
                robot.transform.SetPositionAndRotation(new Vector3(spawn.position.x, robot.transform.position.y, spawn.position.z),
                    Quaternion.LookRotation(north, Vector3.up));
                PlaceOnFloor(robot, floorTop);
            }

            int lights = AddFloodLights(arena);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.35f, 0.37f, 0.42f);

            var cam = Camera.main != null ? Camera.main.GetComponent<ExpoCameraFollow>() : null;
            if (cam != null && robot != null) { cam.target = robot.transform.Find("base_link"); cam.Snap(); }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            SetFirstInBuild(FinalScene);

            // Relatório para conferência
            var all = new Bounds();
            bool first = true;
            int renderers = 0;
            foreach (var r in arena.GetComponentsInChildren<Renderer>())
            {
                renderers++;
                if (first) { all = r.bounds; first = false; } else all.Encapsulate(r.bounds);
            }
            report.AppendLine($"arena bounds: centro {V(all.center)} tamanho {V(all.size)}");
            report.AppendLine($"renderers: {renderers} | colisores COL_: {colliders} | topo do piso: {floorTop:0.###}");
            report.AppendLine($"spawn: {(spawn ? V(spawn.position) : "não encontrado")} | norte da arena: {V(north)}");
            if (robot != null)
                report.AppendLine($"drone: posição {V(robot.transform.position)} yaw {robot.transform.eulerAngles.y:0.#}");
            foreach (var name in new[] { "ROOM_Garagem", "ROOM_Briefing", "ROOM_Corredor", "ROOM_Objetivo", "ROOM_Deposito", "OBJ_Bomba" })
            {
                var t = Find(arena, name);
                report.AppendLine($"{name}: {(t ? V(t.position) : "-")}");
            }
            foreach (var c in arena.GetComponentsInChildren<BoxCollider>())
                report.AppendLine($"col {c.name}: centro {V(c.bounds.center)} tamanho {V(c.bounds.size)}");
            report.AppendLine($"luzes: {lights}");
            File.WriteAllText("Library/arena_final_report.txt", report.ToString());
            Debug.Log($"[ExpoTech] Cena {FinalScene} montada: {colliders} colisores, drone no spawn.");
        }

        /// <summary>COL_* viram BoxCollider do tamanho da malha e perdem o renderer (mesma regra do ExpoTechArenaColliders).</summary>
        static int ConfigureColliders(GameObject arena, PhysicsMaterial ground, out float floorTop)
        {
            int n = 0;
            floorTop = 0f;
            foreach (var t in arena.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject;
                foreach (var mc in go.GetComponents<MeshCollider>()) Object.DestroyImmediate(mc);
                var mf = go.GetComponent<MeshFilter>();
                if (!go.name.StartsWith("COL_"))
                {
                    if (mf != null) GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                    continue;
                }
                if (mf == null || mf.sharedMesh == null) continue;
                // Não usar "??": GetComponent devolve um objeto nulo "falso" da Unity, que o ?? não reconhece.
                var box = go.GetComponent<BoxCollider>();
                if (box == null) box = go.AddComponent<BoxCollider>();
                box.center = mf.sharedMesh.bounds.center;
                box.size = mf.sharedMesh.bounds.size;
                var mr = go.GetComponent<MeshRenderer>();
                if (mr) Object.DestroyImmediate(mr);
                go.isStatic = true;
                if (go.name == "COL_Piso")
                {
                    if (ground != null) box.sharedMaterial = ground;
                    floorTop = box.bounds.max.y;
                }
                n++;
            }
            return n;
        }

        static void PlaceOnFloor(GameObject robot, float floorTop)
        {
            var cols = robot.GetComponentsInChildren<Collider>();
            if (cols.Length == 0) return;
            float minY = float.MaxValue;
            foreach (var c in cols) minY = Mathf.Min(minY, c.bounds.min.y);
            robot.transform.position += Vector3.up * (floorTop + 0.005f - minY);
        }

        static int AddFloodLights(GameObject arena)
        {
            int n = 0;
            foreach (var t in arena.GetComponentsInChildren<Transform>(true))
            {
                if (!t.name.StartsWith("Refletor") || !t.name.EndsWith("_Lampada")) continue;
                var go = new GameObject(t.name + "_Luz");
                go.transform.SetParent(arena.transform, false);
                go.transform.position = t.position + Vector3.down * 0.3f;
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 26f;
                l.intensity = 2.5f;
                l.color = new Color(1f, 0.95f, 0.85f);
                n++;
            }
            return n;
        }

        static void SetFirstInBuild(string path)
        {
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(path, true) };
            foreach (var s in EditorBuildSettings.scenes) if (s.path != path) list.Add(s);
            EditorBuildSettings.scenes = list.ToArray();
        }

        static Transform Find(GameObject root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        static string V(Vector3 v) =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.##}, {1:0.##}, {2:0.##})", v.x, v.y, v.z);
    }
}
