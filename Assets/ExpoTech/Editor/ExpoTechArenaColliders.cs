// Coloque em Assets/ExpoTech/Editor/. Menu: ExpoTech > Configurar colisores da Arena
// Selecione a instancia do ExpoTech_Arena.fbx na cena antes de rodar.
// - Objetos "COL_*": recebem BoxCollider do tamanho da malha e perdem o renderer.
// - Objetos visuais: ficam sem colisor (a fisica usa so as caixas simples).
using UnityEditor;
using UnityEngine;

public static class ExpoTechArenaColliders
{
    [MenuItem("ExpoTech/Configurar colisores da Arena")]
    static void Setup()
    {
        var root = Selection.activeGameObject;
        if (root == null) { Debug.LogError("Selecione a raiz da arena na Hierarchy."); return; }
        int n = 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            var go = t.gameObject;
            foreach (var mc in go.GetComponents<MeshCollider>()) Object.DestroyImmediate(mc);
            if (!go.name.StartsWith("COL_")) continue;
            var mf = go.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            // Não usar "??": GetComponent devolve um objeto nulo "falso" da Unity, que o ?? não reconhece.
            var box = go.GetComponent<BoxCollider>();
            if (box == null) box = go.AddComponent<BoxCollider>();
            box.center = mf.sharedMesh.bounds.center;
            box.size = mf.sharedMesh.bounds.size;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr) Object.DestroyImmediate(mr);
            go.isStatic = true;
            n++;
        }
        Debug.Log($"ExpoTech: {n} colisores configurados.");
    }
}
