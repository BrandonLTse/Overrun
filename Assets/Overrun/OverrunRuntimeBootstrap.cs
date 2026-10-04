using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Zero-setup playable prototype. It builds a small 3x3 building at runtime so the
// project can be tested before art, prefabs, or tilemaps are authored.
public sealed class OverrunRuntimeBootstrap : MonoBehaviour
{
    static readonly Color Floor = new Color(0.075f, 0.09f, 0.11f);
    static readonly Color Wall = new Color(0.48f, 0.53f, 0.60f);
    static readonly Color PlayerColor = new Color(0.2f, 0.75f, 1f);
    static readonly Color[] EnemyColors = { new Color(0.85f, 0.22f, 0.25f), new Color(0.66f, 0.18f, 0.7f), new Color(0.95f, 0.55f, 0.16f) };

    LevelGenerator level;
    FlowFieldService flow;
    WaveDirector director;
    Transform player;
    Camera cam;
    GameObject template;
    readonly List<GameObject> runtimeTemplates = new List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() { if (FindAnyObjectByType<OverrunRuntimeBootstrap>() == null) new GameObject("Overrun Runtime").AddComponent<OverrunRuntimeBootstrap>(); }

    void Awake()
    {
        Application.targetFrameRate = 120;
        int importedRoomW, importedRoomH;
        var importedTemplate = OverrunTiledRoomFactory.TryBuild("OverrunArt/OfficeMap1/OfficeMap1Data", out importedRoomW, out importedRoomH);
        if (importedTemplate)
        {
            // The supplied map is used as a one-room playable preview first.
            // Its door sockets and closure tiles are already authored in Tiled.
            template = importedTemplate;
            runtimeTemplates.Add(template);
        }
        else
        {
            template = CreateRoomTemplate("Lobby", 0);
            runtimeTemplates.Add(template);
            runtimeTemplates.Add(CreateRoomTemplate("Cafeteria", 1));
            runtimeTemplates.Add(CreateRoomTemplate("Server Room", 2));
        }
        level = gameObject.AddComponent<LevelGenerator>();
        level.templates = runtimeTemplates.ConvertAll(t => t.GetComponent<RoomTemplate>()).ToArray();
        level.gridW = importedTemplate ? 1 : 3;
        level.gridH = importedTemplate ? 1 : 3;
        level.roomW = importedTemplate ? importedRoomW : 32;
        level.roomH = importedTemplate ? importedRoomH : 24;
        level.cellSize = 1f;
        level.extraLoops = 2; level.seed = 0; level.minReachableFraction = 0.94f;
        int wallLayer = LayerMask.NameToLayer("Walls");
        if (wallLayer < 0) wallLayer = 0;
        level.wallMask = 1 << wallLayer;

        player = CreatePlayer().transform;
        cam = CreateCamera();
        player.GetComponent<OverrunPlayer>().SetCamera(cam);
        flow = gameObject.AddComponent<FlowFieldService>();
        flow.player = player;
        director = gameObject.AddComponent<WaveDirector>();
        director.level = level; director.player = player; director.cam = cam;
        director.wallMask = level.wallMask;
        director.enemies = CreateEnemyDefinitions();
        CreateHud();
    }

    void Start()
    {
        level.Generate();
        player.GetComponent<OverrunPlayer>().SetSpawnPosition(level.PlayerStart);
        cam.transform.position = new Vector3(player.position.x, player.position.y, -10f);
        flow.Init(level.Map);
        director.Begin();
        foreach (var roomTemplate in runtimeTemplates) roomTemplate.SetActive(false);
    }

    GameObject CreateRoomTemplate(string templateName, int style)
    {
        int walls = LayerMask.NameToLayer("Walls");
        if (walls < 0) walls = 0;
        var root = new GameObject("Runtime " + templateName + " Template"); root.SetActive(false);
        var rt = root.AddComponent<RoomTemplate>();
        float w = levelRoomW(), h = levelRoomH(), t = 0.6f, door = 3f;
        AddSprite(root.transform, "Floor", new Vector2(w, h), new Vector2(w, h) * .5f, Floor, 0, 0);
        rt.blockNorth = AddWall(root.transform, "Block North", new Vector2(door, t), new Vector2(w * .5f, h - t * .5f), walls);
        rt.blockSouth = AddWall(root.transform, "Block South", new Vector2(door, t), new Vector2(w * .5f, t * .5f), walls);
        rt.blockEast = AddWall(root.transform, "Block East", new Vector2(t, door), new Vector2(w - t * .5f, h * .5f), walls);
        rt.blockWest = AddWall(root.transform, "Block West", new Vector2(t, door), new Vector2(t * .5f, h * .5f), walls);
        AddWall(root.transform, "North L", new Vector2((w - door) * .5f, t), new Vector2((w - door) * .25f, h - t * .5f), walls);
        AddWall(root.transform, "North R", new Vector2((w - door) * .5f, t), new Vector2(w - (w - door) * .25f, h - t * .5f), walls);
        AddWall(root.transform, "South L", new Vector2((w - door) * .5f, t), new Vector2((w - door) * .25f, t * .5f), walls);
        AddWall(root.transform, "South R", new Vector2((w - door) * .5f, t), new Vector2(w - (w - door) * .25f, t * .5f), walls);
        AddWall(root.transform, "East T", new Vector2(t, (h - door) * .5f), new Vector2(w - t * .5f, h - (h - door) * .25f), walls);
        AddWall(root.transform, "East B", new Vector2(t, (h - door) * .5f), new Vector2(w - t * .5f, (h - door) * .25f), walls);
        AddWall(root.transform, "West T", new Vector2(t, (h - door) * .5f), new Vector2(t * .5f, h - (h - door) * .25f), walls);
        AddWall(root.transform, "West B", new Vector2(t, (h - door) * .5f), new Vector2(t * .5f, (h - door) * .25f), walls);

        // Non-colliding threshold strips make every generated doorway visible in-game.
        var threshold = new Color(.08f, .11f, .14f);
        AddSprite(root.transform, "Doorway North", new Vector2(door, .18f), new Vector2(w * .5f, h - .34f), threshold, 8, 0);
        AddSprite(root.transform, "Doorway South", new Vector2(door, .18f), new Vector2(w * .5f, .34f), threshold, 8, 0);
        AddSprite(root.transform, "Doorway East", new Vector2(.18f, door), new Vector2(w - .34f, h * .5f), threshold, 8, 0);
        AddSprite(root.transform, "Doorway West", new Vector2(.18f, door), new Vector2(.34f, h * .5f), threshold, 8, 0);

        // Permanent interior partitions create lanes inside the large rooms. Their gaps
        // preserve a central crossing and side routes, so the room stays connected.
        AddWall(root.transform, "Interior South-West", new Vector2(8f, .55f), new Vector2(8f, 6f), walls);
        AddWall(root.transform, "Interior South-East", new Vector2(8f, .55f), new Vector2(24f, 6f), walls);
        AddWall(root.transform, "Interior North-West", new Vector2(8f, .55f), new Vector2(8f, 18f), walls);
        AddWall(root.transform, "Interior North-East", new Vector2(8f, .55f), new Vector2(24f, 18f), walls);
        AddWall(root.transform, "Interior West-Lower", new Vector2(.55f, 4.5f), new Vector2(16f, 8.25f), walls);
        AddWall(root.transform, "Interior West-Upper", new Vector2(.55f, 4.5f), new Vector2(16f, 15.75f), walls);

        var variants = new GameObject[3];
        for (int v = 0; v < variants.Length; v++)
        {
            var group = new GameObject("Obstacle Variant " + v); group.transform.SetParent(root.transform); group.SetActive(v == 0); variants[v] = group;
            if (style == 0 && v == 0) { AddObstacle(group.transform, new Vector2(3, 3), new Vector2(6, 6), walls); AddObstacle(group.transform, new Vector2(3, 3), new Vector2(26, 6), walls); AddObstacle(group.transform, new Vector2(3, 3), new Vector2(10, 18), walls); AddObstacle(group.transform, new Vector2(3, 3), new Vector2(22, 18), walls); }
            if (style == 0 && v == 1) { AddObstacle(group.transform, new Vector2(8, 2), new Vector2(16, 13), walls); AddObstacle(group.transform, new Vector2(3, 3), new Vector2(6, 7), walls); AddObstacle(group.transform, new Vector2(3, 3), new Vector2(26, 7), walls); }
            if (style == 0 && v == 2) { AddObstacle(group.transform, new Vector2(5, 2), new Vector2(8, 7), walls); AddObstacle(group.transform, new Vector2(5, 2), new Vector2(24, 7), walls); AddObstacle(group.transform, new Vector2(5, 2), new Vector2(8, 17), walls); AddObstacle(group.transform, new Vector2(5, 2), new Vector2(24, 17), walls); }
            if (style == 1 && v == 0) { for (int x = 6; x <= 26; x += 7) { AddObstacle(group.transform, new Vector2(4, 2), new Vector2(x, 7), walls); AddObstacle(group.transform, new Vector2(4, 2), new Vector2(x, 12), walls); AddObstacle(group.transform, new Vector2(4, 2), new Vector2(x, 17), walls); } }
            if (style == 1 && v == 1) { AddObstacle(group.transform, new Vector2(24, 2), new Vector2(16, 21), walls); AddObstacle(group.transform, new Vector2(4, 2), new Vector2(7, 7), walls); AddObstacle(group.transform, new Vector2(4, 2), new Vector2(16, 7), walls); AddObstacle(group.transform, new Vector2(4, 2), new Vector2(25, 7), walls); }
            if (style == 1 && v == 2) { AddObstacle(group.transform, new Vector2(2, 7), new Vector2(6, 12), walls); AddObstacle(group.transform, new Vector2(2, 7), new Vector2(12, 12), walls); AddObstacle(group.transform, new Vector2(2, 7), new Vector2(20, 12), walls); AddObstacle(group.transform, new Vector2(2, 7), new Vector2(26, 12), walls); }
            if (style == 2 && v == 0) { for (int x = 6; x <= 26; x += 7) AddObstacle(group.transform, new Vector2(3, 15), new Vector2(x, 12), walls); }
            if (style == 2 && v == 1) { for (int y = 6; y <= 18; y += 6) { AddObstacle(group.transform, new Vector2(20, 2), new Vector2(16, y), walls); AddObstacle(group.transform, new Vector2(4, 2), new Vector2(6, y), walls); AddObstacle(group.transform, new Vector2(4, 2), new Vector2(26, y), walls); } }
            if (style == 2 && v == 2) { AddObstacle(group.transform, new Vector2(3, 10), new Vector2(7, 12), walls); AddObstacle(group.transform, new Vector2(3, 10), new Vector2(14, 12), walls); AddObstacle(group.transform, new Vector2(3, 10), new Vector2(21, 12), walls); AddObstacle(group.transform, new Vector2(3, 10), new Vector2(28, 12), walls); }
        }
        rt.layoutVariants = variants;
        foreach (var pos in new[] { new Vector2(w * .5f, h - 2), new Vector2(w - 2, h * .5f), new Vector2(w * .5f, 2), new Vector2(2, h * .5f) })
        { var s = new GameObject("Spawn Point"); s.transform.SetParent(root.transform); s.transform.localPosition = pos; s.AddComponent<SpawnPoint>(); }
        return root;
    }

    float levelRoomW() => 32f; float levelRoomH() => 24f;
    GameObject AddWall(Transform p, string n, Vector2 size, Vector2 pos, int layer) { var g = AddSprite(p, n, size, pos, Wall, 20, layer); g.AddComponent<BoxCollider2D>().size = Vector2.one; return g; }
    void AddObstacle(Transform p, Vector2 size, Vector2 pos, int layer)
    {
        var g = new GameObject("Obstacle");
        g.transform.SetParent(p); g.transform.localPosition = pos; g.layer = layer;
        var collider = g.AddComponent<BoxCollider2D>(); collider.size = size;
        var collisionVisual = new GameObject("Collision Silhouette"); collisionVisual.transform.SetParent(g.transform); collisionVisual.transform.localScale = new Vector3(size.x, size.y, 1f);
        var collisionRenderer = collisionVisual.AddComponent<SpriteRenderer>(); collisionRenderer.sprite = OverrunVisuals.Sprite(new Color(.16f, .19f, .22f)); collisionRenderer.sortingOrder = 1;

        // Rows are built from individual, uniformly scaled prop sprites rather than one
        // distorted texture. Horizontal and vertical rows use different atlas art.
        bool horizontal = size.x > size.y * 2f;
        bool vertical = size.y > size.x * 2f;
        int art = horizontal ? 4 : vertical ? 3 : (size.x >= 7f && size.y <= 3f ? 0 : size.x >= 4f && size.y <= 3f ? 1 : 5);
        int count = horizontal ? Mathf.Max(1, Mathf.CeilToInt(size.x / 3f)) : vertical ? Mathf.Max(1, Mathf.CeilToInt(size.y / 3f)) : 1;
        float scale = horizontal || vertical ? 2.35f : Mathf.Min(size.x, size.y) * .82f;
        for (int i = 0; i < count; i++)
        {
            var visual = new GameObject("Prop Piece"); visual.transform.SetParent(g.transform);
            if (horizontal) visual.transform.localPosition = new Vector3(-size.x * .5f + (i + .5f) * size.x / count, 0f, 0f);
            else if (vertical) visual.transform.localPosition = new Vector3(0f, -size.y * .5f + (i + .5f) * size.y / count, 0f);
            var sr = visual.AddComponent<SpriteRenderer>(); sr.sprite = OverrunVisuals.Prop(art); sr.sortingOrder = 2;
            visual.transform.localScale = Vector3.one * scale;
        }
    }
    GameObject AddSprite(Transform p, string n, Vector2 size, Vector2 pos, Color color, int order, int layer)
    { var g = new GameObject(n); g.transform.SetParent(p); g.transform.localPosition = pos; var sr = g.AddComponent<SpriteRenderer>(); sr.sprite = OverrunVisuals.Sprite(color); sr.drawMode = SpriteDrawMode.Simple; sr.sortingOrder = order; g.transform.localScale = new Vector3(size.x, size.y, 1f); g.layer = layer; return g; }

    GameObject CreatePlayer()
    {
        var g = new GameObject("Player"); var sr = g.AddComponent<SpriteRenderer>(); sr.sprite = OverrunVisuals.Character(0); sr.color = Color.white; sr.sortingOrder = 50; var gun = new GameObject("Aim Direction"); gun.transform.SetParent(g.transform); gun.transform.localPosition = new Vector3(0f, .55f, 0f); gun.transform.localScale = new Vector3(.16f, .8f, 1f); var gunSr = gun.AddComponent<SpriteRenderer>(); gunSr.sprite = OverrunVisuals.Character(5); gunSr.sortingOrder = 51;
        var collider = g.AddComponent<CircleCollider2D>();
        collider.radius = .38f;
        collider.sharedMaterial = new PhysicsMaterial2D("Player movement") { friction = 0f, bounciness = 0f };
        var rb = g.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0; rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        g.AddComponent<OverrunPlayer>(); return g;
    }
    Camera CreateCamera()
    {
        // SampleScene already contains a camera/listener. Disable the starter camera
        // before creating the runtime camera so Unity always has one active listener.
        foreach (var existingCamera in FindObjectsByType<Camera>()) existingCamera.enabled = false;
        foreach (var existingListener in FindObjectsByType<AudioListener>()) existingListener.enabled = false;
        var g = new GameObject("Main Camera"); g.tag = "MainCamera";
        var c = g.AddComponent<Camera>(); c.orthographic = true; c.orthographicSize = 7.5f;
        g.AddComponent<AudioListener>();
        var follow = g.AddComponent<OverrunCamera>(); follow.target = player; return c;
    }

    EnemyDefinition[] CreateEnemyDefinitions()
    {
        var defs = new EnemyDefinition[3];
        for (int i = 0; i < defs.Length; i++) { var d = ScriptableObject.CreateInstance<EnemyDefinition>(); d.name = new[] { "Shambler", "Runner", "Brute" }[i]; d.prefab = CreateEnemyPrefab(i).GetComponent<Enemy>(); d.maxHealth = new[] { 3f, 2f, 12f }[i]; d.speed = new[] { 2.1f, 3.6f, 1.35f }[i]; d.contactDamage = new[] { 8f, 12f, 20f }[i]; d.cost = new[] { 1, 2, 8 }[i]; d.weight = new[] { 6f, 2f, 1f }[i]; d.unlockTime = new[] { 0f, 20f, 60f }[i]; defs[i] = d; } return defs;
    }
    GameObject CreateEnemyPrefab(int index)
    { var g = new GameObject("Enemy Prefab " + index); g.SetActive(false); var sr = g.AddComponent<SpriteRenderer>(); sr.sprite = OverrunVisuals.Character(index + 1); sr.sortingOrder = 48; sr.transform.localScale = Vector3.one * (index == 2 ? 1.35f : index == 1 ? .8f : 1f); var rb = g.AddComponent<Rigidbody2D>(); rb.gravityScale = 0; rb.freezeRotation = true; rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous; g.AddComponent<CircleCollider2D>().radius = .36f; g.AddComponent<Enemy>(); g.AddComponent<OverrunEnemyContact>(); return g; }

    void CreateHud() { var g = new GameObject("HUD"); var hud = g.AddComponent<OverrunHud>(); hud.player = player; hud.director = director; }
}

public static class OverrunVisuals
{
    static readonly Dictionary<string, Texture2D> atlases = new Dictionary<string, Texture2D>();
    static readonly Dictionary<string, Sprite[]> sprites = new Dictionary<string, Sprite[]>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCaches() { atlases.Clear(); sprites.Clear(); }
    public static Sprite Sprite(Color c) { var tex = new Texture2D(1, 1); tex.name = "Runtime Pixel Fallback"; tex.SetPixel(0, 0, c); tex.Apply(); return UnityEngine.Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1f); }
    public static Sprite Character(int index) => AtlasSprite("OverrunArt/CharactersAtlas", index);
    public static Sprite Prop(int index) => AtlasSprite("OverrunArt/PropsAtlas", index);
    static Sprite AtlasSprite(string path, int index)
    {
        if (!sprites.TryGetValue(path, out var set))
        {
            var tex = Resources.Load<Texture2D>(path);
            if (!tex) return Sprite(Color.magenta);
            tex.filterMode = FilterMode.Point; tex.wrapMode = TextureWrapMode.Clamp;
            set = new Sprite[6]; float w = tex.width / 3f, h = tex.height / 2f;
            for (int i = 0; i < 6; i++) { int x = i % 3, row = i / 3; set[i] = UnityEngine.Sprite.Create(tex, new Rect(x * w, (1 - row) * h, w, h), new Vector2(.5f, .5f), 512f); }
            sprites[path] = set;
        }
        return set[Mathf.Clamp(index, 0, 5)];
    }
}

public sealed class OverrunCamera : MonoBehaviour { public Transform target; void LateUpdate() { if (target) transform.position = new Vector3(target.position.x, target.position.y, -10); } }

public sealed class OverrunPlayer : MonoBehaviour
{
    public float speed = 7f, maxHealth = 100f, fireRate = .16f, damage = 2f; public int level = 1, xp; public bool ChoosingUpgrade { get; private set; } Rigidbody2D rb; float health, fireTimer; Camera cam; Transform aimVisual;
    Vector2 moveInput, spawnPosition;
    void Awake() { rb = GetComponent<Rigidbody2D>(); health = maxHealth; cam = Camera.main; aimVisual = transform.Find("Aim Direction"); }
    public void SetCamera(Camera cameraForAim) { cam = cameraForAim; }
    public void SetSpawnPosition(Vector2 position) { spawnPosition = position; Respawn(); }
    void Respawn()
    {
        rb.linearVelocity = Vector2.zero;
        rb.position = spawnPosition;
        transform.position = new Vector3(spawnPosition.x, spawnPosition.y, transform.position.z);
        Physics2D.SyncTransforms();
    }
    void FixedUpdate() { rb.linearVelocity = moveInput * speed; }
    void OnDisable() { moveInput = Vector2.zero; if (rb) rb.linearVelocity = Vector2.zero; }
    void Update()
    {
        if (!cam) cam = Camera.main;
        var kb = Keyboard.current;
        if (ChoosingUpgrade && kb != null) { if (kb.digit1Key.wasPressedThisFrame) ChooseUpgrade(1); else if (kb.digit2Key.wasPressedThisFrame) ChooseUpgrade(2); else if (kb.digit3Key.wasPressedThisFrame) ChooseUpgrade(3); }
        moveInput = kb == null ? Vector2.zero : new Vector2((kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0), (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0)).normalized;
        if (Mouse.current == null || cam == null) return;
        Vector2 aim = (Vector2)cam.ScreenToWorldPoint(Mouse.current.position.ReadValue()) - rb.position;
        if (aim.sqrMagnitude < .001f) return;
        aim.Normalize();
        float angle = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg - 90f;
        if (aimVisual) aimVisual.rotation = Quaternion.Euler(0f, 0f, angle);
        fireTimer -= Time.deltaTime;
        if (!ChoosingUpgrade && Mouse.current.leftButton.isPressed && fireTimer <= 0)
        { fireTimer = fireRate; var b = new GameObject("Bullet"); b.transform.position = transform.position + (Vector3)(aim * .55f); b.transform.up = aim; b.AddComponent<OverrunBullet>().Init(damage, aim); }
    }
    public void Damage(float amount) { health -= amount; if (health <= 0) { health = maxHealth; Respawn(); } }
    public void AddXp(int amount) { xp += amount; int needed = level * 10; if (xp >= needed) { xp -= needed; level++; ChoosingUpgrade = true; } }
    void ChooseUpgrade(int choice) { if (choice == 1) damage += .75f; else if (choice == 2) speed += .45f; else fireRate = Mathf.Max(.055f, fireRate - .018f); ChoosingUpgrade = false; }
}

public sealed class OverrunBullet : MonoBehaviour
{
    float damage; Vector2 dir;
    public void Init(float d, Vector2 v)
    {
        damage = d; dir = v.normalized; Destroy(gameObject, 1.2f);
        var body = gameObject.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic; body.gravityScale = 0; body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        var sr = gameObject.AddComponent<SpriteRenderer>(); sr.sprite = OverrunVisuals.Character(5); sr.sortingOrder = 52;
        var col = gameObject.AddComponent<CircleCollider2D>(); col.isTrigger = true; col.radius = .12f;
    }
    void Update() { transform.position += (Vector3)(dir * 18f * Time.deltaTime); }
    void OnTriggerEnter2D(Collider2D c)
    {
        // Low cover blocks movement but is low enough for projectiles to pass over.
        if (c.GetComponentInParent<OverrunShootThrough>()) return;
        var e = c.GetComponent<Enemy>();
        if (e) e.TakeDamage(damage);
        Destroy(gameObject);
    }
}

public sealed class OverrunEnemyContact : MonoBehaviour { float timer; void OnCollisionStay2D(Collision2D c) { var p = c.collider.GetComponent<OverrunPlayer>(); if (p && timer <= 0) { timer = .5f; p.Damage(GetComponent<Enemy>().ContactDamage); } } void Update() { timer -= Time.deltaTime; } }

public sealed class OverrunXpPickup : MonoBehaviour { int value; public static void Spawn(Vector2 pos, int amount) { var g = new GameObject("XP Pickup"); g.transform.position = pos; var sr = g.AddComponent<SpriteRenderer>(); sr.sprite = OverrunVisuals.Character(4); sr.sortingOrder = 49; g.AddComponent<CircleCollider2D>().isTrigger = true; var p = g.AddComponent<OverrunXpPickup>(); p.value = amount; Destroy(g, 20f); } void OnTriggerEnter2D(Collider2D c) { var p = c.GetComponent<OverrunPlayer>(); if (!p) return; p.AddXp(value); Destroy(gameObject); } }
public sealed class OverrunHud : MonoBehaviour { public Transform player; public WaveDirector director; GUIStyle style; void OnGUI() { if (style == null) { style = new GUIStyle(GUI.skin.label); style.fontSize = 18; style.normal.textColor = Color.white; } var p = player ? player.GetComponent<OverrunPlayer>() : null; if (!p) return; GUI.Label(new Rect(18, 18, 700, 32), $"OVERRUN   HP {Mathf.CeilToInt(p.maxHealth)}   LV {p.level}   XP {p.xp}/{p.level * 10}", style); GUI.Label(new Rect(18, 45, 900, 32), p.ChoosingUpgrade ? "LEVEL UP: press 1 Damage   2 Move Speed   3 Fire Rate" : "WASD move   Mouse aim + hold left click   Survive the building", style); } }
