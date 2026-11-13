using Godot;
using System;
using System.Collections.Generic;
using TransitEmpire.Core.Domain;

namespace TransitEmpire.Client.Scripts;

public sealed record TrainVisualHandle(
    Node3D Root,
    List<Node3D> Wagons,
    List<MeshInstance3D> CargoMeshes,
    Node3D? Headlights)
{
    public RailWagonCategory DisplayedCategory { get; set; } = RailWagonCategory.GeneralFreight;
    public int DisplayedCargoAmount { get; set; } = -1;
}

public static partial class ProceduralModels3D
{
    public readonly record struct RailTileRenderInput(
        int GridX,
        int GridY,
        Vector3 WorldPos,
        bool ConnectN,
        bool ConnectS,
        bool ConnectE,
        bool ConnectW,
        RailStructureType Structure
    );

    public static Node3D CreateRailChunkNode(List<RailTileRenderInput> tiles)
    {
        var chunkRoot = new Node3D();
        if (tiles.Count == 0)
            return chunkRoot;

        var flatSt = new SurfaceTool();
        flatSt.Begin(Mesh.PrimitiveType.Triangles);
        var shadowSt = new SurfaceTool();
        shadowSt.Begin(Mesh.PrimitiveType.Triangles);

        var ballastCol = new Color("57534e");
        var sleeperCol = new Color("451a03");
        var railSteelCol = new Color("cbd5e1");
        var viaductDeckCol = new Color("64748b");
        var pierCol = new Color("94a3b8");
        var tunnelPortalCol = new Color("334155");

        foreach (var tile in tiles)
        {
            Vector3 wp = tile.WorldPos;
            float baseY = tile.Structure == RailStructureType.Viaduct ? 0.42f : wp.Y + 0.02f;

            if (tile.Structure == RailStructureType.Viaduct)
            {
                // Tabuleiro e pilares de concreto sobre água
                EmitRawBox(shadowSt, new Vector3(1.12f, 0.16f, 1.12f), new Vector3(wp.X, baseY - 0.08f, wp.Z), viaductDeckCol);
                EmitRawBox(shadowSt, new Vector3(0.28f, 0.85f, 0.28f), new Vector3(wp.X, baseY - 0.48f, wp.Z), pierCol);
            }
            else if (tile.Structure == RailStructureType.Tunnel)
            {
                // Portal de pedra para túnel ferroviário
                EmitRawBox(shadowSt, new Vector3(1.26f, 0.72f, 1.26f), new Vector3(wp.X, baseY + 0.34f, wp.Z), tunnelPortalCol);
            }

            // Lastro de brita
            EmitRawBox(flatSt, new Vector3(0.76f, 0.05f, 0.76f), new Vector3(wp.X, baseY + 0.025f, wp.Z), ballastCol);

            bool any = tile.ConnectN || tile.ConnectS || tile.ConnectE || tile.ConnectW;
            if (tile.ConnectN || tile.ConnectS || !any)
            {
                if (tile.ConnectN)
                {
                    EmitRawBox(flatSt, new Vector3(0.72f, 0.04f, 0.56f), new Vector3(wp.X, baseY + 0.025f, wp.Z - 0.72f), ballastCol);
                    EmitRailCorridorSegment(flatSt, wp.X, baseY, wp.Z - 0.50f, lengthZ: 1.0f, horizontalX: false, sleeperCol, railSteelCol);
                }
                if (tile.ConnectS)
                {
                    EmitRawBox(flatSt, new Vector3(0.72f, 0.04f, 0.56f), new Vector3(wp.X, baseY + 0.025f, wp.Z + 0.72f), ballastCol);
                    EmitRailCorridorSegment(flatSt, wp.X, baseY, wp.Z + 0.50f, lengthZ: 1.0f, horizontalX: false, sleeperCol, railSteelCol);
                }
                if (!any)
                {
                    EmitRailCorridorSegment(flatSt, wp.X, baseY, wp.Z, lengthZ: 1.10f, horizontalX: false, sleeperCol, railSteelCol);
                }
            }

            if (tile.ConnectE)
            {
                EmitRawBox(flatSt, new Vector3(0.56f, 0.04f, 0.72f), new Vector3(wp.X + 0.72f, baseY + 0.025f, wp.Z), ballastCol);
                EmitRailCorridorSegment(flatSt, wp.X + 0.50f, baseY, wp.Z, lengthZ: 1.0f, horizontalX: true, sleeperCol, railSteelCol);
            }
            if (tile.ConnectW)
            {
                EmitRawBox(flatSt, new Vector3(0.56f, 0.04f, 0.72f), new Vector3(wp.X - 0.72f, baseY + 0.025f, wp.Z), ballastCol);
                EmitRailCorridorSegment(flatSt, wp.X - 0.50f, baseY, wp.Z, lengthZ: 1.0f, horizontalX: true, sleeperCol, railSteelCol);
            }

            // Trilhos centrais no nó
            EmitRawBox(flatSt, new Vector3(0.68f, 0.04f, 0.14f), new Vector3(wp.X, baseY + 0.055f, wp.Z), sleeperCol);
            EmitRawBox(flatSt, new Vector3(0.08f, 0.06f, 0.54f), new Vector3(wp.X - 0.20f, baseY + 0.09f, wp.Z), railSteelCol);
            EmitRawBox(flatSt, new Vector3(0.08f, 0.06f, 0.54f), new Vector3(wp.X + 0.20f, baseY + 0.09f, wp.Z), railSteelCol);
            if (tile.ConnectE || tile.ConnectW)
            {
                EmitRawBox(flatSt, new Vector3(0.54f, 0.06f, 0.08f), new Vector3(wp.X, baseY + 0.09f, wp.Z - 0.20f), railSteelCol);
                EmitRawBox(flatSt, new Vector3(0.54f, 0.06f, 0.08f), new Vector3(wp.X, baseY + 0.09f, wp.Z + 0.20f), railSteelCol);
            }
        }

        if (!_vertexColorBucketMats.TryGetValue((false, false), out var flatMat))
        {
            flatMat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.88f };
            _vertexColorBucketMats[(false, false)] = flatMat;
        }

        var flatMesh = flatSt.Commit();
        if (flatMesh != null)
        {
            chunkRoot.AddChild(new MeshInstance3D
            {
                Mesh = flatMesh,
                MaterialOverride = flatMat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
        }

        var shadowMesh = shadowSt.Commit();
        if (shadowMesh != null)
        {
            chunkRoot.AddChild(new MeshInstance3D
            {
                Mesh = shadowMesh,
                MaterialOverride = flatMat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On
            });
        }

        return chunkRoot;
    }

    private static void EmitRailCorridorSegment(
        SurfaceTool st,
        float cx,
        float baseY,
        float cz,
        float lengthZ,
        bool horizontalX,
        Color sleeperCol,
        Color railSteelCol)
    {
        for (int s = -1; s <= 1; s++)
        {
            float offset = s * (lengthZ * 0.28f);
            if (horizontalX)
                EmitRawBox(st, new Vector3(0.12f, 0.04f, 0.68f), new Vector3(cx + offset, baseY + 0.055f, cz), sleeperCol);
            else
                EmitRawBox(st, new Vector3(0.68f, 0.04f, 0.12f), new Vector3(cx, baseY + 0.055f, cz + offset), sleeperCol);
        }

        if (horizontalX)
        {
            EmitRawBox(st, new Vector3(lengthZ, 0.06f, 0.08f), new Vector3(cx, baseY + 0.09f, cz - 0.20f), railSteelCol);
            EmitRawBox(st, new Vector3(lengthZ, 0.06f, 0.08f), new Vector3(cx, baseY + 0.09f, cz + 0.20f), railSteelCol);
        }
        else
        {
            EmitRawBox(st, new Vector3(0.08f, 0.06f, lengthZ), new Vector3(cx - 0.20f, baseY + 0.09f, cz), railSteelCol);
            EmitRawBox(st, new Vector3(0.08f, 0.06f, lengthZ), new Vector3(cx + 0.20f, baseY + 0.09f, cz), railSteelCol);
        }
    }

    private static void EmitRawBox(SurfaceTool st, Vector3 size, Vector3 center, Color color)
    {
        Vector3 h = size * 0.5f;
        Vector3[] v =
        {
            new(-h.X, -h.Y,  h.Z), new( h.X, -h.Y,  h.Z), new( h.X,  h.Y,  h.Z), new(-h.X,  h.Y,  h.Z),
            new(-h.X, -h.Y, -h.Z), new(-h.X,  h.Y, -h.Z), new( h.X,  h.Y, -h.Z), new( h.X, -h.Y, -h.Z),
        };
        for (int i = 0; i < 8; i++) v[i] += center;

        void Quad(int a, int b, int c, int d, Vector3 n)
        {
            st.SetColor(color);
            st.SetNormal(n); st.AddVertex(v[a]);
            st.SetNormal(n); st.AddVertex(v[b]);
            st.SetNormal(n); st.AddVertex(v[c]);
            st.SetNormal(n); st.AddVertex(v[a]);
            st.SetNormal(n); st.AddVertex(v[c]);
            st.SetNormal(n); st.AddVertex(v[d]);
        }

        Quad(3, 2, 6, 5, Vector3.Up);
        Quad(0, 1, 2, 3, Vector3.Back);
        Quad(7, 4, 5, 6, Vector3.Forward);
        Quad(4, 0, 3, 5, Vector3.Left);
        Quad(1, 7, 6, 2, Vector3.Right);
    }

    public static (Node3D Root, MeshInstance3D LampMesh) CreateRailSignalNode(RailSignalAspect aspect)
    {
        var root = new Node3D();
        var postMat = GetMat("1e293b", 0.45f, 0.4f);
        AddBox(root, new Vector3(0.10f, 0.92f, 0.10f), new Vector3(0.55f, 0.46f, 0.55f), postMat);
        AddBox(root, new Vector3(0.22f, 0.34f, 0.16f), new Vector3(0.55f, 0.88f, 0.55f), postMat);

        var lampMesh = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.16f, 0.16f, 0.20f) },
            Position = new Vector3(0.55f, 0.90f, 0.55f),
            MaterialOverride = aspect == RailSignalAspect.Red
                ? GetMat("ef4444", 0.2f, 0f, emission: true, emissionEnergy: 2.2f)
                : GetMat("22c55e", 0.2f, 0f, emission: true, emissionEnergy: 1.8f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        root.AddChild(lampMesh);
        return (root, lampMesh);
    }

    public static void UpdateRailSignalLamp(MeshInstance3D lampMesh, RailSignalAspect aspect)
    {
        lampMesh.MaterialOverride = aspect == RailSignalAspect.Red
            ? GetMat("ef4444", 0.2f, 0f, emission: true, emissionEnergy: 2.2f)
            : GetMat("22c55e", 0.2f, 0f, emission: true, emissionEnergy: 1.8f);
    }

    public static Node3D CreateIntermodalYardNode(IntermodalTerminal yard)
    {
        var root = new Node3D();
        var concreteMat = GetMat("94a3b8", 0.85f);
        var gantryYellow = GetMat("f59e0b", 0.40f, 0.25f);
        var steelMat = GetMat("334155", 0.55f, 0.35f);
        var containerBlue = GetMat("2563eb", 0.55f);
        var containerOrange = GetMat("ea580c", 0.55f);
        var roofGreen = GetMat("10b981", 0.65f);

        AddBox(root, new Vector3(1.92f, 0.08f, 1.92f), new Vector3(0f, 0.04f, 0f), concreteMat, castShadow: false);

        // Pórtico guindaste intermodal amarelo (Ponte Rolante Ferrovia <-> Caminhão)
        AddBox(root, new Vector3(0.14f, 1.15f, 0.14f), new Vector3(-0.72f, 0.60f, -0.55f), gantryYellow);
        AddBox(root, new Vector3(0.14f, 1.15f, 0.14f), new Vector3(0.72f, 0.60f, -0.55f), gantryYellow);
        AddBox(root, new Vector3(0.14f, 1.15f, 0.14f), new Vector3(-0.72f, 0.60f, 0.55f), gantryYellow);
        AddBox(root, new Vector3(0.14f, 1.15f, 0.14f), new Vector3(0.72f, 0.60f, 0.55f), gantryYellow);
        AddBox(root, new Vector3(1.64f, 0.14f, 0.22f), new Vector3(0f, 1.22f, -0.55f), gantryYellow);
        AddBox(root, new Vector3(1.64f, 0.14f, 0.22f), new Vector3(0f, 1.22f, 0.55f), gantryYellow);
        AddBox(root, new Vector3(0.28f, 0.16f, 1.28f), new Vector3(0.12f, 1.18f, 0f), steelMat);

        // Pilha de contêineres ISO e escritório operacional
        AddBox(root, new Vector3(0.46f, 0.28f, 0.24f), new Vector3(-0.36f, 0.22f, -0.15f), containerBlue);
        AddBox(root, new Vector3(0.46f, 0.28f, 0.24f), new Vector3(-0.36f, 0.22f, 0.15f), containerOrange);
        AddBox(root, new Vector3(0.46f, 0.28f, 0.24f), new Vector3(-0.36f, 0.50f, 0.0f), containerBlue);
        AddBox(root, new Vector3(0.55f, 0.36f, 0.55f), new Vector3(0.48f, 0.26f, 0.10f), steelMat);
        AddBox(root, new Vector3(0.60f, 0.08f, 0.60f), new Vector3(0.48f, 0.48f, 0.10f), roofGreen, castShadow: false);

        string badge = yard.LinkedSeaportId.HasValue ? $"🏗️🚢 {yard.Name}" : $"🏗️🚂 {yard.Name}";
        root.AddChild(CreateFloatingLabel(badge, new Color("fb923c"), 1.75f, 28));
        OptimizeStaticChildren(root);
        return root;
    }

    public static TrainVisualHandle CreateTrainNode(Train train)
    {
        var root = new Node3D();
        var bodyBlue = GetMat("1e3a8a", 0.35f, 0.35f);
        var noseYellow = GetMat("eab308", 0.35f, 0.20f);
        var chassisMat = GetMat("1e293b", 0.65f, 0.30f);

        // Locomotiva Diesel-Elétrica dianteira
        var loco = new Node3D();
        AddBox(loco, new Vector3(0.54f, 0.14f, 1.18f), new Vector3(0f, 0.14f, 0f), chassisMat);
        AddBox(loco, new Vector3(0.50f, 0.34f, 0.88f), new Vector3(0f, 0.36f, 0.08f), bodyBlue);
        AddBox(loco, new Vector3(0.52f, 0.42f, 0.32f), new Vector3(0f, 0.40f, -0.32f), noseYellow);
        AddBox(loco, new Vector3(0.18f, 0.14f, 0.26f), new Vector3(0f, 0.58f, 0.18f), chassisMat);
        root.AddChild(loco);

        var hlGroup = new Node3D { Position = new Vector3(0f, 0.32f, -0.58f) };
        var beamMat = GetMat("fef08a", 0.1f, 0f, emission: true, emissionEnergy: 2.0f);
        AddBox(hlGroup, new Vector3(0.16f, 0.10f, 0.06f), Vector3.Zero, beamMat, castShadow: false);
        root.AddChild(hlGroup);

        // Vagões articulados especializados
        var wagons = new List<Node3D>();
        var cargoMeshes = new List<MeshInstance3D>();
        int wCount = Math.Min(4, train.WagonCount);
        for (int i = 0; i < wCount; i++)
        {
            var wagon = new Node3D();
            AddBox(wagon, new Vector3(0.48f, 0.10f, 0.84f), new Vector3(0f, 0.12f, 0f), chassisMat);

            var cargoBox = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.42f, 0.26f, 0.72f) },
                Position = new Vector3(0f, 0.30f, 0f),
                MaterialOverride = GetWagonVisualMaterial(train.WagonCategory, train.LoadedCargoAmount > 0),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            wagon.AddChild(cargoBox);
            root.AddChild(wagon);
            wagons.Add(wagon);
            cargoMeshes.Add(cargoBox);
        }

        return new TrainVisualHandle(root, wagons, cargoMeshes, hlGroup)
        {
            DisplayedCategory = train.WagonCategory,
            DisplayedCargoAmount = train.LoadedCargoAmount
        };
    }

    public static Material GetWagonVisualMaterial(RailWagonCategory category, bool hasCargo)
    {
        if (!hasCargo)
            return GetMat("64748b", 0.75f);

        return category switch
        {
            RailWagonCategory.MineralHopper => GetMat("991b1b", 0.72f),
            RailWagonCategory.GrainHopper => GetMat("f59e0b", 0.65f),
            RailWagonCategory.TimberFlatbed => GetMat("78350f", 0.82f),
            RailWagonCategory.ContainerIntermodal => GetMat("0284c7", 0.45f, 0.20f),
            _ => GetMat("475569", 0.65f)
        };
    }
}

