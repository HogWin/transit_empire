using Godot;
using System;
using System.Collections.Generic;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Simulation;

namespace TransitEmpire.Client.Scripts;

public class TruckVisualHandle
{
    public Node3D Root { get; init; } = null!;
    public Node3D Headlights { get; init; } = null!;
    public List<Node3D> Wheels { get; } = new();
    public List<Node3D> SteerPivots { get; } = new();
    public List<(Node3D BedRoot, Vector3 BedScale)> CargoBeds { get; } = new();
    public Node3D? RearTrailerPivot { get; set; }
    public GpuParticles3D? MaintenanceSmoke { get; set; }
    public GpuParticles3D? TireDust { get; set; }
    public GpuParticles3D? RainSpray { get; set; }
    public MeshInstance3D? BeaconLens { get; set; }
    public Node3D? EscortLead { get; set; }
    public MeshInstance3D? EscortRedBeacon { get; set; }
    public MeshInstance3D? EscortAmberBeacon { get; set; }
    public ResourceType DisplayedCargoType { get; set; } = ResourceType.None;
    public int DisplayedCargoBucket { get; set; } = -1;
    public string VisualSignature { get; set; } = string.Empty;
    public Vector3 LastForwardDir { get; set; } = Vector3.Forward;
    public float CurrentSteerYaw { get; set; }
    public float CurrentTrailerYaw { get; set; }
}

public class IndustrialConveyorItem
{
    public Node3D CargoNode { get; init; } = null!;
    public Vector3 StartLocal { get; init; }
    public Vector3 EndLocal { get; init; }
    public float Phase { get; set; }
    public float Speed { get; init; } = 0.45f;
}

/// <summary>
/// Fábrica de modelos 3D no estilo Voxel Tycoon:
/// - Terreno em blocos escalonados (stepped voxels) com topo em grade xadrez sutil e encostas de terra;
/// - Florestas MultiMesh com pinheiros e árvores estilizadas;
/// - Estradas com asfalto grafite, calçadas claras e faixas nítidas;
/// - Edifícios coloridos (tijolinhos, casas menta, torres amarelas/azuis, caixas d'água);
/// - Complexos industriais e depósitos brancos com teto verde.
/// </summary>
public static class ProceduralModels3D
{
    public const float TileSize = 2.0f;

    private static Shader? _waterShader;
    private static Shader? _roadShader;
    private static Shader? _foliageShader;
    private static Shader? _terrainShader;

    private static ShaderMaterial? _waterMat;
    private static readonly Dictionary<RoadTier, ShaderMaterial> _roadMats = new();
    private static readonly Dictionary<string, StandardMaterial3D> _materialCache = new();
    private static readonly Dictionary<Vector3, BoxMesh> _boxMeshCache = new();
    private static readonly Dictionary<(int TopMm, int BottomMm, int HeightMm, int Segments), CylinderMesh> _cylinderMeshCache = new();
    private static readonly Dictionary<Mesh, (Vector3[] Verts, Vector3[] Normals, int[]? Indices)> _meshArrayCache = new();
    private static readonly Dictionary<(bool Metallic, bool Glossy), StandardMaterial3D> _vertexColorBucketMats = new();

    private static StandardMaterial3D? _sharedNightWindowMat;
    private static StandardMaterial3D? _sharedStreetLampMat;

    public static void InitializeSharedMaterials()
    {
        _waterShader ??= GD.Load<Shader>("res://Shaders/water_waves.gdshader");
        _roadShader ??= GD.Load<Shader>("res://Shaders/wet_road_pbr.gdshader");
        _foliageShader ??= GD.Load<Shader>("res://Shaders/wind_foliage.gdshader");
        _terrainShader ??= GD.Load<Shader>("res://Shaders/voxel_terrain.gdshader");

        _roadMats.Clear();
        _materialCache.Clear();
        _boxMeshCache.Clear();
        _cylinderMeshCache.Clear();
        _meshArrayCache.Clear();
        _vertexColorBucketMats.Clear();

        _sharedNightWindowMat = new StandardMaterial3D
        {
            AlbedoColor = new Color("253041"),
            Roughness = 0.45f,
            Metallic = 0.25f,
            EmissionEnabled = false,
            Emission = new Color("fde047"),
            EmissionEnergyMultiplier = 0.0f
        };

        _sharedStreetLampMat = new StandardMaterial3D
        {
            AlbedoColor = new Color("fef08a"),
            Roughness = 0.3f,
            EmissionEnabled = false,
            Emission = new Color("fef08a"),
            EmissionEnergyMultiplier = 0.0f
        };

        foreach (RoadTier tier in Enum.GetValues<RoadTier>())
        {
            if (tier == RoadTier.None) continue;

            Color roadColor = tier switch
            {
                RoadTier.Dirt => new Color("8d6039"),         // Terra batida quente
                RoadTier.Gravel => new Color("7c8594"),       // Cascalho cinza claro
                RoadTier.Asphalt => new Color("3c404a"),      // Asfalto grafite Voxel Tycoon
                RoadTier.Highway => new Color("2f333d"),      // Rodovia grafite escuro
                RoadTier.DualCarriage => new Color("252932"), // Pista dupla expressa
                _ => new Color("8d6039")
            };

            float rough = tier <= RoadTier.Gravel ? 0.92f : 0.78f;

            var sm = new ShaderMaterial { Shader = _roadShader };
            sm.SetShaderParameter("base_color", roadColor);
            sm.SetShaderParameter("base_roughness", rough);
            sm.SetShaderParameter("wetness", 0.0f);
            sm.SetShaderParameter("grain_scale", tier <= RoadTier.Gravel ? 38.0f : 22.0f);
            _roadMats[tier] = sm;
        }
    }

    public static void UpdateGlobalRoadWetness(float wetness)
    {
        foreach (var mat in _roadMats.Values)
        {
            mat.SetShaderParameter("wetness", Mathf.Clamp(wetness, 0f, 1f));
        }
    }

    public static void UpdateGlobalRoadSnowCover(float snowCover)
    {
        float clamped = Mathf.Clamp(snowCover, 0f, 1f);
        foreach (var mat in _roadMats.Values)
        {
            mat.SetShaderParameter("snow_cover", clamped);
        }
    }

    public static void UpdateNightBuildingLights(bool nightActive)
    {
        if (_sharedNightWindowMat != null)
        {
            _sharedNightWindowMat.EmissionEnabled = nightActive;
            _sharedNightWindowMat.EmissionEnergyMultiplier = nightActive ? 1.1f : 0.0f;
        }
        if (_sharedStreetLampMat != null)
        {
            _sharedStreetLampMat.EmissionEnabled = nightActive;
            _sharedStreetLampMat.EmissionEnergyMultiplier = nightActive ? 1.8f : 0.0f;
        }
    }

    public static ShaderMaterial GetWaterShaderMaterial()
    {
        if (_waterMat == null)
        {
            _waterShader ??= GD.Load<Shader>("res://Shaders/water_waves.gdshader");
            _waterMat = new ShaderMaterial { Shader = _waterShader };
        }
        return _waterMat;
    }

    private static StandardMaterial3D GetMat(string hexColor, float roughness = 0.85f, float metallic = 0.02f, bool emission = false, float emissionEnergy = 0.8f)
    {
        string key = $"{hexColor}_{roughness:F2}_{metallic:F2}_{emission}_{emissionEnergy:F2}";
        if (_materialCache.TryGetValue(key, out var cached))
            return cached;

        var col = new Color(hexColor);
        var mat = new StandardMaterial3D
        {
            AlbedoColor = col,
            Roughness = roughness,
            Metallic = metallic
        };
        if (emission)
        {
            mat.EmissionEnabled = true;
            mat.Emission = col;
            mat.EmissionEnergyMultiplier = emissionEnergy;
        }
        _materialCache[key] = mat;
        return mat;
    }

    private static BoxMesh GetBoxMesh(Vector3 size)
    {
        if (!_boxMeshCache.TryGetValue(size, out var mesh))
        {
            mesh = new BoxMesh { Size = size };
            _boxMeshCache[size] = mesh;
        }
        return mesh;
    }

    private static MeshInstance3D AddBox(Node3D parent, Vector3 size, Vector3 pos, Material mat, Vector3? rotDeg = null, bool castShadow = true)
    {
        var mi = new MeshInstance3D
        {
            Mesh = GetBoxMesh(size),
            MaterialOverride = mat,
            Position = pos,
            CastShadow = castShadow
                ? GeometryInstance3D.ShadowCastingSetting.On
                : GeometryInstance3D.ShadowCastingSetting.Off
        };
        if (rotDeg.HasValue)
            mi.RotationDegrees = rotDeg.Value;
        parent.AddChild(mi);
        return mi;
    }

    private static CylinderMesh GetCylinderMesh(float topRadius, float bottomRadius, float height, int segments = 12)
    {
        var key = (
            Mathf.RoundToInt(topRadius * 2000f),
            Mathf.RoundToInt(bottomRadius * 2000f),
            Mathf.RoundToInt(height * 2000f),
            segments
        );
        if (!_cylinderMeshCache.TryGetValue(key, out var mesh))
        {
            mesh = new CylinderMesh
            {
                TopRadius = topRadius,
                BottomRadius = bottomRadius,
                Height = height,
                RadialSegments = segments,
                Rings = 1
            };
            _cylinderMeshCache[key] = mesh;
        }
        return mesh;
    }

    private static StandardMaterial3D GetVertexColorBucketMaterial(bool isMetallic, bool isGlossy)
    {
        var key = (isMetallic, isGlossy);
        if (_vertexColorBucketMats.TryGetValue(key, out var cached))
            return cached;

        var mat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Metallic = isMetallic ? 0.80f : 0.12f,
            Roughness = isGlossy ? 0.28f : (isMetallic ? 0.36f : 0.82f)
        };
        _vertexColorBucketMats[key] = mat;
        return mat;
    }

    private static (Vector3[] Verts, Vector3[] Normals, int[]? Indices) GetCachedMeshArrays(Mesh mesh)
    {
        if (_meshArrayCache.TryGetValue(mesh, out var cached))
            return cached;

        var arrays = mesh is PrimitiveMesh pm ? pm.GetMeshArrays() : mesh.SurfaceGetArrays(0);
        var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var norms = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
        var idxVar = arrays[(int)Mesh.ArrayType.Index];
        int[]? indices = idxVar.VariantType == Variant.Type.Nil ? null : idxVar.AsInt32Array();
        var entry = (verts, norms, indices);
        _meshArrayCache[mesh] = entry;
        return entry;
    }

    /// <summary>
    /// Mescla filhos MeshInstance3D estáticos opacos de um nó container em poucos ArrayMeshes
    /// com Vertex Color por balde de acabamento/sombra, reduzindo drasticamente draw calls e nós.
    /// </summary>
    private static void OptimizeStaticChildren(Node3D container, HashSet<Node>? skipNodes = null)
    {
        int childCount = container.GetChildCount();
        if (childCount < 2)
            return;

        Dictionary<(GeometryInstance3D.ShadowCastingSetting Shadow, bool Metallic, bool Glossy), List<(MeshInstance3D Node, StandardMaterial3D Mat)>>? buckets = null;

        for (int i = 0; i < childCount; i++)
        {
            var child = container.GetChild(i);
            if (child is not MeshInstance3D mi)
                continue;
            if (skipNodes != null && skipNodes.Contains(mi))
                continue;
            if (!mi.Visible || mi.Mesh == null)
                continue;

            var mat = (mi.MaterialOverride as StandardMaterial3D)
                ?? (mi.Mesh is PrimitiveMesh pm ? pm.Material as StandardMaterial3D : null);
            if (mat == null)
                continue;

            if (ReferenceEquals(mat, _sharedNightWindowMat) || ReferenceEquals(mat, _sharedStreetLampMat))
                continue;
            if (mat.Transparency != BaseMaterial3D.TransparencyEnum.Disabled
                || mat.EmissionEnabled
                || mat.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded
                || mat.VertexColorUseAsAlbedo)
                continue;

            buckets ??= new();
            var key = (mi.CastShadow, mat.Metallic >= 0.45f, mat.Roughness <= 0.45f);
            if (!buckets.TryGetValue(key, out var list))
            {
                list = new List<(MeshInstance3D, StandardMaterial3D)>();
                buckets[key] = list;
            }
            list.Add((mi, mat));
        }

        if (buckets == null)
            return;

        foreach (var kv in buckets)
        {
            var list = kv.Value;
            if (list.Count < 2)
                continue;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            foreach (var (mi, mat) in list)
            {
                var (verts, norms, indices) = GetCachedMeshArrays(mi.Mesh!);
                if (verts.Length == 0 || norms.Length != verts.Length)
                    continue;

                Color col = mat.AlbedoColor;
                Transform3D xform = mi.Transform;
                Basis rotBasis = xform.Basis;

                if (indices != null && indices.Length > 0)
                {
                    for (int k = 0; k < indices.Length; k++)
                    {
                        int idx = indices[k];
                        st.SetColor(col);
                        st.SetNormal((rotBasis * norms[idx]).Normalized());
                        st.AddVertex(xform * verts[idx]);
                    }
                }
                else
                {
                    for (int k = 0; k < verts.Length; k++)
                    {
                        st.SetColor(col);
                        st.SetNormal((rotBasis * norms[k]).Normalized());
                        st.AddVertex(xform * verts[k]);
                    }
                }
            }

            var mergedMesh = st.Commit();
            var mergedNode = new MeshInstance3D
            {
                Name = "MergedStaticBatch",
                Mesh = mergedMesh,
                MaterialOverride = GetVertexColorBucketMaterial(kv.Key.Metallic, kv.Key.Glossy),
                CastShadow = kv.Key.Shadow
            };
            container.AddChild(mergedNode);

            foreach (var (oldMi, _) in list)
            {
                container.RemoveChild(oldMi);
                oldMi.QueueFree();
            }
        }
    }

    private static MeshInstance3D AddCylinder(Node3D parent, float topRadius, float bottomRadius, float height, Vector3 pos, Material mat, Vector3? rotDeg = null, int segments = 12, bool castShadow = true)
    {
        var mi = new MeshInstance3D
        {
            Mesh = GetCylinderMesh(topRadius, bottomRadius, height, segments),
            MaterialOverride = mat,
            Position = pos,
            CastShadow = castShadow
                ? GeometryInstance3D.ShadowCastingSetting.On
                : GeometryInstance3D.ShadowCastingSetting.Off
        };
        if (rotDeg.HasValue)
            mi.RotationDegrees = rotDeg.Value;
        parent.AddChild(mi);
        return mi;
    }

    /// <summary>
    /// <summary>
    /// Retorna a elevação escalonada em voxel para qualquer célula do grid (compatível entre terreno e vegetação).
    /// </summary>
    public static float GetTerrainTileHeight(GridCell[,] grid, int gx, int gy)
    {
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);
        if (gx < 0 || gx >= width || gy < 0 || gy >= height)
            return -0.55f;

        var cell = grid[gx, gy];
        if (cell.Terrain == TerrainType.Water)
            return -0.50f;

        if (cell.Terrain == TerrainType.Hills && !cell.HasRoad && cell.OccupantId == 0)
        {
            int hash = Math.Abs(gx * 37 + gy * 73) % 10;
            if (cell.Biome == BiomeType.Alpine)
            {
                // Picos nevados atingem platôs ainda mais altos
                if (hash < 3) return 0.55f;
                if (hash < 6) return 1.10f;
                if (hash < 9) return 1.65f;
                return 2.20f;
            }

            if (hash < 4) return 0.55f;
            if (hash < 8) return 1.10f;
            return 1.65f;
        }

        return 0.0f;
    }

    /// <summary>
    /// Constrói o terreno em blocos escalonados (Stepped Voxels) particionado em chunks espaciais
    /// para frustum culling eficiente em mapas grandes (100x100+), preservando 1 draw call por setor visível.
    /// </summary>
    public static Node3D BuildContinuousTerrainMesh(GridCell[,] grid)
    {
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);
        float ts = TileSize;

        Color GetTopColor(GridCell cell, int gx, int gy, float h)
        {
            if (h < -0.1f)
            {
                return cell.Biome switch
                {
                    BiomeType.Desert => new Color("3594a6"),
                    BiomeType.Alpine => new Color("3b6f8c"),
                    _ => new Color("2b7a9b")
                };
            }

            bool checker = (gx + gy) % 2 == 0;
            float n = ((gx * 17 + gy * 31) % 5) * 0.013f;

            if (cell.Biome == BiomeType.Desert)
            {
                if (h > 1.0f)
                    return checker ? new Color(0.80f + n, 0.58f + n, 0.27f) : new Color(0.75f + n, 0.53f + n, 0.24f);
                if (h > 0.2f)
                    return checker ? new Color(0.85f + n, 0.66f + n, 0.30f) : new Color(0.80f + n, 0.61f + n, 0.27f);

                return checker
                    ? new Color(0.89f + n * 0.6f, 0.74f + n * 0.5f, 0.34f)
                    : new Color(0.84f + n * 0.6f, 0.69f + n * 0.5f, 0.30f);
            }

            if (cell.Biome == BiomeType.Alpine)
            {
                if (h >= 1.0f)
                    return checker ? new Color(0.93f, 0.96f, 0.99f) : new Color(0.87f, 0.91f, 0.96f);
                if (h > 0.2f)
                    return checker ? new Color(0.76f + n, 0.82f + n, 0.88f) : new Color(0.70f + n, 0.76f + n, 0.83f);

                return checker
                    ? new Color(0.54f + n, 0.63f + n, 0.58f)
                    : new Color(0.49f + n, 0.58f + n, 0.54f);
            }

            // Planície verde vibrante e fosca (Temperate)
            if (h > 1.2f)
                return checker ? new Color(0.32f + n, 0.56f + n, 0.28f) : new Color(0.29f + n, 0.52f + n, 0.25f);
            if (h > 0.2f)
                return checker ? new Color(0.34f + n, 0.60f + n, 0.29f) : new Color(0.30f + n, 0.55f + n, 0.26f);

            return checker
                ? new Color(0.36f + n, 0.63f + n, 0.30f)
                : new Color(0.33f + n, 0.59f + n, 0.28f);
        }

        Color GetWallColor(BiomeType biome, bool isLightSide, bool touchesWater)
        {
            if (touchesWater)
                return biome == BiomeType.Alpine ? new Color("94a3b8") : new Color("c2a66c");

            return biome switch
            {
                BiomeType.Desert => isLightSide ? new Color("b87b3e") : new Color("9c632d"),
                BiomeType.Alpine => isLightSide ? new Color("64748b") : new Color("475569"),
                _ => isLightSide ? new Color("83562f") : new Color("6e4726")
            };
        }

        _terrainShader ??= GD.Load<Shader>("res://Shaders/voxel_terrain.gdshader");
        Material terrainMat = _terrainShader != null
            ? new ShaderMaterial { Shader = _terrainShader }
            : new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                Roughness = 0.96f,
                Metallic = 0.0f
            };

        var root = new Node3D { Name = "VoxelTerrainMesh" };
        const int ChunkSize = 16;

        var verts = new List<Vector3>(3072);
        var norms = new List<Vector3>(3072);
        var cols = new List<Color>(3072);
        var indices = new List<int>(4608);

        void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color col)
        {
            int baseIdx = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            norms.Add(normal); norms.Add(normal); norms.Add(normal); norms.Add(normal);
            cols.Add(col); cols.Add(col); cols.Add(col); cols.Add(col);

            indices.Add(baseIdx);
            indices.Add(baseIdx + 1);
            indices.Add(baseIdx + 2);
            indices.Add(baseIdx);
            indices.Add(baseIdx + 2);
            indices.Add(baseIdx + 3);
        }

        var nUp = Vector3.Up;
        var nNorth = new Vector3(0, 0, -1);
        var nSouth = new Vector3(0, 0, 1);
        var nWest = new Vector3(-1, 0, 0);
        var nEast = new Vector3(1, 0, 0);

        for (int cx = 0; cx < width; cx += ChunkSize)
        {
            int maxX = Math.Min(width, cx + ChunkSize);
            for (int cy = 0; cy < height; cy += ChunkSize)
            {
                int maxY = Math.Min(height, cy + ChunkSize);
                verts.Clear();
                norms.Clear();
                cols.Clear();
                indices.Clear();

                for (int x = cx; x < maxX; x++)
                {
                    for (int y = cy; y < maxY; y++)
                    {
                        var cell = grid[x, y];
                        float h = GetTerrainTileHeight(grid, x, y);
                        float x0 = (x - 0.5f) * ts;
                        float x1 = (x + 0.5f) * ts;
                        float z0 = (y - 0.5f) * ts;
                        float z1 = (y + 0.5f) * ts;

                        Color topCol = GetTopColor(cell, x, y, h);
                        AddQuad(
                            new Vector3(x0, h, z0),
                            new Vector3(x1, h, z0),
                            new Vector3(x1, h, z1),
                            new Vector3(x0, h, z1),
                            nUp,
                            topCol
                        );

                        float hN = GetTerrainTileHeight(grid, x, y - 1);
                        if (h > hN)
                            AddQuad(new Vector3(x1, h, z0), new Vector3(x0, h, z0), new Vector3(x0, hN, z0), new Vector3(x1, hN, z0), nNorth, GetWallColor(cell.Biome, false, hN < -0.1f));

                        float hS = GetTerrainTileHeight(grid, x, y + 1);
                        if (h > hS)
                            AddQuad(new Vector3(x0, h, z1), new Vector3(x1, h, z1), new Vector3(x1, hS, z1), new Vector3(x0, hS, z1), nSouth, GetWallColor(cell.Biome, true, hS < -0.1f));

                        float hW = GetTerrainTileHeight(grid, x - 1, y);
                        if (h > hW)
                            AddQuad(new Vector3(x0, h, z0), new Vector3(x0, h, z1), new Vector3(x0, hW, z1), new Vector3(x0, hW, z0), nWest, GetWallColor(cell.Biome, false, hW < -0.1f));

                        float hE = GetTerrainTileHeight(grid, x + 1, y);
                        if (h > hE)
                            AddQuad(new Vector3(x1, h, z1), new Vector3(x1, h, z0), new Vector3(x1, hE, z0), new Vector3(x1, hE, z1), nEast, GetWallColor(cell.Biome, true, hE < -0.1f));
                    }
                }

                if (verts.Count == 0)
                    continue;

                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
                arrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
                arrays[(int)Mesh.ArrayType.Color] = cols.ToArray();
                arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

                var mesh = new ArrayMesh();
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

                root.AddChild(new MeshInstance3D
                {
                    Name = $"TerrainChunk_{cx}_{cy}",
                    Mesh = mesh,
                    MaterialOverride = terrainMat,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
                });
            }
        }

        return root;
    }

    /// <summary>
    /// Gera uma única malha contínua para todos os rios, lagos e canais do mapa (1 draw call),
    /// codificando a proximidade da margem/pilares de ponte no canal COLOR.r para espuma costeira no shader.
    /// </summary>
    public static MeshInstance3D BuildContinuousWaterMesh(GridCell[,] grid)
    {
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);
        float half = TileSize * 0.5f;
        const int subdiv = 2;
        float step = TileSize / subdiv;
        const float waterY = -0.14f;

        int waterTileCount = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (grid[x, y].Terrain == TerrainType.Water)
                    waterTileCount++;
            }
        }

        if (waterTileCount == 0)
        {
            return new MeshInstance3D
            {
                Name = "UnifiedWaterMesh",
                Mesh = new ArrayMesh(),
                MaterialOverride = GetWaterShaderMaterial(),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
        }

        bool IsShoreOrStructureAtWorld(float wx, float wz)
        {
            int gx = Mathf.Clamp(Mathf.RoundToInt(wx / TileSize), 0, width - 1);
            int gy = Mathf.Clamp(Mathf.RoundToInt(wz / TileSize), 0, height - 1);
            var c = grid[gx, gy];
            if (c.Terrain != TerrainType.Water || c.HasRoad)
                return true;

            float thresholdSq = (TileSize * 0.92f) * (TileSize * 0.92f);
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = gx + dx;
                if (nx < 0 || nx >= width) continue;
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int ny = gy + dy;
                    if (ny < 0 || ny >= height) continue;
                    if (grid[nx, ny].Terrain != TerrainType.Water)
                    {
                        float ddx = wx - nx * TileSize;
                        float ddz = wz - ny * TileSize;
                        if (ddx * ddx + ddz * ddz <= thresholdSq)
                            return true;
                    }
                }
            }
            return false;
        }

        Color GetWaterVertexMask(float wx, float wz, bool cellHasBridge)
        {
            bool nearShore = IsShoreOrStructureAtWorld(wx, wz);
            float shoreFactor = nearShore ? 0.85f : (cellHasBridge ? 0.65f : 0.08f);
            return new Color(shoreFactor, 0f, 0f, 1f);
        }

        int quads = waterTileCount * subdiv * subdiv;
        int vertCount = quads * 4;
        int idxCount = quads * 6;
        var verts = new Vector3[vertCount];
        var norms = new Vector3[vertCount];
        var cols = new Color[vertCount];
        var indices = new int[idxCount];

        int v = 0;
        int ic = 0;
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var cell = grid[x, y];
                if (cell.Terrain != TerrainType.Water)
                    continue;

                float x0 = x * TileSize - half;
                float z0 = y * TileSize - half;
                bool hasBridge = cell.HasRoad;

                for (int sx = 0; sx < subdiv; sx++)
                {
                    for (int sz = 0; sz < subdiv; sz++)
                    {
                        float vx0 = x0 + sx * step;
                        float vx1 = vx0 + step;
                        float vz0 = z0 + sz * step;
                        float vz1 = vz0 + step;

                        int baseV = v;
                        verts[v] = new Vector3(vx0, waterY, vz0); norms[v] = Vector3.Up; cols[v] = GetWaterVertexMask(vx0, vz0, hasBridge); v++;
                        verts[v] = new Vector3(vx1, waterY, vz0); norms[v] = Vector3.Up; cols[v] = GetWaterVertexMask(vx1, vz0, hasBridge); v++;
                        verts[v] = new Vector3(vx1, waterY, vz1); norms[v] = Vector3.Up; cols[v] = GetWaterVertexMask(vx1, vz1, hasBridge); v++;
                        verts[v] = new Vector3(vx0, waterY, vz1); norms[v] = Vector3.Up; cols[v] = GetWaterVertexMask(vx0, vz1, hasBridge); v++;

                        indices[ic++] = baseV;
                        indices[ic++] = baseV + 1;
                        indices[ic++] = baseV + 2;
                        indices[ic++] = baseV;
                        indices[ic++] = baseV + 2;
                        indices[ic++] = baseV + 3;
                    }
                }
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = norms;
        arrays[(int)Mesh.ArrayType.Color] = cols;
        arrays[(int)Mesh.ArrayType.Index] = indices;

        var waterMesh = new ArrayMesh();
        waterMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return new MeshInstance3D
        {
            Name = "UnifiedWaterMesh",
            Mesh = waterMesh,
            MaterialOverride = GetWaterShaderMaterial(),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
    }

    /// <summary>
    /// Gera toda a vegetação voxel por bioma particionada em setores espaciais (16x16)
    /// para que o frustum culling e o LOD de distância descartem detalhes fora da câmera.
    /// </summary>
    public static Node3D BuildBiomeVegetationGroup(GridCell[,] grid, int seed = 2026, List<MultiMeshInstance3D>? shadowCastersOut = null)
    {
        int width = grid.GetLength(0);
        int height = grid.GetLength(1);
        var rng = new Random(seed);

        const int VegChunkSize = 16;
        int chunksX = Math.Max(1, (width + VegChunkSize - 1) / VegChunkSize);
        int chunksY = Math.Max(1, (height + VegChunkSize - 1) / VegChunkSize);
        int chunkCount = chunksX * chunksY;

        var temperateBuckets = new List<Transform3D>[chunkCount];
        var alpineBuckets = new List<Transform3D>[chunkCount];
        var cactusBuckets = new List<Transform3D>[chunkCount];
        var rockBuckets = new List<Transform3D>[chunkCount];
        var bushBuckets = new List<Transform3D>[chunkCount];
        var hayBaleBuckets = new List<Transform3D>[chunkCount];

        for (int i = 0; i < chunkCount; i++)
        {
            temperateBuckets[i] = new List<Transform3D>();
            alpineBuckets[i] = new List<Transform3D>();
            cactusBuckets[i] = new List<Transform3D>();
            rockBuckets[i] = new List<Transform3D>();
            bushBuckets[i] = new List<Transform3D>();
            hayBaleBuckets[i] = new List<Transform3D>();
        }

        for (int x = 1; x < width - 1; x++)
        {
            int cx = x / VegChunkSize;
            for (int y = 1; y < height - 1; y++)
            {
                var cell = grid[x, y];
                if (cell.HasRoad || cell.OccupantId != 0 || cell.Terrain == TerrainType.Water)
                    continue;

                int cIdx = cx + (y / VegChunkSize) * chunksX;
                float baseY = GetTerrainTileHeight(grid, x, y);
                bool nearWater = grid[x - 1, y].Terrain == TerrainType.Water
                              || grid[x + 1, y].Terrain == TerrainType.Water
                              || grid[x, y - 1].Terrain == TerrainType.Water
                              || grid[x, y + 1].Terrain == TerrainType.Water;

                // Afloramentos rochosos voxel em encostas (Hills), picos alpinos e margens de rios/lagos
                double rockProb = cell.Terrain == TerrainType.Hills
                    ? (cell.Biome == BiomeType.Alpine ? 0.28 : 0.22)
                    : (nearWater ? 0.16 : 0.03);
                if (rng.NextDouble() < rockProb)
                {
                    float rx = ((float)rng.NextDouble() - 0.5f) * 1.30f;
                    float rz = ((float)rng.NextDouble() - 0.5f) * 1.30f;
                    float rScale = 0.65f + (float)rng.NextDouble() * 0.65f;
                    float rRot = (float)rng.NextDouble() * Mathf.Tau;
                    Basis rBasis = Basis.FromEuler(new Vector3(0, rRot, 0)).Scaled(new Vector3(rScale, rScale * 0.85f, rScale));
                    rockBuckets[cIdx].Add(new Transform3D(rBasis, new Vector3(x * TileSize + rx, baseY, y * TileSize + rz)));
                }

                // Arbustos baixos e fardos de feno em planícies temperadas
                if (cell.Biome == BiomeType.Temperate && cell.Terrain == TerrainType.Plains)
                {
                    if (rng.NextDouble() < 0.14)
                    {
                        float bx = ((float)rng.NextDouble() - 0.5f) * 1.32f;
                        float bz = ((float)rng.NextDouble() - 0.5f) * 1.32f;
                        float bScale = 0.72f + (float)rng.NextDouble() * 0.52f;
                        float bRot = (float)rng.NextDouble() * Mathf.Tau;
                        Basis bBasis = Basis.FromEuler(new Vector3(0, bRot, 0)).Scaled(new Vector3(bScale, bScale, bScale));
                        bushBuckets[cIdx].Add(new Transform3D(bBasis, new Vector3(x * TileSize + bx, baseY, y * TileSize + bz)));
                    }
                    else if (!nearWater && rng.NextDouble() < 0.045)
                    {
                        float hx = ((float)rng.NextDouble() - 0.5f) * 1.15f;
                        float hz = ((float)rng.NextDouble() - 0.5f) * 1.15f;
                        float hRot = (float)rng.NextDouble() * Mathf.Tau;
                        Basis hBasis = Basis.FromEuler(new Vector3(0, hRot, 0));
                        hayBaleBuckets[cIdx].Add(new Transform3D(hBasis, new Vector3(x * TileSize + hx, baseY, y * TileSize + hz)));
                    }
                }

                double prob = cell.Biome switch
                {
                    BiomeType.Desert => cell.Terrain == TerrainType.Hills ? 0.14 : 0.09,
                    BiomeType.Alpine => cell.Terrain == TerrainType.Hills ? 0.36 : 0.14,
                    _ => cell.Terrain == TerrainType.Hills ? 0.50 : 0.16
                };

                if (rng.NextDouble() > prob)
                    continue;

                int count = cell.Biome == BiomeType.Desert ? 1 : rng.Next(1, 3);

                for (int t = 0; t < count; t++)
                {
                    float ox = ((float)rng.NextDouble() - 0.5f) * 1.25f;
                    float oz = ((float)rng.NextDouble() - 0.5f) * 1.25f;
                    float scale = cell.Biome == BiomeType.Desert
                        ? 0.72f + (float)rng.NextDouble() * 0.48f
                        : 0.65f + (float)rng.NextDouble() * 0.45f;
                    float rotY = (float)rng.NextDouble() * Mathf.Tau;

                    Basis basis = Basis.FromEuler(new Vector3(0, rotY, 0)).Scaled(new Vector3(scale, scale, scale));
                    Vector3 pos = new Vector3(x * TileSize + ox, baseY, y * TileSize + oz);
                    var xform = new Transform3D(basis, pos);

                    switch (cell.Biome)
                    {
                        case BiomeType.Desert:
                            cactusBuckets[cIdx].Add(xform);
                            break;
                        case BiomeType.Alpine:
                            alpineBuckets[cIdx].Add(xform);
                            break;
                        default:
                            temperateBuckets[cIdx].Add(xform);
                            break;
                    }
                }
            }
        }

        _foliageShader ??= GD.Load<Shader>("res://Shaders/wind_foliage.gdshader");
        var matGreen = new ShaderMaterial { Shader = _foliageShader };
        var matSnow = new ShaderMaterial { Shader = _foliageShader };
        matSnow.SetShaderParameter("foliage_color_top", new Color("edf4fa"));
        matSnow.SetShaderParameter("foliage_color_bottom", new Color("1f4232"));
        matSnow.SetShaderParameter("wind_strength", 0.04f);

        var root = new Node3D { Name = "BiomeVegetationGroup" };

        for (int i = 0; i < chunkCount; i++)
        {
            if (temperateBuckets[i].Count == 0 && alpineBuckets[i].Count == 0 && cactusBuckets[i].Count == 0 &&
                rockBuckets[i].Count == 0 && bushBuckets[i].Count == 0 && hayBaleBuckets[i].Count == 0)
                continue;

            var sector = new Node3D { Name = $"VegSector_{i}" };
            if (temperateBuckets[i].Count > 0)
            {
                var pines = BuildPineMultiMesh("TemperatePines", temperateBuckets[i], matGreen);
                shadowCastersOut?.Add(pines);
                sector.AddChild(pines);
            }
            if (alpineBuckets[i].Count > 0)
            {
                var snowPines = BuildPineMultiMesh("AlpineSnowPines", alpineBuckets[i], matSnow);
                shadowCastersOut?.Add(snowPines);
                sector.AddChild(snowPines);
            }
            if (cactusBuckets[i].Count > 0)
                sector.AddChild(BuildDesertCactusMultiMesh("DesertCacti", cactusBuckets[i]));
            if (rockBuckets[i].Count > 0)
                sector.AddChild(BuildVoxelRockMultiMesh("VoxelRocksMultiMesh", rockBuckets[i]));
            if (bushBuckets[i].Count > 0)
                sector.AddChild(BuildBushMultiMesh("TemperateBushesMultiMesh", bushBuckets[i]));
            if (hayBaleBuckets[i].Count > 0)
                sector.AddChild(BuildHayBaleMultiMesh("FarmHayBalesMultiMesh", hayBaleBuckets[i]));

            root.AddChild(sector);
        }

        return root;
    }

    private static MultiMeshInstance3D BuildPineMultiMesh(string name, List<Transform3D> transforms, Material material)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        void AddConeSection(float rBottom, float rTop, float y0, float y1, int segs)
        {
            for (int i = 0; i < segs; i++)
            {
                float a0 = (i / (float)segs) * Mathf.Tau;
                float a1 = ((i + 1) / (float)segs) * Mathf.Tau;

                Vector3 b0 = new(Mathf.Cos(a0) * rBottom, y0, Mathf.Sin(a0) * rBottom);
                Vector3 b1 = new(Mathf.Cos(a1) * rBottom, y0, Mathf.Sin(a1) * rBottom);
                Vector3 t0 = new(Mathf.Cos(a0) * rTop, y1, Mathf.Sin(a0) * rTop);
                Vector3 t1 = new(Mathf.Cos(a1) * rTop, y1, Mathf.Sin(a1) * rTop);

                st.AddVertex(b0); st.AddVertex(b1); st.AddVertex(t1);
                st.AddVertex(b0); st.AddVertex(t1); st.AddVertex(t0);
            }
        }

        AddConeSection(0.06f, 0.05f, 0.0f, 0.28f, 5);
        AddConeSection(0.32f, 0.08f, 0.28f, 0.78f, 5);
        AddConeSection(0.24f, 0.01f, 0.58f, 1.12f, 5);

        st.GenerateNormals();
        var treeMesh = st.Commit();
        treeMesh.SurfaceSetMaterial(0, material);

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = treeMesh,
            InstanceCount = transforms.Count
        };
        for (int i = 0; i < transforms.Count; i++)
            multiMesh.SetInstanceTransform(i, transforms[i]);

        return new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeEnd = 135f,
            VisibilityRangeEndMargin = 12f
        };
    }

    private static MultiMeshInstance3D BuildDesertCactusMultiMesh(string name, List<Transform3D> transforms)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        void AddBoxPart(Vector3 center, Vector3 size, Color col)
        {
            Vector3 h = size * 0.5f;
            Vector3 p000 = center + new Vector3(-h.X, -h.Y, -h.Z);
            Vector3 p100 = center + new Vector3(h.X, -h.Y, -h.Z);
            Vector3 p110 = center + new Vector3(h.X, h.Y, -h.Z);
            Vector3 p010 = center + new Vector3(-h.X, h.Y, -h.Z);
            Vector3 p001 = center + new Vector3(-h.X, -h.Y, h.Z);
            Vector3 p101 = center + new Vector3(h.X, -h.Y, h.Z);
            Vector3 p111 = center + new Vector3(h.X, h.Y, h.Z);
            Vector3 p011 = center + new Vector3(-h.X, h.Y, h.Z);

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color shade)
            {
                st.SetColor(shade); st.AddVertex(a); st.SetColor(shade); st.AddVertex(b); st.SetColor(shade); st.AddVertex(c);
                st.SetColor(shade); st.AddVertex(a); st.SetColor(shade); st.AddVertex(c); st.SetColor(shade); st.AddVertex(d);
            }

            Color light = col.Lightened(0.08f);
            Color dark = col.Darkened(0.14f);

            Quad(p011, p111, p110, p010, light); // Top
            Quad(p001, p101, p111, p011, col);   // Front
            Quad(p100, p000, p010, p110, dark);  // Back
            Quad(p000, p001, p011, p010, dark);  // Left
            Quad(p101, p100, p110, p111, col);   // Right
        }

        Color saguaro = new Color("43963c");
        Color saguaroArm = new Color("4da845");

        // Haste principal do cacto voxel
        AddBoxPart(new Vector3(0f, 0.36f, 0f), new Vector3(0.15f, 0.72f, 0.15f), saguaro);
        // Braço esquerdo
        AddBoxPart(new Vector3(-0.14f, 0.34f, 0f), new Vector3(0.14f, 0.09f, 0.09f), saguaroArm);
        AddBoxPart(new Vector3(-0.18f, 0.45f, 0f), new Vector3(0.09f, 0.24f, 0.09f), saguaroArm);
        // Braço direito
        AddBoxPart(new Vector3(0.14f, 0.40f, 0f), new Vector3(0.14f, 0.09f, 0.09f), saguaroArm);
        AddBoxPart(new Vector3(0.18f, 0.50f, 0f), new Vector3(0.09f, 0.22f, 0.09f), saguaroArm);

        st.GenerateNormals();
        var cactusMesh = st.Commit();
        cactusMesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.88f,
            Metallic = 0.0f
        });

        var multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = cactusMesh,
            InstanceCount = transforms.Count
        };
        for (int i = 0; i < transforms.Count; i++)
            multiMesh.SetInstanceTransform(i, transforms[i]);

        return new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeEnd = 135f,
            VisibilityRangeEndMargin = 12f
        };
    }

    private static MultiMeshInstance3D BuildVoxelRockMultiMesh(string name, List<Transform3D> transforms)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        void AddRockBlock(Vector3 center, Vector3 size, Color col)
        {
            Vector3 h = size * 0.5f;
            Vector3 p000 = center + new Vector3(-h.X, -h.Y, -h.Z);
            Vector3 p100 = center + new Vector3(h.X, -h.Y, -h.Z);
            Vector3 p110 = center + new Vector3(h.X, h.Y, -h.Z);
            Vector3 p010 = center + new Vector3(-h.X, h.Y, -h.Z);
            Vector3 p001 = center + new Vector3(-h.X, -h.Y, h.Z);
            Vector3 p101 = center + new Vector3(h.X, -h.Y, h.Z);
            Vector3 p111 = center + new Vector3(h.X, h.Y, h.Z);
            Vector3 p011 = center + new Vector3(-h.X, h.Y, h.Z);

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color shade)
            {
                st.SetColor(shade); st.AddVertex(a); st.SetColor(shade); st.AddVertex(b); st.SetColor(shade); st.AddVertex(c);
                st.SetColor(shade); st.AddVertex(a); st.SetColor(shade); st.AddVertex(c); st.SetColor(shade); st.AddVertex(d);
            }

            Quad(p011, p111, p110, p010, col.Lightened(0.10f));
            Quad(p001, p101, p111, p011, col);
            Quad(p100, p000, p010, p110, col.Darkened(0.15f));
            Quad(p000, p001, p011, p010, col.Darkened(0.12f));
            Quad(p101, p100, p110, p111, col.Lightened(0.04f));
        }

        Color slate = new Color("64748b");
        Color darkSlate = new Color("475569");
        Color mossTop = new Color("78866b");

        AddRockBlock(new Vector3(0f, 0.11f, 0f), new Vector3(0.34f, 0.22f, 0.28f), slate);
        AddRockBlock(new Vector3(-0.10f, 0.21f, 0.04f), new Vector3(0.20f, 0.18f, 0.18f), mossTop);
        AddRockBlock(new Vector3(0.14f, 0.08f, -0.08f), new Vector3(0.18f, 0.16f, 0.20f), darkSlate);

        st.GenerateNormals();
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.92f,
            Metallic = 0.02f
        });

        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = transforms.Count
        };
        for (int i = 0; i < transforms.Count; i++)
            mm.SetInstanceTransform(i, transforms[i]);

        return new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = mm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeEnd = 90f,
            VisibilityRangeEndMargin = 10f
        };
    }

    private static MultiMeshInstance3D BuildBushMultiMesh(string name, List<Transform3D> transforms)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        void AddBushCube(Vector3 center, Vector3 size, Color col)
        {
            Vector3 h = size * 0.5f;
            Vector3 p000 = center + new Vector3(-h.X, -h.Y, -h.Z);
            Vector3 p100 = center + new Vector3(h.X, -h.Y, -h.Z);
            Vector3 p110 = center + new Vector3(h.X, h.Y, -h.Z);
            Vector3 p010 = center + new Vector3(-h.X, h.Y, -h.Z);
            Vector3 p001 = center + new Vector3(-h.X, -h.Y, h.Z);
            Vector3 p101 = center + new Vector3(h.X, -h.Y, h.Z);
            Vector3 p111 = center + new Vector3(h.X, h.Y, h.Z);
            Vector3 p011 = center + new Vector3(-h.X, h.Y, h.Z);

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color shade)
            {
                st.SetColor(shade); st.AddVertex(a); st.SetColor(shade); st.AddVertex(b); st.SetColor(shade); st.AddVertex(c);
                st.SetColor(shade); st.AddVertex(a); st.SetColor(shade); st.AddVertex(c); st.SetColor(shade); st.AddVertex(d);
            }

            Quad(p011, p111, p110, p010, col.Lightened(0.10f));
            Quad(p001, p101, p111, p011, col);
            Quad(p100, p000, p010, p110, col.Darkened(0.14f));
            Quad(p000, p001, p011, p010, col.Darkened(0.10f));
            Quad(p101, p100, p110, p111, col);
        }

        AddBushCube(new Vector3(0f, 0.11f, 0f), new Vector3(0.24f, 0.22f, 0.24f), new Color("3f8f3d"));
        AddBushCube(new Vector3(0.10f, 0.08f, 0.08f), new Vector3(0.16f, 0.16f, 0.16f), new Color("4da349"));
        AddBushCube(new Vector3(-0.09f, 0.09f, -0.06f), new Vector3(0.15f, 0.17f, 0.15f), new Color("347832"));

        st.GenerateNormals();
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.88f,
            Metallic = 0.0f
        });

        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = transforms.Count
        };
        for (int i = 0; i < transforms.Count; i++)
            mm.SetInstanceTransform(i, transforms[i]);

        return new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = mm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeEnd = 80f,
            VisibilityRangeEndMargin = 10f
        };
    }

    private static MultiMeshInstance3D BuildHayBaleMultiMesh(string name, List<Transform3D> transforms)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        void AddHayPart(Vector3 center, Vector3 size, Color col)
        {
            Vector3 h = size * 0.5f;
            Vector3 p000 = center + new Vector3(-h.X, -h.Y, -h.Z);
            Vector3 p100 = center + new Vector3(h.X, -h.Y, -h.Z);
            Vector3 p110 = center + new Vector3(h.X, h.Y, -h.Z);
            Vector3 p010 = center + new Vector3(-h.X, h.Y, -h.Z);
            Vector3 p001 = center + new Vector3(-h.X, -h.Y, h.Z);
            Vector3 p101 = center + new Vector3(h.X, -h.Y, h.Z);
            Vector3 p111 = center + new Vector3(h.X, h.Y, h.Z);
            Vector3 p011 = center + new Vector3(-h.X, h.Y, h.Z);

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color shade)
            {
                st.SetColor(shade); st.AddVertex(a); st.SetColor(shade); st.AddVertex(b); st.SetColor(shade); st.AddVertex(c);
                st.SetColor(shade); st.AddVertex(a); st.SetColor(shade); st.AddVertex(c); st.SetColor(shade); st.AddVertex(d);
            }

            Quad(p011, p111, p110, p010, col.Lightened(0.08f));
            Quad(p001, p101, p111, p011, col);
            Quad(p100, p000, p010, p110, col.Darkened(0.14f));
            Quad(p000, p001, p011, p010, col.Darkened(0.10f));
            Quad(p101, p100, p110, p111, col);
        }

        Color goldenHay = new Color("eab308");
        Color twine = new Color("92400e");
        AddHayPart(new Vector3(0f, 0.11f, 0f), new Vector3(0.28f, 0.22f, 0.22f), goldenHay);
        AddHayPart(new Vector3(-0.07f, 0.11f, 0f), new Vector3(0.03f, 0.23f, 0.23f), twine);
        AddHayPart(new Vector3(0.07f, 0.11f, 0f), new Vector3(0.03f, 0.23f, 0.23f), twine);

        st.GenerateNormals();
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.90f,
            Metallic = 0.0f
        });

        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = transforms.Count
        };
        for (int i = 0; i < transforms.Count; i++)
            mm.SetInstanceTransform(i, transforms[i]);

        return new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = mm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityRangeEnd = 85f,
            VisibilityRangeEndMargin = 10f
        };
    }

    public readonly record struct RoadTileRenderInput(
        int GridX,
        int GridY,
        Vector3 WorldPosition,
        RoadTier Tier,
        bool ConnectN,
        bool ConnectS,
        bool ConnectE,
        bool ConnectW,
        RoadStructureType Structure,
        BiomeType Biome,
        float PavementCondition,
        TerrainType Terrain,
        bool IsBlockedByLandslide);

    private sealed class RoadChunkSurfaceCollector
    {
        public Dictionary<RoadTier, SurfaceTool> RoadbedSurfaces { get; } = new();
        public SurfaceTool? FlatDetailsSurface { get; private set; }
        public SurfaceTool? ShadowStructuresSurface { get; private set; }
        public SurfaceTool? TunnelLampsSurface { get; private set; }

        public void AddRoadbedBox(RoadTier tier, Vector3 size, Vector3 center, float rotYDeg = 0f)
        {
            if (!RoadbedSurfaces.TryGetValue(tier, out var st))
            {
                st = new SurfaceTool();
                st.Begin(Mesh.PrimitiveType.Triangles);
                RoadbedSurfaces[tier] = st;
            }
            EmitBox(st, size, center, null, rotYDeg);
        }

        public void AddFlatColorBox(Vector3 size, Vector3 center, Color color, float rotYDeg = 0f)
        {
            if (FlatDetailsSurface == null)
            {
                FlatDetailsSurface = new SurfaceTool();
                FlatDetailsSurface.Begin(Mesh.PrimitiveType.Triangles);
            }
            EmitBox(FlatDetailsSurface, size, center, color, rotYDeg);
        }

        public void AddShadowColorBox(Vector3 size, Vector3 center, Color color, float rotYDeg = 0f)
        {
            if (ShadowStructuresSurface == null)
            {
                ShadowStructuresSurface = new SurfaceTool();
                ShadowStructuresSurface.Begin(Mesh.PrimitiveType.Triangles);
            }
            EmitBox(ShadowStructuresSurface, size, center, color, rotYDeg);
        }

        public void AddLampBox(Vector3 size, Vector3 center)
        {
            if (TunnelLampsSurface == null)
            {
                TunnelLampsSurface = new SurfaceTool();
                TunnelLampsSurface.Begin(Mesh.PrimitiveType.Triangles);
            }
            EmitBox(TunnelLampsSurface, size, center, null, 0f);
        }

        private static void EmitBox(SurfaceTool st, Vector3 size, Vector3 center, Color? vertexColor, float rotYDeg)
        {
            Vector3 h = size * 0.5f;
            float cos = 1f;
            float sin = 0f;
            bool rotated = MathF.Abs(rotYDeg) > 0.001f;
            if (rotated)
            {
                float rad = Mathf.DegToRad(rotYDeg);
                cos = MathF.Cos(rad);
                sin = MathF.Sin(rad);
            }

            Vector3 TransformPoint(float lx, float ly, float lz)
            {
                if (!rotated)
                    return center + new Vector3(lx, ly, lz);
                float rx = lx * cos + lz * sin;
                float rz = -lx * sin + lz * cos;
                return center + new Vector3(rx, ly, rz);
            }

            Vector3 TransformNormal(float nx, float ny, float nz)
            {
                if (!rotated)
                    return new Vector3(nx, ny, nz);
                return new Vector3(nx * cos + nz * sin, ny, -nx * sin + nz * cos);
            }

            Vector3 p000 = TransformPoint(-h.X, -h.Y, -h.Z);
            Vector3 p100 = TransformPoint(h.X, -h.Y, -h.Z);
            Vector3 p110 = TransformPoint(h.X, h.Y, -h.Z);
            Vector3 p010 = TransformPoint(-h.X, h.Y, -h.Z);
            Vector3 p001 = TransformPoint(-h.X, -h.Y, h.Z);
            Vector3 p101 = TransformPoint(h.X, -h.Y, h.Z);
            Vector3 p111 = TransformPoint(h.X, h.Y, h.Z);
            Vector3 p011 = TransformPoint(-h.X, h.Y, h.Z);

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                st.SetNormal(normal);
                if (vertexColor.HasValue) st.SetColor(vertexColor.Value);
                st.AddVertex(a);
                st.SetNormal(normal);
                if (vertexColor.HasValue) st.SetColor(vertexColor.Value);
                st.AddVertex(b);
                st.SetNormal(normal);
                if (vertexColor.HasValue) st.SetColor(vertexColor.Value);
                st.AddVertex(c);

                st.SetNormal(normal);
                if (vertexColor.HasValue) st.SetColor(vertexColor.Value);
                st.AddVertex(a);
                st.SetNormal(normal);
                if (vertexColor.HasValue) st.SetColor(vertexColor.Value);
                st.AddVertex(c);
                st.SetNormal(normal);
                if (vertexColor.HasValue) st.SetColor(vertexColor.Value);
                st.AddVertex(d);
            }

            // Winding horário compatível com a malha de terreno do projeto
            Quad(p010, p110, p111, p011, TransformNormal(0f, 1f, 0f));   // Topo (+Y)
            Quad(p001, p101, p100, p000, TransformNormal(0f, -1f, 0f));  // Base (-Y)
            Quad(p011, p111, p101, p001, TransformNormal(0f, 0f, 1f));   // Sul (+Z)
            Quad(p110, p010, p000, p100, TransformNormal(0f, 0f, -1f));  // Norte (-Z)
            Quad(p010, p011, p001, p000, TransformNormal(-1f, 0f, 0f));  // Oeste (-X)
            Quad(p111, p110, p100, p101, TransformNormal(1f, 0f, 0f));   // Leste (+X)
        }
    }

    private static StandardMaterial3D? _roadFlatVertexMat;
    private static StandardMaterial3D? _roadShadowVertexMat;

    /// <summary>
    /// Constrói um setor viário em lote (ex: chunk 10x10 tiles) unificando centenas de caixas em poucas superfícies de ArrayMesh.
    /// </summary>
    public static Node3D CreateRoadChunkNode(IEnumerable<RoadTileRenderInput> tiles)
    {
        var root = new Node3D();
        var collector = new RoadChunkSurfaceCollector();

        foreach (var tile in tiles)
        {
            if (tile.Tier == RoadTier.None)
                continue;
            EmitRoadTileBoxes(in tile, collector);
        }

        var flatMesh = new ArrayMesh();
        int flatSurfIdx = 0;

        foreach (var (tier, st) in collector.RoadbedSurfaces)
        {
            var subMesh = st.Commit();
            if (subMesh == null || subMesh.GetSurfaceCount() == 0) continue;
            var arrays = subMesh.SurfaceGetArrays(0);
            flatMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            Material roadMat = _roadMats.TryGetValue(tier, out var sm)
                ? sm
                : GetMat("3c404a", 0.8f);
            flatMesh.SurfaceSetMaterial(flatSurfIdx++, roadMat);
        }

        if (collector.FlatDetailsSurface != null)
        {
            var subMesh = collector.FlatDetailsSurface.Commit();
            if (subMesh != null && subMesh.GetSurfaceCount() > 0)
            {
                _roadFlatVertexMat ??= new StandardMaterial3D
                {
                    VertexColorUseAsAlbedo = true,
                    Roughness = 0.85f,
                    Metallic = 0.08f
                };
                flatMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, subMesh.SurfaceGetArrays(0));
                flatMesh.SurfaceSetMaterial(flatSurfIdx++, _roadFlatVertexMat);
            }
        }

        if (collector.TunnelLampsSurface != null)
        {
            var subMesh = collector.TunnelLampsSurface.Commit();
            if (subMesh != null && subMesh.GetSurfaceCount() > 0)
            {
                flatMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, subMesh.SurfaceGetArrays(0));
                var lampMat = _sharedStreetLampMat ?? GetMat("fbbf24", 0.30f, 0.1f, emission: true, emissionEnergy: 2.2f);
                flatMesh.SurfaceSetMaterial(flatSurfIdx++, lampMat);
            }
        }

        if (flatSurfIdx > 0)
        {
            root.AddChild(new MeshInstance3D
            {
                Name = "ChunkFlatRoads",
                Mesh = flatMesh,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
        }

        if (collector.ShadowStructuresSurface != null)
        {
            var shadowMesh = collector.ShadowStructuresSurface.Commit();
            if (shadowMesh != null && shadowMesh.GetSurfaceCount() > 0)
            {
                _roadShadowVertexMat ??= new StandardMaterial3D
                {
                    VertexColorUseAsAlbedo = true,
                    Roughness = 0.88f,
                    Metallic = 0.05f
                };
                shadowMesh.SurfaceSetMaterial(0, _roadShadowVertexMat);
                root.AddChild(new MeshInstance3D
                {
                    Name = "ChunkRoadStructures",
                    Mesh = shadowMesh,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.On
                });
            }
        }

        return root;
    }

    public static Node3D CreateRoadTileNode(
        RoadTier tier,
        bool connectN,
        bool connectS,
        bool connectE,
        bool connectW,
        int gridX = 0,
        int gridY = 0,
        RoadStructureType structure = RoadStructureType.Ground,
        BiomeType biome = BiomeType.Temperate,
        float pavementCondition = 100f,
        TerrainType terrain = TerrainType.Plains,
        bool isBlockedByLandslide = false)
    {
        var single = new RoadTileRenderInput(
            gridX,
            gridY,
            Vector3.Zero,
            tier,
            connectN,
            connectS,
            connectE,
            connectW,
            structure,
            biome,
            pavementCondition,
            terrain,
            isBlockedByLandslide);
        return CreateRoadChunkNode(new[] { single });
    }

    private static void EmitRoadTileBoxes(in RoadTileRenderInput tile, RoadChunkSurfaceCollector collector)
    {
        RoadTier tier = tile.Tier;
        bool connectN = tile.ConnectN;
        bool connectS = tile.ConnectS;
        bool connectE = tile.ConnectE;
        bool connectW = tile.ConnectW;
        int gridX = tile.GridX;
        int gridY = tile.GridY;
        RoadStructureType structure = tile.Structure;
        BiomeType biome = tile.Biome;
        float pavementCondition = tile.PavementCondition;
        TerrainType terrain = tile.Terrain;
        bool isBlockedByLandslide = tile.IsBlockedByLandslide;
        Vector3 o = tile.WorldPosition;

        float width = tier switch
        {
            RoadTier.Dirt => 0.88f,
            RoadTier.Gravel => 1.06f,
            RoadTier.Asphalt => 1.24f,
            RoadTier.Highway => 1.44f,
            RoadTier.DualCarriage => 1.68f,
            _ => 0.88f
        };

        var sidewalkCol = new Color("d1d5db");
        var lineYellowCol = new Color("facc15");
        var lineWhiteCol = new Color("f8fafc");
        var medianGrassCol = new Color("4d9b4d");
        int connCount = (connectN ? 1 : 0) + (connectS ? 1 : 0) + (connectE ? 1 : 0) + (connectW ? 1 : 0);

        // Calçada base em vias asfaltadas
        if (tier >= RoadTier.Asphalt)
        {
            float swW = width + 0.24f;
            float halfArmSw = (TileSize - swW) * 0.5f + 0.04f;
            float offsetSw = (swW + halfArmSw) * 0.5f - 0.02f;

            collector.AddFlatColorBox(new Vector3(swW, 0.04f, swW), o + new Vector3(0, 0.02f, 0), sidewalkCol);
            if (connectN) collector.AddFlatColorBox(new Vector3(swW, 0.04f, halfArmSw), o + new Vector3(0, 0.02f, -offsetSw), sidewalkCol);
            if (connectS) collector.AddFlatColorBox(new Vector3(swW, 0.04f, halfArmSw), o + new Vector3(0, 0.02f, offsetSw), sidewalkCol);
            if (connectE) collector.AddFlatColorBox(new Vector3(halfArmSw, 0.04f, swW), o + new Vector3(offsetSw, 0.02f, 0), sidewalkCol);
            if (connectW) collector.AddFlatColorBox(new Vector3(halfArmSw, 0.04f, swW), o + new Vector3(-offsetSw, 0.02f, 0), sidewalkCol);
        }

        // Leito central da via
        collector.AddRoadbedBox(tier, new Vector3(width, 0.06f, width), o + new Vector3(0, 0.03f, 0));

        float halfArm = (TileSize - width) * 0.5f + 0.04f;
        float offset = (width + halfArm) * 0.5f - 0.02f;

        if (connectN) collector.AddRoadbedBox(tier, new Vector3(width, 0.06f, halfArm), o + new Vector3(0, 0.03f, -offset));
        if (connectS) collector.AddRoadbedBox(tier, new Vector3(width, 0.06f, halfArm), o + new Vector3(0, 0.03f, offset));
        if (connectE) collector.AddRoadbedBox(tier, new Vector3(halfArm, 0.06f, width), o + new Vector3(offset, 0.03f, 0));
        if (connectW) collector.AddRoadbedBox(tier, new Vector3(halfArm, 0.06f, width), o + new Vector3(-offset, 0.03f, 0));

        // Chanfros suaves nos cantos internos de curvas de 90° (L-curves) e cruzamentos
        float chamferPos = width * 0.44f;
        Vector3 chamferSize = new(0.26f, 0.058f, 0.26f);
        if (connectN && connectE)
            collector.AddRoadbedBox(tier, chamferSize, o + new Vector3(chamferPos, 0.029f, -chamferPos), 45f);
        if (connectN && connectW)
            collector.AddRoadbedBox(tier, chamferSize, o + new Vector3(-chamferPos, 0.029f, -chamferPos), 45f);
        if (connectS && connectE)
            collector.AddRoadbedBox(tier, chamferSize, o + new Vector3(chamferPos, 0.029f, chamferPos), 45f);
        if (connectS && connectW)
            collector.AddRoadbedBox(tier, chamferSize, o + new Vector3(-chamferPos, 0.029f, chamferPos), 45f);

        // Faixas brancas tracejadas nítidas e faixas de pedestres (zebra) em cruzamentos 3+ vias
        if (tier >= RoadTier.Asphalt)
        {
            var stripeCol = tier == RoadTier.Highway ? lineYellowCol : lineWhiteCol;
            if (connCount < 3)
            {
                if (connectN || connectS)
                    collector.AddFlatColorBox(new Vector3(0.06f, 0.065f, 0.34f), o + new Vector3(0, 0.035f, 0), stripeCol);
                else if (connectE || connectW)
                    collector.AddFlatColorBox(new Vector3(0.34f, 0.065f, 0.06f), o + new Vector3(0, 0.035f, 0), stripeCol);
            }

            if (connectN) collector.AddFlatColorBox(new Vector3(0.06f, 0.065f, 0.28f), o + new Vector3(0, 0.035f, -0.62f), stripeCol);
            if (connectS) collector.AddFlatColorBox(new Vector3(0.06f, 0.065f, 0.28f), o + new Vector3(0, 0.035f, 0.62f), stripeCol);
            if (connectE) collector.AddFlatColorBox(new Vector3(0.28f, 0.065f, 0.06f), o + new Vector3(0.62f, 0.035f, 0), stripeCol);
            if (connectW) collector.AddFlatColorBox(new Vector3(0.28f, 0.065f, 0.06f), o + new Vector3(-0.62f, 0.035f, 0), stripeCol);

            // Faixas de pedestres (zebra) nos braços de entroncamentos urbanos/rodoviários (3+ conexões)
            if (connCount >= 3)
            {
                float zebraDist = width * 0.46f + 0.10f;
                for (int s = -2; s <= 2; s++)
                {
                    float lateral = s * 0.20f;
                    if (connectN)
                        collector.AddFlatColorBox(new Vector3(0.09f, 0.066f, 0.20f), o + new Vector3(lateral, 0.036f, -zebraDist), lineWhiteCol);
                    if (connectS)
                        collector.AddFlatColorBox(new Vector3(0.09f, 0.066f, 0.20f), o + new Vector3(lateral, 0.036f, zebraDist), lineWhiteCol);
                    if (connectE)
                        collector.AddFlatColorBox(new Vector3(0.20f, 0.066f, 0.09f), o + new Vector3(zebraDist, 0.036f, lateral), lineWhiteCol);
                    if (connectW)
                        collector.AddFlatColorBox(new Vector3(0.20f, 0.066f, 0.09f), o + new Vector3(-zebraDist, 0.036f, lateral), lineWhiteCol);
                }
            }
        }

        if (tier == RoadTier.DualCarriage)
        {
            collector.AddFlatColorBox(new Vector3(0.16f, 0.09f, 0.72f), o + new Vector3(0, 0.045f, 0), sidewalkCol);
            collector.AddFlatColorBox(new Vector3(0.10f, 0.11f, 0.58f), o + new Vector3(0, 0.055f, 0), medianGrassCol);
        }

        // Guard-rails metálicos em trechos elevados de serra (Hills) sem túnel
        if (terrain == TerrainType.Hills && structure == RoadStructureType.Ground)
        {
            var guardRailCol = new Color("94a3b8");
            float grOff = width * 0.5f + 0.08f;
            if (!connectN && (connectE || connectW))
                collector.AddFlatColorBox(new Vector3(TileSize * 0.88f, 0.12f, 0.05f), o + new Vector3(0, 0.09f, -grOff), guardRailCol);
            if (!connectS && (connectE || connectW))
                collector.AddFlatColorBox(new Vector3(TileSize * 0.88f, 0.12f, 0.05f), o + new Vector3(0, 0.09f, grOff), guardRailCol);
            if (!connectW && (connectN || connectS))
                collector.AddFlatColorBox(new Vector3(0.05f, 0.12f, TileSize * 0.88f), o + new Vector3(-grOff, 0.09f, 0), guardRailCol);
            if (!connectE && (connectN || connectS))
                collector.AddFlatColorBox(new Vector3(0.05f, 0.12f, TileSize * 0.88f), o + new Vector3(grOff, 0.09f, 0), guardRailCol);
        }

        // Degradação Visual de Pavimento: remendos de asfalto, trincas/panelas e cones de sinalização
        if (pavementCondition < 75f || isBlockedByLandslide)
        {
            var patchCol = new Color("1e2229");
            var potholeCol = new Color("111318");
            float seedOff = ((gridX * 31 + gridY * 17) & 7) * 0.03f - 0.10f;

            // Remendos escuros de desgaste moderado (< 75%)
            collector.AddFlatColorBox(new Vector3(0.34f, 0.064f, 0.26f), o + new Vector3(-0.18f + seedOff, 0.034f, 0.14f), patchCol);
            collector.AddFlatColorBox(new Vector3(0.26f, 0.064f, 0.32f), o + new Vector3(0.22f, 0.034f, -0.18f - seedOff), patchCol);

            // Desgaste severo (< 50%): buracos profundos + cones laranjas de alerta na borda
            if (pavementCondition < 50f || isBlockedByLandslide)
            {
                collector.AddFlatColorBox(new Vector3(0.22f, 0.067f, 0.18f), o + new Vector3(0.10f, 0.035f, 0.08f), potholeCol);
                collector.AddFlatColorBox(new Vector3(0.16f, 0.067f, 0.22f), o + new Vector3(-0.20f, 0.035f, -0.12f), potholeCol);

                var coneOrange = new Color("f97316");
                var coneWhite = new Color("f8fafc");
                float edgeX = width * 0.38f;
                collector.AddFlatColorBox(new Vector3(0.09f, 0.14f, 0.09f), o + new Vector3(edgeX, 0.10f, -0.26f), coneOrange);
                collector.AddFlatColorBox(new Vector3(0.095f, 0.04f, 0.095f), o + new Vector3(edgeX, 0.11f, -0.26f), coneWhite);
                collector.AddFlatColorBox(new Vector3(0.09f, 0.14f, 0.09f), o + new Vector3(-edgeX, 0.10f, 0.26f), coneOrange);
                collector.AddFlatColorBox(new Vector3(0.095f, 0.04f, 0.095f), o + new Vector3(-edgeX, 0.11f, 0.26f), coneWhite);
            }

            // Bloqueio por deslizamento de encosta: rochas e barreira zebrada sobre a pista
            if (isBlockedByLandslide)
            {
                var boulderCol = new Color("57534e");
                var hazardCol = new Color("dc2626");
                collector.AddShadowColorBox(new Vector3(0.46f, 0.34f, 0.42f), o + new Vector3(-0.08f, 0.20f, 0.04f), boulderCol);
                collector.AddShadowColorBox(new Vector3(0.32f, 0.26f, 0.30f), o + new Vector3(0.24f, 0.16f, -0.14f), boulderCol);
                collector.AddShadowColorBox(new Vector3(0.76f, 0.12f, 0.08f), o + new Vector3(0f, 0.18f, -0.42f), hazardCol);
            }
        }

        // Estruturas Voxel Automáticas: Ponte (sobre Água) e Túnel (através de Morro/Montanha)
        if (structure == RoadStructureType.Bridge)
        {
            var deckCol = tier <= RoadTier.Gravel ? new Color("7c532c") : new Color("64748b");
            var pillarCol = new Color("475569");
            var railCol = tier <= RoadTier.Gravel ? new Color("9a6b3b") : new Color("cbd5e1");

            float deckW = MathF.Max(width + 0.34f, 1.48f);
            collector.AddShadowColorBox(new Vector3(TileSize, 0.14f, deckW), o + new Vector3(0, -0.07f, 0), deckCol);
            if (connectN || connectS)
                collector.AddShadowColorBox(new Vector3(deckW, 0.14f, TileSize), o + new Vector3(0, -0.07f, 0), deckCol);

            // Pilares submersos apoiando o tabuleiro no fundo do lago/rio (-0.58f)
            float pOff = deckW * 0.42f;
            foreach (float px in new[] { -pOff, pOff })
            {
                foreach (float pz in new[] { -pOff, pOff })
                {
                    collector.AddFlatColorBox(new Vector3(0.22f, 0.54f, 0.22f), o + new Vector3(px, -0.34f, pz), pillarCol);
                }
            }

            // Viga transversal de reforço
            collector.AddFlatColorBox(new Vector3(deckW + 0.08f, 0.16f, 0.26f), o + new Vector3(0, -0.15f, 0), pillarCol);

            // Guarda-corpos nos lados livres
            float railOff = deckW * 0.47f;
            if (!connectN && (connectE || connectW || (!connectN && !connectS)))
                collector.AddShadowColorBox(new Vector3(TileSize * 0.96f, 0.18f, 0.08f), o + new Vector3(0, 0.11f, -railOff), railCol);
            if (!connectS && (connectE || connectW || (!connectN && !connectS)))
                collector.AddShadowColorBox(new Vector3(TileSize * 0.96f, 0.18f, 0.08f), o + new Vector3(0, 0.11f, railOff), railCol);
            if (!connectW && (connectN || connectS))
                collector.AddShadowColorBox(new Vector3(0.08f, 0.18f, TileSize * 0.96f), o + new Vector3(-railOff, 0.11f, 0), railCol);
            if (!connectE && (connectN || connectS))
                collector.AddShadowColorBox(new Vector3(0.08f, 0.18f, TileSize * 0.96f), o + new Vector3(railOff, 0.11f, 0), railCol);
        }
        else if (structure == RoadStructureType.Tunnel)
        {
            var portalStone = new Color("475569");
            var portalTrim = new Color("334155");
            Color capColor = biome switch
            {
                BiomeType.Desert => new Color("d5a948"),
                BiomeType.Alpine => new Color("edf2f7"),
                _ => new Color("539e4c")
            };

            bool nsAxis = (connectN || connectS) && !(connectE && connectW && !connectN && !connectS);
            if (!connectN && !connectS && (connectE || connectW))
                nsAxis = false;

            // Vão livre de 0.86f de altura garante que até o Bi-Trem (topo 0.68f) atravesse sem colisão
            if (nsAxis)
            {
                collector.AddShadowColorBox(new Vector3(0.32f, 0.96f, TileSize), o + new Vector3(-0.84f, 0.48f, 0), portalStone);
                collector.AddShadowColorBox(new Vector3(0.32f, 0.96f, TileSize), o + new Vector3(0.84f, 0.48f, 0), portalStone);
                collector.AddShadowColorBox(new Vector3(TileSize, 0.24f, TileSize), o + new Vector3(0, 0.98f, 0), portalTrim);
                collector.AddShadowColorBox(new Vector3(TileSize, 0.14f, TileSize), o + new Vector3(0, 1.16f, 0), capColor);
                // Fitas de iluminação interna âmbar no teto do túnel
                collector.AddLampBox(new Vector3(0.10f, 0.04f, TileSize * 0.82f), o + new Vector3(-0.34f, 0.84f, 0));
                collector.AddLampBox(new Vector3(0.10f, 0.04f, TileSize * 0.82f), o + new Vector3(0.34f, 0.84f, 0));
            }
            else
            {
                collector.AddShadowColorBox(new Vector3(TileSize, 0.96f, 0.32f), o + new Vector3(0, 0.48f, -0.84f), portalStone);
                collector.AddShadowColorBox(new Vector3(TileSize, 0.96f, 0.32f), o + new Vector3(0, 0.48f, 0.84f), portalStone);
                collector.AddShadowColorBox(new Vector3(TileSize, 0.24f, TileSize), o + new Vector3(0, 0.98f, 0), portalTrim);
                collector.AddShadowColorBox(new Vector3(TileSize, 0.14f, TileSize), o + new Vector3(0, 1.16f, 0), capColor);
                // Fitas de iluminação interna âmbar no teto do túnel
                collector.AddLampBox(new Vector3(TileSize * 0.82f, 0.04f, 0.10f), o + new Vector3(0, 0.84f, -0.34f));
                collector.AddLampBox(new Vector3(TileSize * 0.82f, 0.04f, 0.10f), o + new Vector3(0, 0.84f, 0.34f));
            }
        }
    }

    /// <summary>
    /// Constrói edifícios urbanos no estilo exato das capturas (prédios de tijolinhos vermelhos,
    /// casas menta com telhado duas águas, torres azuis/amarelas e caixas d'água azuis).
    /// </summary>
    public static Node3D CreateCityBuildingNode(bool isCityCenter, int seedHash)
    {
        var root = new Node3D();
        var sidewalkMat = GetMat("d1d5db", 0.90f);
        AddBox(root, new Vector3(1.86f, 0.06f, 1.86f), new Vector3(0, 0.03f, 0), sidewalkMat, castShadow: false);

        Material winMat = _sharedNightWindowMat ?? GetMat("253041", 0.45f);
        var whiteTrimMat = GetMat("f1f5f9", 0.80f);
        var darkRoofMat = GetMat("3f4451", 0.85f);

        if (isCityCenter)
        {
            // Torre Corporativa Branca & Preta + Caixa d'Água Azul icônica (images (6).jpg / (7).jpg)
            var towerMat = GetMat("f8fafc", 0.75f);
            AddBox(root, new Vector3(1.10f, 2.50f, 1.10f), new Vector3(-0.18f, 1.28f, -0.10f), towerMat);
            AddBox(root, new Vector3(1.14f, 2.05f, 0.72f), new Vector3(-0.18f, 1.32f, -0.10f), winMat, castShadow: false);
            AddBox(root, new Vector3(0.72f, 2.05f, 1.14f), new Vector3(-0.18f, 1.32f, -0.10f), winMat, castShadow: false);
            AddBox(root, new Vector3(1.16f, 0.12f, 1.16f), new Vector3(-0.18f, 2.56f, -0.10f), darkRoofMat, castShadow: false);

            // Caixa d'água azul elevada ao lado
            var steelLeg = GetMat("475569", 0.6f);
            var tankBlue = GetMat("2563eb", 0.55f);
            AddBox(root, new Vector3(0.06f, 0.85f, 0.06f), new Vector3(0.55f, 0.45f, 0.40f), steelLeg, castShadow: false);
            AddBox(root, new Vector3(0.06f, 0.85f, 0.06f), new Vector3(0.75f, 0.45f, 0.40f), steelLeg, castShadow: false);
            AddCylinder(root, 0.22f, 0.22f, 0.45f, new Vector3(0.65f, 1.05f, 0.40f), tankBlue);
            OptimizeStaticChildren(root);
            return root;
        }

        int archetype = Math.Abs(seedHash) % 5;

        if (archetype == 0)
        {
            // Prédio de Tijolinho Vermelho com Molduras Brancas (images (4).jpg / (8).jpg)
            var brickMat = GetMat("c94a38", 0.85f);
            AddBox(root, new Vector3(1.18f, 1.35f, 1.10f), new Vector3(0, 0.70f, 0), brickMat);
            AddBox(root, new Vector3(1.24f, 0.14f, 1.16f), new Vector3(0, 1.42f, 0), darkRoofMat, castShadow: false);
            // Molduras brancas de janelas em 3 andares
            for (int floor = 0; floor < 3; floor++)
            {
                float fy = 0.34f + floor * 0.38f;
                AddBox(root, new Vector3(1.22f, 0.18f, 0.82f), new Vector3(0, fy, 0), whiteTrimMat, castShadow: false);
                AddBox(root, new Vector3(1.24f, 0.13f, 0.76f), new Vector3(0, fy, 0), winMat, castShadow: false);
            }
        }
        else if (archetype == 1)
        {
            // Casa Verde-Menta com Telhado Cinza Escuro (images (4).jpg / (8).jpg)
            var mintMat = GetMat("54b89c", 0.82f);
            AddBox(root, new Vector3(1.26f, 0.68f, 1.02f), new Vector3(0, 0.37f, 0), mintMat);
            AddBox(root, new Vector3(1.34f, 0.22f, 1.10f), new Vector3(0, 0.80f, 0), darkRoofMat);
            AddBox(root, new Vector3(1.06f, 0.24f, 1.05f), new Vector3(0, 0.40f, 0), whiteTrimMat, castShadow: false);
            AddBox(root, new Vector3(0.98f, 0.18f, 1.07f), new Vector3(0, 0.40f, 0), winMat, castShadow: false);
        }
        else if (archetype == 2)
        {
            // Torre Comercial Azul Celeste (images (6).jpg)
            var blueMat = GetMat("3898ec", 0.70f);
            AddBox(root, new Vector3(1.12f, 1.85f, 1.06f), new Vector3(0, 0.95f, 0), blueMat);
            AddBox(root, new Vector3(1.16f, 0.10f, 1.10f), new Vector3(0, 1.90f, 0), whiteTrimMat, castShadow: false);
            AddBox(root, new Vector3(1.15f, 1.45f, 0.76f), new Vector3(0, 0.98f, 0), winMat, castShadow: false);
        }
        else if (archetype == 3)
        {
            // Edifício Amarelo Mostarda (images (6).jpg / (8).jpg)
            var yellowMat = GetMat("eab308", 0.75f);
            AddBox(root, new Vector3(1.14f, 1.65f, 1.08f), new Vector3(0, 0.85f, 0), yellowMat);
            AddBox(root, new Vector3(1.18f, 0.12f, 1.12f), new Vector3(0, 1.72f, 0), darkRoofMat, castShadow: false);
            AddBox(root, new Vector3(1.16f, 1.20f, 0.75f), new Vector3(0, 0.90f, 0), winMat, castShadow: false);
        }
        else
        {
            // Casa Bege com Telhado Terracota (images (4).jpg)
            var creamMat = GetMat("e6d3a7", 0.85f);
            var terraMat = GetMat("d9534f", 0.80f);
            AddBox(root, new Vector3(1.22f, 0.65f, 1.00f), new Vector3(0, 0.35f, 0), creamMat);
            AddBox(root, new Vector3(1.30f, 0.24f, 1.08f), new Vector3(0, 0.76f, 0), terraMat);
            AddBox(root, new Vector3(0.95f, 0.22f, 1.03f), new Vector3(0, 0.38f, 0), winMat, castShadow: false);
        }

        OptimizeStaticChildren(root);
        return root;
    }

    public static Node3D CreateIndustryComplexNode(Industry industry, out Node3D? animatedPart)
    {
        return CreateIndustryComplexNode(industry, default, out animatedPart, null);
    }

    public static Node3D CreateIndustryComplexNode(
        Industry industry,
        IndustrialConnections connections,
        out Node3D? animatedPart,
        List<IndustrialConveyorItem>? conveyorItems)
    {
        animatedPart = null;
        var root = new Node3D();

        // Pátio cinza-claro limpo estilo Voxel Tycoon
        var yardMat = GetMat("cbd5e1", 0.88f);
        var whiteWall = GetMat("f1f5f9", 0.80f);
        var greenTrim = GetMat("22c55e", 0.75f);
        var darkMetal = GetMat("3f4451", 0.65f);
        var pipeMetal = GetMat("94a3b8", 0.42f, 0.55f);
        var yellowGantry = GetMat("eab308", 0.55f, 0.15f);
        var beltMat = GetMat("1e293b", 0.90f);
        var railMat = GetMat("475569", 0.62f, 0.25f);
        var cargoMat = GetCargoAccentMaterial(industry.OutputResource);

        AddBox(root, new Vector3(1.90f, 0.06f, 1.90f), new Vector3(0, 0.03f, 0), yardMat, castShadow: false);

        switch (industry.OutputResource)
        {
            case ResourceType.Grain:
            {
                // Fazenda com Celeiro Vermelho + 3 Silos Metálicos + Tubos Helicoidais + Pórtico de Carga
                var barnMat = GetMat("b91c1c", 0.80f);
                var siloMat = GetMat("cbd5e1", 0.55f, 0.4f);
                AddBox(root, new Vector3(0.86f, 0.66f, 0.96f), new Vector3(-0.36f, 0.36f, -0.1f), barnMat);
                foreach (float z in new[] { -0.46f, 0.0f, 0.46f })
                {
                    AddCylinder(root, 0.22f, 0.22f, 1.15f, new Vector3(0.42f, 0.60f, z), siloMat);
                    AddCylinder(root, 0.02f, 0.24f, 0.24f, new Vector3(0.42f, 1.28f, z), siloMat);
                }

                // Tubo elevador de grãos e pórtico amarelo lateral
                AddPipeSegment(root, new Vector3(-0.12f, 0.48f, -0.42f), new Vector3(0.42f, 1.05f, -0.46f), 0.045f, pipeMetal, yellowGantry);
                AddPipeSegment(root, new Vector3(0.42f, 0.98f, -0.46f), new Vector3(0.42f, 0.98f, 0.46f), 0.042f, pipeMetal, yellowGantry);
                AddConveyorBeltSegment(root, new Vector3(0.18f, 0.18f, 0.58f), new Vector3(-0.35f, 0.28f, 0.58f), 0.22f, railMat, beltMat, cargoMat, conveyorItems);
                AddYellowGantryFrame(root, new Vector3(-0.35f, 0.06f, 0.62f), 0.72f, 0.78f, 0.26f, yellowGantry, darkMetal);

                // Lavoura dourada com fileiras de trigo (realçadas durante Safra Recorde)
                var cropRowMat = GetMat("eab308", 0.78f);
                var cropHeadMat = GetMat("facc15", 0.68f, 0.10f, emission: true, emissionEnergy: 0.35f);
                var harvestGroup = new Node3D { Name = "HarvestCropFields" };
                for (int r = 0; r < 3; r++)
                {
                    float rx = -0.68f + r * 0.22f;
                    AddBox(harvestGroup, new Vector3(0.14f, 0.10f, 0.42f), new Vector3(rx, 0.09f, -0.68f), cropRowMat, castShadow: false);
                    AddBox(harvestGroup, new Vector3(0.10f, 0.04f, 0.38f), new Vector3(rx, 0.15f, -0.68f), cropHeadMat, castShadow: false);
                }
                root.AddChild(harvestGroup);
                break;
            }
            case ResourceType.Food:
            {
                // Moinho Cerealista + Galpão Branco/Verde + Esteira de Alimentação & Dutos de Silo
                AddBox(root, new Vector3(1.08f, 0.78f, 0.94f), new Vector3(-0.18f, 0.42f, -0.1f), whiteWall);
                AddBox(root, new Vector3(1.14f, 0.12f, 1.00f), new Vector3(-0.18f, 0.85f, -0.1f), greenTrim, castShadow: false);
                AddCylinder(root, 0.26f, 0.36f, 1.38f, new Vector3(0.48f, 0.72f, -0.25f), whiteWall);

                var rotor = new Node3D { Name = "WindmillBlades", Position = new Vector3(0.48f, 1.20f, 0.06f) };
                AddBox(rotor, new Vector3(0.10f, 1.20f, 0.05f), Vector3.Zero, GetMat("78350f", 0.75f), castShadow: false);
                AddBox(rotor, new Vector3(1.20f, 0.10f, 0.05f), Vector3.Zero, GetMat("78350f", 0.75f), castShadow: false);
                root.AddChild(rotor);
                animatedPart = rotor;

                // Esteira coberta e tubos conectando moinho ao galpão e doca frontal
                AddPipeSegment(root, new Vector3(0.32f, 0.88f, -0.22f), new Vector3(0.05f, 0.74f, -0.15f), 0.055f, pipeMetal, yellowGantry);
                AddConveyorBeltSegment(root, new Vector3(-0.56f, 0.18f, 0.58f), new Vector3(0.36f, 0.28f, 0.58f), 0.24f, railMat, beltMat, cargoMat, conveyorItems);
                AddYellowGantryFrame(root, new Vector3(0.42f, 0.06f, 0.58f), 0.66f, 0.80f, 0.24f, yellowGantry, darkMetal);
                break;
            }
            case ResourceType.Lumber:
            {
                // Serraria com Pórtico Amarelo + Esteira de Toras + Tubo de Serragem
                var woodMat = GetMat("92400e", 0.85f);
                AddBox(root, new Vector3(1.14f, 0.62f, 0.86f), new Vector3(-0.2f, 0.34f, -0.2f), woodMat);
                AddYellowGantryFrame(root, new Vector3(0.05f, 0.06f, 0.50f), 1.38f, 0.94f, 0.32f, yellowGantry, darkMetal);
                AddConveyorBeltSegment(root, new Vector3(-0.68f, 0.18f, 0.52f), new Vector3(0.68f, 0.18f, 0.52f), 0.26f, railMat, beltMat, cargoMat, conveyorItems);
                AddPipeSegment(root, new Vector3(0.36f, 0.62f, -0.22f), new Vector3(0.68f, 0.95f, -0.58f), 0.055f, pipeMetal, yellowGantry);
                AddCylinder(root, 0.18f, 0.12f, 0.65f, new Vector3(0.68f, 0.55f, -0.58f), pipeMetal);
                break;
            }
            case ResourceType.Fuel:
            case ResourceType.CrudeOil:
            {
                // Tanques Amarelos e Prateados + Malha de Tubulações & Válvulas
                AddCylinder(root, 0.38f, 0.38f, 0.86f, new Vector3(-0.42f, 0.46f, -0.34f), yellowGantry);
                AddCylinder(root, 0.38f, 0.38f, 0.86f, new Vector3(0.42f, 0.46f, -0.34f), yellowGantry);
                AddBox(root, new Vector3(1.30f, 0.56f, 0.60f), new Vector3(0, 0.31f, 0.45f), whiteWall);
                AddBox(root, new Vector3(1.36f, 0.08f, 0.66f), new Vector3(0, 0.62f, 0.45f), greenTrim, castShadow: false);

                // Tubulação elevada ligando os dois tanques e o prédio de refino
                AddPipeSegment(root, new Vector3(-0.42f, 0.82f, -0.34f), new Vector3(0.42f, 0.82f, -0.34f), 0.055f, pipeMetal, yellowGantry);
                AddPipeSegment(root, new Vector3(-0.18f, 0.82f, -0.34f), new Vector3(-0.18f, 0.56f, 0.38f), 0.05f, pipeMetal, yellowGantry);
                AddPipeSegment(root, new Vector3(0.22f, 0.82f, -0.34f), new Vector3(0.22f, 0.56f, 0.38f), 0.05f, pipeMetal, yellowGantry);
                AddYellowGantryFrame(root, new Vector3(0.02f, 0.06f, 0.08f), 0.88f, 0.96f, 0.24f, yellowGantry, darkMetal);

                // Balancim mecânico (Pumpjack / Cavalo-de-Pau) oscilante nos poços/refinarias de petróleo
                AddBox(root, new Vector3(0.18f, 0.48f, 0.18f), new Vector3(-0.66f, 0.27f, 0.48f), darkMetal);
                var pumpBeam = new Node3D { Name = "PumpjackBeam", Position = new Vector3(-0.66f, 0.52f, 0.48f) };
                AddBox(pumpBeam, new Vector3(0.10f, 0.10f, 0.58f), Vector3.Zero, yellowGantry);
                AddBox(pumpBeam, new Vector3(0.16f, 0.24f, 0.14f), new Vector3(0f, 0.02f, 0.28f), darkMetal); // Cabeça do cavalo
                AddBox(pumpBeam, new Vector3(0.18f, 0.18f, 0.16f), new Vector3(0f, -0.02f, -0.26f), yellowGantry); // Contrapeso
                root.AddChild(pumpBeam);
                animatedPart = pumpBeam;

                // Coluna de vapor/gás estilizada na torre de craqueamento
                AddCylinder(root, 0.08f, 0.11f, 1.42f, new Vector3(0.64f, 0.74f, 0.42f), pipeMetal);
                root.AddChild(CreateChimneySmokeParticles(new Vector3(0.64f, 1.48f, 0.42f), isDarkSmoke: false));
                break;
            }
            case ResourceType.Coal:
            {
                // Mina de Carvão: Pátio escuro, tremonha e esteira carregada de hulha
                AddBox(root, new Vector3(1.16f, 0.72f, 0.90f), new Vector3(-0.14f, 0.39f, -0.22f), darkMetal);
                AddBox(root, new Vector3(0.56f, 0.36f, 0.52f), new Vector3(0.46f, 0.21f, -0.30f), GetMat("1e293b", 0.92f));
                AddCylinder(root, 0.10f, 0.14f, 1.35f, new Vector3(-0.48f, 0.70f, -0.42f), pipeMetal);
                root.AddChild(CreateChimneySmokeParticles(new Vector3(-0.48f, 1.40f, -0.42f), isDarkSmoke: true));
                AddConveyorBeltSegment(root, new Vector3(-0.62f, 0.18f, 0.56f), new Vector3(0.32f, 0.48f, 0.14f), 0.28f, railMat, beltMat, cargoMat, conveyorItems);
                AddYellowGantryFrame(root, new Vector3(0.24f, 0.06f, 0.56f), 0.96f, 0.86f, 0.28f, yellowGantry, darkMetal);
                break;
            }
            default:
            {
                // Fornos, Siderúrgica & Fábricas: Esteira Inclinada + Tubos de Vapor + Pórtico Pesado Amarelo
                var furnaceRed = GetMat("d9534f", 0.80f);
                AddBox(root, new Vector3(1.22f, 0.76f, 0.96f), new Vector3(0, 0.41f, -0.18f), whiteWall);
                AddBox(root, new Vector3(1.28f, 0.12f, 1.02f), new Vector3(0, 0.82f, -0.18f), darkMetal, castShadow: false);
                AddCylinder(root, 0.11f, 0.16f, 1.65f, new Vector3(-0.42f, 0.85f, -0.40f), furnaceRed);
                AddCylinder(root, 0.11f, 0.16f, 1.65f, new Vector3(0.42f, 0.85f, -0.40f), furnaceRed);

                // Exaustor rotativo no teto da planta industrial
                var roofFan = new Node3D { Name = "RoofExhaustFan", Position = new Vector3(0f, 0.90f, -0.18f) };
                AddCylinder(roofFan, 0.18f, 0.18f, 0.06f, Vector3.Zero, darkMetal, castShadow: false);
                AddBox(roofFan, new Vector3(0.30f, 0.03f, 0.06f), new Vector3(0f, 0.03f, 0f), pipeMetal, castShadow: false);
                AddBox(roofFan, new Vector3(0.06f, 0.03f, 0.30f), new Vector3(0f, 0.03f, 0f), pipeMetal, castShadow: false);
                root.AddChild(roofFan);
                animatedPart = roofFan;

                // Plumas de fumaça/vapor nas duas chaminés industriais
                root.AddChild(CreateChimneySmokeParticles(new Vector3(-0.42f, 1.70f, -0.40f), isDarkSmoke: true));
                root.AddChild(CreateChimneySmokeParticles(new Vector3(0.42f, 1.70f, -0.40f), isDarkSmoke: false));

                // Esteira inclinada alimentando o galpão + tubulação industrial e pórtico amarelo
                AddConveyorBeltSegment(root, new Vector3(-0.68f, 0.16f, 0.58f), new Vector3(0.24f, 0.52f, 0.18f), 0.26f, railMat, beltMat, cargoMat, conveyorItems);
                AddPipeSegment(root, new Vector3(-0.42f, 1.05f, -0.40f), new Vector3(0.42f, 1.05f, -0.40f), 0.055f, pipeMetal, yellowGantry);
                AddPipeSegment(root, new Vector3(0.42f, 0.65f, -0.15f), new Vector3(0.68f, 0.65f, 0.52f), 0.05f, pipeMetal, yellowGantry);
                AddYellowGantryFrame(root, new Vector3(0.32f, 0.06f, 0.54f), 0.92f, 0.86f, 0.28f, yellowGantry, darkMetal);
                break;
            }
        }

        // Indicador de integração logística quando próximo a um Hub Logístico
        if (connections.NearbyHubCount > 0)
        {
            var beaconMat = GetMat("38bdf8", 0.35f, 0.3f);
            AddBox(root, new Vector3(0.36f, 0.18f, 0.36f), new Vector3(-0.64f, 0.15f, -0.64f), darkMetal);
            AddCylinder(root, 0.06f, 0.08f, 0.85f, new Vector3(-0.64f, 0.60f, -0.64f), yellowGantry);
            AddBox(root, new Vector3(0.16f, 0.12f, 0.16f), new Vector3(-0.64f, 1.05f, -0.64f), beaconMat, castShadow: false);
        }

        // Módulos conectados dinamicamente às bordas adjacentes (Estradas, Hubs e Indústrias vizinhas)
        if (connections.LinkN)
            AddConnectedEdgeModule(root, 0f, connections.HeavyBridgeN, yellowGantry, darkMetal, pipeMetal, railMat, beltMat, cargoMat, conveyorItems);
        if (connections.LinkS)
            AddConnectedEdgeModule(root, MathF.PI, connections.HeavyBridgeS, yellowGantry, darkMetal, pipeMetal, railMat, beltMat, cargoMat, conveyorItems);
        if (connections.LinkE)
            AddConnectedEdgeModule(root, -MathF.PI * 0.5f, connections.HeavyBridgeE, yellowGantry, darkMetal, pipeMetal, railMat, beltMat, cargoMat, conveyorItems);
        if (connections.LinkW)
            AddConnectedEdgeModule(root, MathF.PI * 0.5f, connections.HeavyBridgeW, yellowGantry, darkMetal, pipeMetal, railMat, beltMat, cargoMat, conveyorItems);

        HashSet<Node>? skip = null;
        if (animatedPart != null || (conveyorItems != null && conveyorItems.Count > 0))
        {
            skip = new HashSet<Node>();
            if (animatedPart != null) skip.Add(animatedPart);
            if (conveyorItems != null)
            {
                foreach (var ci in conveyorItems)
                    skip.Add(ci.CargoNode);
            }
        }
        OptimizeStaticChildren(root, skip);
        return root;
    }

    private static StandardMaterial3D GetCargoAccentMaterial(ResourceType output)
    {
        string hex = output switch
        {
            ResourceType.Grain or ResourceType.Food => "facc15",
            ResourceType.Timber or ResourceType.Lumber => "b45309",
            ResourceType.IronOre => "475569",
            ResourceType.Coal => "1e293b",
            ResourceType.Steel => "60a5fa",
            ResourceType.CrudeOil or ResourceType.Fuel => "f97316",
            ResourceType.Machinery => "0284c7",
            _ => "34d399"
        };
        return GetMat(hex, 0.68f, 0.15f);
    }

    private static void AddConnectedEdgeModule(
        Node3D root,
        float yawRadians,
        bool isHeavyBridge,
        StandardMaterial3D yellowMat,
        StandardMaterial3D darkMetal,
        StandardMaterial3D pipeMat,
        StandardMaterial3D railMat,
        StandardMaterial3D beltMat,
        StandardMaterial3D cargoMat,
        List<IndustrialConveyorItem>? conveyorItems)
    {
        var edgeRoot = new Node3D
        {
            Rotation = new Vector3(0f, yawRadians, 0f)
        };
        root.AddChild(edgeRoot);

        float reachZ = isHeavyBridge ? -1.32f : -0.98f;

        // Pórtico amarelo de embarque na borda conectada
        AddYellowGantryFrame(edgeRoot, new Vector3(0f, 0.06f, -0.78f), 0.78f, 0.78f, 0.22f, yellowMat, darkMetal);

        // Esteira transportadora saindo do pátio em direção à estrada/instalação vizinha
        AddConveyorBeltSegment(
            edgeRoot,
            new Vector3(-0.14f, 0.20f, -0.34f),
            new Vector3(-0.14f, 0.20f, reachZ),
            0.24f,
            railMat,
            beltMat,
            cargoMat,
            conveyorItems);

        // Tubo aéreo paralelo com flange amarelo e suportes verticais
        AddPipeSegment(
            edgeRoot,
            new Vector3(0.20f, 0.62f, -0.30f),
            new Vector3(0.20f, 0.62f, reachZ),
            0.055f,
            pipeMat,
            yellowMat);
        AddBox(edgeRoot, new Vector3(0.06f, 0.60f, 0.06f), new Vector3(0.20f, 0.32f, -0.84f), darkMetal, castShadow: false);

        HashSet<Node>? edgeSkip = null;
        if (conveyorItems != null && conveyorItems.Count > 0)
        {
            edgeSkip = new HashSet<Node>();
            foreach (var ci in conveyorItems)
                edgeSkip.Add(ci.CargoNode);
        }
        OptimizeStaticChildren(edgeRoot, edgeSkip);
    }

    private static void AddYellowGantryFrame(
        Node3D parent,
        Vector3 baseCenter,
        float spanX,
        float height,
        float depth,
        StandardMaterial3D yellowMat,
        StandardMaterial3D darkMetal)
    {
        float halfX = spanX * 0.5f;
        float legW = 0.09f;

        // Pilares amarelos e sapatas escuras
        AddBox(parent, new Vector3(0.14f, 0.08f, depth), baseCenter + new Vector3(-halfX, 0.04f, 0f), darkMetal, castShadow: false);
        AddBox(parent, new Vector3(0.14f, 0.08f, depth), baseCenter + new Vector3(halfX, 0.04f, 0f), darkMetal, castShadow: false);
        AddBox(parent, new Vector3(legW, height, legW), baseCenter + new Vector3(-halfX, height * 0.5f, 0f), yellowMat);
        AddBox(parent, new Vector3(legW, height, legW), baseCenter + new Vector3(halfX, height * 0.5f, 0f), yellowMat);

        // Viga superior dupla + carrinho talha central
        AddBox(parent, new Vector3(spanX + 0.16f, 0.11f, MathF.Max(0.14f, depth * 0.65f)), baseCenter + new Vector3(0f, height + 0.03f, 0f), yellowMat);
        AddBox(parent, new Vector3(0.20f, 0.10f, 0.16f), baseCenter + new Vector3(0f, height - 0.06f, 0f), darkMetal, castShadow: false);
        AddBox(parent, new Vector3(0.04f, 0.18f, 0.04f), baseCenter + new Vector3(0f, height - 0.18f, 0f), darkMetal, castShadow: false);
    }

    private static void AddPipeSegment(
        Node3D parent,
        Vector3 start,
        Vector3 end,
        float radius,
        StandardMaterial3D pipeMat,
        StandardMaterial3D? flangeMat = null)
    {
        Vector3 delta = end - start;
        float len = delta.Length();
        if (len < 0.02f) return;

        Vector3 dir = delta / len;
        Vector3 refVec = MathF.Abs(dir.Y) < 0.9f ? Vector3.Up : Vector3.Right;
        Vector3 xAxis = dir.Cross(refVec).Normalized();
        Vector3 zAxis = xAxis.Cross(dir).Normalized();
        var basis = new Basis(xAxis, dir, zAxis);

        var cylMesh = GetCylinderMesh(radius, radius, len, 8);
        var pipeNode = new MeshInstance3D
        {
            Mesh = cylMesh,
            MaterialOverride = pipeMat,
            Transform = new Transform3D(basis, (start + end) * 0.5f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On
        };
        parent.AddChild(pipeNode);

        if (flangeMat != null)
        {
            var ringMesh = GetCylinderMesh(radius * 1.45f, radius * 1.45f, 0.055f, 8);
            parent.AddChild(new MeshInstance3D
            {
                Mesh = ringMesh,
                MaterialOverride = flangeMat,
                Transform = new Transform3D(basis, start.Lerp(end, 0.25f)),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
            parent.AddChild(new MeshInstance3D
            {
                Mesh = ringMesh,
                MaterialOverride = flangeMat,
                Transform = new Transform3D(basis, start.Lerp(end, 0.75f)),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
        }
    }

    private static void AddConveyorBeltSegment(
        Node3D parent,
        Vector3 start,
        Vector3 end,
        float width,
        StandardMaterial3D railMat,
        StandardMaterial3D beltMat,
        StandardMaterial3D? cargoMat = null,
        List<IndustrialConveyorItem>? conveyorItems = null)
    {
        Vector3 delta = end - start;
        float len = delta.Length();
        if (len < 0.05f) return;

        Vector3 dir = delta / len;
        Vector3 refVec = Vector3.Up;
        Vector3 right = dir.Cross(refVec).Normalized();
        if (right.LengthSquared() < 0.01f) right = Vector3.Right;
        Vector3 up = right.Cross(dir).Normalized();
        var basis = new Basis(right, up, -dir);
        Vector3 mid = (start + end) * 0.5f;

        // Calha metálica e tapete escuro da esteira
        var railNode = new MeshInstance3D
        {
            Mesh = GetBoxMesh(new Vector3(width, 0.08f, len)),
            MaterialOverride = railMat,
            Transform = new Transform3D(basis, mid),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        parent.AddChild(railNode);

        var beltNode = new MeshInstance3D
        {
            Mesh = GetBoxMesh(new Vector3(width * 0.74f, 0.095f, len * 0.98f)),
            MaterialOverride = beltMat,
            Transform = new Transform3D(basis, mid + up * 0.012f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        parent.AddChild(beltNode);

        // Pés de sustentação da esteira até o piso do pátio
        foreach (float t in new[] { 0.18f, 0.82f })
        {
            Vector3 p = start.Lerp(end, t);
            if (p.Y > 0.08f)
                AddBox(parent, new Vector3(width * 0.75f, p.Y, 0.05f), new Vector3(p.X, p.Y * 0.5f, p.Z), railMat, castShadow: false);
        }

        // Volume animado percorrendo a esteira
        if (cargoMat != null && conveyorItems != null)
        {
            Vector3 cargoStart = start + up * 0.095f;
            Vector3 cargoEnd = end + up * 0.095f;
            var cargo = new MeshInstance3D
            {
                Mesh = GetBoxMesh(new Vector3(width * 0.52f, 0.11f, width * 0.52f)),
                MaterialOverride = cargoMat,
                Position = cargoStart,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            parent.AddChild(cargo);
            conveyorItems.Add(new IndustrialConveyorItem
            {
                CargoNode = cargo,
                StartLocal = cargoStart,
                EndLocal = cargoEnd,
                Phase = (conveyorItems.Count * 0.37f) % 1.0f,
                Speed = 0.48f
            });
        }
    }

    public static GpuParticles3D CreateWeatherRainSystem()
    {
        var procMat = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(24f, 1f, 24f),
            Direction = new Vector3(-0.10f, -1.0f, 0.04f),
            Spread = 2.5f,
            Gravity = new Vector3(-2.0f, -36.0f, 0f),
            InitialVelocityMin = 22.0f,
            InitialVelocityMax = 26.0f,
            Color = new Color(0.70f, 0.84f, 0.96f, 0.42f)
        };

        var dropMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.72f, 0.85f, 0.98f, 0.40f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };

        var dropMesh = new BoxMesh
        {
            Size = new Vector3(0.02f, 0.75f, 0.02f),
            Material = dropMat
        };

        return new GpuParticles3D
        {
            Name = "RainWeatherParticles",
            Position = new Vector3(0, 20f, 0),
            Amount = 420,
            Lifetime = 0.75f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-30, -25, -30), new Vector3(60, 30, 60)),
            ProcessMaterial = procMat,
            DrawPass1 = dropMesh,
            Emitting = false
        };
    }

    public static GpuParticles3D CreateWeatherSnowSystem()
    {
        var procMat = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(26f, 1f, 26f),
            Direction = new Vector3(-0.35f, -1.0f, 0.20f),
            Spread = 16.0f,
            Gravity = new Vector3(-1.4f, -4.8f, 0.8f),
            InitialVelocityMin = 3.5f,
            InitialVelocityMax = 6.2f,
            ScaleMin = 0.8f,
            ScaleMax = 1.5f,
            Color = new Color(0.95f, 0.98f, 1.0f, 0.82f)
        };

        var flakeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.96f, 0.98f, 1.0f, 0.85f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };

        var flakeMesh = new BoxMesh
        {
            Size = new Vector3(0.08f, 0.08f, 0.08f),
            Material = flakeMat
        };

        return new GpuParticles3D
        {
            Name = "SnowBlizzardParticles",
            Position = new Vector3(0, 16f, 0),
            Amount = 360,
            Lifetime = 2.6f,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-32, -22, -32), new Vector3(64, 28, 64)),
            ProcessMaterial = procMat,
            DrawPass1 = flakeMesh,
            Emitting = false
        };
    }

    private static GpuParticles3D CreateChimneySmokeParticles(Vector3 localPos, bool isDarkSmoke)
    {
        var procMat = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.08f,
            Direction = new Vector3(0.18f, 1.0f, -0.10f),
            Spread = 14.0f,
            Gravity = new Vector3(0.25f, 0.65f, -0.10f),
            InitialVelocityMin = 0.35f,
            InitialVelocityMax = 0.72f,
            ScaleMin = 0.85f,
            ScaleMax = 1.65f,
            Color = isDarkSmoke
                ? new Color(0.28f, 0.31f, 0.36f, 0.55f)
                : new Color(0.90f, 0.94f, 0.98f, 0.48f)
        };

        var puffMat = new StandardMaterial3D
        {
            AlbedoColor = isDarkSmoke
                ? new Color(0.30f, 0.33f, 0.38f, 0.55f)
                : new Color(0.92f, 0.95f, 0.99f, 0.48f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.92f
        };

        var puffMesh = new BoxMesh
        {
            Size = new Vector3(0.13f, 0.13f, 0.13f),
            Material = puffMat
        };

        return new GpuParticles3D
        {
            Name = "ChimneySmokeParticles",
            Position = localPos,
            Amount = 16,
            Lifetime = 1.85f,
            Emitting = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-2, -1, -2), new Vector3(4, 5, 4)),
            ProcessMaterial = procMat,
            DrawPass1 = puffMesh
        };
    }

    public static GpuParticles3D CreateConstructionBurstParticles(Vector3 worldPos)
    {
        var procMat = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(0.55f, 0.08f, 0.55f),
            Direction = new Vector3(0f, 1.0f, 0f),
            Spread = 65.0f,
            Gravity = new Vector3(0f, -4.2f, 0f),
            InitialVelocityMin = 1.2f,
            InitialVelocityMax = 2.6f,
            ScaleMin = 0.7f,
            ScaleMax = 1.35f,
            Color = new Color(0.86f, 0.78f, 0.64f, 0.78f)
        };

        var dustMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.86f, 0.78f, 0.64f, 0.78f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.95f
        };

        var dustMesh = new BoxMesh
        {
            Size = new Vector3(0.11f, 0.11f, 0.11f),
            Material = dustMat
        };

        return new GpuParticles3D
        {
            Name = "ConstructionBurstParticles",
            Position = worldPos + new Vector3(0f, 0.15f, 0f),
            Amount = 22,
            Lifetime = 0.65f,
            OneShot = true,
            Explosiveness = 0.90f,
            Emitting = true,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ProcessMaterial = procMat,
            DrawPass1 = dustMesh
        };
    }

    public static Node3D CreateGarageNode(Garage garage)
    {
        var root = new Node3D();
        // Depósito branco com borda verde e portões escuros (idêntico aos depósitos de images (5).jpg e (8).jpg)
        var padMat = GetMat("d1d5db", 0.88f);
        var wallMat = GetMat("f8fafc", 0.78f);
        var roofTrimMat = GetMat("22c55e", 0.70f);
        var doorMat = GetMat("334155", 0.65f);

        AddBox(root, new Vector3(1.86f, 0.06f, 1.86f), new Vector3(0, 0.03f, 0), padMat, castShadow: false);
        AddBox(root, new Vector3(1.48f, 0.74f, 1.28f), new Vector3(0, 0.40f, -0.08f), wallMat);
        AddBox(root, new Vector3(1.56f, 0.12f, 1.36f), new Vector3(0, 0.82f, -0.08f), roofTrimMat, castShadow: false);
        AddBox(root, new Vector3(0.45f, 0.50f, 1.32f), new Vector3(-0.36f, 0.28f, -0.08f), doorMat, castShadow: false);
        AddBox(root, new Vector3(0.45f, 0.50f, 1.32f), new Vector3(0.36f, 0.28f, -0.08f), doorMat, castShadow: false);

        OptimizeStaticChildren(root);
        var label = CreateFloatingLabel($"🔧 {garage.Name}", new Color("facc15"), 1.50f, 32);
        root.AddChild(label);
        return root;
    }

    public static Node3D CreateLogisticsHubNode(LogisticsHub hub, out Node3D? animatedRoofFan)
    {
        var root = new Node3D();
        var padMat = GetMat("d1d5db", 0.88f);
        var wallMat = GetMat("f8fafc", 0.78f);
        var awningBlue = GetMat("2563eb", 0.65f);
        var darkMetal = GetMat("334155", 0.60f);
        var fanMetal = GetMat("94a3b8", 0.45f, 0.50f);

        AddBox(root, new Vector3(1.90f, 0.06f, 1.90f), new Vector3(0, 0.03f, 0), padMat, castShadow: false);
        AddBox(root, new Vector3(1.58f, 0.80f, 1.34f), new Vector3(0, 0.43f, 0), wallMat);
        AddBox(root, new Vector3(1.66f, 0.12f, 1.42f), new Vector3(0, 0.88f, 0), awningBlue, castShadow: false);

        // Exaustor rotativo de ventilação no teto do Hub Cross-Docking
        var fanPivot = new Node3D { Name = "HubRoofFan", Position = new Vector3(0f, 0.96f, 0f) };
        AddCylinder(fanPivot, 0.20f, 0.20f, 0.06f, Vector3.Zero, darkMetal, castShadow: false);
        AddBox(fanPivot, new Vector3(0.34f, 0.03f, 0.06f), new Vector3(0f, 0.03f, 0f), fanMetal, castShadow: false);
        AddBox(fanPivot, new Vector3(0.06f, 0.03f, 0.34f), new Vector3(0f, 0.03f, 0f), fanMetal, castShadow: false);
        root.AddChild(fanPivot);
        animatedRoofFan = fanPivot;

        OptimizeStaticChildren(root);
        var label = CreateFloatingLabel($"📦 {hub.Name} (+20%)", new Color("34d399"), 1.55f, 32);
        root.AddChild(label);
        return root;
    }

    public static Node3D CreateLogisticsHubNode(LogisticsHub hub)
    {
        return CreateLogisticsHubNode(hub, out _);
    }

    public static Node3D CreateBusTerminalNode(BusTerminal terminal)
    {
        var root = new Node3D { Name = $"BusTerminal_{terminal.Id}" };
        var asphaltMat = GetMat("334155", 0.88f);
        var platformMat = GetMat("cbd5e1", 0.80f);
        var canopyMat = GetMat("0284c7", 0.45f, 0.35f);
        var pillarMat = GetMat("e2e8f0", 0.55f, 0.40f);
        var yellowCurbMat = GetMat("facc15", 0.55f);
        var glassMat = GetMat("38bdf8", 0.22f, 0.70f);
        var beaconMat = GetMat("38bdf8", 0.2f, 0.4f, true, 1.3f);

        AddBox(root, new Vector3(1.92f, 0.06f, 1.92f), new Vector3(0, 0.03f, 0), asphaltMat, castShadow: false);
        AddBox(root, new Vector3(1.68f, 0.12f, 0.84f), new Vector3(0, 0.12f, -0.32f), platformMat);
        AddBox(root, new Vector3(1.68f, 0.04f, 0.08f), new Vector3(0, 0.18f, 0.08f), yellowCurbMat, castShadow: false);

        // Saguão de vidro e cobertura curva da rodoviária
        AddBox(root, new Vector3(1.08f, 0.48f, 0.52f), new Vector3(0, 0.42f, -0.38f), glassMat);
        AddBox(root, new Vector3(1.64f, 0.08f, 0.96f), new Vector3(0, 0.74f, -0.18f), canopyMat);
        foreach (float px in new[] { -0.68f, 0f, 0.68f })
            AddCylinder(root, 0.045f, 0.045f, 0.58f, new Vector3(px, 0.45f, 0.08f), pillarMat, castShadow: false);

        // Totem luminoso azul
        AddBox(root, new Vector3(0.18f, 0.92f, 0.18f), new Vector3(0.74f, 0.50f, 0.52f), pillarMat);
        AddBox(root, new Vector3(0.22f, 0.22f, 0.22f), new Vector3(0.74f, 0.98f, 0.52f), beaconMat, castShadow: false);

        OptimizeStaticChildren(root);
        root.AddChild(CreateFloatingLabel($"🚌 {terminal.Name}", new Color("38bdf8"), 1.50f, 32));
        return root;
    }

    public static Node3D CreateTrafficControlNode(RoadNode node)
    {
        var root = new Node3D { Name = $"TrafficControl_{node.Id}" };
        var islandMat = GetMat("94a3b8", 0.78f);
        var grassMat = GetMat("22c55e", 0.85f);
        var poleMat = GetMat("1e293b", 0.55f);
        var redMat = GetMat("ef4444", 0.40f);
        var amberMat = GetMat("facc15", 0.35f);
        var greenMat = GetMat("22c55e", 0.35f);

        // Ilha central de rotatória inteligente
        AddBox(root, new Vector3(0.46f, 0.14f, 0.46f), new Vector3(0, 0.11f, 0), islandMat, castShadow: false);
        AddBox(root, new Vector3(0.32f, 0.10f, 0.32f), new Vector3(0, 0.21f, 0), grassMat, castShadow: false);

        // Dois pórticos de semáforo nos cantos opostos
        for (int i = 0; i < 2; i++)
        {
            float sx = i == 0 ? 0.68f : -0.68f;
            float sz = i == 0 ? 0.68f : -0.68f;
            AddBox(root, new Vector3(0.08f, 0.72f, 0.08f), new Vector3(sx, 0.38f, sz), poleMat);
            AddBox(root, new Vector3(0.14f, 0.28f, 0.14f), new Vector3(sx, 0.68f, sz), poleMat);
            AddBox(root, new Vector3(0.16f, 0.06f, 0.16f), new Vector3(sx, 0.76f, sz), redMat, castShadow: false);
            AddBox(root, new Vector3(0.16f, 0.06f, 0.16f), new Vector3(sx, 0.68f, sz), amberMat, castShadow: false);
            AddBox(root, new Vector3(0.16f, 0.06f, 0.16f), new Vector3(sx, 0.60f, sz), greenMat, castShadow: false);
        }

        OptimizeStaticChildren(root);
        return root;
    }

    public static Node3D CreateTollStationNode(TollStation station, out Node3D leftGatePivot, out Node3D rightGatePivot, out Label3D weighBoardLabel)
    {
        var root = new Node3D { Name = $"TollStation_{station.Id}" };
        var scalePadMat = GetMat("475569", 0.60f);
        var boothMat = GetMat("f8fafc", 0.75f);
        var roofMat = GetMat("eab308", 0.55f);
        var stripeMat = GetMat("0f172a", 0.60f);
        var ledFrameMat = GetMat("020617", 0.35f, 0.45f);
        var gateArmMat = GetMat("ef4444", 0.50f);
        var gateTipMat = GetMat("f8fafc", 0.50f);

        // Plataforma de balança de pesagem integrada sobre a pista
        AddBox(root, new Vector3(1.50f, 0.08f, 1.50f), new Vector3(0, 0.10f, 0), scalePadMat, castShadow: false);
        // Cabines laterais elevadas para comportar gabarito de caminhões TopLine / Globetrotter / Bi-Trem
        AddBox(root, new Vector3(0.30f, 0.80f, 0.56f), new Vector3(-0.78f, 0.44f, 0), boothMat);
        AddBox(root, new Vector3(0.30f, 0.80f, 0.56f), new Vector3(0.78f, 0.44f, 0), boothMat);
        // Pórtico aéreo amarelo e faixa de advertência (vão livre de 0.86f)
        AddBox(root, new Vector3(1.94f, 0.16f, 0.64f), new Vector3(0, 0.96f, 0), roofMat);
        AddBox(root, new Vector3(1.88f, 0.06f, 0.68f), new Vector3(0, 0.88f, 0), stripeMat, castShadow: false);

        // Painel luminoso 3D da Balança Eletrônica no topo do pórtico
        AddBox(root, new Vector3(1.36f, 0.24f, 0.14f), new Vector3(0, 1.18f, 0), ledFrameMat, castShadow: false);
        weighBoardLabel = CreateFloatingLabel($"⚖️ {station.LastWeighDisplay}", new Color("4ade80"), 1.24f, 24);
        root.AddChild(weighBoardLabel);

        // Cancelas automáticas articuladas (abrem quando caminhões passam pela praça)
        leftGatePivot = new Node3D { Name = "LeftGatePivot", Position = new Vector3(-0.60f, 0.34f, 0.26f) };
        AddBox(leftGatePivot, new Vector3(0.54f, 0.05f, 0.05f), new Vector3(0.27f, 0f, 0f), gateArmMat, castShadow: false);
        AddBox(leftGatePivot, new Vector3(0.18f, 0.055f, 0.055f), new Vector3(0.27f, 0f, 0f), gateTipMat, castShadow: false);
        root.AddChild(leftGatePivot);

        rightGatePivot = new Node3D { Name = "RightGatePivot", Position = new Vector3(0.60f, 0.34f, -0.26f) };
        AddBox(rightGatePivot, new Vector3(0.54f, 0.05f, 0.05f), new Vector3(-0.27f, 0f, 0f), gateArmMat, castShadow: false);
        AddBox(rightGatePivot, new Vector3(0.18f, 0.055f, 0.055f), new Vector3(-0.27f, 0f, 0f), gateTipMat, castShadow: false);
        root.AddChild(rightGatePivot);

        var label = CreateFloatingLabel($"🚧 {station.Name}", new Color("facc15"), 1.68f, 26);
        root.AddChild(label);
        OptimizeStaticChildren(root);
        return root;
    }

    public static Node3D CreateTollStationNode(TollStation station, out Node3D leftGatePivot, out Node3D rightGatePivot)
    {
        return CreateTollStationNode(station, out leftGatePivot, out rightGatePivot, out _);
    }

    public static Node3D CreateTollStationNode(TollStation station)
    {
        return CreateTollStationNode(station, out _, out _, out _);
    }

    public static TruckVisualHandle CreateTruckMeshNode(Truck truck, FleetLivery? liveryOverride = null)
    {
        var root = new Node3D { Name = $"Truck_{truck.Id}" };
        var handle = new TruckVisualHandle
        {
            Root = root,
            Headlights = new Node3D { Name = "Headlights", Visible = false },
            VisualSignature = $"{(liveryOverride?.GetRenderKey() ?? "default")}|{(int)truck.Rank}"
        };

        int specId = truck.Spec.Capacity switch
        {
            <= 15 => 1,  // 1. Utilitário F-4000 Carga Seca
            <= 30 => 2,  // 2. Scania 113H TopLine 360
            <= 55 => 3,  // 3. Mercedes-Benz L-1620 Bicudo
            <= 95 => 4,  // 4. Volvo FH 540 Globetrotter 6x2
            <= 165 => 5, // 5. Peterbilt 389 Bi-Trem King
            _ => 6       // 6. Scania R730 V8 Rodotrem 9 Eixos
        };

        int paintVariant = Math.Abs(truck.Id) % 2;
        bool useCustomLivery = liveryOverride is { IsCustomized: true };
        var (paintMetal, paintRough) = liveryOverride?.Finish switch
        {
            PaintFinish.Matte => (0.06f, 0.90f),
            PaintFinish.Chrome => (0.96f, 0.14f),
            PaintFinish.Metallic when useCustomLivery => (0.68f, 0.30f),
            _ => (0.48f, 0.62f)
        };
        string ResolveCabHex(string defaultHex) => useCustomLivery ? liveryOverride!.PrimaryColorHex : defaultHex;
        string ResolveStripeHex(string defaultHex) => useCustomLivery ? liveryOverride!.StripeColorHex : defaultHex;
        float ResolveCabMetal(float defaultMetal) => useCustomLivery ? paintMetal : defaultMetal;
        float? ResolveCabRough() => useCustomLivery ? paintRough : null;

        if (truck.Spec.IsPassengerBus)
        {
            bool isDoubleDecker = truck.Spec.Capacity > 60;
            var bodyMat = GetMat(ResolveCabHex("#0284c7").TrimStart('#'), ResolveCabRough() ?? 0.40f, ResolveCabMetal(0.45f));
            var stripeMat = GetMat(ResolveStripeHex("#facc15").TrimStart('#'), 0.38f, 0.45f);
            var darkChassisMat = GetMat("11151d", 0.85f, 0.20f);
            var busGlassMat = GetMat("1e3a5f", 0.20f, 0.85f);
            var busTireMat = GetMat("1c1f24", 0.92f, 0.05f);
            var acMat = GetMat("f1f5f9", 0.65f, 0.20f);
            var headMat = GetMat("fef08a", 0.2f, 0.4f, true, 1.6f);

            float bodyH = isDoubleDecker ? 0.52f : 0.36f;
            float bodyCenterY = isDoubleDecker ? 0.36f : 0.28f;
            float busLen = isDoubleDecker ? 1.18f : 1.04f;

            AddBox(root, new Vector3(0.36f, 0.08f, busLen * 0.95f), new Vector3(0f, 0.10f, 0f), darkChassisMat, castShadow: false);
            AddBox(root, new Vector3(0.40f, bodyH, busLen), new Vector3(0f, bodyCenterY, 0f), bodyMat);
            AddBox(root, new Vector3(0.415f, 0.07f, busLen * 0.98f), new Vector3(0f, 0.21f, 0f), stripeMat, castShadow: false);
            AddBox(root, new Vector3(0.41f, 0.13f, busLen * 0.86f), new Vector3(0f, isDoubleDecker ? 0.30f : 0.35f, -0.02f), busGlassMat, castShadow: false);
            if (isDoubleDecker)
            {
                AddBox(root, new Vector3(0.41f, 0.13f, busLen * 0.86f), new Vector3(0f, 0.52f, -0.02f), busGlassMat, castShadow: false);
            }
            AddBox(root, new Vector3(0.24f, 0.05f, 0.34f), new Vector3(0f, bodyCenterY + bodyH * 0.5f + 0.025f, 0.05f), acMat, castShadow: false);
            AddBox(root, new Vector3(0.24f, 0.045f, 0.04f), new Vector3(0f, bodyCenterY + bodyH * 0.38f, -busLen * 0.505f), headMat, castShadow: false);

            float[] wheelZs = isDoubleDecker ? new[] { -0.36f, 0.24f, 0.42f } : new[] { -0.32f, 0.32f };
            foreach (float wz in wheelZs)
            {
                foreach (float wx in new[] { -0.19f, 0.19f })
                {
                    var wp = AddCylinder(root, 0.09f, 0.09f, 0.06f, new Vector3(wx, 0.09f, wz), busTireMat, new Vector3(0, 0, 90), 10, false);
                    handle.Wheels.Add(wp);
                }
            }

            foreach (float hx in new[] { -0.13f, 0.13f })
                AddBox(handle.Headlights, new Vector3(0.06f, 0.05f, 0.05f), new Vector3(hx, 0.16f, -busLen * 0.51f), headMat, castShadow: false);
            root.AddChild(handle.Headlights);

            var busSkip = new HashSet<Node> { handle.Headlights };
            foreach (var w in handle.Wheels) busSkip.Add(w);
            OptimizeStaticChildren(root, busSkip);
            return handle;
        }

        const float S = 0.48f; // Escala de conversão Showcase -> Mundo 3D Godot (TileSize = 2.0f)
        float shiftX = specId == 6 ? 0.44f : (specId == 5 ? 0.32f : (specId == 4 ? 0.14f : 0.0f));
        float? rearHitchX = specId == 6 ? (-1.12f + shiftX) : (specId == 5 ? (-0.93f + shiftX) : null);

        if (rearHitchX.HasValue)
        {
            var trailerPivot = new Node3D
            {
                Name = "RearTrailerPivot",
                Position = new Vector3(0f, 0f, -rearHitchX.Value * S)
            };
            root.AddChild(trailerPivot);
            handle.RearTrailerPivot = trailerPivot;
        }

        (Node3D TargetParent, float LocalCx) ResolveParentAndX(float cx)
        {
            if (rearHitchX.HasValue && handle.RearTrailerPivot != null && cx < rearHitchX.Value - 0.08f)
            {
                return (handle.RearTrailerPivot, cx - rearHitchX.Value);
            }
            return (root, cx);
        }

        // Helpers de mapeamento de coordenadas do Showcase (X = frente, Y = altura, Z = lateral)
        // para o espaço local do caminhão no Godot (-Z = frente do LookAt, +Y = altura, +X = lateral)
        void BoxS(float cx, float cy, float cz, float w, float h, float d, string hex, float metal = 0.25f, float emissive = 0.0f, Vector3? rotDeg = null, bool shadow = true, float? roughOverride = null)
        {
            string cleanHex = hex.TrimStart('#');
            float rough = roughOverride ?? Mathf.Clamp(1.0f - metal * 0.65f, 0.18f, 0.92f);
            var mat = GetMat(cleanHex, rough, metal, emissive > 0.05f, emissive * 1.15f);
            var (parentNode, localCx) = ResolveParentAndX(cx);
            Vector3 size = new Vector3(d * S, h * S, w * S);
            Vector3 pos = new Vector3(cz * S, cy * S, -localCx * S);
            AddBox(parentNode, size, pos, mat, rotDeg, shadow);
        }

        void CylS(float cx, float cy, float cz, float rTop, float rBot, float len, int segs, char axis, string hex, float metal = 0.40f, float emissive = 0.0f, bool shadow = true)
        {
            string cleanHex = hex.TrimStart('#');
            float rough = Mathf.Clamp(1.0f - metal * 0.65f, 0.18f, 0.92f);
            var mat = GetMat(cleanHex, rough, metal, emissive > 0.05f, emissive * 1.15f);
            var (parentNode, localCx) = ResolveParentAndX(cx);
            Vector3 pos = new Vector3(cz * S, cy * S, -localCx * S);
            Vector3? rot = axis switch
            {
                'X' => new Vector3(90f, 0f, 0f), // Longitudinal (frente-trás)
                'Z' => new Vector3(0f, 0f, 90f), // Transversal (lado-a-lado)
                _ => null                        // Vertical (Y)
            };
            AddCylinder(parentNode, rTop * S, rBot * S, len * S, pos, mat, rot, segs, shadow);
        }

        void AddFramedCabinWindows(float cabX, float cabY, float cabW, float cabH, float cabD, string cabHex, float windshieldTiltDeg = 6f)
        {
            float wsH = cabH * 0.38f;
            float wsY = cabY + cabH * 0.18f;
            // Para-brisa Azul-Ardósia Metálico com leve inclinação realista e limpadores duplos
            BoxS(cabX + cabW * 0.5f - 0.02f, wsY, 0f, 0.08f, wsH, cabD * 0.84f, "#1e3a5f", 0.88f, 0f, new Vector3(windshieldTiltDeg, 0f, 0f), false);
            for (int w = -1; w <= 1; w += 2)
            {
                // Limpadores de para-brisa
                BoxS(cabX + cabW * 0.5f + 0.015f, wsY - wsH * 0.32f, w * (cabD * 0.18f), 0.02f, 0.025f, cabD * 0.28f, "#0f172a", 0.4f, 0f, new Vector3(0f, 0f, -10f), false);
            }

            foreach (int s in new[] { -1, 1 })
            {
                // Colunas A na cor da carroceria
                BoxS(cabX + cabW * 0.5f - 0.02f, wsY, s * (cabD * 0.5f - 0.03f), 0.09f, wsH + 0.04f, 0.07f, cabHex, 0.45f, 0f, new Vector3(windshieldTiltDeg * 0.7f, 0f, 0f), false);
                // Janelas laterais embutidas
                BoxS(cabX + cabW * 0.06f, wsY, s * (cabD * 0.5f - 0.015f), cabW * 0.62f, wsH * 0.92f, 0.06f, "#1e3a5f", 0.88f, 0f, null, false);
                // Maçanetas cromadas das portas
                BoxS(cabX - cabW * 0.12f, cabY - cabH * 0.08f, s * (cabD * 0.5f + 0.01f), 0.10f, 0.03f, 0.03f, "#f8fafc", 0.95f, 0f, null, false);
            }
        }

        void AddSolidCargoBed(float cx, float fy, float lenX, float widZ, float stakeH, bool hasRearLights, Vector3 cargoScale)
        {
            // Sub-chassi e assoalho naval maciço
            BoxS(cx, fy - 0.08f, 0f, lenX - 0.04f, 0.16f, widZ * 0.62f, "#11151d", 0.30f);
            BoxS(cx, fy, 0f, lenX, 0.09f, widZ, "#78350f", 0.20f);
            // Painel frontal (malhal/guarda-cabeça)
            BoxS(cx + lenX * 0.5f - 0.04f, fy + (stakeH + 0.18f) * 0.5f, 0f, 0.08f, stakeH + 0.18f, widZ, "#334155", 0.40f);

            float rearX = cx - lenX * 0.5f;
            BoxS(rearX + 0.04f, fy + (stakeH * 0.92f) * 0.5f, 0f, 0.08f, stakeH * 0.92f, widZ, "#334155", 0.40f);

            if (hasRearLights)
            {
                // Para-choque traseiro homologado CONTRAN (faixas vermelho/branco) + placa Mercosul
                BoxS(rearX + 0.02f, fy - 0.19f, 0f, 0.06f, 0.09f, widZ * 0.94f, "#f8fafc", 0.90f, 0f, null, false);
                foreach (float rz in new[] { -widZ * 0.30f, 0f, widZ * 0.30f })
                {
                    BoxS(rearX + 0.015f, fy - 0.19f, rz - 0.04f, 0.065f, 0.038f, 0.12f, "#ef4444", 0.40f, 0.85f, null, false);
                    BoxS(rearX + 0.015f, fy - 0.19f, rz + 0.06f, 0.065f, 0.038f, 0.08f, "#f8fafc", 0.80f, 0.75f, null, false);
                }

                // Lanternas Traseiras Triplas Embutidas (Freio Vermelho + Seta Âmbar + Ré Branca)
                foreach (int s in new[] { -1, 1 })
                {
                    BoxS(rearX + 0.04f, fy - 0.13f, s * (widZ * 0.36f), 0.05f, 0.12f, 0.05f, "#11151d", 0.30f, 0f, null, false);
                    BoxS(rearX + 0.015f, fy - 0.095f, s * (widZ * 0.33f), 0.05f, 0.095f, 0.24f, "#0d0f12", 0.30f, 0f, null, false);
                    BoxS(rearX + 0.008f, fy - 0.095f, s * (widZ * 0.33f + 0.062f), 0.056f, 0.068f, 0.082f, "#ef4444", 0.50f, 1.0f, null, false);
                    BoxS(rearX + 0.008f, fy - 0.095f, s * (widZ * 0.33f - 0.014f), 0.056f, 0.068f, 0.058f, "#f59e0b", 0.50f, 0.95f, null, false);
                    BoxS(rearX + 0.008f, fy - 0.095f, s * (widZ * 0.33f - 0.072f), 0.056f, 0.068f, 0.046f, "#f8fafc", 0.60f, 0.90f, null, false);
                    BoxS(rearX + 0.008f, fy + stakeH * 0.82f, s * (widZ * 0.44f), 0.04f, 0.032f, 0.05f, "#ef4444", 0.50f, 1.0f, null, false);
                }
            }

            // Guardas laterais em madeira naval, frisos metálicos, protetor ciclista e fueiros verticais
            foreach (int s in new[] { -1, 1 })
            {
                float pz = s * (widZ * 0.5f - 0.03f);
                BoxS(cx, fy + stakeH * 0.5f, pz, lenX - 0.04f, stakeH, 0.055f, "#92400e", 0.25f);
                BoxS(cx, fy + stakeH, pz, lenX, 0.045f, 0.068f, "#334155", 0.45f, 0f, null, false);
                BoxS(cx, fy + stakeH * 0.48f, pz, lenX, 0.032f, 0.065f, "#334155", 0.45f, 0f, null, false);
                BoxS(cx + lenX * 0.18f, fy - 0.08f, s * (widZ * 0.5f - 0.04f), lenX * 0.42f, 0.12f, 0.04f, "#334155", 0.40f, 0f, null, false);

                int num = Math.Max(4, (int)MathF.Floor(lenX / 0.28f));
                float step = (lenX - 0.12f) / (num - 1);
                for (int i = 0; i < num; i++)
                {
                    float sx = (cx - lenX * 0.5f + 0.06f) + i * step;
                    BoxS(sx, fy + stakeH * 0.5f, pz, 0.045f, stakeH + 0.04f, 0.072f, "#334155", 0.45f, 0f, null, false);
                }
            }

            // Nó de ancoragem para a carga 3D dinâmica desta caçamba
            var (bedParent, bedLocalCx) = ResolveParentAndX(cx);
            var cargoBedNode = new Node3D
            {
                Name = $"CargoBed_{handle.CargoBeds.Count}",
                Position = new Vector3(0f, (fy + 0.02f) * S, -bedLocalCx * S)
            };
            bedParent.AddChild(cargoBedNode);
            handle.CargoBeds.Add((cargoBedNode, cargoScale));
        }

        var wheels = new List<(float X, float Z, bool IsSteer, bool IsDual)>();
        float wheelScale = 1.0f;
        float hlX = 1.35f, hlY = 0.42f, hlZ = 0.28f;
        float? fogY = null, fogZ = null, roofLightY = null;
        int roofLightCount = 0;
        float roofBadgeX = 0.44f, roofBadgeY = 1.04f;

        if (specId == 1)
        {
            // 🚐 1. FORD F-4000 CARGA SECA (4x2 Rodado Duplo)
            string cabHex = ResolveCabHex(paintVariant == 0 ? "#1d4ed8" : "#f8fafc");
            float cabMetal = ResolveCabMetal(0.48f);
            float? cabRough = ResolveCabRough();
            string stripeHex = ResolveStripeHex("#facc15");
            BoxS(0.06f + shiftX, 0.34f, 0f, 2.12f, 0.24f, 0.68f, "#11151d", 0.30f);
            BoxS(0.44f + shiftX, 0.68f, 0f, 0.66f, 0.68f, 0.78f, cabHex, cabMetal, 0f, null, true, cabRough);
            AddFramedCabinWindows(0.44f + shiftX, 0.68f, 0.66f, 0.68f, 0.78f, cabHex, 10f);

            // Capô Série F com inclinação frontal sutil e Power Dome esculpido
            BoxS(0.96f + shiftX, 0.55f, 0f, 0.50f, 0.40f, 0.76f, cabHex, cabMetal, 0f, new Vector3(-3.5f, 0f, 0f), true, cabRough);
            BoxS(0.95f + shiftX, 0.77f, 0f, 0.46f, 0.06f, 0.44f, cabHex, cabMetal, 0f, new Vector3(-3.5f, 0f, 0f), true, cabRough);
            if (useCustomLivery)
            {
                foreach (int s in new[] { -1, 1 })
                    BoxS(0.68f + shiftX, 0.52f, s * 0.40f, 0.92f, 0.055f, 0.025f, stripeHex, 0.55f, 0.18f, null, false);
            }
            // Grade cromada dupla Ford Série F + Emblema Oval Azul Central
            BoxS(1.20f + shiftX, 0.56f, 0f, 0.06f, 0.28f, 0.68f, "#11151d", 0.40f);
            BoxS(1.21f + shiftX, 0.56f, 0f, 0.08f, 0.26f, 0.62f, "#f8fafc", 0.95f);
            BoxS(1.225f + shiftX, 0.56f, 0f, 0.04f, 0.04f, 0.58f, "#11151d", 0.40f, 0f, null, false);
            BoxS(1.23f + shiftX, 0.56f, 0f, 0.04f, 0.08f, 0.14f, "#1d4ed8", 0.75f, 0.2f, null, false);
            // Para-choque cromado pesado
            BoxS(1.22f + shiftX, 0.35f, 0f, 0.14f, 0.15f, 0.82f, "#f8fafc", 0.95f);

            foreach (int s in new[] { -1, 1 })
            {
                BoxS(1.22f + shiftX, 0.58f, s * 0.26f, 0.09f, 0.12f, 0.14f, "#fef08a", 0.50f, 1.0f, null, false);
                BoxS(1.22f + shiftX, 0.48f, s * 0.26f, 0.09f, 0.05f, 0.14f, "#facc15", 0.50f, 0.90f, null, false);
                // Estribos e retrovisores cromados de alça dupla
                BoxS(0.44f + shiftX, 0.32f, s * 0.39f, 0.58f, 0.12f, 0.10f, "#f8fafc", 0.90f, 0f, null, false);
                BoxS(0.66f + shiftX, 0.80f, s * 0.43f, 0.07f, 0.18f, 0.05f, "#f8fafc", 0.90f, 0f, null, false);
            }

            // Giroflex âmbar de serviço no teto da F-4000
            CylS(0.44f + shiftX, 1.04f, 0f, 0.11f, 0.13f, 0.05f, 12, 'Y', "#11151d", 0.30f, 0f, false);
            handle.BeaconLens = AddCylinder(
                root, 0.08f * S, 0.09f * S, 0.11f * S,
                new Vector3(0f, 1.11f * S, -(0.44f + shiftX) * S),
                GetMat("fde047", 0.25f, 0.3f, true, 1.4f),
                null, 12, false
            );

            AddSolidCargoBed(-0.38f + shiftX, 0.45f, 1.12f, 0.80f, 0.25f, true, new Vector3(0.72f, 0.78f, 0.85f));
            wheelScale = 0.88f;
            wheels.AddRange(new[]
            {
                (0.84f, 0.39f, true, false), (0.84f, -0.39f, true, false),
                (-0.55f, 0.39f, false, true), (-0.55f, -0.39f, false, true)
            });
            hlX = 1.25f; hlY = 0.58f; hlZ = 0.26f;
            roofBadgeX = 0.28f; roofBadgeY = 1.03f;
        }
        else if (specId == 2)
        {
            // 🚚 2. SCANIA 113H 360 TOPLINE BICUDO (Série 3)
            string cabHex = ResolveCabHex(paintVariant == 0 ? "#38bdf8" : "#f8fafc");
            float cabMetal = ResolveCabMetal(0.45f);
            float? cabRough = ResolveCabRough();
            BoxS(-0.02f + shiftX, 0.36f, 0f, 2.62f, 0.26f, 0.74f, "#11151d", 0.30f);
            BoxS(0.50f + shiftX, 0.78f, 0f, 0.70f, 0.84f, 0.82f, cabHex, cabMetal, 0f, null, true, cabRough);
            // Teto alto TopLine aerodinâmico com defletor inclinado
            BoxS(0.47f + shiftX, 1.29f, 0f, 0.64f, 0.22f, 0.78f, cabHex, cabMetal, 0f, new Vector3(5f, 0f, 0f), true, cabRough);
            AddFramedCabinWindows(0.50f + shiftX, 0.78f, 0.70f, 0.84f, 0.82f, cabHex, 8f);

            // Capô bicudo Série 3 com inclinação frontal autêntica
            BoxS(1.09f + shiftX, 0.64f, 0f, 0.60f, 0.55f, 0.72f, cabHex, cabMetal, 0f, new Vector3(-5.5f, 0f, 0f), true, cabRough);
            BoxS(1.06f + shiftX, 0.42f, 0f, 0.56f, 0.32f, 0.66f, "#11151d", 0.30f);
            // Grade frontal Série 3 com 5 frisos horizontais + emblema SCANIA 113H 360
            BoxS(1.37f + shiftX, 0.64f, 0f, 0.06f, 0.46f, 0.56f, "#11151d", 0.30f);
            foreach (float gy in new[] { 0.48f, 0.56f, 0.64f, 0.72f, 0.80f })
            {
                BoxS(1.385f + shiftX, gy, 0f, 0.08f, 0.032f, 0.52f, cabHex, cabMetal, 0f, null, false, cabRough);
            }
            BoxS(1.395f + shiftX, 0.83f, 0f, 0.04f, 0.035f, 0.26f, "#f8fafc", 0.95f, 0.2f, null, false);
            BoxS(1.38f + shiftX, 0.35f, 0f, 0.16f, 0.20f, 0.86f, cabHex, cabMetal, 0f, null, true, cabRough);
            BoxS(0.84f + shiftX, 1.18f, 0f, 0.16f, 0.08f, 0.84f, cabHex, cabMetal, 0f, new Vector3(-12f, 0f, 0f), true, cabRough);
            // Chaminé cromada vertical + filtro de ar ciclônico lateral preto
            CylS(0.12f + shiftX, 1.06f, -0.41f, 0.05f, 0.05f, 1.26f, 14, 'Y', "#f8fafc", 0.95f);
            CylS(0.82f + shiftX, 0.72f, -0.44f, 0.09f, 0.09f, 0.34f, 14, 'Y', "#11151d", 0.40f);

            // Três Marias âmbar no topo do TopLine
            foreach (float lz in new[] { -0.22f, 0f, 0.22f })
            {
                BoxS(0.76f + shiftX, 1.40f, lz, 0.07f, 0.06f, 0.06f, "#facc15", 0.50f, 1.0f, null, false);
            }

            foreach (int s in new[] { -1, 1 })
            {
                BoxS(1.06f + shiftX, 0.54f, s * 0.39f, 0.58f, 0.10f, 0.14f, cabHex, cabMetal, 0f, null, true, cabRough);
                BoxS(0.62f + shiftX, 0.36f, s * 0.39f, 0.36f, 0.20f, 0.12f, cabHex, cabMetal, 0f, null, true, cabRough);
                CylS(0.18f + shiftX, 0.35f, s * 0.39f, 0.14f, 0.14f, 0.56f, 14, 'X', "#f8fafc", 0.92f);
                // As 3 faixas clássicas anos 90 (ou cor da transportadora quando personalizada)
                BoxS(0.76f + shiftX, 0.69f, s * 0.42f, 1.20f, 0.045f, 0.025f, ResolveStripeHex("#ec4899"), 0.30f, 0.35f, null, false);
                BoxS(0.76f + shiftX, 0.635f, s * 0.42f, 1.20f, 0.045f, 0.025f, useCustomLivery ? cabHex : "#7c3aed", 0.30f, 0.20f, null, false);
                BoxS(0.76f + shiftX, 0.58f, s * 0.42f, 1.20f, 0.045f, 0.025f, ResolveStripeHex("#1e3a8a"), 0.30f, 0.35f, null, false);
                // Faróis principais e milhas de neblina âmbar no para-choque
                BoxS(1.40f + shiftX, 0.38f, s * 0.28f, 0.08f, 0.11f, 0.16f, "#fef08a", 0.50f, 1.0f, null, false);
                BoxS(1.40f + shiftX, 0.29f, s * 0.16f, 0.08f, 0.07f, 0.10f, "#facc15", 0.50f, 0.95f, null, false);
                BoxS(0.74f + shiftX, 0.94f, s * 0.46f, 0.08f, 0.24f, 0.05f, "#11151d", 0.40f, 0f, null, false);
            }

            AddSolidCargoBed(-0.52f + shiftX, 0.47f, 1.54f, 0.86f, 0.29f, true, new Vector3(0.96f, 0.95f, 0.96f));
            wheelScale = 1.0f;
            wheels.AddRange(new[]
            {
                (1.06f, 0.43f, true, false), (1.06f, -0.43f, true, false),
                (-0.82f, 0.43f, false, true), (-0.82f, -0.43f, false, true)
            });
            hlX = 1.43f; hlY = 0.38f; hlZ = 0.28f; fogY = 0.29f; fogZ = 0.16f;
            roofBadgeX = 0.42f; roofBadgeY = 1.41f;
        }
        else if (specId == 3)
        {
            // 🚛 3. MERCEDES-BENZ L-1620 BICUDO CLÁSSICO ("Rei da Estrada")
            string cabHex = ResolveCabHex(paintVariant == 0 ? "#15803d" : "#1d4ed8");
            float cabMetal = ResolveCabMetal(0.45f);
            float? cabRough = ResolveCabRough();
            BoxS(-0.04f + shiftX, 0.36f, 0f, 2.70f, 0.26f, 0.74f, "#11151d", 0.30f);
            BoxS(0.54f + shiftX, 0.78f, 0f, 0.68f, 0.84f, 0.82f, cabHex, cabMetal, 0f, null, true, cabRough);
            AddFramedCabinWindows(0.54f + shiftX, 0.78f, 0.68f, 0.84f, 0.82f, cabHex, 9f);

            // Capô arredondado e inclinado característico do MB L-1620
            BoxS(1.06f + shiftX, 0.59f, 0f, 0.56f, 0.46f, 0.72f, cabHex, cabMetal, 0f, new Vector3(-8.5f, 0f, 0f), true, cabRough);
            BoxS(1.02f + shiftX, 0.83f, 0f, 0.48f, 0.11f, 0.64f, cabHex, cabMetal, 0f, new Vector3(-11f, 0f, 0f), true, cabRough);
            BoxS(1.04f + shiftX, 0.42f, 0f, 0.54f, 0.30f, 0.68f, "#11151d", 0.30f);
            // Grade preta com barra horizontal cromada e Estrela Mercedes-Benz central
            BoxS(1.33f + shiftX, 0.60f, 0f, 0.08f, 0.34f, 0.56f, "#11151d", 0.35f, 0f, new Vector3(-5f, 0f, 0f));
            BoxS(1.345f + shiftX, 0.60f, 0f, 0.09f, 0.04f, 0.54f, "#f8fafc", 0.95f, 0f, null, false);
            CylS(1.355f + shiftX, 0.60f, 0f, 0.065f, 0.065f, 0.04f, 14, 'X', "#f8fafc", 0.95f, 0.2f, false);
            BoxS(1.35f + shiftX, 0.36f, 0f, 0.14f, 0.18f, 0.86f, "#f8fafc", 0.95f);
            BoxS(0.86f + shiftX, 1.16f, 0f, 0.18f, 0.07f, 0.84f, "#f8fafc", 0.95f, 0f, new Vector3(-10f, 0f, 0f));
            CylS(0.18f + shiftX, 1.05f, 0.40f, 0.045f, 0.045f, 1.18f, 12, 'Y', "#f8fafc", 0.95f);

            foreach (int s in new[] { -1, 1 })
            {
                // Para-lamas abaulados + faixa lateral 1620
                BoxS(1.02f + shiftX, 0.53f, s * 0.39f, 0.54f, 0.11f, 0.15f, cabHex, cabMetal, 0f, null, true, cabRough);
                BoxS(0.76f + shiftX, 0.64f, s * 0.42f, 1.14f, 0.06f, 0.025f, ResolveStripeHex("#f8fafc"), 0.55f, 0.2f, null, false);
                CylS(0.16f + shiftX, 0.35f, s * 0.39f, 0.14f, 0.14f, 0.62f, 14, 'X', "#11151d", 0.45f);
                // 2 cornetas pneumáticas cromadas no teto
                CylS(0.56f + shiftX, 1.24f, s * 0.22f, 0.035f, 0.06f, 0.38f, 12, 'X', "#f8fafc", 0.95f, 0f, false);
                BoxS(1.36f + shiftX, 0.38f, s * 0.28f, 0.08f, 0.12f, 0.16f, "#fef08a", 0.50f, 1.0f, null, false);
                BoxS(1.36f + shiftX, 0.38f, s * 0.39f, 0.08f, 0.06f, 0.10f, "#f59e0b", 0.50f, 0.90f, null, false);
                BoxS(0.74f + shiftX, 0.92f, s * 0.46f, 0.07f, 0.22f, 0.05f, "#11151d", 0.40f, 0f, null, false);
            }

            AddSolidCargoBed(-0.56f + shiftX, 0.47f, 1.60f, 0.88f, 0.30f, true, new Vector3(1.08f, 1.0f, 1.0f));
            wheelScale = 1.02f;
            wheels.AddRange(new[]
            {
                (1.02f, 0.44f, true, false), (1.02f, -0.44f, true, false),
                (-0.88f, 0.44f, false, true), (-0.88f, -0.44f, false, true)
            });
            hlX = 1.39f; hlY = 0.38f; hlZ = 0.28f;
            roofBadgeX = 0.44f; roofBadgeY = 1.21f;
        }
        else if (specId == 4)
        {
            // 🚜 4. VOLVO FH 540 GLOBETROTTER 6x2 (Trucado Cara-Chata)
            string cabHex = ResolveCabHex(paintVariant == 0 ? "#ea580c" : "#dc2626");
            float cabMetal = ResolveCabMetal(0.48f);
            float? cabRough = ResolveCabRough();
            BoxS(-0.12f + shiftX, 0.37f, 0f, 3.08f, 0.28f, 0.76f, "#11151d", 0.30f);
            BoxS(0.78f + shiftX, 0.80f, 0f, 0.98f, 0.90f, 0.88f, cabHex, cabMetal, 0f, null, true, cabRough);
            // Teto alto aerodinâmico Globetrotter + Testeira Iluminada GLOBETROTTER
            BoxS(0.74f + shiftX, 1.40f, 0f, 0.90f, 0.36f, 0.84f, cabHex, cabMetal, 0f, new Vector3(6f, 0f, 0f), true, cabRough);
            BoxS(1.19f + shiftX, 1.42f, 0f, 0.06f, 0.14f, 0.62f, ResolveStripeHex("#f8fafc"), 0.40f, 0.95f, null, false);
            AddFramedCabinWindows(0.78f + shiftX, 0.84f, 0.98f, 0.84f, 0.88f, cabHex, 8f);

            // Barra cromada no teto com 4 faróis de milha LED
            BoxS(1.21f + shiftX, 1.56f, 0f, 0.08f, 0.05f, 0.76f, "#f8fafc", 0.95f, 0f, null, false);
            foreach (float lz in new[] { -0.27f, -0.09f, 0.09f, 0.27f })
            {
                BoxS(1.24f + shiftX, 1.56f, lz, 0.08f, 0.08f, 0.08f, "#fef08a", 0.50f, 1.0f, null, false);
            }
            // Grade Volvo FH preta/cromada + Barra Diagonal Iron Mark
            BoxS(1.26f + shiftX, 0.58f, 0f, 0.08f, 0.52f, 0.70f, "#11151d", 0.35f);
            BoxS(1.27f + shiftX, 0.58f, 0f, 0.09f, 0.44f, 0.60f, "#334155", 0.75f);
            BoxS(1.285f + shiftX, 0.58f, 0f, 0.03f, 0.045f, 0.62f, "#f8fafc", 0.98f, 0.2f, new Vector3(0f, 0f, 32f), false);
            BoxS(1.29f + shiftX, 0.58f, 0f, 0.04f, 0.09f, 0.09f, "#0284c7", 0.85f, 0.3f, null, false);
            BoxS(1.26f + shiftX, 0.34f, 0f, 0.14f, 0.22f, 0.88f, cabHex, cabMetal, 0f, null, true, cabRough);

            foreach (int s in new[] { -1, 1 })
            {
                CylS(0.24f + shiftX, 1.15f, s * 0.41f, 0.05f, 0.05f, 1.38f, 12, 'Y', "#f8fafc", 0.95f);
                // Faróis principais com assinatura DRL em "V" do Volvo FH
                BoxS(1.28f + shiftX, 0.42f, s * 0.34f, 0.09f, 0.20f, 0.16f, "#fef08a", 0.50f, 1.0f, null, false);
                // Saias laterais aerodinâmicas integrais (Full Side-Skirts) + friso/faixa
                BoxS(0.05f + shiftX, 0.38f, s * 0.42f, 1.18f, 0.28f, 0.08f, cabHex, cabMetal, 0f, null, true, cabRough);
                BoxS(0.05f + shiftX, 0.38f, s * 0.43f, 1.16f, 0.04f, 0.09f, ResolveStripeHex("#f8fafc"), 0.85f, useCustomLivery ? 0.2f : 0f, null, false);
                if (useCustomLivery)
                    BoxS(0.76f + shiftX, 0.62f, s * 0.45f, 0.92f, 0.055f, 0.025f, ResolveStripeHex("#facc15"), 0.55f, 0.2f, null, false);
                BoxS(1.12f + shiftX, 0.98f, s * 0.48f, 0.08f, 0.28f, 0.06f, "#11151d", 0.40f, 0f, null, false);
            }

            AddSolidCargoBed(-0.68f + shiftX, 0.49f, 1.90f, 0.92f, 0.34f, true, new Vector3(1.30f, 1.12f, 1.06f));
            wheelScale = 1.05f;
            wheels.AddRange(new[]
            {
                (0.92f, 0.46f, true, false), (0.92f, -0.46f, true, false),
                (-0.82f, 0.46f, false, true), (-0.82f, -0.46f, false, true),
                (-1.30f, 0.46f, false, true), (-1.30f, -0.46f, false, true)
            });
            hlX = 1.31f; hlY = 0.42f; hlZ = 0.34f; roofLightY = 1.56f; roofLightCount = 4;
            roofBadgeX = 0.66f; roofBadgeY = 1.59f;
        }
        else if (specId == 5)
        {
            // 🏁 5. PETERBILT 389 / KENWORTH W900 BI-TREM KING (5 Eixos / 18 Pneus)
            string cabHex = ResolveCabHex(paintVariant == 0 ? "#18181b" : "#1e1b4b");
            float cabMetal = ResolveCabMetal(0.52f);
            float? cabRough = ResolveCabRough();
            BoxS(-0.48f + shiftX, 0.37f, 0f, 4.08f, 0.28f, 0.76f, "#11151d", 0.30f);
            BoxS(0.88f + shiftX, 0.84f, 0f, 0.92f, 0.92f, 0.90f, cabHex, cabMetal, 0f, null, true, cabRough);
            BoxS(0.78f + shiftX, 1.43f, 0f, 0.78f, 0.34f, 0.86f, cabHex, cabMetal, 0f, null, true, cabRough);
            // Faixa lateral Imperial ao longo da cabine-leito e capô longo
            BoxS(1.14f + shiftX, 0.68f, 0f, 1.48f, 0.07f, 0.91f, ResolveStripeHex("#facc15"), 0.75f, 0.25f, null, false);
            AddFramedCabinWindows(0.92f + shiftX, 0.86f, 0.84f, 0.86f, 0.90f, cabHex, 5f);

            // Capô longo americano + grade vertical cromada + Bullbar pesado + Emblema Oval Vermelho Peterbilt
            BoxS(1.48f + shiftX, 0.68f, 0f, 0.56f, 0.62f, 0.76f, cabHex, cabMetal, 0f, null, true, cabRough);
            BoxS(1.44f + shiftX, 0.44f, 0f, 0.54f, 0.34f, 0.72f, "#11151d", 0.30f);
            BoxS(1.75f + shiftX, 0.68f, 0f, 0.10f, 0.58f, 0.56f, "#f8fafc", 0.95f);
            BoxS(1.805f + shiftX, 0.94f, 0f, 0.04f, 0.06f, 0.14f, "#dc2626", 0.75f, 0.3f, null, false);
            BoxS(1.79f + shiftX, 0.46f, 0f, 0.16f, 0.38f, 0.90f, "#f8fafc", 0.95f);
            BoxS(1.30f + shiftX, 1.24f, 0f, 0.20f, 0.08f, 0.90f, "#f8fafc", 0.95f, 0f, new Vector3(-8f, 0f, 0f));

            // 5 luzes de teto âmbar clássicas norte-americanas
            foreach (float lz in new[] { -0.30f, -0.15f, 0f, 0.15f, 0.30f })
            {
                BoxS(1.16f + shiftX, 1.61f, lz, 0.08f, 0.07f, 0.07f, "#facc15", 0.50f, 1.0f, null, false);
            }
            foreach (int s in new[] { -1, 1 })
            {
                BoxS(1.42f + shiftX, 0.54f, s * 0.40f, 0.54f, 0.12f, 0.16f, cabHex, cabMetal, 0f, null, true, cabRough);
                // Chaminés duplas cromadas de grande diâmetro e tanques duplos de inox polido
                CylS(0.46f + shiftX, 1.24f, s * 0.45f, 0.065f, 0.065f, 1.60f, 14, 'Y', "#f8fafc", 0.95f);
                CylS(0.86f + shiftX, 0.36f, s * 0.43f, 0.16f, 0.16f, 0.82f, 14, 'X', "#f8fafc", 0.95f);
                // Faróis duplos em casulos cromados salientes
                BoxS(1.72f + shiftX, 0.56f, s * 0.34f, 0.12f, 0.16f, 0.16f, "#f8fafc", 0.95f, 0f, null, false);
                BoxS(1.76f + shiftX, 0.56f, s * 0.34f, 0.08f, 0.12f, 0.12f, "#fef08a", 0.50f, 1.0f, null, false);
            }

            // Quinta-roda / Dolly de acoplamento e 2 semirreboques graneleiros Guerra
            CylS(-0.93f + shiftX, 0.44f, 0f, 0.28f, 0.28f, 0.16f, 16, 'Y', "#f8fafc", 0.90f, 0f, false);
            AddSolidCargoBed(-0.18f + shiftX, 0.50f, 1.38f, 0.92f, 0.36f, false, new Vector3(0.92f, 1.12f, 1.05f));
            AddSolidCargoBed(-1.68f + shiftX, 0.50f, 1.38f, 0.92f, 0.36f, true, new Vector3(0.92f, 1.12f, 1.05f));

            wheelScale = 1.05f;
            wheels.AddRange(new[]
            {
                (1.42f, 0.46f, true, false), (1.42f, -0.46f, true, false),
                (0.42f, 0.46f, false, true), (0.42f, -0.46f, false, true),
                (-0.58f, 0.46f, false, true), (-0.58f, -0.46f, false, true),
                (-1.72f, 0.46f, false, true), (-1.72f, -0.46f, false, true),
                (-2.18f, 0.46f, false, true), (-2.18f, -0.46f, false, true)
            });
            hlX = 1.79f; hlY = 0.56f; hlZ = 0.34f;
            roofBadgeX = 0.72f; roofBadgeY = 1.61f;
        }
        else
        {
            // 👑 6. SCANIA R730 V8 RODOTREM 9 EIXOS (Gigante Exclusivo Nível 5 / Fase 5)
            string cabHex = ResolveCabHex(paintVariant == 0 ? "#0f172a" : "#1e3a8a");
            float cabMetal = ResolveCabMetal(0.55f);
            float? cabRough = ResolveCabRough();
            // Longarina estendida para cavalo 6x4 + 1º Semirreboque + Dolly + 2º Semirreboque
            BoxS(-0.78f + shiftX, 0.37f, 0f, 4.92f, 0.28f, 0.78f, "#11151d", 0.30f);

            // Cabine R730 V8 Topline Imperial com detalhes em Vermelho Griffin & Ouro V8 (ou Livery personalizada)
            BoxS(1.16f + shiftX, 0.82f, 0f, 0.96f, 0.92f, 0.90f, cabHex, cabMetal, 0f, null, true, cabRough);
            BoxS(1.12f + shiftX, 1.42f, 0f, 0.88f, 0.36f, 0.86f, cabHex, cabMetal, 0f, new Vector3(6f, 0f, 0f), true, cabRough);
            BoxS(1.56f + shiftX, 1.44f, 0f, 0.06f, 0.14f, 0.64f, ResolveStripeHex("#facc15"), 0.50f, 0.95f, null, false);
            AddFramedCabinWindows(1.16f + shiftX, 0.86f, 0.96f, 0.86f, 0.90f, cabHex, 7f);

            // Grade Scania R730 em "V" profunda com frisos cromados e emblema V8 730 vermelho/dourado
            BoxS(1.64f + shiftX, 0.60f, 0f, 0.08f, 0.58f, 0.72f, "#090d16", 0.40f);
            foreach (float gy in new[] { 0.40f, 0.50f, 0.60f, 0.70f, 0.80f })
            {
                BoxS(1.66f + shiftX, gy, 0f, 0.08f, 0.032f, 0.60f, "#f8fafc", 0.95f, 0f, null, false);
            }
            BoxS(1.675f + shiftX, 0.50f, 0.18f, 0.04f, 0.07f, 0.12f, "#dc2626", 0.80f, 0.6f, null, false);
            BoxS(1.64f + shiftX, 0.34f, 0f, 0.16f, 0.22f, 0.90f, cabHex, cabMetal, 0f, null, true, cabRough);

            // Barra de 6 faróis de milha LED no teto Topline
            BoxS(1.58f + shiftX, 1.58f, 0f, 0.08f, 0.05f, 0.80f, "#f8fafc", 0.95f, 0f, null, false);
            foreach (float lz in new[] { -0.32f, -0.19f, -0.06f, 0.06f, 0.19f, 0.32f })
            {
                BoxS(1.61f + shiftX, 1.58f, lz, 0.08f, 0.08f, 0.07f, "#fef08a", 0.50f, 1.0f, null, false);
            }

            foreach (int s in new[] { -1, 1 })
            {
                // Chaminés V8 cromadas duplas + Saias V8 com frisos Vermelho Griffin e Ouro (ou Livery personalizada)
                CylS(0.62f + shiftX, 1.22f, s * 0.44f, 0.06f, 0.06f, 1.50f, 12, 'Y', "#f8fafc", 0.95f);
                BoxS(0.66f + shiftX, 0.38f, s * 0.43f, 0.92f, 0.28f, 0.08f, cabHex, cabMetal, 0f, null, true, cabRough);
                BoxS(0.95f + shiftX, 0.66f, s * 0.46f, 1.15f, 0.05f, 0.025f, ResolveStripeHex("#dc2626"), 0.50f, 0.4f, null, false);
                BoxS(0.95f + shiftX, 0.60f, s * 0.46f, 1.15f, 0.035f, 0.025f, ResolveStripeHex("#facc15"), 0.80f, 0.4f, null, false);
                BoxS(1.66f + shiftX, 0.42f, s * 0.35f, 0.09f, 0.18f, 0.16f, "#fef08a", 0.50f, 1.0f, null, false);
                BoxS(1.66f + shiftX, 0.29f, s * 0.22f, 0.09f, 0.07f, 0.12f, "#facc15", 0.50f, 0.95f, null, false);
                BoxS(1.48f + shiftX, 0.98f, s * 0.49f, 0.08f, 0.28f, 0.06f, "#11151d", 0.40f, 0f, null, false);
            }

            // Dolly intermediário articulado (rala + cambão) + 2 Semirreboques de alta capacidade
            CylS(-1.12f + shiftX, 0.44f, 0f, 0.30f, 0.30f, 0.16f, 16, 'Y', "#f8fafc", 0.90f, 0f, false);
            BoxS(-1.12f + shiftX, 0.36f, 0f, 0.64f, 0.10f, 0.36f, ResolveStripeHex("#dc2626"), 0.50f, 0f, null, false);
            AddSolidCargoBed(-0.24f + shiftX, 0.50f, 1.56f, 0.94f, 0.38f, false, new Vector3(1.06f, 1.18f, 1.08f));
            AddSolidCargoBed(-2.14f + shiftX, 0.50f, 1.56f, 0.94f, 0.38f, true, new Vector3(1.06f, 1.18f, 1.08f));

            // 9 Eixos Reais: 1 direcional + 2 tração cavalo (6x4) + 2 no 1º reboque + 2 no Dolly + 2 no 2º reboque
            wheelScale = 1.04f;
            float[] axleXs = { 1.32f, 0.52f, 0.12f, -0.52f, -0.88f, -1.24f, -1.58f, -2.38f, -2.76f };
            for (int a = 0; a < axleXs.Length; a++)
            {
                bool steer = a == 0;
                wheels.Add((axleXs[a], 0.46f, steer, !steer));
                wheels.Add((axleXs[a], -0.46f, steer, !steer));
            }
            hlX = 1.67f; hlY = 0.42f; hlZ = 0.35f; fogY = 0.29f; fogZ = 0.22f; roofLightY = 1.58f; roofLightCount = 6;
            roofBadgeX = 1.02f; roofBadgeY = 1.60f;
        }

        // Insígnia Dourada de Veterania no Teto (★★ Veterano / ★★★ Lenda da Estrada)
        if (truck.Rank >= TruckRank.Veteran)
        {
            float bx = roofBadgeX + shiftX;
            BoxS(bx, roofBadgeY + 0.02f, 0f, 0.20f, 0.035f, 0.42f, "#0f172a", 0.55f, 0f, null, false);
            if (truck.Rank == TruckRank.Veteran)
            {
                foreach (float bz in new[] { -0.10f, 0.10f })
                {
                    BoxS(bx, roofBadgeY + 0.055f, bz, 0.12f, 0.045f, 0.11f, "#facc15", 0.92f, 0.65f, new Vector3(0f, 45f, 0f), false, 0.18f);
                }
            }
            else
            {
                // Lenda da Estrada: 3 estrelas douradas brilhantes + coroa/asas laterais em ouro
                BoxS(bx - 0.03f, roofBadgeY + 0.045f, 0f, 0.10f, 0.05f, 0.52f, "#f59e0b", 0.94f, 0.45f, null, false, 0.16f);
                foreach (float bz in new[] { -0.15f, 0f, 0.15f })
                {
                    float starScale = bz == 0f ? 1.25f : 1.0f;
                    BoxS(bx + (bz == 0f ? 0.02f : 0f), roofBadgeY + 0.075f, bz, 0.11f * starScale, 0.06f * starScale, 0.11f * starScale, "#fef08a", 0.96f, 1.15f, new Vector3(0f, 45f, 0f), false, 0.12f);
                }
            }
        }

        // Eixos Rígidos, Diferenciais (Pumpkin) e Caixas de Roda Sólidas
        for (int i = 0; i < wheels.Count; i += 2)
        {
            var wp = wheels[i];
            float axX = wp.X + shiftX;
            CylS(axX, 0.24f * wheelScale, 0f, 0.055f * wheelScale, 0.055f * wheelScale, MathF.Abs(wp.Z) * 1.95f, 10, 'Z', "#11151d", 0.40f, 0f, false);
            // Bojo central do diferencial nos eixos traseiros e caixa de roda interna
            if (!wp.IsSteer)
            {
                BoxS(axX, 0.24f * wheelScale, 0f, 0.16f * wheelScale, 0.16f * wheelScale, 0.18f * wheelScale, "#18181b", 0.45f, 0f, null, false);
            }
            BoxS(axX, 0.34f * wheelScale, 0f, 0.40f * wheelScale, 0.20f * wheelScale, MathF.Abs(wp.Z) * 1.65f, "#11151d", 0.30f, 0f, null, false);
        }

        // Pneus Radiais 295/80 R22.5 + Rodas de Aço/Alcoa 10 Furos + Cubo Redutor Planetário
        var tireMat = GetMat("1c1f24", 0.90f, 0.05f);
        var grooveMat = GetMat("0d0f12", 0.96f, 0.02f);
        var rimMat = GetMat("d4d8e0", 0.28f, 0.82f);
        var chromeMat = GetMat("f8fafc", 0.18f, 0.95f);
        var rearHubMat = GetMat("1e293b", 0.38f, 0.65f);

        foreach (var wp in wheels)
        {
            float rTire = 0.24f * wheelScale * S;
            float rRim = 0.136f * wheelScale * S;
            float rHub = 0.052f * wheelScale * S;
            float tireW = 0.152f * wheelScale * S;
            float outSign = wp.Z >= 0 ? 1f : -1f;

            var (wheelParent, wheelLocalCx) = ResolveParentAndX(wp.X + shiftX);
            var steerPivot = new Node3D
            {
                Position = new Vector3(wp.Z * S, rTire, -wheelLocalCx * S)
            };
            var spinNode = new Node3D();
            steerPivot.AddChild(spinNode);

            void BuildSingleTire(float xOffset)
            {
                // Banda de rodagem principal + bochechas laterais abauladas + sulco central escuro
                AddCylinder(spinNode, rTire, rTire, tireW * 0.88f, new Vector3(xOffset, 0, 0), tireMat, new Vector3(0, 0, 90), 18, false);
                AddCylinder(spinNode, rTire * 0.93f, rTire * 0.93f, tireW * 1.02f, new Vector3(xOffset, 0, 0), tireMat, new Vector3(0, 0, 90), 18, false);
                AddCylinder(spinNode, rTire * 1.005f, rTire * 1.005f, tireW * 0.18f, new Vector3(xOffset, 0, 0), grooveMat, new Vector3(0, 0, 90), 18, false);
            }

            BuildSingleTire(0f);
            if (wp.IsDual)
            {
                BuildSingleTire(-outSign * tireW * 1.06f);
            }

            // Aro 22.5" Alcoa + anel escuro de ventilação + prisioneiros cromados + cubo planetário com nervuras
            float dishX = outSign * tireW * (wp.IsDual ? 0.22f : 0.38f);
            float hubCapX = outSign * tireW * 0.49f;
            AddCylinder(spinNode, rRim, rRim, tireW * 0.52f, new Vector3(dishX * 0.5f, 0, 0), rimMat, new Vector3(0, 0, 90), 16, false);
            AddCylinder(spinNode, rRim * 0.72f, rRim * 0.72f, tireW * 0.56f, new Vector3(dishX * 0.55f, 0, 0), grooveMat, new Vector3(0, 0, 90), 14, false);
            AddCylinder(spinNode, rHub * 1.38f, rHub * 1.38f, tireW * 0.60f, new Vector3(dishX * 0.60f, 0, 0), chromeMat, new Vector3(0, 0, 90), 10, false);
            AddCylinder(spinNode, rHub, rHub * 0.92f, tireW * 0.36f, new Vector3(hubCapX, 0, 0), wp.IsDual ? rearHubMat : chromeMat, new Vector3(0, 0, 90), 12, false);
            // Nervuras transversais no cubo para evidenciar a rotação real da roda
            AddBox(spinNode, new Vector3(0.012f, rHub * 1.65f, rHub * 0.32f), new Vector3(hubCapX + outSign * 0.006f, 0, 0), chromeMat, null, false);
            AddBox(spinNode, new Vector3(0.012f, rHub * 0.32f, rHub * 1.65f), new Vector3(hubCapX + outSign * 0.006f, 0, 0), chromeMat, null, false);
            OptimizeStaticChildren(spinNode);

            wheelParent.AddChild(steerPivot);
            handle.Wheels.Add(spinNode);
            if (wp.IsSteer)
            {
                handle.SteerPivots.Add(steerPivot);
            }
        }

        // Faróis Noturnos: SpotLight3D + Feixes Volumétricos 3D Duplos (idênticos ao Showcase)
        var spot = new SpotLight3D
        {
            LightColor = new Color("fef08a"),
            LightEnergy = 2.2f,
            SpotRange = 8.2f,
            SpotAngle = 34f,
            ShadowEnabled = false,
            Position = new Vector3(0f, (hlY + 0.08f) * S, -(hlX + shiftX + 0.05f) * S),
            RotationDegrees = new Vector3(-11f, 0f, 0f)
        };
        handle.Headlights.AddChild(spot);

        var outerBeamMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.95f, 0.58f, 0.13f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        var coreBeamMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(1.0f, 0.99f, 0.90f, 0.22f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };

        void AddVolumetricCone(float fx, float fy, float fz, float rStart, float rEnd, float len, float pitchDeg, Material mat)
        {
            var conePivot = new Node3D
            {
                Position = new Vector3(fz * S, fy * S, -(fx + shiftX) * S),
                RotationDegrees = new Vector3(pitchDeg, 0f, 0f)
            };
            AddCylinder(
                conePivot,
                rStart * S,
                rEnd * S,
                len * S,
                new Vector3(0f, 0f, -len * 0.5f * S),
                mat,
                new Vector3(-90f, 0f, 0f),
                16,
                false
            );
            handle.Headlights.AddChild(conePivot);
        }

        foreach (int s in new[] { -1, 1 })
        {
            AddVolumetricCone(hlX, hlY, s * hlZ, 0.055f, 0.42f, 2.25f, -6.0f, outerBeamMat);
            AddVolumetricCone(hlX, hlY, s * hlZ, 0.030f, 0.18f, 1.55f, -6.0f, coreBeamMat);
            if (fogY.HasValue && fogZ.HasValue)
            {
                AddVolumetricCone(hlX, fogY.Value, s * fogZ.Value, 0.035f, 0.22f, 1.15f, -8.0f, outerBeamMat);
            }
        }
        if (roofLightY.HasValue && roofLightCount > 0)
        {
            foreach (float rz in new[] { -0.24f, 0.24f })
            {
                AddVolumetricCone(hlX - 0.04f, roofLightY.Value, rz, 0.028f, 0.19f, 1.55f, -10.5f, outerBeamMat);
            }
        }

        root.AddChild(handle.Headlights);

        var smokeProc = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.06f,
            Direction = new Vector3(0f, 1.0f, 0.35f),
            Spread = 18.0f,
            Gravity = new Vector3(0f, 0.9f, 0f),
            InitialVelocityMin = 0.35f,
            InitialVelocityMax = 0.75f,
            ScaleMin = 0.8f,
            ScaleMax = 1.4f,
            Color = new Color(0.18f, 0.20f, 0.24f, 0.65f)
        };
        var smokeMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.20f, 0.22f, 0.26f, 0.65f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.95f
        };
        var smokeMesh = new BoxMesh
        {
            Size = new Vector3(0.09f, 0.09f, 0.09f),
            Material = smokeMat
        };
        var smokeNode = new GpuParticles3D
        {
            Name = "MaintenanceSmokeParticles",
            Position = new Vector3(0f, 0.62f, -0.18f),
            Amount = 14,
            Lifetime = 1.1f,
            Emitting = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ProcessMaterial = smokeProc,
            DrawPass1 = smokeMesh
        };
        root.AddChild(smokeNode);
        handle.MaintenanceSmoke = smokeNode;

        // Poeira levantada pelos pneus em estradas de terra e cascalho (Dirt / Gravel)
        var dustProc = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(0.24f, 0.04f, 0.24f),
            Direction = new Vector3(0f, 0.45f, 1.0f),
            Spread = 28.0f,
            Gravity = new Vector3(0f, 0.25f, 0f),
            InitialVelocityMin = 0.40f,
            InitialVelocityMax = 0.95f,
            ScaleMin = 0.7f,
            ScaleMax = 1.35f,
            Color = new Color(0.76f, 0.64f, 0.48f, 0.52f)
        };
        var dustMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.78f, 0.66f, 0.50f, 0.50f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.96f
        };
        var dustMesh = new BoxMesh
        {
            Size = new Vector3(0.08f, 0.08f, 0.08f),
            Material = dustMat
        };
        var dustNode = new GpuParticles3D
        {
            Name = "TireDustParticles",
            Position = new Vector3(0f, 0.08f, 0.35f),
            Amount = 12,
            Lifetime = 0.75f,
            Emitting = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ProcessMaterial = dustProc,
            DrawPass1 = dustMesh
        };
        root.AddChild(dustNode);
        handle.TireDust = dustNode;

        // Respingos d'água levantados pelos pneus no asfalto sob chuva/tempestade
        var sprayProc = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(0.24f, 0.03f, 0.22f),
            Direction = new Vector3(0f, 0.55f, 1.0f),
            Spread = 24.0f,
            Gravity = new Vector3(0f, -2.5f, 0f),
            InitialVelocityMin = 0.65f,
            InitialVelocityMax = 1.25f,
            ScaleMin = 0.65f,
            ScaleMax = 1.15f,
            Color = new Color(0.82f, 0.92f, 1.0f, 0.45f)
        };
        var sprayMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.84f, 0.93f, 1.0f, 0.44f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.35f
        };
        var sprayMesh = new BoxMesh
        {
            Size = new Vector3(0.06f, 0.06f, 0.06f),
            Material = sprayMat
        };
        var sprayNode = new GpuParticles3D
        {
            Name = "RainSprayParticles",
            Position = new Vector3(0f, 0.07f, 0.35f),
            Amount = 12,
            Lifetime = 0.50f,
            Emitting = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ProcessMaterial = sprayProc,
            DrawPass1 = sprayMesh
        };
        root.AddChild(sprayNode);
        handle.RainSpray = sprayNode;

        // Viatura Escolta Batedora (visível durante Comboio Especial / Carga Indivisível 3x)
        var escortLead = new Node3D
        {
            Name = "EscortLeadVehicle",
            Position = new Vector3(0f, 0f, -(hlX + shiftX + 1.05f) * S),
            Visible = truck.IsSpecialConvoyActive
        };
        var escortBodyMat = GetMat("facc15", 0.45f, 0.35f);
        var escortGlassMat = GetMat("1e3a5f", 0.25f, 0.85f);
        var escortTireMat = GetMat("1c1f24", 0.90f, 0.05f);
        AddBox(escortLead, new Vector3(0.26f, 0.14f, 0.46f), new Vector3(0f, 0.11f, 0f), escortBodyMat, castShadow: false);
        AddBox(escortLead, new Vector3(0.22f, 0.10f, 0.22f), new Vector3(0f, 0.22f, 0.02f), escortGlassMat, castShadow: false);
        foreach (var (wx, wz) in new[] { (-0.13f, -0.14f), (0.13f, -0.14f), (-0.13f, 0.14f), (0.13f, 0.14f) })
            AddCylinder(escortLead, 0.06f, 0.06f, 0.05f, new Vector3(wx, 0.06f, wz), escortTireMat, new Vector3(0, 0, 90), 10, false);

        handle.EscortRedBeacon = AddBox(escortLead, new Vector3(0.08f, 0.05f, 0.07f), new Vector3(-0.055f, 0.295f, 0.02f), GetMat("ef4444", 0.2f, 0.3f, true, 1.8f), castShadow: false);
        handle.EscortAmberBeacon = AddBox(escortLead, new Vector3(0.08f, 0.05f, 0.07f), new Vector3(0.055f, 0.295f, 0.02f), GetMat("fde047", 0.2f, 0.3f, true, 1.8f), castShadow: false);
        root.AddChild(escortLead);
        handle.EscortLead = escortLead;

        var truckSkip = new HashSet<Node> { handle.Headlights, escortLead, handle.EscortRedBeacon, handle.EscortAmberBeacon };
        if (handle.RearTrailerPivot != null) truckSkip.Add(handle.RearTrailerPivot);
        foreach (var w in handle.Wheels) truckSkip.Add(w);
        foreach (var sp in handle.SteerPivots) truckSkip.Add(sp);
        foreach (var (bed, _) in handle.CargoBeds) truckSkip.Add(bed);

        OptimizeStaticChildren(root, truckSkip);
        if (handle.RearTrailerPivot != null)
            OptimizeStaticChildren(handle.RearTrailerPivot, truckSkip);
        OptimizeStaticChildren(escortLead, truckSkip);

        UpdateTruckCargoVisuals(handle, truck);

        return handle;
    }

    /// <summary>
    /// Atualiza a carga 3D visível sobre as caçambas do caminhão (idêntica ao seletor de carga do Showcase).
    /// </summary>
    public static void UpdateTruckCargoVisuals(TruckVisualHandle handle, Truck truck)
    {
        if (handle.CargoBeds.Count == 0)
            return;

        ResourceType cargoType = truck.LoadedCargoAmount > 0 ? truck.LoadedCargoType : ResourceType.None;
        int capacity = Math.Max(1, truck.Spec.Capacity);
        int fillBucket = truck.LoadedCargoAmount <= 0
            ? 0
            : Math.Clamp((int)MathF.Ceiling((truck.LoadedCargoAmount / (float)capacity) * 3f), 1, 3);

        if (handle.DisplayedCargoType == cargoType && handle.DisplayedCargoBucket == fillBucket)
            return;

        handle.DisplayedCargoType = cargoType;
        handle.DisplayedCargoBucket = fillBucket;

        const float S = 0.48f;
        float fillHeightScale = fillBucket switch
        {
            1 => 0.62f,
            2 => 0.82f,
            _ => 1.00f
        };

        foreach (var (bedRoot, bedScale) in handle.CargoBeds)
        {
            while (bedRoot.GetChildCount() > 0)
            {
                var child = bedRoot.GetChild(0);
                bedRoot.RemoveChild(child);
                child.QueueFree();
            }

            if (cargoType == ResourceType.None || fillBucket == 0)
                continue;

            float sx = bedScale.X;
            float sy = bedScale.Y * fillHeightScale;
            float sz = bedScale.Z;

            void CBox(float x, float y, float z, float w, float h, float d, string hex, float metal = 0.20f)
            {
                var mat = GetMat(hex.TrimStart('#'), Mathf.Clamp(1f - metal * 0.6f, 0.2f, 0.9f), metal);
                AddBox(
                    bedRoot,
                    new Vector3(d * sz * S, h * sy * S, w * sx * S),
                    new Vector3(z * sz * S, y * sy * S, -x * sx * S),
                    mat,
                    null,
                    false
                );
            }

            void CCyl(float x, float y, float z, float r, float len, char axis, string hex, float metal = 0.25f)
            {
                var mat = GetMat(hex.TrimStart('#'), Mathf.Clamp(1f - metal * 0.6f, 0.2f, 0.9f), metal);
                Vector3? rot = axis switch
                {
                    'X' => new Vector3(90f, 0f, 0f),
                    'Z' => new Vector3(0f, 0f, 90f),
                    _ => null
                };
                float rScaled = r * sy * S;
                float lScaled = (axis == 'X' ? len * sx : (axis == 'Y' ? len * sy : len * sz)) * S;
                AddCylinder(
                    bedRoot,
                    rScaled,
                    rScaled,
                    lScaled,
                    new Vector3(z * sz * S, y * sy * S, -x * sx * S),
                    mat,
                    rot,
                    12,
                    false
                );
            }

            switch (cargoType)
            {
                case ResourceType.Grain:
                    // 🌾 Grãos: fardos/sacos dourados empilhados em camadas
                    for (int l = 0; l < fillBucket; l++)
                    {
                        for (int r = -1; r <= 1; r++)
                        {
                            for (int c = -1; c <= 1; c += 2)
                            {
                                CBox(r * 0.34f, 0.10f + l * 0.13f, c * 0.14f, 0.28f, 0.13f, 0.22f, "#f59e0b", 0.15f);
                            }
                        }
                    }
                    break;

                case ResourceType.Timber:
                case ResourceType.Lumber:
                    // 🪵 Madeira Bruta / Processada: toras cilíndricas com casca e cerne + fueiros vermelhos
                    (float py, float pz)[] logPositions =
                    {
                        (0.14f, -0.22f), (0.14f, 0f), (0.14f, 0.22f),
                        (0.29f, -0.11f), (0.29f, 0.11f), (0.44f, 0f)
                    };
                    int maxLogs = fillBucket == 1 ? 3 : (fillBucket == 2 ? 5 : 6);
                    for (int i = 0; i < maxLogs; i++)
                    {
                        var (py, pz) = logPositions[i];
                        CCyl(0f, py, pz, 0.085f, 1.22f, 'X', "#78350f", 0.12f);
                        CCyl(0f, py, pz, 0.070f, 1.23f, 'X', "#fde68a", 0.15f);
                    }
                    CBox(-0.34f, 0.24f, 0f, 0.04f, 0.40f, 0.58f, "#ef4444", 0.30f);
                    CBox(0.34f, 0.24f, 0f, 0.04f, 0.40f, 0.58f, "#ef4444", 0.30f);
                    break;

                case ResourceType.CrudeOil:
                case ResourceType.Fuel:
                    // 🛢️ Combustível / Petróleo: tanque cilíndrico inox espelhado com faixa vermelha e domos
                    string tankHex = cargoType == ResourceType.CrudeOil ? "#334155" : "#e2e8f0";
                    CCyl(0f, 0.30f, 0f, 0.30f, 1.22f, 'X', tankHex, 0.85f);
                    CBox(0f, 0.30f, 0f, 1.23f, 0.10f, 0.61f, "#ef4444", 0.40f);
                    CCyl(-0.28f, 0.60f, 0f, 0.08f, 0.08f, 'Y', "#334155", 0.50f);
                    CCyl(0.28f, 0.60f, 0f, 0.08f, 0.08f, 'Y', "#334155", 0.50f);
                    break;

                case ResourceType.IronOre:
                case ResourceType.Steel:
                    // 🧱 Minério / Aço / Construção: palete de madeira + blocos minerais + vigas de aço
                    CBox(-0.30f, 0.06f, 0f, 0.48f, 0.06f, 0.56f, "#d97706", 0.20f);
                    CBox(-0.30f, 0.23f, 0f, 0.44f, 0.28f, 0.50f, cargoType == ResourceType.IronOre ? "#7f1d1d" : "#b91c1c", 0.18f);
                    CBox(0.28f, 0.20f, -0.15f, 0.54f, 0.28f, 0.18f, "#94a3b8", 0.65f);
                    CBox(0.28f, 0.20f, 0.15f, 0.54f, 0.28f, 0.18f, "#94a3b8", 0.65f);
                    break;

                case ResourceType.Coal:
                    // 🪨 Carvão Mineral: monte escuro de hulha sobre caçamba
                    CBox(0f, 0.18f, 0f, 1.12f, 0.22f, 0.54f, "#1e293b", 0.15f);
                    if (fillBucket >= 2)
                        CBox(0f, 0.34f, 0f, 0.86f, 0.16f, 0.40f, "#0f172a", 0.10f);
                    break;

                case ResourceType.Machinery:
                    // ⚙️ Maquinário Pesado & Materiais Tier 3: módulos industriais amarelos + vigas azuis
                    CBox(-0.24f, 0.22f, 0f, 0.56f, 0.34f, 0.52f, "#eab308", 0.45f);
                    CBox(0.28f, 0.18f, 0f, 0.44f, 0.26f, 0.46f, "#0284c7", 0.55f);
                    break;

                default:
                    // 🥫 Alimentos Processados / Bens Industriais: contêineres e caixotes coloridos
                    CBox(-0.30f, 0.20f, -0.14f, 0.36f, 0.28f, 0.26f, "#0284c7", 0.30f);
                    CBox(-0.30f, 0.20f, 0.14f, 0.36f, 0.28f, 0.26f, "#e11d48", 0.30f);
                    CBox(0.18f, 0.20f, -0.14f, 0.36f, 0.28f, 0.26f, "#e11d48", 0.30f);
                    CBox(0.18f, 0.20f, 0.14f, 0.36f, 0.28f, 0.26f, "#0284c7", 0.30f);
                    if (fillBucket >= 2)
                    {
                        CBox(-0.06f, 0.40f, 0f, 0.44f, 0.20f, 0.34f, "#facc15", 0.30f);
                    }
                    break;
            }
            OptimizeStaticChildren(bedRoot);
        }
    }

    public static Label3D CreateFloatingLabel(string text, Color modulate, float heightY = 1.8f, int fontSize = 36)
    {
        return new Label3D
        {
            Text = text,
            Modulate = modulate,
            OutlineModulate = new Color(0.08f, 0.09f, 0.11f, 0.98f),
            OutlineSize = 14,
            FontSize = fontSize,
            PixelSize = 0.0085f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            NoDepthTest = true,
            Position = new Vector3(0, heightY, 0)
        };
    }

    /// <summary>
    /// Constrói veículo voxel NPC: Caminhão Rival corporativo, Ônibus Intermunicipal ou Carro de Passeio.
    /// Frente apontando para -Z para alinhar com LookAt().
    /// </summary>
    public static Node3D CreateTrafficVehicleNode(TrafficVehicle vehicle, out Node3D headlights)
    {
        var root = new Node3D { Name = $"Traffic_{vehicle.Id}" };
        headlights = new Node3D { Name = "Headlights", Visible = false };

        string cleanPrimary = vehicle.PrimaryColorHex.TrimStart('#');
        string cleanStripe = vehicle.StripeColorHex.TrimStart('#');
        var bodyMat = GetMat(cleanPrimary, 0.48f, 0.35f);
        var stripeMat = GetMat(cleanStripe, 0.40f, 0.45f);
        var darkMat = GetMat("11151d", 0.80f, 0.20f);
        var glassMat = GetMat("1e3a5f", 0.22f, 0.85f);
        var tireMat = GetMat("1c1f24", 0.92f, 0.05f);
        var lightMat = GetMat("fef08a", 0.2f, 0.4f, true, 1.5f);

        if (vehicle.Kind == TrafficVehicleKind.RivalTruck)
        {
            // Caminhão Baú da Empresa Concorrente com cor corporativa + faixa
            AddBox(root, new Vector3(0.32f, 0.10f, 0.96f), new Vector3(0f, 0.14f, 0.02f), darkMat, castShadow: false);
            AddBox(root, new Vector3(0.36f, 0.36f, 0.30f), new Vector3(0f, 0.32f, -0.32f), bodyMat);
            AddBox(root, new Vector3(0.34f, 0.14f, 0.04f), new Vector3(0f, 0.38f, -0.46f), glassMat, castShadow: false);
            // Baú de carga corporativo rival
            AddBox(root, new Vector3(0.40f, 0.40f, 0.62f), new Vector3(0f, 0.38f, 0.16f), bodyMat);
            AddBox(root, new Vector3(0.415f, 0.07f, 0.60f), new Vector3(0f, 0.38f, 0.16f), stripeMat, castShadow: false);

            foreach (float z in new[] { -0.30f, 0.10f, 0.36f })
            {
                AddCylinder(root, 0.10f, 0.10f, 0.07f, new Vector3(-0.18f, 0.10f, z), tireMat, new Vector3(0, 0, 90), 10, false);
                AddCylinder(root, 0.10f, 0.10f, 0.07f, new Vector3(0.18f, 0.10f, z), tireMat, new Vector3(0, 0, 90), 10, false);
            }
            foreach (float hx in new[] { -0.12f, 0.12f })
                AddBox(headlights, new Vector3(0.06f, 0.05f, 0.06f), new Vector3(hx, 0.19f, -0.48f), lightMat, castShadow: false);
        }
        else if (vehicle.Kind == TrafficVehicleKind.IntercityBus)
        {
            // Ônibus Rodoviário Intermunicipal com janelas panorâmicas e letreiro luminoso
            AddBox(root, new Vector3(0.36f, 0.34f, 0.96f), new Vector3(0f, 0.25f, 0f), bodyMat);
            AddBox(root, new Vector3(0.375f, 0.06f, 0.94f), new Vector3(0f, 0.21f, 0f), stripeMat, castShadow: false);
            AddBox(root, new Vector3(0.37f, 0.13f, 0.84f), new Vector3(0f, 0.33f, 0f), glassMat, castShadow: false);
            AddBox(root, new Vector3(0.24f, 0.04f, 0.04f), new Vector3(0f, 0.39f, -0.47f), lightMat, castShadow: false);

            foreach (float z in new[] { -0.30f, 0.28f })
            {
                AddCylinder(root, 0.085f, 0.085f, 0.06f, new Vector3(-0.17f, 0.085f, z), tireMat, new Vector3(0, 0, 90), 10, false);
                AddCylinder(root, 0.085f, 0.085f, 0.06f, new Vector3(0.17f, 0.085f, z), tireMat, new Vector3(0, 0, 90), 10, false);
            }
            foreach (float hx in new[] { -0.12f, 0.12f })
                AddBox(headlights, new Vector3(0.06f, 0.04f, 0.05f), new Vector3(hx, 0.15f, -0.49f), lightMat, castShadow: false);
        }
        else
        {
            // Carro de Passeio Voxel compacto
            AddBox(root, new Vector3(0.25f, 0.12f, 0.48f), new Vector3(0f, 0.11f, 0f), bodyMat);
            AddBox(root, new Vector3(0.22f, 0.10f, 0.24f), new Vector3(0f, 0.21f, 0.03f), bodyMat);
            AddBox(root, new Vector3(0.23f, 0.07f, 0.20f), new Vector3(0f, 0.21f, 0.03f), glassMat, castShadow: false);

            foreach (float z in new[] { -0.15f, 0.15f })
            {
                AddCylinder(root, 0.055f, 0.055f, 0.045f, new Vector3(-0.12f, 0.055f, z), tireMat, new Vector3(0, 0, 90), 8, false);
                AddCylinder(root, 0.055f, 0.055f, 0.045f, new Vector3(0.12f, 0.055f, z), tireMat, new Vector3(0, 0, 90), 8, false);
            }
            foreach (float hx in new[] { -0.08f, 0.08f })
                AddBox(headlights, new Vector3(0.045f, 0.035f, 0.04f), new Vector3(hx, 0.11f, -0.25f), lightMat, castShadow: false);
        }

        root.AddChild(headlights);
        OptimizeStaticChildren(root, new HashSet<Node> { headlights });
        return root;
    }

    /// <summary>
    /// Constrói Terminal Portuário de Contêineres &amp; Graneleiro com guindastes Portainer animados e navio cargueiro atracável.
    /// </summary>
    public static Node3D CreateSeaportNode(SeaportTerminal seaport, out List<Node3D> portainerTrolleys, out Node3D shipNode, out Label3D statusLabel)
    {
        portainerTrolleys = new List<Node3D>();
        var root = new Node3D { Name = $"Seaport_{seaport.Id}" };

        var concreteMat = GetMat("cbd5e1", 0.85f);
        var asphaltMat = GetMat("334155", 0.90f);
        var craneOrange = GetMat("ea580c", 0.55f, 0.25f);
        var craneCyan = GetMat("0284c7", 0.55f, 0.25f);
        var steelMat = GetMat("475569", 0.50f, 0.60f);
        var officeMat = GetMat("f8fafc", 0.72f);

        // Cais de concreto portuário
        AddBox(root, new Vector3(1.94f, 0.18f, 1.94f), new Vector3(0f, 0.09f, 0f), concreteMat);
        AddBox(root, new Vector3(1.70f, 0.04f, 1.70f), new Vector3(0f, 0.19f, 0f), asphaltMat, castShadow: false);

        // Escritório alfandegário e contêineres empilhados no pátio
        AddBox(root, new Vector3(0.46f, 0.36f, 0.44f), new Vector3(-0.56f, 0.38f, -0.52f), officeMat);
        AddBox(root, new Vector3(0.50f, 0.06f, 0.48f), new Vector3(-0.56f, 0.58f, -0.52f), craneCyan, castShadow: false);

        string[] containerHexes = { "dc2626", "2563eb", "f59e0b", "0d9488", "7c3aed" };
        for (int i = 0; i < 4; i++)
        {
            var cMat = GetMat(containerHexes[i % containerHexes.Length], 0.70f);
            float cx = -0.45f + (i % 2) * 0.38f;
            float cz = 0.28f + (i / 2) * 0.28f;
            AddBox(root, new Vector3(0.32f, 0.22f, 0.22f), new Vector3(cx, 0.31f, cz), cMat);
            if (i < 2)
                AddBox(root, new Vector3(0.32f, 0.22f, 0.22f), new Vector3(cx, 0.53f, cz), GetMat(containerHexes[(i + 2) % containerHexes.Length], 0.70f));
        }

        // Direção do cais em direção ao tile de água
        int dx = seaport.WaterDockX - seaport.GridX;
        int dz = seaport.WaterDockY - seaport.GridY;
        float dockYaw = MathF.Atan2(dx, dz); // +Z local aponta para o mar

        var waterfrontPivot = new Node3D { Name = "WaterfrontPivot", Rotation = new Vector3(0f, dockYaw, 0f) };
        root.AddChild(waterfrontPivot);

        // Dois guindastes Portainer alinhados na beira do cais (+Z aponta sobre a água)
        foreach (float gx in new[] { -0.46f, 0.46f })
        {
            var craneMat = gx < 0 ? craneOrange : craneCyan;
            foreach (float sx in new[] { -0.10f, 0.10f })
            {
                AddBox(waterfrontPivot, new Vector3(0.06f, 1.05f, 0.06f), new Vector3(gx + sx, 0.72f, 0.42f), craneMat);
                AddBox(waterfrontPivot, new Vector3(0.06f, 1.05f, 0.06f), new Vector3(gx + sx, 0.72f, 0.78f), craneMat);
            }
            // Lança horizontal projetada sobre o navio (+Z)
            AddBox(waterfrontPivot, new Vector3(0.24f, 0.08f, 1.45f), new Vector3(gx, 1.24f, 0.96f), craneMat);
            AddBox(waterfrontPivot, new Vector3(0.05f, 0.24f, 0.05f), new Vector3(gx, 1.38f, 0.58f), steelMat, castShadow: false);

            // Trolley móvel + spreader suspenso
            var trolley = new Node3D { Name = "PortainerTrolley", Position = new Vector3(gx, 1.15f, 0.90f) };
            AddBox(trolley, new Vector3(0.18f, 0.07f, 0.20f), Vector3.Zero, steelMat, castShadow: false);
            AddBox(trolley, new Vector3(0.03f, 0.34f, 0.03f), new Vector3(0f, -0.20f, 0f), steelMat, castShadow: false);
            AddBox(trolley, new Vector3(0.24f, 0.08f, 0.16f), new Vector3(0f, -0.38f, 0f), craneOrange, castShadow: false);
            waterfrontPivot.AddChild(trolley);
            portainerTrolleys.Add(trolley);
        }

        // Navio cargueiro voxel na lâmina d'água ao lado do cais
        shipNode = new Node3D { Name = "CargoShip", Position = new Vector3(0f, -0.02f, 1.48f) };
        var hullRed = GetMat("991b1b", 0.65f);
        var hullDark = GetMat("1e293b", 0.70f);
        var bridgeWhite = GetMat("f8fafc", 0.72f);

        AddBox(shipNode, new Vector3(1.68f, 0.28f, 0.56f), new Vector3(0f, 0.12f, 0f), hullDark);
        AddBox(shipNode, new Vector3(1.56f, 0.10f, 0.58f), new Vector3(0f, 0.02f, 0f), hullRed, castShadow: false);
        // Proa angulada e ponte de comando na popa
        AddBox(shipNode, new Vector3(0.32f, 0.24f, 0.36f), new Vector3(0.84f, 0.14f, 0f), hullDark);
        AddBox(shipNode, new Vector3(0.34f, 0.42f, 0.44f), new Vector3(-0.58f, 0.44f, 0f), bridgeWhite);
        AddBox(shipNode, new Vector3(0.10f, 0.16f, 0.10f), new Vector3(-0.66f, 0.70f, 0f), hullRed, castShadow: false);
        // Contêineres no convés do navio
        for (int c = 0; c < 3; c++)
        {
            float bx = -0.18f + c * 0.34f;
            AddBox(shipNode, new Vector3(0.28f, 0.20f, 0.38f), new Vector3(bx, 0.36f, 0f), GetMat(containerHexes[c], 0.70f));
        }
        waterfrontPivot.AddChild(shipNode);

        string shipTag = seaport.IsShipDocked ? "⚓ Navio Atracado (+45%)" : "🌊 Navio em Alto-Mar";
        statusLabel = CreateFloatingLabel($"⚓ {seaport.Name}\n[{shipTag}]", new Color("38bdf8"), 2.15f, 32);
        root.AddChild(statusLabel);
        OptimizeStaticChildren(shipNode);
        OptimizeStaticChildren(root);
        return root;
    }

    /// <summary>
    /// Constrói Mega-Obra Regional com visual evolutivo (Etapas 1..3 e Monumento Concluído).
    /// </summary>
    public static Node3D CreateLandmarkNode(RegionalLandmark landmark, out List<Node3D> animatedRotors)
    {
        animatedRotors = new List<Node3D>();
        var root = new Node3D { Name = $"Landmark_{landmark.Id}" };
        int stage = landmark.IsCompleted ? 4 : landmark.CurrentStage;

        var padMat = GetMat("94a3b8", 0.85f);
        var concreteMat = GetMat("e2e8f0", 0.78f);
        var steelMat = GetMat("475569", 0.55f, 0.55f);
        var scaffoldMat = GetMat("f59e0b", 0.65f);
        var goldMat = GetMat("facc15", 0.35f, 0.65f);

        AddBox(root, new Vector3(1.90f, 0.08f, 1.90f), new Vector3(0f, 0.04f, 0f), padMat, castShadow: false);

        if (landmark.Kind == LandmarkKind.HydroelectricDam)
        {
            var waterMat = GetMat("0284c7", 0.22f, 0.35f);
            float wallH = 0.28f * stage;
            AddBox(root, new Vector3(1.66f, wallH, 0.44f), new Vector3(0f, wallH * 0.5f + 0.08f, 0f), concreteMat);
            if (stage >= 2)
            {
                AddBox(root, new Vector3(1.20f, 0.16f, 0.56f), new Vector3(0f, 0.16f, 0.44f), waterMat, castShadow: false);
                AddBox(root, new Vector3(0.46f, 0.34f, 0.36f), new Vector3(-0.48f, 0.25f, -0.44f), steelMat);
            }
            if (stage >= 3)
            {
                AddBox(root, new Vector3(0.46f, 0.42f, 0.36f), new Vector3(0.48f, 0.29f, -0.44f), steelMat);
                AddBox(root, new Vector3(1.72f, 0.08f, 0.16f), new Vector3(0f, wallH + 0.10f, 0f), landmark.IsCompleted ? goldMat : scaffoldMat, castShadow: false);
            }
        }
        else if (landmark.Kind == LandmarkKind.MunicipalStadium)
        {
            var turfMat = GetMat("16a34a", 0.82f);
            AddBox(root, new Vector3(1.06f, 0.06f, 0.72f), new Vector3(0f, 0.11f, 0f), turfMat, castShadow: false);
            float tierH = 0.16f * stage;
            AddBox(root, new Vector3(1.56f, tierH, 0.22f), new Vector3(0f, tierH * 0.5f + 0.08f, -0.54f), concreteMat);
            AddBox(root, new Vector3(1.56f, tierH, 0.22f), new Vector3(0f, tierH * 0.5f + 0.08f, 0.54f), concreteMat);
            if (stage >= 2)
            {
                AddBox(root, new Vector3(0.22f, tierH, 1.30f), new Vector3(-0.68f, tierH * 0.5f + 0.08f, 0f), concreteMat);
                AddBox(root, new Vector3(0.22f, tierH, 1.30f), new Vector3(0.68f, tierH * 0.5f + 0.08f, 0f), concreteMat);
            }
            if (stage >= 3)
            {
                var canopyMat = landmark.IsCompleted ? GetMat("2563eb", 0.50f) : scaffoldMat;
                AddBox(root, new Vector3(1.62f, 0.08f, 0.28f), new Vector3(0f, tierH + 0.14f, -0.54f), canopyMat, castShadow: false);
                AddBox(root, new Vector3(1.62f, 0.08f, 0.28f), new Vector3(0f, tierH + 0.14f, 0.54f), canopyMat, castShadow: false);
            }
        }
        else // WindFarmPark
        {
            var whiteMat = GetMat("f8fafc", 0.65f);
            var bladeMat = GetMat("e2e8f0", 0.50f);
            Vector3[] towerOffsets = { new(-0.50f, 0f, -0.40f), new(0.50f, 0f, 0.40f), new(0.0f, 0f, -0.02f) };
            int towerCount = Math.Min(3, stage);
            for (int t = 0; t < towerCount; t++)
            {
                var p = towerOffsets[t];
                float h = 0.65f + stage * 0.18f;
                AddCylinder(root, 0.045f, 0.065f, h, new Vector3(p.X, h * 0.5f + 0.08f, p.Z), whiteMat);
                AddBox(root, new Vector3(0.10f, 0.10f, 0.16f), new Vector3(p.X, h + 0.10f, p.Z), steelMat, castShadow: false);

                if (stage >= 2)
                {
                    var rotor = new Node3D { Name = $"WindTurbineRotor_{t}", Position = new Vector3(p.X, h + 0.10f, p.Z + 0.09f) };
                    for (int b = 0; b < 3; b++)
                    {
                        var bladeHolder = new Node3D { RotationDegrees = new Vector3(0f, 0f, b * 120f) };
                        AddBox(bladeHolder, new Vector3(0.035f, 0.42f, 0.02f), new Vector3(0f, 0.21f, 0f), bladeMat, castShadow: false);
                        rotor.AddChild(bladeHolder);
                    }
                    root.AddChild(rotor);
                    animatedRotors.Add(rotor);
                }
            }
        }

        string statusTag = landmark.IsCompleted
            ? "🏆 Concluída (Bônus Ativo)"
            : $"Etapa {landmark.CurrentStage}/{RegionalLandmark.MaxStages}";
        var label = CreateFloatingLabel($"🏗️ {landmark.Name}\n[{statusTag}]", landmark.IsCompleted ? new Color("facc15") : new Color("fbbf24"), 2.0f, 30);
        root.AddChild(label);
        OptimizeStaticChildren(root);
        return root;
    }
}
