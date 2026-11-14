using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.LowLevel;

namespace RoboticsMcp
{
    /// <summary>
    /// Servidor TCP local: uma requisição JSON por linha, executada na main thread do Editor.
    /// Recriado automaticamente após cada domain reload.
    /// </summary>
    [InitializeOnLoad]
    public static class BridgeServer
    {
        const string PortPref = "RoboticsMcp.Port";
        const string EnabledPref = "RoboticsMcp.Enabled";
        const string CompileKey = "RoboticsMcp.CompileMessages";

        class Pending
        {
            public string Line;
            public string Response;
            public readonly ManualResetEventSlim Done = new ManualResetEventSlim(false);
        }

        static TcpListener listener;
        static Thread acceptThread;
        static volatile bool running;
        static readonly ConcurrentQueue<Pending> queue = new ConcurrentQueue<Pending>();
        static readonly ConcurrentQueue<Dictionary<string, object>> logs = new ConcurrentQueue<Dictionary<string, object>>();

        public static int Port
        {
            get => EditorPrefs.GetInt(PortPref, 8765);
            set => EditorPrefs.SetInt(PortPref, value);
        }

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledPref, true);
            set => EditorPrefs.SetBool(EnabledPref, value);
        }

        public static bool IsRunning => running;
        public static string LastError { get; private set; }
        public static int CommandsServed { get; private set; }

        static BridgeServer()
        {
            EditorApplication.update += ProcessQueue;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            Application.logMessageReceivedThreaded += OnLog;
            CompilationPipeline.compilationStarted += _ => SessionState.SetString(CompileKey, "[]");
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode) InstallPlayerLoopPump();
            };
            if (EditorApplication.isPlaying) InstallPlayerLoopPump();
            if (Enabled) Start();
        }

        struct McpBridgePump { }

        /// <summary>
        /// Em Play Mode o EditorApplication.update pode não rodar (ex.: editor em batchmode);
        /// o PlayerLoop sempre roda, então processamos a fila por ele também.
        /// </summary>
        static void InstallPlayerLoopPump()
        {
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var systems = new List<PlayerLoopSystem>(loop.subSystemList ?? new PlayerLoopSystem[0]);
            if (systems.Exists(s => s.type == typeof(McpBridgePump))) return;
            systems.Add(new PlayerLoopSystem { type = typeof(McpBridgePump), updateDelegate = ProcessQueue });
            loop.subSystemList = systems.ToArray();
            PlayerLoop.SetPlayerLoop(loop);
        }

        public static void Start()
        {
            if (running) return;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, Port);
                // Após um domain reload o Windows pode manter a porta presa por conexões antigas.
                listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Start();
                running = true;
                LastError = null;
                acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "RoboticsMcpBridge" };
                acceptThread.Start();
            }
            catch (Exception e)
            {
                try { listener?.Stop(); } catch { }
                listener = null;
                // Só avisa uma vez por erro; ProcessQueue tenta de novo a cada segundo.
                if (LastError != e.Message)
                    Debug.LogWarning($"[Robotics MCP] Não foi possível escutar na porta {Port}: {e.Message}. Tentando de novo...");
                LastError = e.Message;
            }
        }

        public static void Stop()
        {
            running = false;
            try { listener?.Stop(); } catch { }
            listener = null;
            // Libera quem ainda espera resposta; o cliente Python reconecta depois do reload.
            while (queue.TryDequeue(out var pending))
            {
                pending.Response = MiniJson.Serialize(new Dictionary<string, object> { { "ok", false }, { "error", "Unity recarregando scripts; tente de novo" } });
                pending.Done.Set();
            }
        }

        static double nextRetry;

        static void RetryStartIfNeeded()
        {
            if (running || !Enabled || EditorApplication.timeSinceStartup < nextRetry) return;
            nextRetry = EditorApplication.timeSinceStartup + 1.0;
            Start();
        }

        public static void Restart()
        {
            Stop();
            Start();
        }

        static void AcceptLoop()
        {
            while (running)
            {
                TcpClient client;
                try { client = listener.AcceptTcpClient(); }
                catch { break; }
                new Thread(() => HandleClient(client)) { IsBackground = true }.Start();
            }
        }

        static void HandleClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    var stream = client.GetStream();
                    var reader = new StreamReader(stream, new UTF8Encoding(false));
                    string line = reader.ReadLine();
                    if (string.IsNullOrEmpty(line)) return;

                    var pending = new Pending { Line = line };
                    queue.Enqueue(pending);
                    string response = pending.Done.Wait(TimeSpan.FromMinutes(10))
                        ? pending.Response
                        : MiniJson.Serialize(new Dictionary<string, object> { { "ok", false }, { "error", "timeout esperando a main thread do Unity" } });

                    var bytes = new UTF8Encoding(false).GetBytes(response + "\n");
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
                catch (Exception e)
                {
                    LastError = e.Message;
                }
            }
        }

        static void ProcessQueue()
        {
            RetryStartIfNeeded();
            while (queue.TryDequeue(out var pending))
            {
                pending.Response = Execute(pending.Line);
                CommandsServed++;
                pending.Done.Set();
            }
        }

        static string Execute(string line)
        {
            object id = null;
            try
            {
                var request = (Dictionary<string, object>)MiniJson.Parse(line);
                request.TryGetValue("id", out id);
                string command = (string)request["command"];
                var parameters = request.TryGetValue("params", out var p) && p is Dictionary<string, object> d
                    ? d
                    : new Dictionary<string, object>();
                object result = Commands.Run(command, new Args(parameters));
                return MiniJson.Serialize(new Dictionary<string, object> { { "id", id }, { "ok", true }, { "result", result } });
            }
            catch (Exception e)
            {
                var inner = e is System.Reflection.TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                return MiniJson.Serialize(new Dictionary<string, object> { { "id", id }, { "ok", false }, { "error", inner.GetType().Name + ": " + inner.Message } });
            }
        }

        // ---------------------------------------------------------------- console

        static void OnLog(string message, string stackTrace, LogType type)
        {
            logs.Enqueue(new Dictionary<string, object>
            {
                { "type", type.ToString() },
                { "message", message },
                { "stack", type == LogType.Error || type == LogType.Exception ? stackTrace : "" },
                { "time", DateTime.Now.ToString("HH:mm:ss") },
            });
            while (logs.Count > 500) logs.TryDequeue(out _);
        }

        public static List<object> RecentLogs(int count, bool errorsOnly)
        {
            var all = new List<object>();
            foreach (var entry in CompileMessages()) all.Add(entry);
            foreach (var entry in logs)
            {
                string type = (string)entry["type"];
                if (errorsOnly && type != "Error" && type != "Exception" && type != "Assert") continue;
                all.Add(entry);
            }
            return all.GetRange(Math.Max(0, all.Count - count), Math.Min(count, all.Count));
        }

        static void OnAssemblyCompiled(string assembly, CompilerMessage[] messages)
        {
            var list = (List<object>)MiniJson.Parse(SessionState.GetString(CompileKey, "[]"));
            foreach (var m in messages)
            {
                if (m.type != CompilerMessageType.Error) continue;
                list.Add(new Dictionary<string, object>
                {
                    { "type", "CompileError" },
                    { "message", m.message },
                    { "file", m.file },
                    { "line", m.line },
                });
            }
            SessionState.SetString(CompileKey, MiniJson.Serialize(list));
        }

        /// <summary>Erros da última compilação (sobrevivem ao domain reload via SessionState).</summary>
        public static List<object> CompileMessages() =>
            (List<object>)MiniJson.Parse(SessionState.GetString(CompileKey, "[]"));
    }

    public class BridgeWindow : EditorWindow
    {
        [MenuItem("Tools/Robotics MCP")]
        static void Open() => GetWindow<BridgeWindow>("Robotics MCP");

        void OnGUI()
        {
            EditorGUILayout.LabelField("Robotics MCP Bridge", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Status", BridgeServer.IsRunning ? $"Listening em 127.0.0.1:{BridgeServer.Port}" : "Parado");
            EditorGUILayout.LabelField("Comandos atendidos", BridgeServer.CommandsServed.ToString());
            if (!string.IsNullOrEmpty(BridgeServer.LastError))
                EditorGUILayout.HelpBox(BridgeServer.LastError, MessageType.Warning);

            EditorGUI.BeginChangeCheck();
            int port = EditorGUILayout.IntField("Porta", BridgeServer.Port);
            bool enabled = EditorGUILayout.Toggle("Iniciar automaticamente", BridgeServer.Enabled);
            if (EditorGUI.EndChangeCheck())
            {
                BridgeServer.Port = port;
                BridgeServer.Enabled = enabled;
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(BridgeServer.IsRunning ? "Restart" : "Start")) BridgeServer.Restart();
            if (BridgeServer.IsRunning && GUILayout.Button("Stop")) BridgeServer.Stop();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox("Se mudar a porta, defina UNITY_MCP_PORT com o mesmo valor na configuração do servidor MCP.", MessageType.Info);
        }

        void OnInspectorUpdate() => Repaint();
    }
}
