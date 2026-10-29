using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// A plain room shell whose obstacle positions are sampled again for every run.
/// All dimensions use the same one-unit / 16-pixel cells as GridMap.
public sealed class OverrunProceduralRoom : MonoBehaviour
{
    public const string ResourceFolder = "OverrunArt/ProceduralProps/";
    public int width = 32, height = 24;
    public int minObstacles = 14, maxObstacles = 24;
    public int PlacedCount { get; private set; }
    public int DoorWidth => 4;

    sealed class Prop
    {
        public readonly string name;
        public readonly Vector2 footprint;
        public readonly float baseOffset;
        public readonly bool lowCover;
        public readonly float horizontalOffset;
        public Sprite sprite;

        public Prop(string name, float widthPixels, float depthPixels, float bottomPixels, bool lowCover = false, float horizontalPixels = 0)
        {
            this.name = name;
            footprint = new Vector2(widthPixels, depthPixels) / 16f;
            baseOffset = bottomPixels / 16f;
            this.lowCover = lowCover;
            horizontalOffset = horizontalPixels / 16f;
        }
    }

    static readonly Prop[] Props =
    {
        new Prop("Container_4_Gray_Horizontal_Overgrown_Dark-Green", 35, 13, 2),
        new Prop("Container_8_Red_Horizontal_Overgrown_Green", 35, 13, 2),
        new Prop("Container_12_Green_Horizontal_Overgrown_Bleak-Yellow", 35, 13, 2),
        new Prop("2-Tires_Grass_Dark-Green", 12, 8, 2, true),
        new Prop("Barrel_red_1", 12, 9, 0, true, -1.5f),
        new Prop("Garbage-Bin_2", 25, 9, 0, true),
        new Prop("Garbage-Bin_4", 16, 9, 0, true),
        new Prop("Refrigerator", 16, 10, 0),
        new Prop("Vending-machine_Blue_Overgrown_Green", 15, 11, 1),
        new Prop("Vending-machine_Red_Overgrown_Bleak-Yellow", 15, 11, 1)
    };
    static Tile floorTile, wallTile;
    readonly List<GameObject> obstacles = new List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCaches()
    {
        floorTile = wallTile = null;
        foreach (var prop in Props) prop.sprite = null;
    }

    public static GameObject CreateTemplate(int width, int height, int minProps, int maxProps)
    {
        if (width < 20 || height < 20) throw new ArgumentOutOfRangeException("Procedural rooms need at least 20 cells per side.");
        int walls = LayerMask.NameToLayer("Walls");
        if (walls < 0) throw new InvalidOperationException("The Walls layer is required for procedural rooms.");
        EnsureArt();
        var root = new GameObject("Procedural Room Template");
        root.SetActive(false);
        root.AddComponent<Grid>();
        var template = root.AddComponent<RoomTemplate>();
        var room = root.AddComponent<OverrunProceduralRoom>();
        room.width = width;
        room.height = height;
        room.minObstacles = Mathf.Max(0, minProps);
        room.maxObstacles = Mathf.Max(room.minObstacles, maxProps);

        var floor = CreateMap(root.transform, "Floor", false, walls);
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            floor.SetTile(new Vector3Int(x, y, 0), floorTile);

        var perimeter = CreateMap(root.transform, "Walls", true, walls);
        int doorX = width / 2 - room.DoorWidth / 2, doorY = height / 2 - room.DoorWidth / 2;
        for (int x = 0; x < width; x++)
        {
            if (x >= doorX && x < doorX + room.DoorWidth) continue;
            perimeter.SetTile(new Vector3Int(x, 0, 0), wallTile);
            perimeter.SetTile(new Vector3Int(x, height - 1, 0), wallTile);
        }
        for (int y = 1; y < height - 1; y++)
        {
            if (y >= doorY && y < doorY + room.DoorWidth) continue;
            perimeter.SetTile(new Vector3Int(0, y, 0), wallTile);
            perimeter.SetTile(new Vector3Int(width - 1, y, 0), wallTile);
        }
        perimeter.gameObject.AddComponent<OverrunWallDepth>().Build(perimeter);

        template.blockNorth = CreateClosure(root.transform, "North", new Vector3Int(doorX, height - 1, 0), Vector3Int.right, room.DoorWidth, walls);
        template.blockSouth = CreateClosure(root.transform, "South", new Vector3Int(doorX, 0, 0), Vector3Int.right, room.DoorWidth, walls);
        template.blockEast = CreateClosure(root.transform, "East", new Vector3Int(width - 1, doorY, 0), Vector3Int.up, room.DoorWidth, walls);
        template.blockWest = CreateClosure(root.transform, "West", new Vector3Int(0, doorY, 0), Vector3Int.up, room.DoorWidth, walls);

        foreach (var position in new[] { new Vector2(2.5f, 2.5f), new Vector2(width - 2.5f, 2.5f),
            new Vector2(2.5f, height - 2.5f), new Vector2(width - 2.5f, height - 2.5f) })
        {
            var point = new GameObject("Edge Spawn Point");
            point.transform.SetParent(root.transform, false);
            point.transform.localPosition = position;
            point.AddComponent<SpawnPoint>();
        }
        return root;
    }

    public void Randomize(System.Random rng)
    {
        ClearObstacles();
        EnsureArt();
        int target = rng.Next(minObstacles, maxObstacles + 1);
        var occupied = new List<RectInt>();
        for (int attempt = 0; attempt < target * 50 && PlacedCount < target; attempt++)
        {
            int propIndex = rng.Next(Props.Length); var prop = Props[propIndex];
            // Reserve the entire artwork, not only its smaller ground collider.
            // This also keeps tall sprites out of doorway and perimeter lanes.
            int w = Mathf.CeilToInt(prop.sprite.rect.width / 16f);
            int h = Mathf.CeilToInt(prop.sprite.rect.height / 16f);
            var area = new RectInt(rng.Next(4, width - 3 - w), rng.Next(4, height - 3 - h), w, h);
            if (area.xMin < width / 2 + 2 && area.xMax > width / 2 - 2) continue;
            if (area.yMin < height / 2 + 2 && area.yMax > height / 2 - 2) continue;
            var padded = new RectInt(area.x - 2, area.y - 2, area.width + 4, area.height + 4);
            bool overlaps = false;
            foreach (var previous in occupied) if (padded.Overlaps(previous)) { overlaps = true; break; }
            if (overlaps) continue;
            occupied.Add(area);

            var obstacle = CreateProp(transform, propIndex, new Vector2(area.x + area.width * .5f, area.y));
            obstacles.Add(obstacle);
            PlacedCount++;
        }
    }

    public static int PropCount => Props.Length;
    public static GameObject CreateProp(Transform parent, int index, Vector2 localPosition)
    {
        EnsureArt(); var prop = Props[index];
        var obstacle = new GameObject(prop.name); obstacle.transform.SetParent(parent, false);
        obstacle.transform.localPosition = localPosition; obstacle.layer = LayerMask.NameToLayer("Walls");
        var renderer = obstacle.AddComponent<SpriteRenderer>(); renderer.sprite = prop.sprite; renderer.sortingOrder = 21;
        var collider = obstacle.AddComponent<PolygonCollider2D>();
        collider.pathCount = 1;
        float left = prop.horizontalOffset - prop.footprint.x * .5f, right = left + prop.footprint.x;
        float bottom = prop.baseOffset, top = bottom + prop.footprint.y;
        float bevel = Mathf.Min(2f / 16, prop.footprint.y * .25f);
        collider.SetPath(0, new[] { new Vector2(left + bevel, bottom), new Vector2(right - bevel, bottom),
            new Vector2(right, bottom + bevel), new Vector2(right, top - bevel), new Vector2(right - bevel, top),
            new Vector2(left + bevel, top), new Vector2(left, top - bevel), new Vector2(left, bottom + bevel) });
        collider.sharedMaterial = OverrunCollisionMaterial.Sliding;
        OverrunDepthSort.Attach(obstacle, bottom + prop.footprint.y * .5f);
        if (prop.lowCover) obstacle.AddComponent<OverrunShootThrough>();
        return obstacle;
    }

    public void ClearObstacles()
    {
        foreach (var obstacle in obstacles)
        {
            if (!obstacle) continue;
            // Disable before deferred destruction so the connectivity scan cannot
            // observe colliders from the previous placement attempt.
            obstacle.SetActive(false);
            if (Application.isPlaying) Destroy(obstacle); else DestroyImmediate(obstacle);
        }
        obstacles.Clear();
        PlacedCount = 0;
    }

    static Tilemap CreateMap(Transform parent, string name, bool solid, int walls)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var map = go.AddComponent<Tilemap>();
        go.AddComponent<TilemapRenderer>().sortingOrder = solid ? 20 : 0;
        if (solid)
        {
            go.layer = walls;
            go.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
            var collider = go.AddComponent<TilemapCollider2D>();
            var composite = go.AddComponent<CompositeCollider2D>();
            collider.compositeOperation = Collider2D.CompositeOperation.Merge;
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
        }
        return map;
    }

    static GameObject CreateClosure(Transform parent, string side, Vector3Int start, Vector3Int step, int count, int walls)
    {
        var map = CreateMap(parent, "Door Closure " + side, true, walls);
        for (int i = 0; i < count; i++) map.SetTile(start + step * i, wallTile);
        map.gameObject.AddComponent<OverrunWallDepth>().Build(map);
        return map.gameObject;
    }

    static void EnsureArt()
    {
        foreach (var prop in Props)
        {
            if (!prop.sprite) prop.sprite = Resources.Load<Sprite>(ResourceFolder + prop.name);
            if (!prop.sprite) throw new InvalidOperationException("Missing procedural prop sprite: " + prop.name);
        }
        if (!floorTile)
        {
            var texture = Resources.Load<Texture2D>("OverrunArt/OfficeMap1/Buildings_gray_TileSet");
            if (!texture) throw new InvalidOperationException("Missing building floor atlas.");
            // OfficeMap1's floor GID 60, interpreted against its original 13-column atlas.
            var sprite = Sprite.Create(texture, new Rect(112, texture.height - 80, 16, 16), new Vector2(.5f, .5f), 16);
            floorTile = MakeTile(sprite, Tile.ColliderType.None);
        }
        if (!wallTile)
        {
            // A fully opaque 16px masonry cap. The art fills precisely the same
            // cell as its collider, including room corners and closed doorways.
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            texture.name = "Procedural Masonry Wall";
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
            {
                bool edge = x < 2 || y < 2 || x > 13 || y > 13;
                bool mortar = y == 7 || (x == 7 && y > 7) || (x == 3 && y < 7);
                texture.SetPixel(x, y, edge ? new Color(.19f, .22f, .25f) :
                    mortar ? new Color(.29f, .32f, .35f) : new Color(.45f, .48f, .51f));
            }
            texture.Apply();
            wallTile = MakeTile(Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(.5f, .5f), 16), Tile.ColliderType.Grid);
        }
    }

    static Tile MakeTile(Sprite sprite, Tile.ColliderType collision)
    {
        var tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = sprite;
        tile.colliderType = collision;
        return tile;
    }
}
