using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEngine;
using UnityEngine.Tilemaps;

/// Marker placed on short cover tilemaps. Movement collides with these objects,
/// but bullets pass through them because they are low enough to shoot over.
public sealed class OverrunShootThrough : MonoBehaviour { }

/// Small runtime TMX importer used by the playable prototype. It intentionally
/// supports the map features used by the project rather than trying to replace
/// a full editor importer: external TSX tilesets, CSV tile layers, groups,
/// doorway object groups, and simple spawn-point object groups.
public static class OverrunTiledRoomFactory
{
    const int TiledGidMask = 0x1FFFFFFF;

    sealed class TilesetData
    {
        public int firstGid;
        public int tileWidth;
        public int tileHeight;
        public int columns;
        public int tileCount;
        public int margin;
        public int spacing;
        public Texture2D texture;
        public readonly Dictionary<int, Sprite> sprites = new Dictionary<int, Sprite>();
    }

    sealed class DoorSocket
    {
        public string side;
        public Rect pixelRect;
    }

    sealed class BuildContext
    {
        public readonly string resourcePath;
        public readonly string resourceDirectory;
        public readonly int width;
        public readonly int height;
        public readonly int tileWidth;
        public readonly int tileHeight;
        public readonly int wallLayer;
        public readonly List<TilesetData> tilesets = new List<TilesetData>();
        public readonly List<DoorSocket> doorSockets = new List<DoorSocket>();
        public readonly HashSet<Vector2Int> blockedCells = new HashSet<Vector2Int>();
        public readonly Dictionary<string, Tilemap> doorMaps = new Dictionary<string, Tilemap>();
        public readonly Dictionary<string, GameObject> doorObjects = new Dictionary<string, GameObject>();
        public GameObject root;

        public BuildContext(string path, XmlElement map, int walls)
        {
            resourcePath = path;
            int slash = path.LastIndexOf('/');
            resourceDirectory = slash < 0 ? string.Empty : path.Substring(0, slash);
            width = AttrInt(map, "width", 0);
            height = AttrInt(map, "height", 0);
            tileWidth = AttrInt(map, "tilewidth", 16);
            tileHeight = AttrInt(map, "tileheight", 16);
            wallLayer = walls;
        }
    }

    /// Loads a map stored in Resources as OfficeMap1.xml, with its TSX files
    /// stored beside it as Building.xml and OfficeFurniture.xml.
    public static GameObject TryBuild(string resourcePath, out int roomWidth, out int roomHeight)
    {
        roomWidth = roomHeight = 0;
        var mapAsset = Resources.Load<TextAsset>(resourcePath);
        if (!mapAsset)
        {
            Debug.LogWarning("Overrun Tiled room not found at Resources/" + resourcePath + ". Falling back to runtime rooms.");
            return null;
        }

        var document = new XmlDocument();
        try { document.LoadXml(mapAsset.text); }
        catch (Exception ex)
        {
            Debug.LogError("Could not parse Tiled map " + resourcePath + ": " + ex.Message);
            return null;
        }

        var map = document.DocumentElement;
        if (map == null || map.Name != "map")
        {
            Debug.LogError("Tiled asset does not contain a map root: " + resourcePath);
            return null;
        }

        int walls = LayerMask.NameToLayer("Walls");
        if (walls < 0) walls = 0;
        var context = new BuildContext(resourcePath, map, walls);
        roomWidth = context.width;
        roomHeight = context.height;
        if (!LoadTilesets(context, map)) return null;
        ReadDoorSockets(context, map);

        var root = new GameObject("Tiled Room - " + Path.GetFileNameWithoutExtension(resourcePath));
        root.SetActive(false);
        root.AddComponent<Grid>().cellSize = Vector3.one;
        var template = root.AddComponent<RoomTemplate>();
        context.root = root;

        CreateDoorMaps(context);
        int layerSequence = 0;
        foreach (XmlNode child in map.ChildNodes)
            RenderNode(context, child, string.Empty, ref layerSequence);

        template.blockNorth = GetDoorObject(context, "North");
        template.blockEast = GetDoorObject(context, "East");
        template.blockSouth = GetDoorObject(context, "South");
        template.blockWest = GetDoorObject(context, "West");

        AddMapSpawnPoints(context, map);
        if (root.GetComponentsInChildren<SpawnPoint>(true).Length == 0)
            AddFallbackSpawnPoints(context);

        return root;
    }

    static bool LoadTilesets(BuildContext context, XmlElement map)
    {
        foreach (XmlNode reference in map.SelectNodes("./tileset"))
        {
            int firstGid = AttrInt(reference, "firstgid", 1);
            string source = Attr(reference, "source");
            if (string.IsNullOrEmpty(source)) continue;

            string tsxName = Path.GetFileNameWithoutExtension(source);
            var tsxAsset = Resources.Load<TextAsset>(JoinResource(context.resourceDirectory, tsxName + "Data"));
            if (!tsxAsset) tsxAsset = Resources.Load<TextAsset>(JoinResource(context.resourceDirectory, tsxName));
            if (!tsxAsset)
            {
                Debug.LogError("Missing Tiled tileset TextAsset: " + tsxName);
                continue;
            }

            var tsx = new XmlDocument();
            try { tsx.LoadXml(tsxAsset.text); }
            catch (Exception ex) { Debug.LogError("Could not parse tileset " + tsxName + ": " + ex.Message); continue; }

            var tileset = tsx.DocumentElement;
            if (tileset == null) continue;
            var image = tileset.SelectSingleNode("./image") as XmlElement;
            if (image == null) continue;
            string imageName = Path.GetFileNameWithoutExtension(Attr(image, "source"));
            var texture = Resources.Load<Texture2D>(JoinResource(context.resourceDirectory, imageName));
            if (!texture)
            {
                Debug.LogError("Missing Tiled tileset texture: " + imageName);
                continue;
            }

            int sourceWidth = AttrInt(image, "width", 0);
            int sourceHeight = AttrInt(image, "height", 0);
            if (texture.width != sourceWidth || texture.height != sourceHeight)
            {
                Debug.LogError($"Tiled tileset {imageName} was resized to {texture.width}x{texture.height}; " +
                    $"the TSX requires {sourceWidth}x{sourceHeight}. Set Non Power of 2 to None, " +
                    "disable compression and reimport the texture. Refusing to slice incorrect tiles.");
                return false;
            }
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            context.tilesets.Add(new TilesetData
            {
                firstGid = firstGid,
                tileWidth = AttrInt(tileset, "tilewidth", context.tileWidth),
                tileHeight = AttrInt(tileset, "tileheight", context.tileHeight),
                columns = AttrInt(tileset, "columns", 0),
                tileCount = AttrInt(tileset, "tilecount", 0),
                margin = AttrInt(tileset, "margin", 0),
                spacing = AttrInt(tileset, "spacing", 0),
                texture = texture
            });
        }
        context.tilesets.Sort((a, b) => a.firstGid.CompareTo(b.firstGid));
        return context.tilesets.Count > 0;
    }

    static void ReadDoorSockets(BuildContext context, XmlElement map)
    {
        foreach (XmlNode group in map.SelectNodes("./objectgroup"))
        {
            string name = Attr(group, "name");
            string side = DoorSide(name);
            if (string.IsNullOrEmpty(side)) continue;

            foreach (XmlNode objectNode in group.SelectNodes("./object"))
            {
                float x = AttrFloat(objectNode, "x", 0f);
                float y = AttrFloat(objectNode, "y", 0f);
                float w = AttrFloat(objectNode, "width", context.tileWidth);
                float h = AttrFloat(objectNode, "height", context.tileHeight);
                context.doorSockets.Add(new DoorSocket { side = side, pixelRect = new Rect(x, y, Mathf.Max(1f, w), Mathf.Max(1f, h)) });
            }
        }
    }

    static void CreateDoorMaps(BuildContext context)
    {
        foreach (string side in new[] { "North", "East", "South", "West" })
        {
            var go = CreateTilemapObject(context.root.transform, "Door Closure " + side, 80, true, false, context.wallLayer);
            context.doorObjects[side] = go;
            context.doorMaps[side] = go.GetComponent<Tilemap>();
        }
    }

    static GameObject GetDoorObject(BuildContext context, string side)
    {
        return context.doorObjects.TryGetValue(side, out var go) ? go : null;
    }

    static void RenderNode(BuildContext context, XmlNode node, string groupCategory, ref int layerSequence)
    {
        if (node.NodeType != XmlNodeType.Element) return;
        string name = Attr(node, "name");

        if (node.Name == "group")
        {
            string category = name == "ShortObjects" || name == "TallObjects" ? name : groupCategory;
            foreach (XmlNode child in node.ChildNodes) RenderNode(context, child, category, ref layerSequence);
            return;
        }

        if (node.Name != "layer") return;
        int thisLayerOrder = layerSequence++;
        string categoryForLayer = name == "ShortObjects" || name == "TallObjects" ? name : groupCategory;
        string normalized = name.ToLowerInvariant();
        if (normalized == "doorclosures")
        {
            foreach (var doorMap in context.doorMaps.Values)
                doorMap.GetComponent<TilemapRenderer>().sortingOrder = thisLayerOrder;
            RenderDoorClosures(context, node);
            return;
        }

        bool isWall = normalized == "walls";
        bool isShort = categoryForLayer == "ShortObjects";
        bool isTall = categoryForLayer == "TallObjects";
        // Tiled draws later layers over earlier layers. Preserve that order so
        // under-chairs, chairs, items, and shelves do not flatten into one band.
        int sortingOrder = thisLayerOrder;
        bool collidable = isWall || isShort || isTall;
        var mapObject = CreateTilemapObject(context.root.transform, name, sortingOrder, collidable, isShort, context.wallLayer);
        var tilemap = mapObject.GetComponent<Tilemap>();
        RenderTileLayer(context, node, tilemap, collidable);
        tilemap.CompressBounds();
        tilemap.RefreshAllTiles();
    }

    static void RenderTileLayer(BuildContext context, XmlNode layer, Tilemap tilemap, bool collidable)
    {
        var data = layer.SelectSingleNode("./data");
        if (data == null) return;
        int expected = context.width * context.height;
        var values = ParseCsv(data.InnerText, expected);
        for (int i = 0; i < values.Length; i++)
        {
            int rawGid = values[i];
            int gid = rawGid & TiledGidMask;
            if (gid == 0) continue;
            int x = i % context.width;
            int y = i / context.width;
            var cell = new Vector2Int(x, y);
            if (collidable) context.blockedCells.Add(cell);
            var tile = MakeTile(context, gid, collidable);
            if (tile) tilemap.SetTile(ToUnityCell(context, x, y), tile);
        }
    }

    static void RenderDoorClosures(BuildContext context, XmlNode layer)
    {
        var data = layer.SelectSingleNode("./data");
        if (data == null) return;
        var values = ParseCsv(data.InnerText, context.width * context.height);
        for (int i = 0; i < values.Length; i++)
        {
            int gid = values[i] & TiledGidMask;
            if (gid == 0) continue;
            int x = i % context.width;
            int y = i / context.width;
            context.blockedCells.Add(new Vector2Int(x, y));
            string side = FindDoorSide(context, x, y);
            if (string.IsNullOrEmpty(side)) continue;
            var tile = MakeTile(context, gid, true);
            if (tile) context.doorMaps[side].SetTile(ToUnityCell(context, x, y), tile);
        }
    }

    static void AddMapSpawnPoints(BuildContext context, XmlElement map)
    {
        foreach (XmlNode group in map.SelectNodes("./objectgroup"))
        {
            if (!string.Equals(Attr(group, "name"), "SpawnPoints", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (XmlNode objectNode in group.SelectNodes("./object"))
            {
                float x = AttrFloat(objectNode, "x", 0f);
                float y = AttrFloat(objectNode, "y", 0f);
                var go = new GameObject(string.IsNullOrEmpty(Attr(objectNode, "name")) ? "Spawn Point" : Attr(objectNode, "name"));
                go.transform.SetParent(context.root.transform, false);
                go.transform.localPosition = TiledPixelToWorld(context, x, y);
                go.AddComponent<SpawnPoint>();
            }
        }
    }

    static void AddFallbackSpawnPoints(BuildContext context)
    {
        foreach (var socket in context.doorSockets)
        {
            float cx = socket.pixelRect.center.x / context.tileWidth;
            float cy = socket.pixelRect.center.y / context.tileHeight;
            int x = Mathf.Clamp(Mathf.FloorToInt(cx), 0, context.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(cy), 0, context.height - 1);
            for (int inset = 3; inset <= 10; inset++)
            {
                int sx = x;
                int sy = y;
                if (socket.side == "North") sy = inset;
                else if (socket.side == "South") sy = context.height - 1 - inset;
                else if (socket.side == "East") sx = context.width - 1 - inset;
                else if (socket.side == "West") sx = inset;
                var cell = new Vector2Int(sx, sy);
                if (sx < 0 || sy < 0 || sx >= context.width || sy >= context.height || context.blockedCells.Contains(cell)) continue;
                var go = new GameObject("Spawn Point " + socket.side);
                go.transform.SetParent(context.root.transform, false);
                go.transform.localPosition = CellToWorld(context, sx, sy);
                go.AddComponent<SpawnPoint>();
                break;
            }
        }
    }

    static GameObject CreateTilemapObject(Transform parent, string name, int sortingOrder, bool collidable, bool shootThrough, int layer)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.layer = layer;
        var tilemap = go.AddComponent<Tilemap>();
        var renderer = go.AddComponent<TilemapRenderer>();
        renderer.sortingOrder = sortingOrder;
        renderer.mode = TilemapRenderer.Mode.Chunk;

        if (shootThrough) go.AddComponent<OverrunShootThrough>();
        if (collidable)
        {
            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;
            body.simulated = true;
            var tileCollider = go.AddComponent<TilemapCollider2D>();
            var composite = go.AddComponent<CompositeCollider2D>();
            tileCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
            // Solid polygons also block probes inside furniture footprints;
            // outline-only shapes can misidentify desk interiors as safe spawns.
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
        }
        return go;
    }

    static Tile MakeTile(BuildContext context, int gid, bool collidable)
    {
        var sprite = FindSprite(context, gid);
        if (!sprite) return null;
        var tile = ScriptableObject.CreateInstance<Tile>();
        tile.name = "Runtime Tiled Tile " + gid;
        tile.sprite = sprite;
        tile.colliderType = collidable ? Tile.ColliderType.Grid : Tile.ColliderType.None;
        return tile;
    }

    static Sprite FindSprite(BuildContext context, int gid)
    {
        TilesetData chosen = null;
        for (int i = 0; i < context.tilesets.Count; i++)
        {
            if (context.tilesets[i].firstGid <= gid) chosen = context.tilesets[i];
            else break;
        }
        if (chosen == null) return null;
        int localId = gid - chosen.firstGid;
        if (chosen.sprites.TryGetValue(localId, out var cached)) return cached;
        if (chosen.columns <= 0 || localId < 0 || localId >= chosen.tileCount)
        {
            Debug.LogError("Invalid tile ID or missing TSX atlas columns: " + gid);
            return null;
        }
        int x = chosen.margin + (localId % chosen.columns) * (chosen.tileWidth + chosen.spacing);
        int y = chosen.margin + (localId / chosen.columns) * (chosen.tileHeight + chosen.spacing);
        if (x + chosen.tileWidth > chosen.texture.width || y + chosen.tileHeight > chosen.texture.height)
        {
            Debug.LogError("Tiled sprite falls outside its source image: " + gid);
            return null;
        }
        var rect = new Rect(x, chosen.texture.height - y - chosen.tileHeight, chosen.tileWidth, chosen.tileHeight);
        var sprite = Sprite.Create(chosen.texture, rect, new Vector2(.5f, .5f), chosen.tileWidth, 0, SpriteMeshType.FullRect);
        sprite.name = "Tiled Sprite " + gid;
        chosen.sprites[localId] = sprite;
        return sprite;
    }

    static Vector3Int ToUnityCell(BuildContext context, int x, int tiledY) => new Vector3Int(x, context.height - 1 - tiledY, 0);
    static Vector3 CellToWorld(BuildContext context, int x, int tiledY) => new Vector3(x + .5f, context.height - tiledY - .5f, 0f);
    static Vector3 TiledPixelToWorld(BuildContext context, float pixelX, float pixelY) => new Vector3(pixelX / context.tileWidth, context.height - pixelY / context.tileHeight, 0f);

    static string FindDoorSide(BuildContext context, int x, int y)
    {
        Vector2 pixelCenter = new Vector2((x + .5f) * context.tileWidth, (y + .5f) * context.tileHeight);
        foreach (var socket in context.doorSockets)
            if (socket.pixelRect.Contains(pixelCenter)) return socket.side;
        if (y == 0) return "North";
        if (x == context.width - 1) return "East";
        if (y == context.height - 1) return "South";
        if (x == 0) return "West";
        return null;
    }

    static string DoorSide(string name)
    {
        if (name.IndexOf("North", StringComparison.OrdinalIgnoreCase) >= 0) return "North";
        if (name.IndexOf("East", StringComparison.OrdinalIgnoreCase) >= 0) return "East";
        if (name.IndexOf("South", StringComparison.OrdinalIgnoreCase) >= 0) return "South";
        if (name.IndexOf("West", StringComparison.OrdinalIgnoreCase) >= 0) return "West";
        return null;
    }

    static int[] ParseCsv(string csv, int expected)
    {
        var values = new int[expected];
        string[] parts = csv.Split(new[] { ',', '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        int count = Mathf.Min(expected, parts.Length);
        for (int i = 0; i < count; i++) int.TryParse(parts[i], out values[i]);
        return values;
    }

    static string JoinResource(string directory, string name) => string.IsNullOrEmpty(directory) ? name : directory + "/" + name;
    static string Attr(XmlNode node, string name) => node?.Attributes?[name]?.Value ?? string.Empty;
    static int AttrInt(XmlNode node, string name, int fallback) => int.TryParse(Attr(node, name), out var value) ? value : fallback;
    static float AttrFloat(XmlNode node, string name, float fallback) => float.TryParse(Attr(node, name), out var value) ? value : fallback;
}
