using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ExpoTech.EditorTools
{
    /// <summary>Mede o modelo do drone (pivôs, eixos, bounds, materiais) e grava em Library/expo_model_probe.txt.</summary>
    public static class ExpoModelProbe
    {
        public const string ModelPath = "Assets/ExpoTech/Art/Drone/Drone.fbx";

        [MenuItem("ExpoTech/Diagnóstico/Medir modelo do drone")]
        public static void Probe()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            var sb = new StringBuilder();
            sb.AppendLine($"importer globalScale={importer.globalScale} useFileScale={importer.useFileScale} fileScale={importer.fileScale} bakeAxis={importer.bakeAxisConversion}");
            var go = (GameObject)Object.Instantiate(model);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                sb.Append($"{Path(t, go.transform)} | localPos {V(t.localPosition)} localEuler {V(t.localEulerAngles)} localScale {V(t.localScale)} | worldPos {V(t.position)} right {V(t.right)} up {V(t.up)} fwd {V(t.forward)}");
                var mf = t.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    sb.Append($" | meshBounds c{V(mf.sharedMesh.bounds.center)} s{V(mf.sharedMesh.bounds.size)} verts {mf.sharedMesh.vertexCount}");
                var r = t.GetComponent<Renderer>();
                if (r != null)
                {
                    sb.Append($" | worldBounds c{V(r.bounds.center)} s{V(r.bounds.size)} | mats:");
                    foreach (var m in r.sharedMaterials) sb.Append($" {(m ? m.name + "(" + m.shader.name + ")" : "null")}");
                }
                sb.AppendLine();
            }
            Object.DestroyImmediate(go);
            File.WriteAllText("Library/expo_model_probe.txt", sb.ToString());
            Debug.Log("[ExpoTech] Modelo medido: Library/expo_model_probe.txt");
        }

        static string Path(Transform t, Transform root) => t == root ? t.name : Path(t.parent, root) + "/" + t.name;

        static string V(Vector3 v) =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", v.x, v.y, v.z);

        /// <summary>Salva o que a câmera principal está vendo (útil em Play Mode) em Library/expo_capture_main.png.</summary>
        [MenuItem("ExpoTech/Diagnóstico/Capturar câmera principal")]
        public static void CaptureMain()
        {
            var cam = Camera.main;
            if (cam == null) { Debug.LogError("[ExpoTech] Sem Camera.main"); return; }
            var prev = cam.targetTexture;
            var rt = new RenderTexture(960, 540, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            tex.Apply();
            File.WriteAllBytes("Library/expo_capture_main.png", tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = prev;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }

        /// <summary>Renderiza o drone de três ângulos em Library/expo_capture_*.png.</summary>
        [MenuItem("ExpoTech/Diagnóstico/Capturar imagens do drone")]
        public static void Capture()
        {
            var robot = GameObject.Find("expo_drone");
            var target = robot != null ? robot.transform.Find("base_link") : null;
            Vector3 c = target != null ? target.position : Vector3.zero;
            var go = new GameObject("ExpoCaptureCam");
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.fieldOfView = 40f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.35f, 0.4f, 0.47f);
                Shot(cam, c + new Vector3(3.5f, 1.6f, 4.5f), c, "Library/expo_capture_front.png");
                Shot(cam, c + new Vector3(0f, 1.2f, -6f), c, "Library/expo_capture_back.png");
                Shot(cam, c + new Vector3(-6.5f, 0.6f, 0f), c, "Library/expo_capture_side.png");
                Debug.Log("[ExpoTech] Imagens salvas em Library/expo_capture_*.png");
            }
            finally { Object.DestroyImmediate(go); }
        }

        static void Shot(Camera cam, Vector3 pos, Vector3 look, string file)
        {
            cam.transform.position = pos;
            cam.transform.LookAt(look);
            var rt = new RenderTexture(960, 540, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            RenderTexture.active = null;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
    }
}
