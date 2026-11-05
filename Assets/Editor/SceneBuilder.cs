using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Generates every asset (materials, prefabs, waves) and the Main scene from code,
/// so the project needs no external art. Run: Unity -executeMethod SceneBuilder.Build
/// </summary>
public static class SceneBuilder
{
    const int Cols = 12, Rows = 9;
    const float Cell = 3f;
    const string Gen = "Assets/Generated";

    static readonly Vector2Int[] Corners =
    {
        new Vector2Int(0, 1), new Vector2Int(10, 1), new Vector2Int(10, 4),
        new Vector2Int(1, 4), new Vector2Int(1, 7), new Vector2Int(11, 7)
    };

    // ---------- step 1: TextMeshPro essentials (own batch run) ----------
    public static void ImportTmp()
    {
        string path = "Packages/com.unity.ugui/Package Resources/TMP Essential Resources.unitypackage";
        AssetDatabase.importPackageCompleted += _ => EditorApplication.Exit(0);
        AssetDatabase.importPackageFailed += (_, e) => { Debug.LogError("TMP import failed: " + e); EditorApplication.Exit(1); };
        AssetDatabase.ImportPackage(path, false);
    }

    // ---------- step 2: build everything ----------
    [MenuItem("TowerDefence/Build Scene")]
    public static void Build()
    {
        UnityEditorInternal.InternalEditorUtility.AddTag("Enemy");
        foreach (string f in new[] { "Generated", "Generated/Materials", "Generated/Prefabs", "Generated/Waves", "Scenes" })
            Directory.CreateDirectory(Path.Combine("Assets", f));
        AssetDatabase.Refresh();

        // --- materials
        Material grass = Mat("Grass", new Color(0.30f, 0.50f, 0.28f));
        Material road = Mat("Road", new Color(0.55f, 0.45f, 0.33f));
        Material node = Mat("Node", new Color(0.36f, 0.62f, 0.38f));
        Material grunt = Mat("Grunt", new Color(0.85f, 0.25f, 0.25f));
        Material fast = Mat("Fast", new Color(0.95f, 0.80f, 0.20f));
        Material tank = Mat("Tank", new Color(0.45f, 0.20f, 0.60f));
        Material steel = Mat("Steel", new Color(0.55f, 0.58f, 0.62f));
        Material dark = Mat("Dark", new Color(0.15f, 0.16f, 0.18f));
        Material orange = Mat("Orange", new Color(0.95f, 0.50f, 0.15f));
        Material cyan = Mat("Cyan", new Color(0.2f, 0.85f, 0.95f));
        Material spark = Mat("Spark", new Color(1f, 0.85f, 0.4f), "Sprites/Default");
        Material beam = Mat("Beam", new Color(0.3f, 0.9f, 1f), "Sprites/Default");

        // --- effects / bullets
        GameObject impact = MakeImpact(spark);
        GameObject bulletStd = MakeBullet("Bullet", orange, 0.3f, 25, 0f, impact);
        GameObject bulletMissile = MakeBullet("Missile", dark, 0.45f, 45, 2.6f, impact);

        // --- turrets
        GameObject tStd = MakeTurret("StandardTurret", steel, orange, bulletStd, 9f, 2.2f, false, 0, beam);
        GameObject tMis = MakeTurret("MissileTurret", dark, orange, bulletMissile, 12f, 0.7f, false, 0, beam);
        GameObject tLas = MakeTurret("LaserTurret", dark, cyan, null, 8f, 1f, true, 45, beam);

        // --- enemies
        Transform eGrunt = MakeEnemy("Grunt", grunt, 0.9f, 130f, 6.5f, 10);
        Transform eFast = MakeEnemy("Fast", fast, 0.7f, 75f, 12f, 14);
        Transform eTank = MakeEnemy("Tank", tank, 1.4f, 700f, 3.8f, 40);

        // --- waves
        var waves = new List<WaveDefinition>
        {
            Wave(1, G(eGrunt, 6, 0.9f)),
            Wave(2, G(eGrunt, 10, 0.7f)),
            Wave(3, G(eGrunt, 8, 0.6f), G(eFast, 4, 0.5f, 1f)),
            Wave(4, G(eFast, 12, 0.4f)),
            Wave(5, G(eTank, 2, 2f), G(eGrunt, 10, 0.5f, 1f)),
            Wave(6, G(eGrunt, 12, 0.4f), G(eFast, 8, 0.4f, 1f), G(eTank, 2, 2f, 1f)),
            Wave(7, G(eTank, 4, 1.8f), G(eFast, 12, 0.3f, 1f)),
            Wave(8, G(eTank, 6, 1.5f), G(eGrunt, 20, 0.3f, 1f), G(eFast, 10, 0.3f, 1f)),
        };

        // ================= scene =================
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // map
        var map = new GameObject("Map").transform;
        var ground = Prim(PrimitiveType.Cube, "Ground", map, new Vector3(Cols * Cell / 2f, -0.05f, Rows * Cell / 2f),
            new Vector3(Cols * Cell + 8f, 0.1f, Rows * Cell + 8f), grass, true);

        HashSet<Vector2Int> pathCells = PathCells();
        foreach (Vector2Int c in pathCells)
            Prim(PrimitiveType.Cube, "Road", map, CellPos(c, 0.05f), new Vector3(Cell, 0.1f, Cell), road, true);

        var nodes = new GameObject("Nodes").transform;
        int nodeCount = 0;
        for (int x = 0; x < Cols; x++)
            for (int z = 0; z < Rows; z++)
            {
                var c = new Vector2Int(x, z);
                if (pathCells.Contains(c) || !NextToPath(c, pathCells)) continue;
                GameObject n = Prim(PrimitiveType.Cube, "Node", nodes, CellPos(c, 0.2f),
                    new Vector3(Cell * 0.88f, 0.4f, Cell * 0.88f), node, false);
                Node nd = n.AddComponent<Node>();
                nd.hoverColor = new Color(0.55f, 0.85f, 0.55f);
                nd.notEnoughMoneyColor = new Color(0.85f, 0.35f, 0.35f);
                nd.positionOffset = new Vector3(0f, 0.2f, 0f);
                nodeCount++;
            }

        // waypoints
        var wp = new GameObject("WayPoints");
        wp.AddComponent<WayPoints>();
        for (int i = 0; i < Corners.Length; i++)
        {
            var p = new GameObject("WP" + i).transform;
            p.SetParent(wp.transform);
            p.position = CellPos(Corners[i], 0.6f);
        }
        var spawn = new GameObject("SpawnPoint").transform;
        spawn.position = CellPos(Corners[0], 0.6f) + Vector3.left * Cell;

        // game master
        var gm = new GameObject("GameMaster");
        var stats = gm.AddComponent<PlayerStats>();
        stats.startLives = 10;
        stats.startMoney = 400;
        var manager = gm.AddComponent<GameManager>();
        gm.AddComponent<BuildManager>();
        var spawner = gm.AddComponent<WaveSpawner>();
        spawner.spawnPoint = spawn;
        spawner.enemyPrefab = eGrunt;
        spawner.timeBetweenWaves = 6f;
        spawner.waves = waves.ToArray();
        var paused = gm.AddComponent<Paused>();

        var shopGo = new GameObject("Shop");
        shopGo.transform.SetParent(gm.transform);
        var shop = shopGo.AddComponent<Shop>();
        shop.standardTurret = Blueprint(shopGo.transform, "Standard", tStd, 100);
        shop.missileLauncher = Blueprint(shopGo.transform, "Missile", tMis, 250);
        shop.LaserLauncher = Blueprint(shopGo.transform, "Laser", tLas, 300);

        // camera + light
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.16f, 0.2f);
        camGo.AddComponent<AudioListener>();
        camGo.AddComponent<CameraController>();
        camGo.transform.position = new Vector3(Cols * Cell / 2f, 34f, -2f);
        camGo.transform.rotation = Quaternion.Euler(62f, 0f, 0f);

        var light = new GameObject("Directional Light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        light.intensity = 1.1f;

        // ================= UI =================
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        Transform ui = canvasGo.transform;

        var moneyText = Text(ui, "Money", "$500", 34, TextAlignmentOptions.Left, new Vector2(0, 1), new Vector2(20, -20), new Vector2(240, 50));
        var livesText = Text(ui, "Lives", "10 LIVES", 34, TextAlignmentOptions.Right, new Vector2(1, 1), new Vector2(-20, -20), new Vector2(300, 50));
        var waveText = Text(ui, "Wave", "WAVE 1", 34, TextAlignmentOptions.Center, new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(400, 50));
        ui.Find("Money").gameObject.AddComponent<MoneyUI>().moneyText = moneyText;
        ui.Find("Lives").gameObject.AddComponent<LifeUI>().livesText = livesText;
        var hud = ui.Find("Wave").gameObject.AddComponent<WaveHUD>();
        hud.text = waveText;
        hud.spawner = spawner;

        // shop bar
        var bStd = Btn(ui, "Standard $100", new Vector2(0.5f, 0), new Vector2(-220, 40), new Vector2(200, 60), new Color(0.25f, 0.35f, 0.5f));
        var bMis = Btn(ui, "Missile $250", new Vector2(0.5f, 0), new Vector2(0, 40), new Vector2(200, 60), new Color(0.45f, 0.3f, 0.2f));
        var bLas = Btn(ui, "Laser $300", new Vector2(0.5f, 0), new Vector2(220, 40), new Vector2(200, 60), new Color(0.2f, 0.45f, 0.5f));
        UnityEventTools.AddPersistentListener(bStd.onClick, new UnityAction(shop.SelectStandardTurret));
        UnityEventTools.AddPersistentListener(bMis.onClick, new UnityAction(shop.SelectAnotherTurret));
        UnityEventTools.AddPersistentListener(bLas.onClick, new UnityAction(shop.SelectLaserTurret));

        var bNext = Btn(ui, "Next wave", new Vector2(1, 0), new Vector2(-120, 40), new Vector2(200, 60), new Color(0.3f, 0.5f, 0.3f));
        UnityEventTools.AddPersistentListener(bNext.onClick, new UnityAction(spawner.SkipCountdown));
        var bPause = Btn(ui, "Pause (P)", new Vector2(0, 0), new Vector2(120, 40), new Vector2(200, 60), new Color(0.35f, 0.35f, 0.35f));
        UnityEventTools.AddPersistentListener(bPause.onClick, new UnityAction(paused.Toggle));

        // panels
        GameObject pausePanel = Panel(ui, "PausePanel", "PAUSED", out Transform pauseRoot);
        var bResume = Btn(pauseRoot, "Resume", new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(240, 64), new Color(0.3f, 0.5f, 0.3f));
        UnityEventTools.AddPersistentListener(bResume.onClick, new UnityAction(paused.Toggle));
        paused.ui = pausePanel;

        GameObject overPanel = Panel(ui, "GameOverPanel", "GAME OVER", out Transform overRoot);
        var overRounds = Text(overRoot, "Rounds", "0", 56, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(400, 70));
        var overGo = overPanel.AddComponent<GameOver>();
        overGo.roundsText = overRounds;
        var bRetry = Btn(overRoot, "Retry", new Vector2(0.5f, 0.5f), new Vector2(0, -70), new Vector2(240, 64), new Color(0.5f, 0.3f, 0.3f));
        UnityEventTools.AddPersistentListener(bRetry.onClick, new UnityAction(overGo.Retry));

        GameObject winPanel = Panel(ui, "WinPanel", "YOU WIN!", out Transform winRoot);
        var winRounds = Text(winRoot, "Rounds", "0", 56, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(400, 70));
        var winGo = winPanel.AddComponent<GameOver>();
        winGo.roundsText = winRounds;
        var bPlay = Btn(winRoot, "Play again", new Vector2(0.5f, 0.5f), new Vector2(0, -70), new Vector2(240, 64), new Color(0.3f, 0.5f, 0.3f));
        UnityEventTools.AddPersistentListener(bPlay.onClick, new UnityAction(winGo.Retry));

        manager.gameOverUI = overPanel;
        manager.winUI = winPanel;
        pausePanel.SetActive(false);
        overPanel.SetActive(false);
        winPanel.SetActive(false);

        // save
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Main.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true) };
        AssetDatabase.SaveAssets();
        Debug.Log("BUILD OK: nodes=" + nodeCount + " path cells=" + pathCells.Count + " waves=" + waves.Count);
    }

    // ---------- map helpers ----------
    static Vector3 CellPos(Vector2Int c, float y) { return new Vector3((c.x + 0.5f) * Cell, y, (c.y + 0.5f) * Cell); }

    static HashSet<Vector2Int> PathCells()
    {
        var set = new HashSet<Vector2Int>();
        for (int i = 0; i < Corners.Length - 1; i++)
        {
            Vector2Int a = Corners[i], b = Corners[i + 1];
            Vector2Int step = new Vector2Int(System.Math.Sign(b.x - a.x), System.Math.Sign(b.y - a.y));
            for (Vector2Int c = a; c != b; c += step) set.Add(c);
            set.Add(b);
        }
        return set;
    }

    static bool NextToPath(Vector2Int c, HashSet<Vector2Int> path)
    {
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if ((dx != 0 || dz != 0) && path.Contains(new Vector2Int(c.x + dx, c.y + dz))) return true;
        return false;
    }

    // ---------- asset helpers ----------
    static Material Mat(string name, Color c, string shader = "Standard")
    {
        var m = new Material(Shader.Find(shader)) { color = c };
        AssetDatabase.CreateAsset(m, Gen + "/Materials/" + name + ".mat");
        return m;
    }

    static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 pos, Vector3 scale, Material m, bool noCollider)
    {
        var go = GameObject.CreatePrimitive(t);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = m;
        if (noCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    static GameObject Save(GameObject go, string name)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, Gen + "/Prefabs/" + name + ".prefab");
        Object.DestroyImmediate(go);
        return prefab;
    }

    static GameObject MakeImpact(Material m)
    {
        var go = new GameObject("Impact");
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = false;
        main.duration = 0.5f;
        main.startLifetime = 0.45f;
        main.startSpeed = 4f;
        main.startSize = 0.28f;
        main.startColor = new Color(1f, 0.8f, 0.35f);
        main.stopAction = ParticleSystemStopAction.Destroy;
        var em = ps.emission;
        em.rateOverTime = 0;
        em.SetBursts(new[] { new ParticleSystem.Burst(0f, 16) });
        var sh = ps.shape;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.2f;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = m;
        return Save(go, "Impact");
    }

    static GameObject MakeBullet(string name, Material m, float scale, int damage, float radius, GameObject impact)
    {
        var go = Prim(PrimitiveType.Sphere, name, null, Vector3.zero, Vector3.one * scale, m, true);
        var b = go.AddComponent<Bullet>();
        b.damage = damage;
        b.explosionRadius = radius;
        b.impactEffect = impact;
        return Save(go, name);
    }

    static GameObject MakeTurret(string name, Material body, Material accent, GameObject bullet, float range, float rate,
        bool laser, int dps, Material beam)
    {
        var root = new GameObject(name);
        Prim(PrimitiveType.Cube, "Base", root.transform, new Vector3(0, 0.2f, 0), new Vector3(1f, 0.4f, 1f), body, true);
        var head = new GameObject("Head").transform;
        head.SetParent(root.transform, false);
        head.localPosition = new Vector3(0, 0.7f, 0);
        Prim(PrimitiveType.Cube, "Gun", head, Vector3.zero, new Vector3(0.7f, 0.5f, 0.7f), body, true).transform.localPosition = Vector3.zero;
        var barrel = Prim(PrimitiveType.Cube, "Barrel", head, Vector3.zero, new Vector3(0.22f, 0.22f, 1.1f), accent, true);
        barrel.transform.localPosition = new Vector3(0, 0, 0.6f);
        var fp = new GameObject("FirePoint").transform;
        fp.SetParent(head, false);
        fp.localPosition = new Vector3(0, 0, 1.2f);

        var t = root.AddComponent<Turret>();
        t.partToRotate = head;
        t.firePoint = fp;
        t.range = range;
        t.fireRate = rate;
        t.bulletPrefab = bullet;
        t.useLaser = laser;
        if (laser)
        {
            var lgo = new GameObject("Laser");
            lgo.transform.SetParent(root.transform, false);
            var lr = lgo.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.startWidth = lr.endWidth = 0.18f;
            lr.useWorldSpace = true;
            lr.sharedMaterial = beam;
            lr.enabled = false;
            t.lineRenderer = lr;
            t.damageOverTime = dps;
            t.slowPct = 0.5f;
        }
        return Save(root, name);
    }

    static Transform MakeEnemy(string name, Material m, float scale, float hp, float speed, int value)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.tag = "Enemy";
        go.transform.localScale = Vector3.one * scale;
        go.GetComponent<Renderer>().sharedMaterial = m;
        var e = go.AddComponent<Enemies>();
        e.health = hp;
        e.startSpeed = speed;
        e.value = value;
        go.AddComponent<EnemyMovement>();

        // world-space health bar
        var cgo = new GameObject("HealthBar", typeof(RectTransform), typeof(Canvas));
        cgo.transform.SetParent(go.transform, false);
        cgo.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        var rt = cgo.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(100, 12);
        cgo.transform.localPosition = new Vector3(0, 1.7f, 0);
        cgo.transform.localScale = Vector3.one * (1.6f / 100f / scale);
        cgo.AddComponent<Billboard>();

        Sprite sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
        bg.transform.SetParent(cgo.transform, false);
        Stretch(bg.GetComponent<RectTransform>());
        var bgImg = bg.GetComponent<Image>();
        bgImg.sprite = sprite;
        bgImg.color = new Color(0, 0, 0, 0.7f);

        var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fill.transform.SetParent(cgo.transform, false);
        Stretch(fill.GetComponent<RectTransform>());
        var fillImg = fill.GetComponent<Image>();
        fillImg.sprite = sprite;
        fillImg.type = Image.Type.Filled;
        fillImg.fillMethod = Image.FillMethod.Horizontal;
        fillImg.fillAmount = 1f;
        fillImg.color = new Color(0.25f, 0.9f, 0.3f);
        e.HealthBar = fillImg;

        return Save(go, name).transform;
    }

    static TurretBlueprint Blueprint(Transform parent, string name, GameObject prefab, int cost)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var bp = go.AddComponent<TurretBlueprint>();
        bp.prefab = prefab;
        bp.cost = cost;
        return bp;
    }

    static WaveDefinition.Group G(Transform prefab, int count, float interval, float delay = 0f)
    {
        return new WaveDefinition.Group { enemyPrefab = prefab, count = count, spawnInterval = interval, delayBefore = delay };
    }

    static WaveDefinition Wave(int n, params WaveDefinition.Group[] groups)
    {
        var w = ScriptableObject.CreateInstance<WaveDefinition>();
        w.groups = groups;
        AssetDatabase.CreateAsset(w, Gen + "/Waves/Wave" + n + ".asset");
        return w;
    }

    // ---------- UI helpers ----------
    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    static TextMeshProUGUI Text(Transform parent, string name, string txt, int size, TextAlignmentOptions align,
        Vector2 anchor, Vector2 pos, Vector2 sz)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = sz;
        var t = go.AddComponent<TextMeshProUGUI>();
        t.text = txt;
        t.fontSize = size;
        t.alignment = align;
        t.color = Color.white;
        return t;
    }

    static Button Btn(Transform parent, string label, Vector2 anchor, Vector2 pos, Vector2 sz, Color col)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = sz;
        var img = go.GetComponent<Image>();
        img.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        img.type = Image.Type.Sliced;
        img.color = col;
        var t = Text(go.transform, "Label", label, 26, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), Vector2.zero, sz);
        Stretch(t.rectTransform);
        return go.GetComponent<Button>();
    }

    static GameObject Panel(Transform parent, string name, string title, out Transform root)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Stretch(go.GetComponent<RectTransform>());
        go.GetComponent<Image>().color = new Color(0, 0, 0, 0.75f);
        Text(go.transform, "Title", title, 72, TextAlignmentOptions.Center, new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(800, 100));
        root = go.transform;
        return go;
    }
}
