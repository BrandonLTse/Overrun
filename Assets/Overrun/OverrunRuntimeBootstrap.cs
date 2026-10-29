using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Zero-setup playable prototype. It builds a small 3x3 building at runtime so the
// project can be tested before art, prefabs, or tilemaps are authored.
public sealed class OverrunRuntimeBootstrap : MonoBehaviour
{
    const float CharacterHeightInTiles = 2f;
    static readonly Color Floor = new Color(0.075f, 0.09f, 0.11f);
    static readonly Color Wall = new Color(0.48f, 0.53f, 0.60f);
    static readonly Color PlayerColor = new Color(0.2f, 0.75f, 1f);
    static readonly Color[] EnemyColors = { new Color(0.85f, 0.22f, 0.25f), new Color(0.66f, 0.18f, 0.7f), new Color(0.95f, 0.55f, 0.16f) };

    [Header("Building generation")]
    public bool useTiledPreview;
    [Min(1)] public int buildingColumns = 3, buildingRows = 3;
    [Min(20)] public int roomWidth = 32, roomHeight = 24;
    [Min(0)] public int minRoomObstacles = 14, maxRoomObstacles = 24;
    public int buildingSeed;

    LevelGenerator level;
    FlowFieldService flow;
    WaveDirector director;
    Transform player;
    Camera cam;
    GameObject template;
    bool worldDepthSorting;
    readonly List<GameObject> runtimeTemplates = new List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void EnsureSession()
    {
        var runtime = FindAnyObjectByType<OverrunRuntimeBootstrap>();
        if (!runtime) runtime = new GameObject("Overrun Runtime").AddComponent<OverrunRuntimeBootstrap>();
        // Enter Play Mode may keep scene objects while dropping managed grid data.
        // Initialize eagerly and let Start's guard avoid a second generation pass.
        runtime.Start();
    }

    void Awake()
    {
        Application.targetFrameRate = 120;
        int importedRoomW = 0, importedRoomH = 0;
        var importedTemplate = useTiledPreview ? OverrunTiledRoomFactory.TryBuild("OverrunArt/OfficeMap1/OfficeMap1Data", out importedRoomW, out importedRoomH) : null;
        worldDepthSorting = !importedTemplate;
        if (importedTemplate)
        {
            // The supplied map is used as a one-room playable preview first.
            // Its door sockets and closure tiles are already authored in Tiled.
            template = importedTemplate;
            runtimeTemplates.Add(template);
        }
        else
        {
            template = OverrunProceduralRoom.CreateTemplate(roomWidth, roomHeight, minRoomObstacles, maxRoomObstacles);
            runtimeTemplates.Add(template);
        }
        level = gameObject.AddComponent<LevelGenerator>();
        level.templates = runtimeTemplates.ConvertAll(t => t.GetComponent<RoomTemplate>()).ToArray();
        level.gridW = importedTemplate ? 1 : buildingColumns;
        level.gridH = importedTemplate ? 1 : buildingRows;
        level.roomW = importedTemplate ? importedRoomW : roomWidth;
        level.roomH = importedTemplate ? importedRoomH : roomHeight;
        level.cellSize = 1f;
        level.extraLoops = 2; level.seed = buildingSeed; level.minReachableFraction = importedTemplate ? 0.94f : 1f;
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
        OverrunAudio.Ensure(player);
    }

    void Start()
    {
        if (!level) level = GetComponent<LevelGenerator>();
        if (level.Map != null && OverrunMenus.Instance && OverrunMenus.Instance.IsReady) { OverrunAudio.Ensure(player); return; }
        Time.timeScale = 1; AudioListener.pause = false;
        if (!player) player = FindAnyObjectByType<OverrunPlayer>().transform;
        if (!cam) cam = Camera.main;
        if (!flow) flow = GetComponent<FlowFieldService>();
        if (!director) director = GetComponent<WaveDirector>();
        level.Generate();
        var actor = player.GetComponent<OverrunPlayer>(); actor.InitializeSystems(); actor.SetSpawnPosition(level.PlayerStart);
        OverrunAudio.Ensure(player);
        cam.transform.position = new Vector3(player.position.x, player.position.y, -10f);
        flow.Init(level.Map);
        foreach (var roomTemplate in runtimeTemplates) roomTemplate.SetActive(false);
        var menus = GetComponent<OverrunMenus>(); if (!menus) menus = gameObject.AddComponent<OverrunMenus>();
        menus.Initialize(this, player.GetComponent<OverrunPlayer>(), director);
    }

    public void ResetRun(bool startWaves)
    {
        OverrunAudio.Ensure(player).ResetWorld();
        director.EndRun();
        OverrunBullet.ClearActive();
        OverrunBandagePickup.ClearActive(); ClearTransient<OverrunZombieCorpse>();
        ClearTransient<OverrunWeaponDeathVisual>();
        ClearTransient<OverrunShotBurst>(); ClearTransient<OverrunUpgradeFx>();
        player.GetComponent<OverrunPlayer>().ResetRun();
        flow.Init(level.Map); flow.RebuildAtPlayer();
        cam.GetComponent<OverrunCamera>().ResetForRun();
        if (startWaves) director.Begin();
    }

    static void ClearTransient<T>() where T : Component
    {
        foreach (var item in FindObjectsByType<T>()) { item.gameObject.SetActive(false); Destroy(item.gameObject); }
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
        var g = new GameObject("Player"); var visual = new GameObject("Character Visual"); visual.transform.SetParent(g.transform); visual.transform.localPosition = Vector3.zero; var sr = visual.AddComponent<SpriteRenderer>(); sr.sprite = OverrunVisuals.Character(0); sr.color = Color.white; sr.sortingOrder = 50; FitCharacterToTileHeight(sr); var animated = g.AddComponent<OverrunCharacterAnimator>(); animated.trackRigidbody = false; animated.useFullCharacter = false; animated.useHandsOverlay = false;
        var gun = new GameObject("Weapon"); gun.transform.SetParent(g.transform, false); gun.AddComponent<OverrunWeaponFeedback>();
        var collider = g.AddComponent<CircleCollider2D>();
        collider.radius = .45f;
        collider.sharedMaterial = new PhysicsMaterial2D("Player movement") { friction = 0f, bounciness = 0f };
        var rb = g.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0; rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        if (worldDepthSorting) OverrunDepthSort.Attach(g);
        g.AddComponent<OverrunPlayer>(); g.AddComponent<OverrunActorAudio>().isPlayer = true; return g;
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
    {
        var g = new GameObject("Enemy Prefab " + index); g.SetActive(false);
        var visual = new GameObject("Character Visual"); visual.transform.SetParent(g.transform, false);
        var sr = visual.AddComponent<SpriteRenderer>(); sr.sortingOrder = 48;
        if (index == 0)
        {
            sr.sprite = OverrunSmallZombieAnimator.Frames(OverrunSmallZombieAnimator.Facing.Down, OverrunSmallZombieAnimator.Pose.Idle)[0];
            visual.transform.localScale = Vector3.one * 2;
            g.AddComponent<OverrunSmallZombieAnimator>();
        }
        else if (index == 1)
        {
            sr.sprite = OverrunRunnerZombieAnimator.Frames(OverrunSmallZombieAnimator.Facing.Down, OverrunSmallZombieAnimator.Pose.Idle)[0];
            visual.transform.localScale = Vector3.one * 2;
            g.AddComponent<OverrunRunnerZombieAnimator>();
        }
        else
        {
            sr.sprite = OverrunBruteZombieAnimator.Frames(OverrunSmallZombieAnimator.Facing.Down, OverrunSmallZombieAnimator.Pose.Idle)[0];
            visual.transform.localScale = Vector3.one * 2;
            g.AddComponent<OverrunBruteZombieAnimator>();
        }
        var rb = g.AddComponent<Rigidbody2D>(); rb.gravityScale = 0; rb.freezeRotation = true; rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        var collider = g.AddComponent<CircleCollider2D>(); collider.radius = .36f; collider.sharedMaterial = OverrunCollisionMaterial.Sliding;
        if (worldDepthSorting) OverrunDepthSort.Attach(g);
        g.AddComponent<Enemy>(); g.AddComponent<OverrunEnemyContact>(); g.AddComponent<OverrunActorAudio>();
        return g;
    }

    static void FitCharacterToTileHeight(SpriteRenderer renderer)
    {
        if (!renderer || !renderer.sprite || renderer.sprite.bounds.size.y <= 0f) return;
        float scale = CharacterHeightInTiles / renderer.sprite.bounds.size.y;
        renderer.transform.localScale = Vector3.one * scale;
    }

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

public sealed class OverrunCharacterAnimator : MonoBehaviour
{
    const string AnimationAtlasPath = "OverrunArt/OverrunCharacterAnimationAtlas";
    const int Columns = 6, Rows = 24, CellWidth = 16, CellHeight = 18, Frames = 6;
    const float CharacterHeightInTiles = 2f;
    static Texture2D atlas;
    static Sprite[] frames;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetAnimationCache() { atlas = null; frames = null; }

    public bool trackRigidbody = true;
    public bool useFullCharacter;
    public bool useHandsOverlay;
    public float framesPerSecond = 8f;

    SpriteRenderer renderer;
    Rigidbody2D body;
    Transform visualTransform;
    Sprite fallbackSprite;
    Vector2 lastFacing = Vector2.down;
    bool moving;
    int direction;
    int frame;
    public int FrameIndex => frame;
    float frameTimer;
    bool warnedMissingAtlas;

    void Awake()
    {
        renderer = GetComponent<SpriteRenderer>();
        if (!renderer) renderer = GetComponentInChildren<SpriteRenderer>();
        body = GetComponent<Rigidbody2D>();
        visualTransform = renderer ? renderer.transform : transform;
        fallbackSprite = renderer ? renderer.sprite : null;
        EnsureFrames();
        ApplyFrame();
    }

    void Update()
    {
        if (OverrunMenus.GameplayBlocked) return;
        EnsureFrames();
        if (trackRigidbody && body)
        {
            Vector2 velocity = body.linearVelocity;
            SetMotionInternal(velocity, velocity.sqrMagnitude > .001f ? velocity : lastFacing);
        }
        if (!renderer || frames == null) return;
        frameTimer += Time.deltaTime;
        if (moving && frameTimer >= 1f / Mathf.Max(.01f, framesPerSecond))
        {
            frameTimer = 0f;
            frame = (frame + 1) % Frames;
            ApplyFrame();
        }
    }

    public void SetMotion(Vector2 movement, Vector2 facing)
    {
        if (trackRigidbody) return;
        SetMotionInternal(movement, facing);
    }

    void SetMotionInternal(Vector2 movement, Vector2 facing)
    {
        bool nextMoving = movement.sqrMagnitude > .001f;
        int nextDirection = DirectionFrom(facing.sqrMagnitude > .001f ? facing : lastFacing);
        if (facing.sqrMagnitude > .001f) lastFacing = facing.normalized;
        if (nextMoving != moving || nextDirection != direction)
        {
            moving = nextMoving;
            direction = nextDirection;
            frame = 0;
            frameTimer = 0f;
            ApplyFrame();
        }
        else moving = nextMoving;
    }

    int DirectionFrom(Vector2 facing)
    {
        if (Mathf.Abs(facing.x) > Mathf.Abs(facing.y)) return facing.x < 0f ? 2 : 1;
        return facing.y > 0f ? 3 : 0;
    }

    void EnsureFrames()
    {
        if (frames != null) return;
        if (!atlas) atlas = Resources.Load<Texture2D>(AnimationAtlasPath);
        if (!atlas)
        {
            if (!warnedMissingAtlas) { Debug.LogWarning("Character animation atlas not found at Resources/" + AnimationAtlasPath + ". Keeping the static character sprite."); warnedMissingAtlas = true; }
            return;
        }
        atlas.filterMode = FilterMode.Point;
        atlas.wrapMode = TextureWrapMode.Clamp;
        if (atlas.width < Columns * CellWidth || atlas.height < Rows * CellHeight)
        {
            if (!warnedMissingAtlas) { Debug.LogWarning("Character animation atlas has unexpected dimensions. Expected at least " + (Columns * CellWidth) + "x" + (Rows * CellHeight) + " pixels."); warnedMissingAtlas = true; }
            return;
        }
        var builtFrames = new Sprite[Rows * Frames];
        for (int row = 0; row < Rows; row++) for (int column = 0; column < Frames; column++)
        {
            var rect = new Rect(column * CellWidth, atlas.height - (row + 1) * CellHeight, CellWidth, CellHeight);
            builtFrames[row * Frames + column] = Sprite.Create(atlas, rect, new Vector2(.5f, .5f), 16f, 0, SpriteMeshType.FullRect);
        }
        for (int i = 0; i < builtFrames.Length; i++)
            if (!builtFrames[i]) { if (!warnedMissingAtlas) { Debug.LogWarning("Character animation atlas could not create all frame sprites. Keeping the static character sprite."); warnedMissingAtlas = true; } return; }
        frames = builtFrames;
    }

    void ApplyFrame()
    {
        if (!renderer || frames == null) return;
        int layerOffset = useHandsOverlay ? 8 : useFullCharacter ? 4 : 0;
        int row = (moving ? 12 : 0) + layerOffset + direction;
        int index = Mathf.Clamp(row * Frames + frame, 0, frames.Length - 1);
        var nextSprite = frames[index];
        if (!nextSprite) { if (fallbackSprite) renderer.sprite = fallbackSprite; return; }
        renderer.sprite = nextSprite;
        if (nextSprite.bounds.size.y > 0f && visualTransform)
            visualTransform.localScale = Vector3.one * (CharacterHeightInTiles / nextSprite.bounds.size.y);
    }
}

public sealed class OverrunCamera : MonoBehaviour
{
    public Transform target;
    [Range(0, .15f)] public float shotKick = .045f;
    Vector2 recoil;
    public void ResetForRun() { recoil = Vector2.zero; if (target) transform.position = new Vector3(target.position.x, target.position.y, -10); }
    public void Shot(Vector2 direction) { recoil = Vector2.ClampMagnitude(recoil - direction * shotKick, .08f); }
    void LateUpdate()
    {
        if (OverrunMenus.GameplayBlocked) return;
        if (target) transform.position = new Vector3(target.position.x + recoil.x, target.position.y + recoil.y, -10);
        recoil = Vector2.MoveTowards(recoil, Vector2.zero, Time.deltaTime * .85f);
    }
}

public sealed class OverrunPlayer : MonoBehaviour
{
    public float speed = 7f, maxHealth = 100f, fireRate = .16f, damage = 2f;
    public bool ChoosingUpgrade => upgrades && upgrades.Choosing;
    public float Health => health;
    Rigidbody2D rb; float health, fireTimer; Camera cam; Transform aimVisual; OverrunCharacterAnimator characterAnimator;
    Vector2 moveInput, spawnPosition, lastFacing = Vector2.down;
    OverrunWeaponFeedback weapon;
    OverrunUpgradeSystem upgrades;
    OverrunWeaponSystem weapons;
    bool suppressFireUntilRelease, triggerHeld;
    void Awake() { InitializeSystems(); health = maxHealth; weapons.ResetRun(); }
    void OnEnable() { InitializeSystems(); }
    public void InitializeSystems()
    {
        if (!rb) rb = GetComponent<Rigidbody2D>(); if (!cam) cam = Camera.main;
        if (!aimVisual) aimVisual = transform.Find("Weapon"); if (!aimVisual) aimVisual = transform.Find("Aim Direction");
        if (!weapon && aimVisual) weapon = aimVisual.GetComponent<OverrunWeaponFeedback>();
        if (!characterAnimator) characterAnimator = GetComponent<OverrunCharacterAnimator>();
        if (!upgrades) upgrades = GetComponent<OverrunUpgradeSystem>(); if (!upgrades) upgrades = gameObject.AddComponent<OverrunUpgradeSystem>();
        if (!weapons) weapons = GetComponent<OverrunWeaponSystem>();
        if (!weapons) { weapons = gameObject.AddComponent<OverrunWeaponSystem>(); weapons.ResetRun(); }
    }
    public void SetCamera(Camera cameraForAim) { cam = cameraForAim; }
    public void SetSpawnPosition(Vector2 position) { spawnPosition = position; Respawn(); }
    public void ClearMenuInput()
    {
        moveInput = Vector2.zero; if (rb) rb.linearVelocity = Vector2.zero;
        suppressFireUntilRelease = true;
        triggerHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;
    }
    public void WeaponChanged() { ClearMenuInput(); fireTimer = Mathf.Max(fireTimer, .2f); }
    public void ResetRun()
    {
        ClearMenuInput(); health = maxHealth; fireTimer = .25f; lastFacing = Vector2.down;
        weapons.ResetRun();
        upgrades.ResetRun(); Respawn();
        if (characterAnimator) characterAnimator.SetMotion(Vector2.zero, Vector2.down);
        if (weapon) weapon.ResetForRun();
    }
    void Respawn()
    {
        rb.linearVelocity = Vector2.zero;
        rb.position = spawnPosition;
        transform.position = new Vector3(spawnPosition.x, spawnPosition.y, transform.position.z);
        Physics2D.SyncTransforms();
    }
    void FixedUpdate() { rb.linearVelocity = OverrunMenus.GameplayBlocked ? Vector2.zero : moveInput * speed; }
    void OnDisable() { moveInput = Vector2.zero; if (rb) rb.linearVelocity = Vector2.zero; }
    void Update()
    {
        if (OverrunMenus.InputBlocked) { ClearMenuInput(); return; }
        if (Mouse.current == null || !Mouse.current.leftButton.isPressed) suppressFireUntilRelease = false;
        if (!cam) cam = Camera.main;
        var kb = Keyboard.current;
        if (kb != null)
        {
            // Decide the context before handling a key. A reward pick must not
            // fall through and also change weapons on the same frame.
            if (ChoosingUpgrade) { if (kb.digit1Key.wasPressedThisFrame) ChooseUpgrade(1); else if (kb.digit2Key.wasPressedThisFrame) ChooseUpgrade(2); else if (kb.digit3Key.wasPressedThisFrame) ChooseUpgrade(3); }
            else
            {
                if (kb.digit1Key.wasPressedThisFrame) weapons.TryEquip(1); else if (kb.digit2Key.wasPressedThisFrame) weapons.TryEquip(2);
                if (kb.rKey.wasPressedThisFrame) weapons.TryReload();
            }
        }
        moveInput = kb == null ? Vector2.zero : new Vector2((kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0), (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0)).normalized;
        Vector2 aim = moveInput.sqrMagnitude > .001f ? moveInput : lastFacing;
        if (Mouse.current != null && cam != null)
        {
            var mouseAim = (Vector2)cam.ScreenToWorldPoint(Mouse.current.position.ReadValue()) - rb.position;
            if (mouseAim.sqrMagnitude > .001f) aim = mouseAim.normalized;
        }
        if (aim.sqrMagnitude > .001f) lastFacing = aim.normalized;
        if (characterAnimator) characterAnimator.SetMotion(moveInput, lastFacing);
        if (weapon) weapon.SetAim(aim);
        if (Mouse.current == null || cam == null) return;
        float angle = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg - 90f;
        if (aimVisual) aimVisual.rotation = Quaternion.Euler(0f, 0f, angle);
        fireTimer -= Time.deltaTime;
        bool down = Mouse.current.leftButton.isPressed;
        bool pressed = down && !triggerHeld; triggerHeld = down;
        if (!ChoosingUpgrade && !weapons.IsReloading && !suppressFireUntilRelease && (weapons.Automatic ? down : pressed) && fireTimer <= 0)
        {
            if (!weapons.TryConsumeShot()) { fireTimer = .12f; return; }
            OverrunAudio.World(weapons.Automatic ? OverrunSound.RifleShot : OverrunSound.PistolShot, rb.position);
            fireTimer = fireRate * weapons.IntervalMultiplier;
            Vector2 muzzle = weapon ? weapon.MuzzlePosition : rb.position + aim * .65f;
            Vector2 flash = upgrades.FireWeapon(rb.position, muzzle, damage * weapons.DamageMultiplier, aim, GetComponentsInChildren<Collider2D>());
            if (weapon) weapon.Shot(flash, aim);
            var follow = cam.GetComponent<OverrunCamera>();
            if (follow) follow.Shot(aim);
        }
    }
    public void Damage(float amount)
    {
        if (OverrunMenus.GameplayBlocked || health <= 0 || amount <= 0 || ChoosingUpgrade || (upgrades && upgrades.AbsorbHit())) return;
        health = Mathf.Max(0, health - amount);
        if (health <= 0)
        {
            if (weapon) weapon.SpawnDeath(); weapons.CancelReload();
            ClearMenuInput();
            if (OverrunMenus.Instance) OverrunMenus.Instance.EndRun();
        }
        else OverrunAudio.World(OverrunSound.PlayerHit, rb.position);
    }
    public bool TryHeal(float amount)
    {
        if (OverrunMenus.GameplayBlocked || health <= 0 || amount <= 0 || health >= maxHealth) return false;
        health = Mathf.Min(maxHealth, health + amount); return true;
    }
    public bool ChooseUpgrade(int choice) { return upgrades && upgrades.Choose(choice - 1); }
    public void RewardChosen() { ClearMenuInput(); fireTimer = Mathf.Max(fireTimer, .25f); }
}

public sealed class OverrunEnemyContact : MonoBehaviour
{
    float timer;
    Enemy enemy;
    void Awake() { enemy = GetComponent<Enemy>(); }
    void OnEnable() { timer = 0; }
    void OnCollisionStay2D(Collision2D collision)
    {
        var player = collision.collider.GetComponentInParent<OverrunPlayer>();
        if (player && timer <= 0 && enemy && enemy.TryMeleeAttack(player)) timer = .5f;
    }
    void Update() { if (!OverrunMenus.GameplayBlocked) timer -= Time.deltaTime; }
}

