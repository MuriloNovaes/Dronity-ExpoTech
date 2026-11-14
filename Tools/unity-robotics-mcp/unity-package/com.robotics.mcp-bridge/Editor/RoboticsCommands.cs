using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace RoboticsMcp
{
    /// <summary>
    /// Comandos de robótica. URDF Importer e ROS-TCP-Connector são acessados por reflexão para a
    /// ponte compilar mesmo antes desses pacotes serem instalados e sobreviver a mudanças de versão.
    /// </summary>
    public static partial class Commands
    {
        const string UrdfNamespace = "Unity.Robotics.UrdfImporter";
        const string RosNamespace = "Unity.Robotics.ROSTCPConnector";
        const string UrdfPackage = "urdf-importer";
        const string RosPackage = "ros-tcp-connector";

        static Type UrdfType(string name) => Util.FindInNamespace(name, UrdfNamespace);
        static Type RosType(string name) => Util.FindInNamespace(name, RosNamespace);

        static Type Require(Type type, string name, string package) =>
            type ?? throw new InvalidOperationException($"Tipo {name} não encontrado. Instale o pacote {package} (unity_install_robotics_packages) e espere a compilação.");

        // ---------------------------------------------------------------- URDF

        static object ImportUrdf(Args a)
        {
            var extensions = Require(UrdfType("UrdfRobotExtensions"), "UrdfRobotExtensions", UrdfPackage);
            var settingsType = Require(UrdfType("ImportSettings"), "ImportSettings", UrdfPackage);

            string path = a.Required("path").Replace('\\', '/');
            string assetPath = EnsureInsideAssets(path);

            var settings = Activator.CreateInstance(settingsType);
            string axis = a.Str("axis", "y") == "z" ? "zAxis" : "yAxis";
            SetEnumField(settings, "chosenAxis", axis);
            SetEnumField(settings, "choosenAxis", axis); // grafia de versões antigas do pacote
            SetEnumField(settings, "convexMethod", a.Str("convexMethod", "vhacd") == "unity" ? "unity" : "vHACD");

            var before = new HashSet<GameObject>(SceneManager.GetActiveScene().GetRootGameObjects());
            GameObject robot = null;
            var routine = Util.InvokeLoose(extensions, "Create", Path.GetFullPath(assetPath).Replace('\\', '/'), settings) as IEnumerator;
            if (routine == null) throw new InvalidOperationException("UrdfRobotExtensions.Create não retornou um IEnumerator");
            int guard = 0;
            while (routine.MoveNext() && guard++ < 100000)
                if (routine.Current is GameObject go) robot = go;

            robot ??= SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => !before.Contains(g));
            if (robot == null) throw new InvalidOperationException("Import terminou mas nenhum robô novo apareceu na cena. Veja unity_console.");

            Undo.RegisterCreatedObjectUndo(robot, "MCP Import URDF");
            // O importer adiciona um Controller de demonstração que reescreve stiffness/damping de todas
            // as juntas no Start e lê o Input antigo (exceção em projetos com o Input System).
            bool removedController = false;
            if (a.Bool("removeDemoController", true))
                foreach (var c in robot.GetComponents<MonoBehaviour>())
                    if (c != null && c.GetType().FullName == UrdfNamespace + ".Control.Controller")
                    {
                        Object.DestroyImmediate(c);
                        removedController = true;
                    }
            Util.MarkSceneDirty(robot);
            var report = (Dictionary<string, object>)InspectRobot(new Args(new Dictionary<string, object> { { "root", Util.PathOf(robot.transform) } }));
            report["urdfAsset"] = assetPath;
            report["removedDemoController"] = removedController;
            return report;
        }

        /// <summary>O URDF Importer só funciona com arquivos dentro de Assets: copia a pasta inteira se preciso.</summary>
        static string EnsureInsideAssets(string path)
        {
            string projectRoot = Path.GetFullPath(".").Replace('\\', '/').TrimEnd('/') + "/";
            string full = Path.GetFullPath(path).Replace('\\', '/');
            if (!File.Exists(full)) throw new FileNotFoundException("URDF não encontrado: " + full);
            if (full.StartsWith(projectRoot + "Assets/")) return full.Substring(projectRoot.Length);

            string sourceDir = Path.GetDirectoryName(full);
            string target = "Assets/URDF/" + Path.GetFileName(sourceDir);
            CopyDirectory(sourceDir, target, null);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            return target + "/" + Path.GetFileName(full);
        }

        static void SetEnumField(object target, string field, string value)
        {
            var f = target.GetType().GetField(field);
            if (f == null || !f.FieldType.IsEnum) return;
            var match = Enum.GetNames(f.FieldType).FirstOrDefault(n => string.Equals(n, value, StringComparison.OrdinalIgnoreCase));
            if (match != null) f.SetValue(target, Enum.Parse(f.FieldType, match));
        }

        static string JointName(ArticulationBody body)
        {
            var urdfJoint = body.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name.StartsWith("UrdfJoint"));
            var name = urdfJoint != null ? Util.GetMember(urdfJoint, "jointName") as string : null;
            return string.IsNullOrEmpty(name) ? body.name : name;
        }

        // ---------------------------------------------------------------- inspeção

        static object InspectRobot(Args a)
        {
            var root = Util.Find(a.Required("root"));
            var bodies = root.GetComponentsInChildren<ArticulationBody>(true);
            if (bodies.Length == 0)
                throw new InvalidOperationException($"'{root.name}' não tem ArticulationBody. Importe com unity_import_urdf.");

            var warnings = new List<object>();
            var joints = new List<object>();
            float totalMass = 0;
            foreach (var b in bodies)
            {
                totalMass += b.mass;
                int colliders = b.GetComponentsInChildren<Collider>(true).Count(c => c.attachedArticulationBody == b);
                var info = new Dictionary<string, object>
                {
                    { "path", Util.PathOf(b.transform) },
                    { "joint", JointName(b) },
                    { "type", b.isRoot ? "root" : b.jointType.ToString() },
                    { "mass", b.mass },
                    { "colliders", colliders },
                    { "useGravity", b.useGravity },
                };
                if (b.isRoot)
                {
                    info["immovable"] = b.immovable;
                    info["solverIterations"] = b.solverIterations;
                }
                else if (b.jointType != ArticulationJointType.FixedJoint)
                {
                    var d = b.xDrive;
                    info["drive"] = new Dictionary<string, object>
                    {
                        { "stiffness", d.stiffness }, { "damping", d.damping }, { "forceLimit", d.forceLimit },
                        { "lowerLimit", d.lowerLimit }, { "upperLimit", d.upperLimit },
                        { "target", d.target }, { "targetVelocity", d.targetVelocity }, { "driveType", d.driveType.ToString() },
                    };
                    info["jointFriction"] = b.jointFriction;
                    info["motion"] = new Dictionary<string, object>
                    {
                        { "twistLock", b.twistLock.ToString() }, { "swingYLock", b.swingYLock.ToString() }, { "swingZLock", b.swingZLock.ToString() },
                        { "linearLockX", b.linearLockX.ToString() },
                    };
                    bool limitedShut = d.lowerLimit >= d.upperLimit;
                    if (b.jointType == ArticulationJointType.RevoluteJoint && b.twistLock != ArticulationDofLock.FreeMotion && limitedShut)
                        warnings.Add($"{JointName(b)}: rotação limitada a [{d.lowerLimit}, {d.upperLimit}] = junta travada. Para roda contínua use twistLock = FreeMotion.");
                    if (d.forceLimit <= 0) warnings.Add($"{JointName(b)}: forceLimit = 0, a junta não consegue se mover pelo drive.");
                    if (d.stiffness == 0 && d.damping == 0) warnings.Add($"{JointName(b)}: stiffness e damping = 0, junta solta (ok só se for passiva).");
                }

                var parent = b.transform.parent ? b.transform.parent.GetComponentInParent<ArticulationBody>() : null;
                if (parent != null && parent.mass > 0 && b.mass > 0)
                {
                    float ratio = Mathf.Max(parent.mass / b.mass, b.mass / parent.mass);
                    if (ratio > 10f) warnings.Add($"{b.name}: razão de massa {ratio:0.#}:1 com '{parent.name}' (>10 deixa o PhysX instável).");
                }
                if (b.mass < 0.01f) warnings.Add($"{b.name}: massa {b.mass} kg muito baixa.");
                if (colliders == 0 && !b.isRoot) warnings.Add($"{b.name}: sem colliders.");
                joints.Add(info);
            }

            var rootBody = bodies.FirstOrDefault(b => b.isRoot);
            if (rootBody != null && !rootBody.immovable)
                warnings.Add("Root não é immovable: correto para robô móvel; para braço fixo use unity_configure_joints(immovable_root=true).");
            if (Physics.defaultSolverIterations < 10 && bodies.Length > 4)
                warnings.Add($"Physics.defaultSolverIterations = {Physics.defaultSolverIterations}; cadeias longas pedem >= 10 (unity_set_physics).");
            if (Time.fixedDeltaTime > 0.02f)
                warnings.Add($"fixedDeltaTime = {Time.fixedDeltaTime}; use 0.01–0.02 para robôs.");

            return new Dictionary<string, object>
            {
                { "robot", Util.PathOf(root.transform) },
                { "bodies", bodies.Length },
                { "movableJoints", bodies.Count(b => !b.isRoot && b.jointType != ArticulationJointType.FixedJoint) },
                { "totalMass", totalMass },
                { "hasRosConnection", RosType("ROSConnection") != null && Object.FindAnyObjectByType(RosType("ROSConnection")) != null },
                { "joints", joints },
                { "warnings", warnings },
            };
        }

        // ---------------------------------------------------------------- juntas e física

        static object ConfigureJoints(Args a)
        {
            var root = Util.Find(a.Required("root"));
            var filter = new Regex(a.Str("nameFilter", ".*"), RegexOptions.IgnoreCase);
            var changed = new List<object>();

            foreach (var b in root.GetComponentsInChildren<ArticulationBody>(true))
            {
                if (b.isRoot)
                {
                    Undo.RecordObject(b, "MCP Configure Root");
                    if (a.Has("immovableRoot")) b.immovable = a.Bool("immovableRoot");
                    if (a.Has("solverIterations")) b.solverIterations = a.Int("solverIterations");
                    if (a.Has("solverIterations")) b.solverVelocityIterations = Mathf.Max(1, a.Int("solverIterations") / 2);
                }
                string name = JointName(b);
                if (!filter.IsMatch(name) && !filter.IsMatch(b.name)) continue;

                Undo.RecordObject(b, "MCP Configure Joints");
                if (a.Has("linearDamping")) b.linearDamping = a.Float("linearDamping");
                if (a.Has("angularDamping")) b.angularDamping = a.Float("angularDamping");
                if (a.Has("useGravity")) b.useGravity = a.Bool("useGravity");
                if (b.isRoot || b.jointType == ArticulationJointType.FixedJoint) continue;

                if (a.Has("jointFriction")) b.jointFriction = a.Float("jointFriction");
                b.xDrive = ApplyDrive(b.xDrive, a);
                if (b.jointType == ArticulationJointType.SphericalJoint)
                {
                    b.yDrive = ApplyDrive(b.yDrive, a);
                    b.zDrive = ApplyDrive(b.zDrive, a);
                }
                EditorUtility.SetDirty(b);
                changed.Add(name);
            }
            Util.MarkSceneDirty(root);
            return new Dictionary<string, object> { { "changed", changed }, { "count", changed.Count } };
        }

        static ArticulationDrive ApplyDrive(ArticulationDrive d, Args a)
        {
            if (a.Has("stiffness")) d.stiffness = a.Float("stiffness");
            if (a.Has("damping")) d.damping = a.Float("damping");
            if (a.Has("forceLimit")) d.forceLimit = a.Float("forceLimit");
            if (a.Has("driveType")) d.driveType = (ArticulationDriveType)Enum.Parse(typeof(ArticulationDriveType), a.Str("driveType"), true);
            return d;
        }

        static object SetPhysics(Args a)
        {
            if (a.Has("fixedTimestep")) Time.fixedDeltaTime = a.Float("fixedTimestep");
            if (a.Has("solverIterations")) Physics.defaultSolverIterations = a.Int("solverIterations");
            if (a.Has("solverVelocityIterations")) Physics.defaultSolverVelocityIterations = a.Int("solverVelocityIterations");
            if (a.Vec3("gravity") is Vector3 g) Physics.gravity = g;
            if (a.Has("contactOffset")) Physics.defaultContactOffset = a.Float("contactOffset");
            AssetDatabase.SaveAssets();
            return new Dictionary<string, object>
            {
                { "fixedDeltaTime", Time.fixedDeltaTime },
                { "solverIterations", Physics.defaultSolverIterations },
                { "solverVelocityIterations", Physics.defaultSolverVelocityIterations },
                { "gravity", Physics.gravity },
                { "contactOffset", Physics.defaultContactOffset },
            };
        }

        // ---------------------------------------------------------------- visual do Blender

        static object AttachVisual(Args a)
        {
            var link = Util.Find(a.Required("link")).transform;
            string assetPath = a.Required("asset");
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath)
                ?? throw new FileNotFoundException("Modelo não encontrado em Assets: " + assetPath + " (use unity_import_files antes)");

            // O URDF Importer cria <link>/Visuals/<visual>/<mesh>. Colisões ficam em <link>/Collisions.
            var visuals = link.Find("Visuals") ?? link;
            var existing = visuals.GetComponentsInChildren<MeshFilter>(true)
                .Where(mf => !IsUnder(mf.transform, link.Find("Collisions")) && mf.GetComponentInParent<ArticulationBody>() == link.GetComponent<ArticulationBody>())
                .ToList();

            var old = visuals.Find("BlenderVisual");
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, visuals);
            instance.name = "BlenderVisual";
            instance.transform.position = link.position;
            instance.transform.rotation = link.rotation;
            instance.transform.localScale = Vector3.one;

            var result = new Dictionary<string, object> { { "object", Util.PathOf(instance.transform) } };
            if (a.Bool("align", true) && existing.Count > 0)
            {
                var target = WorldBounds(existing);
                var source = WorldBounds(instance.GetComponentsInChildren<MeshFilter>(true));
                float scale = source.size.magnitude > 1e-6f ? target.size.magnitude / source.size.magnitude : 1f;
                // Corrige diferença de unidade (cm/m) do FBX, mas ignora ruído pequeno.
                if (Mathf.Abs(scale - 1f) > 0.05f) instance.transform.localScale = Vector3.one * scale;

                Quaternion best = link.rotation;
                float bestScore = float.MaxValue;
                foreach (var r in AxisRotations())
                {
                    instance.transform.rotation = link.rotation * r;
                    var b = WorldBounds(instance.GetComponentsInChildren<MeshFilter>(true));
                    float score = (b.size - target.size).magnitude + (b.center - target.center).magnitude;
                    if (score < bestScore) { bestScore = score; best = instance.transform.rotation; }
                }
                instance.transform.rotation = best;
                var final = WorldBounds(instance.GetComponentsInChildren<MeshFilter>(true));
                float residual = (final.center - target.center).magnitude;
                result["scaleApplied"] = scale;
                result["localEuler"] = instance.transform.localEulerAngles;
                result["alignmentError"] = bestScore;
                result["alignmentOk"] = bestScore < 0.05f * Mathf.Max(target.size.magnitude, 1e-3f);
                if (residual > 0.05f * target.size.magnitude)
                    result["hint"] = "Centro não bateu: confira se a origem do objeto no Blender está no ponto da junta e se o FBX foi exportado pelo script.";
            }

            if (a.Bool("hideExisting", true))
                foreach (var mf in existing)
                    if (mf.TryGetComponent<MeshRenderer>(out var mr)) { Undo.RecordObject(mr, "MCP Hide Visual"); mr.enabled = false; }

            Undo.RegisterCreatedObjectUndo(instance, "MCP Attach Visual");
            Util.MarkSceneDirty(instance);
            result["hiddenUrdfVisuals"] = a.Bool("hideExisting", true) ? existing.Count : 0;
            return result;
        }

        static bool IsUnder(Transform t, Transform parent)
        {
            if (parent == null) return false;
            for (; t != null; t = t.parent) if (t == parent) return true;
            return false;
        }

        static Bounds WorldBounds(IEnumerable<MeshFilter> filters)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (var mf in filters)
            {
                if (mf.sharedMesh == null) continue;
                var local = mf.sharedMesh.bounds;
                var m = mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var w = m.MultiplyPoint3x4(corner);
                    if (!any) { bounds = new Bounds(w, Vector3.zero); any = true; }
                    else bounds.Encapsulate(w);
                }
            }
            return bounds;
        }

        /// <summary>As 24 rotações que alinham eixos (todas as combinações de 90°).</summary>
        static IEnumerable<Quaternion> AxisRotations()
        {
            var axes = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var forward in axes)
                foreach (var up in axes)
                    if (Mathf.Abs(Vector3.Dot(forward, up)) < 0.5f)
                        yield return Quaternion.LookRotation(forward, up);
        }

        // ---------------------------------------------------------------- ROS

        static object SetupRosConnection(Args a)
        {
            var type = Require(RosType("ROSConnection"), "ROSConnection", RosPackage);
            var existing = (Component)Object.FindAnyObjectByType(type);
            Component conn;
            bool created = existing == null;
            if (created)
            {
                var go = new GameObject("ROSConnection");
                Undo.RegisterCreatedObjectUndo(go, "MCP ROSConnection");
                conn = Undo.AddComponent(go, type);
            }
            else conn = existing;

            var so = new SerializedObject(conn);
            var applied = new List<object>();
            void Set(string[] names, Action<SerializedProperty> setter, string label)
            {
                foreach (var n in names)
                {
                    var prop = so.FindProperty(n);
                    if (prop == null) continue;
                    setter(prop);
                    applied.Add(label);
                    return;
                }
            }
            Set(new[] { "m_RosIPAddress", "rosIPAddress" }, p => p.stringValue = a.Str("ip", "127.0.0.1"), "ip");
            Set(new[] { "m_RosPort", "rosPort" }, p => p.intValue = a.Int("port", 10000), "port");
            Set(new[] { "m_ShowHUD", "m_ShowHud", "showHud" }, p => p.boolValue = a.Bool("showHud", true), "showHud");
            Set(new[] { "m_ConnectOnStart", "connectOnStart" }, p => p.boolValue = a.Bool("connectOnStart", true), "connectOnStart");
            so.ApplyModifiedProperties();

            // Fallback para versões com propriedades públicas em vez dos campos serializados acima.
            if (!applied.Contains("ip")) Util.SetMember(conn, "RosIPAddress", a.Str("ip", "127.0.0.1"));
            if (!applied.Contains("port")) Util.SetMember(conn, "RosPort", a.Int("port", 10000));

            EditorUtility.SetDirty(conn);
            Util.MarkSceneDirty(conn.gameObject);
            return new Dictionary<string, object>
            {
                { "object", Util.PathOf(conn.transform) },
                { "created", created },
                { "ip", a.Str("ip", "127.0.0.1") },
                { "port", a.Int("port", 10000) },
                { "protocol", CurrentDefines().Contains("ROS2") ? "ros2" : "ros1" },
            };
        }

        static object SetRosProtocol(Args a)
        {
            bool ros2 = a.Required("protocol") == "ros2";
            var targets = new HashSet<NamedBuildTarget> { CurrentTarget(), NamedBuildTarget.Standalone };
            foreach (var target in targets)
            {
                var defines = PlayerSettings.GetScriptingDefineSymbols(target).Split(';').Where(s => s.Length > 0 && s != "ROS2").ToList();
                if (ros2) defines.Add("ROS2");
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", defines));
            }
            return new Dictionary<string, object> { { "protocol", ros2 ? "ros2" : "ros1" }, { "note", "Símbolo ROS2 atualizado; o projeto vai recompilar." } };
        }

        static object GenerateMessages(Args a)
        {
            string input = Path.GetFullPath(a.Required("path"));
            string output = a.Str("outputPath", "Assets/RosMessages");
            const string ns = RosNamespace + ".MessageGeneration";
            var generators = new (string ext, string type, string single, string directory)[]
            {
                (".msg", "MessageAutoGen", "GenerateSingleMessage", "GenerateDirectoryMessages"),
                (".srv", "ServiceAutoGen", "GenerateSingleService", "GenerateDirectoryServices"),
                (".action", "ActionAutoGen", "GenerateSingleAction", "GenerateDirectoryActions"),
            };

            var processed = new List<object>();
            var warnings = new List<object>();
            var errors = new List<object>();
            var startedAt = DateTime.UtcNow.AddSeconds(-1);
            foreach (var g in generators)
            {
                bool isDir = Directory.Exists(input);
                if (!isDir && !input.EndsWith(g.ext, StringComparison.OrdinalIgnoreCase)) continue;
                var sources = isDir ? Directory.GetFiles(input, "*" + g.ext, SearchOption.AllDirectories) : new[] { input };
                if (sources.Length == 0) continue;

                var type = Require(Util.FindInNamespace(g.type, ns), g.type, RosPackage);
                try
                {
                    // Os geradores retornam a lista de avisos, não os arquivos criados.
                    if (Util.InvokeLoose(type, isDir ? g.directory : g.single, input, output) is IEnumerable list)
                        foreach (var w in list) warnings.Add(w?.ToString());
                    processed.AddRange(sources.Select(Path.GetFileName));
                }
                catch (Exception e)
                {
                    errors.Add($"{g.ext}: {(e.InnerException ?? e).Message}");
                }
            }
            if (processed.Count == 0 && errors.Count == 0)
                throw new ArgumentException("Nenhum .msg/.srv/.action encontrado em " + input);
            AssetDatabase.Refresh();
            var written = Directory.Exists(output)
                ? Directory.GetFiles(output, "*.cs", SearchOption.AllDirectories)
                    .Where(f => File.GetLastWriteTimeUtc(f) >= startedAt)
                    .Select(f => (object)f.Replace('\\', '/')).ToList()
                : new List<object>();
            return new Dictionary<string, object>
            {
                { "outputPath", output }, { "processed", processed }, { "writtenFiles", written },
                { "warnings", warnings }, { "errors", errors },
            };
        }
    }
}
