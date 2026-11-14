using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Gera uma arena tatica de interior (inspirada em jogos de operacoes taticas):
/// 4 salas, portas, paredes reforcadas, barricada, sacos de areia, caixas, moveis e pilares.
/// Menu: Tools > Drone > Construir Arena Tatica (janela) ou ExpoTech > Construir arena tática (direto).
/// Adaptado para o expo_drone articulado (o original fica em RobotDescription/expo_drone_source).
/// </summary>
public class TacticalArenaBuilder : EditorWindow
{
    /// <summary>Constrói a arena sem janela nem diálogo (usado pelo MCP / automação).</summary>
    [MenuItem("ExpoTech/Construir arena tática")]
    static void BuildDirect()
    {
        var builder = CreateInstance<TacticalArenaBuilder>();
        builder.Build(showDialog: false);
        DestroyImmediate(builder);
    }

    float scale = 1f;
    bool addSensor = true;
    bool moveDrone = true;

    static Transform root;
    static float S = 1f;
    static Material mFloor, mWall, mReinforced, mWood, mCrate, mMetal, mSandbag, mYellow, mSofa, mDark, mBlue;

    [MenuItem("Tools/Drone/Construir Arena Tatica")]
    static void Open()
    {
        GetWindow<TacticalArenaBuilder>("Arena Tatica");
    }

    void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Gera um cenario tatico de interior com paredes, portas, barricada, sacos de areia, caixas e pilares. " +
            "Obstaculos baixos (pulo) e altos (desvio) para testar o sensor do drone.",
            MessageType.Info);
        scale = EditorGUILayout.Slider("Escala do cenario", scale, 0.3f, 2f);
        addSensor = EditorGUILayout.Toggle("Adicionar sensor ao drone", addSensor);
        moveDrone = EditorGUILayout.Toggle("Mover drone p/ o inicio", moveDrone);
        EditorGUILayout.Space();
        if (GUILayout.Button("Construir arena", GUILayout.Height(32))) Build();
        if (GUILayout.Button("Apagar arena")) Clear();
    }

    // ---------------------------------------------------------------
    static void Clear()
    {
        var old = GameObject.Find("TacticalArena");
        if (old != null) Undo.DestroyObjectImmediate(old);
    }

    void Build(bool showDialog = true)
    {
        Clear();
        S = scale;

        if (!AssetDatabase.IsValidFolder("Assets/ArenaMaterials"))
            AssetDatabase.CreateFolder("Assets", "ArenaMaterials");

        mFloor      = Mat("Arena_Piso",      new Color(0.30f, 0.31f, 0.33f));
        mWall       = Mat("Arena_Parede",    new Color(0.70f, 0.67f, 0.60f));
        mReinforced = Mat("Arena_Reforcada", new Color(0.16f, 0.18f, 0.20f));
        mWood       = Mat("Arena_Madeira",   new Color(0.42f, 0.28f, 0.15f));
        mCrate      = Mat("Arena_Caixa",     new Color(0.26f, 0.32f, 0.18f));
        mMetal      = Mat("Arena_Metal",     new Color(0.32f, 0.35f, 0.38f));
        mSandbag    = Mat("Arena_Saco",      new Color(0.60f, 0.52f, 0.36f));
        mYellow     = Mat("Arena_Amarelo",   new Color(0.90f, 0.70f, 0.08f));
        mSofa       = Mat("Arena_Sofa",      new Color(0.35f, 0.14f, 0.14f));
        mDark       = Mat("Arena_Escuro",    new Color(0.12f, 0.13f, 0.15f));
        mBlue       = Mat("Arena_Azul",      new Color(0.10f, 0.30f, 0.70f));

        var go = new GameObject("TacticalArena");
        Undo.RegisterCreatedObjectUndo(go, "Construir arena");
        root = go.transform;

        const float W = 20f;   // meia largura  (x)
        const float D = 14f;   // meia profundidade (z)

        // ---------------- piso ----------------
        Box("Piso", V(0f, -0.25f, 0f), V(2f * W + 4f, 0.5f, 2f * D + 4f), mFloor);

        // ---------------- paredes externas ----------------
        Wall("Parede_Norte", -W, D, W, D, mWall);
        Wall("Parede_Sul",   -W, -D, W, -D, mWall);
        Wall("Parede_Oeste", -W, -D, -W, D, mWall);
        Wall("Parede_Leste",  W, -D,  W, D, mWall);

        // ---------------- paredes internas (portas de 5 m) ----------------
        Wall("Parede_A1", -W, 0f, -12.5f, 0f, mWall);          // porta em x = -10
        Wall("Parede_A2", -7.5f, 0f, 7.5f, 0f, mReinforced);   // parede reforcada central
        Wall("Parede_A3", 12.5f, 0f, W, 0f, mWall);            // porta em x = +10
        Wall("Parede_B1", 0f, -D, 0f, -9.5f, mWall);           // porta em z = -7
        Wall("Parede_B2", 0f, -4.5f, 0f, 0f, mWall);
        Wall("Parede_C1", 0f, 0f, 0f, 4.5f, mWall);            // porta em z = +7
        Wall("Parede_C2", 0f, 9.5f, 0f, D, mWall);

        // faixas amarelas nas portas e ponto de partida
        Deco("Faixa_Porta_1", V(-10f, 0.012f, -1.4f), V(4.5f, 0.02f, 0.25f), mYellow);
        Deco("Faixa_Porta_2", V(10f, 0.012f, -1.4f),  V(4.5f, 0.02f, 0.25f), mYellow);
        Deco("Faixa_Porta_3", V(1.4f, 0.012f, -7f),   V(0.25f, 0.02f, 4.5f), mYellow);
        Deco("Faixa_Porta_4", V(1.4f, 0.012f, 7f),    V(0.25f, 0.02f, 4.5f), mYellow);
        Cyl("Ponto_Inicio", -15f, -12.5f, 4f, 0.02f, mBlue, false);

        // ---------------- SALA SUDOESTE (inicio) ----------------
        Box("Caixa_Baixa_SW",   V(-15f, 0.35f, -8f),  V(2.6f, 0.7f, 1.2f), mCrate);     // pula
        Box("Armario_Alto_SW",  V(-15f, 1.25f, -4f),  V(3f, 2.5f, 1.4f),   mMetal);     // desvia
        Box("Mesa_Baixa_SW",    V(-7f, 0.35f, -9f),   V(3f, 0.7f, 1.4f),   mWood);      // pula
        Cyl("Pilar_SW", -5f, -5f, 1.2f, 5f, mDark);                                      // desvia
        Box("Sacos_Areia_Porta", V(-10f, 0.3f, 0f),   V(4.4f, 0.6f, 1f),   mSandbag);   // pula (porta)

        // ---------------- SALA SUDESTE ----------------
        Box("Barricada_Reforcada", V(0f, 1.75f, -8.575f), V(0.4f, 3.5f, 1.35f), mWood); // fecha parte da porta
        Box("Pilha_Caixas_SE",  V(6f, 1f, -10f),      V(2f, 2f, 2f),       mCrate);     // desvia
        Box("Caixa_Baixa_SE1",  V(12f, 0.35f, -9f),   V(2.4f, 0.7f, 1.2f), mCrate);     // pula
        Box("Caixa_Baixa_SE2",  V(16f, 0.35f, -6f),   V(2.4f, 0.7f, 1.2f), mCrate, 20f);
        Cyl("Barril_Alto_1", 14f, -3f, 0.9f, 1.3f, mMetal);                              // desvia
        Cyl("Barril_Alto_2", 15.2f, -3.4f, 0.9f, 1.3f, mMetal);

        // ---------------- SALA NOROESTE ----------------
        Box("Sofa_Baixo",       V(-15f, 0.4f, 10f),   V(4f, 0.8f, 1.6f),   mSofa);
        Box("Estante_Alta",     V(-19f, 1.5f, 7f),    V(1f, 3f, 5f),       mWood);
        Box("Mesa_Centro",      V(-12f, 0.25f, 6f),   V(2f, 0.5f, 1.2f),   mWood);
        Cyl("Pilar_NW", -6f, 10f, 1.2f, 5f, mDark);

        // ---------------- SALA NORDESTE ----------------
        Box("Mesa_Operacoes",   V(10f, 0.375f, 8f),   V(4f, 0.75f, 1.6f),  mWood);
        Box("Caixa_Tatica",     V(16f, 0.5f, 10f),    V(2.2f, 1f, 2.2f),   mCrate);
        Cyl("Balde_Baixo_1", 5f, 4f, 0.9f, 0.6f, mMetal);
        Cyl("Balde_Baixo_2", 6.5f, 5f, 0.9f, 0.6f, mMetal);
        Cyl("Pilar_NE", 12f, 12f, 1.2f, 5f, mDark);

        // ---------------- luzes ----------------
        bool hasDirectional = false;
        foreach (var l in FindLights())
            if (l.type == LightType.Directional) hasDirectional = true;
        if (!hasDirectional)
        {
            var dl = new GameObject("Luz_Direcional");
            dl.transform.SetParent(root, false);
            dl.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
            var L = dl.AddComponent<Light>();
            L.type = LightType.Directional;
            L.intensity = 0.8f;
            L.color = new Color(0.85f, 0.9f, 1f);
        }
        AddLight("Luz_SW", V3(-10f, 4.5f, -7f));
        AddLight("Luz_SE", V3(10f, 4.5f, -7f));
        AddLight("Luz_NW", V3(-10f, 4.5f, 7f));
        AddLight("Luz_NE", V3(10f, 4.5f, 7f));
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.35f, 0.38f, 0.45f);

        // ---------------- chao antigo, drone e sensor ----------------
        var plane = GameObject.Find("Plane") ?? GameObject.Find("TestFloor");
        if (plane != null && plane.transform.parent == null)
        {
            Undo.RecordObject(plane, "Desativar plano antigo");
            plane.SetActive(false);
        }

        var drone = FindDrone();
        if (drone != null)
        {
            if (moveDrone)
            {
                Undo.RecordObject(drone.transform, "Mover drone");
                // mantém a altura (o robô articulado já está apoiado no chão em y = 0)
                drone.transform.SetPositionAndRotation(new Vector3(-15f * S, drone.transform.position.y, -12.5f * S), Quaternion.identity);
            }
            if (addSensor && drone.GetComponent<ExpoTech.ExpoDroneSmartAvoidance>() == null)
                Debug.LogWarning("[ExpoTech] O drone não tem ExpoDroneSmartAvoidance; adicione pelo MCP.");
        }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());

        if (showDialog) EditorUtility.DisplayDialog("Arena criada",
            drone != null
                ? "Arena criada. O drone foi levado ao ponto de partida (circulo azul).\nAperte Play!"
                : "Arena criada, mas nao encontrei o expo_drone na cena.",
            "OK");
    }

    // ---------------------------------------------------------------
    // Auxiliares
    // ---------------------------------------------------------------
    static Vector3 V(float x, float y, float z) { return new Vector3(x, y, z); }       // medida/posicao local (escala aplicada no helper)
    static Vector3 V3(float x, float y, float z) { return new Vector3(x, y, z) * S; }  // posicao no mundo ja escalada

    static GameObject Box(string name, Vector3 center, Vector3 size, Material m, float yaw = 0f, bool collider = true)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(root, false);
        g.transform.localPosition = center * S;
        g.transform.localScale = size * S;
        g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        g.GetComponent<Renderer>().sharedMaterial = m;
        if (!collider) Object.DestroyImmediate(g.GetComponent<Collider>());
        GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.BatchingStatic);
        return g;
    }

    static GameObject Deco(string name, Vector3 center, Vector3 size, Material m)
    {
        return Box(name, center, size, m, 0f, false);
    }

    static GameObject Cyl(string name, float x, float z, float diameter, float height, Material m, bool collider = true)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);   // altura padrao = 2, diametro = 1
        g.name = name;
        g.transform.SetParent(root, false);
        g.transform.localPosition = new Vector3(x, height * 0.5f, z) * S;
        g.transform.localScale = new Vector3(diameter * S, height * 0.5f * S, diameter * S);
        g.GetComponent<Renderer>().sharedMaterial = m;
        if (!collider) Object.DestroyImmediate(g.GetComponent<Collider>());
        return g;
    }

    // parede alinhada aos eixos: de (x1,z1) ate (x2,z2)
    static void Wall(string name, float x1, float z1, float x2, float z2, Material m, float h = 5f, float t = 0.5f)
    {
        bool alongX = Mathf.Approximately(z1, z2);
        float len = alongX ? Mathf.Abs(x2 - x1) : Mathf.Abs(z2 - z1);
        Vector3 c = new Vector3((x1 + x2) * 0.5f, h * 0.5f, (z1 + z2) * 0.5f);
        Vector3 size = alongX ? new Vector3(len + t, h, t) : new Vector3(t, h, len + t);
        Box(name, c, size, m);
    }

    static void AddLight(string name, Vector3 worldPos)
    {
        var g = new GameObject(name);
        g.transform.SetParent(root, false);
        g.transform.position = worldPos;
        var l = g.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = 24f * S;
        l.intensity = 1.5f;
        l.color = new Color(1f, 0.93f, 0.8f);
    }

    static Material Mat(string name, Color c)
    {
        string path = "Assets/ArenaMaterials/" + name + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            Shader sh = null;
            if (GraphicsSettings.currentRenderPipeline != null)
            {
                sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("HDRP/Lit");
            }
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh);
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = c;
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.15f);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Light[] FindLights()
    {
#if UNITY_2023_1_OR_NEWER
        return UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
#else
        return UnityEngine.Object.FindObjectsOfType<Light>();
#endif
    }

    static GameObject FindDrone()
    {
        return GameObject.Find("expo_drone");
    }
}
