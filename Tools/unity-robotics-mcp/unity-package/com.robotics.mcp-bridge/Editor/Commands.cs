using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoboticsMcp
{
    /// <summary>Comandos gerais do Editor. Os de robótica estão em RoboticsCommands.cs.</summary>
    public static partial class Commands
    {
        static readonly Dictionary<string, Func<Args, object>> handlers = new Dictionary<string, Func<Args, object>>
        {
            { "ping", Ping },
            { "install_packages", InstallPackages },
            { "get_console", a => BridgeServer.RecentLogs(a.Int("count", 50), a.Bool("errorsOnly")) },
            { "play_mode", PlayMode },
            { "execute_menu", a => new Dictionary<string, object> { { "executed", EditorApplication.ExecuteMenuItem(a.Required("path")) } } },
            { "save_scene", SaveScene },
            { "get_hierarchy", GetHierarchy },
            { "set_transform", SetTransform },
            { "add_component", AddComponent },
            { "create_prefab", CreatePrefab },
            { "write_script", WriteScript },
            { "import_files", ImportFiles },
            { "import_urdf", ImportUrdf },
            { "inspect_robot", InspectRobot },
            { "configure_joints", ConfigureJoints },
            { "set_physics", SetPhysics },
            { "attach_visual", AttachVisual },
            { "setup_ros_connection", SetupRosConnection },
            { "set_ros_protocol", SetRosProtocol },
            { "generate_messages", GenerateMessages },
        };

        public static object Run(string command, Args args)
        {
            if (!handlers.TryGetValue(command, out var handler))
                throw new ArgumentException($"Comando desconhecido '{command}'. Disponíveis: {string.Join(", ", handlers.Keys)}");
            return handler(args);
        }

        // ---------------------------------------------------------------- projeto

        static AddAndRemoveRequest packageRequest;

        static object Ping(Args a)
        {
            string manifest = File.Exists("Packages/manifest.json") ? File.ReadAllText("Packages/manifest.json") : "";
            var packages = new Dictionary<string, object>
            {
                { "ros-tcp-connector", new Dictionary<string, object> { { "inManifest", manifest.Contains("com.unity.robotics.ros-tcp-connector") }, { "loaded", RosType("ROSConnection") != null } } },
                { "urdf-importer", new Dictionary<string, object> { { "inManifest", manifest.Contains("com.unity.robotics.urdf-importer") }, { "loaded", UrdfType("UrdfRobotExtensions") != null } } },
                { "visualizations", new Dictionary<string, object> { { "inManifest", manifest.Contains("com.unity.robotics.visualizations") } } },
            };
            var result = new Dictionary<string, object>
            {
                { "unityVersion", Application.unityVersion },
                { "projectPath", Path.GetDirectoryName(Application.dataPath) },
                { "activeScene", SceneManager.GetActiveScene().path },
                { "isPlaying", EditorApplication.isPlaying },
                { "isCompiling", EditorApplication.isCompiling || EditorApplication.isUpdating },
                { "rosProtocol", CurrentDefines().Contains("ROS2") ? "ros2" : "ros1" },
                { "packages", packages },
                { "compileErrors", BridgeServer.CompileMessages() },
                { "physics", new Dictionary<string, object> { { "fixedDeltaTime", Time.fixedDeltaTime }, { "solverIterations", Physics.defaultSolverIterations }, { "solverVelocityIterations", Physics.defaultSolverVelocityIterations } } },
            };
            if (packageRequest != null)
                result["packageInstall"] = packageRequest.IsCompleted
                    ? (packageRequest.Status == StatusCode.Success ? "success" : "failed: " + packageRequest.Error?.message)
                    : "in progress";
            return result;
        }

        static object InstallPackages(Args a)
        {
            var toAdd = a.List("packages").Select(p => (string)p).ToArray();
            if (toAdd.Length == 0) throw new ArgumentException("Nenhum pacote informado");
            packageRequest = Client.AddAndRemove(toAdd, null);
            return new Dictionary<string, object>
            {
                { "started", true },
                { "packages", toAdd },
                { "note", "Instalação em andamento; o Unity vai recompilar. Acompanhe com unity_status (packageInstall)." },
            };
        }

        static object PlayMode(Args a)
        {
            switch (a.Required("action"))
            {
                case "enter": EditorApplication.isPlaying = true; break;
                case "exit": EditorApplication.isPlaying = false; break;
                case "pause": EditorApplication.isPaused = true; break;
                case "unpause": EditorApplication.isPaused = false; break;
                default: throw new ArgumentException("action deve ser enter|exit|pause|unpause");
            }
            return new Dictionary<string, object> { { "isPlaying", EditorApplication.isPlaying }, { "isPaused", EditorApplication.isPaused } };
        }

        static object SaveScene(Args a)
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Saia do Play Mode antes de salvar a cena");
            var scene = SceneManager.GetActiveScene();
            string path = a.Str("path", scene.path);
            // Cena sem nome abriria o diálogo "Save As" e travaria a ponte: exige um caminho.
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("A cena ainda não tem arquivo; informe path, ex.: 'Assets/Scenes/Robot.unity'");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            bool saved = EditorSceneManager.SaveScene(scene, path);
            return new Dictionary<string, object> { { "saved", saved }, { "path", path } };
        }

        static NamedBuildTarget CurrentTarget() =>
            NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);

        static List<string> CurrentDefines() =>
            PlayerSettings.GetScriptingDefineSymbols(CurrentTarget()).Split(';').Where(s => s.Length > 0).ToList();

        // ---------------------------------------------------------------- cena

        static object GetHierarchy(Args a)
        {
            int depth = a.Int("depth", 3);
            bool components = a.Bool("includeComponents", true);
            IEnumerable<GameObject> roots = a.Has("root")
                ? new[] { Util.Find(a.Str("root")) }
                : SceneManager.GetActiveScene().GetRootGameObjects();
            return roots.Select(r => Describe(r.transform, depth, components)).ToList<object>();
        }

        static Dictionary<string, object> Describe(Transform t, int depth, bool components)
        {
            var node = new Dictionary<string, object> { { "name", t.name }, { "path", Util.PathOf(t) } };
            if (!t.gameObject.activeSelf) node["active"] = false;
            if (components)
                node["components"] = t.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name).ToList();
            if (t.childCount > 0)
            {
                if (depth > 0)
                    node["children"] = Enumerable.Range(0, t.childCount).Select(i => (object)Describe(t.GetChild(i), depth - 1, components)).ToList();
                else
                    node["childCount"] = t.childCount;
            }
            return node;
        }

        static object SetTransform(Args a)
        {
            var go = Util.Find(a.Required("object"));
            var t = go.transform;
            Undo.RecordObject(t, "MCP Set Transform");
            bool local = a.Bool("local");
            var root = go.GetComponent<ArticulationBody>();

            if (a.Vec3("position") is Vector3 p) { if (local) t.localPosition = p; else t.position = p; }
            if (a.Vec3("rotation") is Vector3 r) { if (local) t.localEulerAngles = r; else t.eulerAngles = r; }
            if (a.Vec3("scale") is Vector3 s) t.localScale = s;
            // Root de articulação em play mode precisa de TeleportRoot para não "puxar" a cadeia.
            if (Application.isPlaying && root != null && root.isRoot) root.TeleportRoot(t.position, t.rotation);

            Util.MarkSceneDirty(go);
            return new Dictionary<string, object> { { "position", t.position }, { "eulerAngles", t.eulerAngles }, { "localScale", t.localScale } };
        }

        static object AddComponent(Args a)
        {
            var go = Util.Find(a.Required("object"));
            string typeName = a.Required("type");
            var type = Util.FindType(typeName);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
                throw new ArgumentException($"Componente '{typeName}' não encontrado. Se acabou de criar o script, espere a compilação (unity_status.isCompiling).");

            var comp = go.GetComponent(type);
            bool created = comp == null;
            if (created) comp = Undo.AddComponent(go, type);
            else Undo.RecordObject(comp, "MCP Set Fields");

            var applied = new List<object>();
            var missing = new List<object>();
            foreach (var kv in a.Dict("fields"))
            {
                if (Util.SetMember(comp, kv.Key, kv.Value)) applied.Add(kv.Key);
                else missing.Add(kv.Key);
            }
            EditorUtility.SetDirty(comp);
            Util.MarkSceneDirty(go);
            return new Dictionary<string, object> { { "component", type.FullName }, { "created", created }, { "fieldsSet", applied }, { "unknownFields", missing } };
        }

        static object CreatePrefab(Args a)
        {
            var go = Util.Find(a.Required("object"));
            string path = a.Required("path");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.UserAction);
            return new Dictionary<string, object> { { "path", AssetDatabase.GetAssetPath(prefab) } };
        }

        static object WriteScript(Args a)
        {
            string path = a.Required("path").Replace('\\', '/');
            if (!path.StartsWith("Assets/")) throw new ArgumentException("O caminho deve começar com Assets/");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            bool existed = File.Exists(path);
            File.WriteAllText(path, a.Required("content"));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return new Dictionary<string, object>
            {
                { "path", path },
                { "overwritten", existed },
                { "note", "O Unity vai compilar e recarregar o domínio; a ponte volta sozinha em alguns segundos." },
            };
        }

        static object ImportFiles(Args a)
        {
            string source = Path.GetFullPath(a.Required("source"));
            string destination = a.Str("destination", "Assets/Imported").Replace('\\', '/');
            if (!destination.StartsWith("Assets")) throw new ArgumentException("destination deve estar dentro de Assets");

            var copied = new List<object>();
            if (Directory.Exists(source))
            {
                string target = Path.Combine(destination, Path.GetFileName(source.TrimEnd('\\', '/')));
                CopyDirectory(source, target, copied);
            }
            else if (File.Exists(source))
            {
                Directory.CreateDirectory(destination);
                string target = Path.Combine(destination, Path.GetFileName(source));
                File.Copy(source, target, true);
                copied.Add(target.Replace('\\', '/'));
            }
            else throw new FileNotFoundException(source);

            AssetDatabase.Refresh();
            return new Dictionary<string, object> { { "copied", copied.Count }, { "files", copied.Take(50).ToList() } };
        }

        public static void CopyDirectory(string source, string target, List<object> copied)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source))
            {
                if (file.EndsWith(".meta")) continue;
                string dest = Path.Combine(target, Path.GetFileName(file));
                File.Copy(file, dest, true);
                copied?.Add(dest.Replace('\\', '/'));
            }
            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)), copied);
        }
    }
}
