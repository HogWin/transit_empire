using System;
using System.Collections.Generic;
using Godot;
using TransitEmpire.Core.Domain;
using TransitEmpire.Core.Simulation;

namespace TransitEmpire.Client.Scripts;

/// <summary>
/// Controle customizado de Minimapa Estratégico (terreno/biomas, vias, estruturas, frota ao vivo e navegação por clique/arrasto).
/// </summary>
public partial class MinimapOverlay : Control
{
    public event Action<int, int>? OnNavigateToCell;

    public MapOverlayMode OverlayMode { get; set; } = MapOverlayMode.Standard;

    private ImageTexture? _mapTexture;
    private int _gridWidth = 100;
    private int _gridHeight = 100;
    private int[,]? _trafficBuffer;
    private Vector2 _cameraGridPos = new(50, 50);
    private float _cameraDistance = 32f;
    private readonly List<(Vector2 GridPos, bool ManualSchedule)> _truckDots = new();
    private bool _isDragging = false;
    private bool _needsRedraw = true;

    public MinimapOverlay()
    {
        CustomMinimumSize = new Vector2(164, 164);
        TextureFilter = TextureFilterEnum.Nearest;
        MouseDefaultCursorShape = CursorShape.PointingHand;
    }

    public void RebuildStaticMap(SimulationBridge bridge)
    {
        var grid = bridge.World.Grid;
        _gridWidth = grid.GetLength(0);
        _gridHeight = grid.GetLength(1);

        if (_trafficBuffer == null || _trafficBuffer.GetLength(0) != _gridWidth || _trafficBuffer.GetLength(1) != _gridHeight)
            _trafficBuffer = new int[_gridWidth, _gridHeight];

        if (OverlayMode == MapOverlayMode.Traffic)
            bridge.Engine.FillCellTrafficMap(_trafficBuffer);

        var img = Image.CreateEmpty(_gridWidth, _gridHeight, false, Image.Format.Rgba8);

        for (int x = 0; x < _gridWidth; x++)
        {
            for (int y = 0; y < _gridHeight; y++)
            {
                var cell = grid[x, y];
                Color col = GetTerrainBiomeColor(cell);
                if (OverlayMode != MapOverlayMode.Standard)
                    col = col.Darkened(0.40f);

                if (cell.HasRoad)
                {
                    if (OverlayMode == MapOverlayMode.Traffic)
                    {
                        int count = _trafficBuffer[x, y];
                        col = count switch
                        {
                            0 => new Color("22c55e"),
                            1 or 2 => new Color("facc15"),
                            _ => new Color("ef4444")
                        };
                    }
                    else if (OverlayMode == MapOverlayMode.Pavement)
                    {
                        col = cell.RoadLevel switch
                        {
                            RoadTier.Dirt => new Color("a16207"),
                            RoadTier.Gravel => new Color("e2e8f0"),
                            RoadTier.Asphalt => new Color("3b82f6"),
                            RoadTier.Highway => new Color("f59e0b"),
                            RoadTier.DualCarriage => new Color("06b6d4"),
                            _ => new Color("a16207")
                        };
                    }
                    else if (OverlayMode == MapOverlayMode.Profitability)
                    {
                        long rev = bridge.Engine.GetCellCorridorRevenue(x, y);
                        if (rev <= 0)
                            col = new Color("475569");
                        else if (rev < 800)
                            col = new Color("eab308");
                        else
                            col = new Color("10b981");
                    }
                    else
                    {
                        col = cell.StructureType == RoadStructureType.Bridge
                            ? new Color("60a5fa")
                            : cell.RoadLevel switch
                            {
                                RoadTier.Dirt => new Color("92400e"),
                                RoadTier.Gravel => new Color("cbd5e1"),
                                RoadTier.Asphalt => new Color("334155"),
                                RoadTier.Highway => new Color("f59e0b"),
                                RoadTier.DualCarriage => new Color("38bdf8"),
                                _ => new Color("92400e")
                            };
                    }
                }

                img.SetPixel(x, y, col);
            }
        }

        // Cidades (amarelo vibrante), Indústrias (vermelho/coral), Garagens (azul claro) e Hubs (esmeralda)
        foreach (var city in bridge.World.Cities)
        {
            foreach (var (ux, uy) in city.UrbanTiles)
            {
                if (ux >= 0 && ux < _gridWidth && uy >= 0 && uy < _gridHeight)
                    img.SetPixel(ux, uy, (ux == city.CenterX && uy == city.CenterY) ? new Color("fef08a") : new Color("eab308"));
            }
        }

        foreach (var ind in bridge.World.Industries)
        {
            PaintCross(img, ind.GridX, ind.GridY, new Color("ef4444"));
        }

        foreach (var garage in bridge.Engine.Garages)
        {
            if (garage.GridX >= 0 && garage.GridX < _gridWidth && garage.GridY >= 0 && garage.GridY < _gridHeight)
                img.SetPixel(garage.GridX, garage.GridY, new Color("38bdf8"));
        }

        foreach (var hub in bridge.Engine.LogisticsHubs)
        {
            if (hub.GridX >= 0 && hub.GridX < _gridWidth && hub.GridY >= 0 && hub.GridY < _gridHeight)
                img.SetPixel(hub.GridX, hub.GridY, new Color("10b981"));
        }

        foreach (var seaport in bridge.World.Seaports)
        {
            PaintCross(img, seaport.GridX, seaport.GridY, new Color("22d3ee"), radius: 1);
        }

        foreach (var lm in bridge.World.Landmarks)
        {
            PaintCross(img, lm.GridX, lm.GridY, new Color("c084fc"), radius: 1);
        }

        _mapTexture = ImageTexture.CreateFromImage(img);
        _needsRedraw = true;
        QueueRedraw();
    }

    private void PaintCross(Image img, int cx, int cy, Color col, int radius = 0)
    {
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                if (Math.Abs(dx) + Math.Abs(dy) > radius && radius > 0) continue;
                int x = cx + dx;
                int y = cy + dy;
                if (x >= 0 && x < _gridWidth && y >= 0 && y < _gridHeight)
                    img.SetPixel(x, y, col);
            }
        }
    }

    private static Color GetTerrainBiomeColor(in GridCell cell)
    {
        if (cell.Terrain == TerrainType.Water)
            return new Color("2563eb");

        if (cell.Biome == BiomeType.Desert)
            return cell.Terrain == TerrainType.Hills ? new Color("b87b3e") : new Color("dfb352");

        if (cell.Biome == BiomeType.Alpine)
            return cell.Terrain == TerrainType.Hills ? new Color("f1f5f9") : new Color("7d9487");

        return cell.Terrain == TerrainType.Hills ? new Color("3d7a36") : new Color("53a348");
    }

    public void UpdateDynamicMarkers(Vector2 cameraGridPos, float cameraDistance, IReadOnlyList<(Vector2 GridPos, bool Manual)> trucks)
    {
        bool changed = _needsRedraw
            || _cameraGridPos.DistanceSquaredTo(cameraGridPos) > 0.0025f
            || MathF.Abs(_cameraDistance - cameraDistance) > 0.05f
            || _truckDots.Count != trucks.Count;

        if (!changed)
        {
            for (int i = 0; i < trucks.Count; i++)
            {
                if (_truckDots[i].GridPos.DistanceSquaredTo(trucks[i].GridPos) > 0.0025f || _truckDots[i].ManualSchedule != trucks[i].Manual)
                {
                    changed = true;
                    break;
                }
            }
        }

        if (!changed)
            return;

        _needsRedraw = false;
        _cameraGridPos = cameraGridPos;
        _cameraDistance = cameraDistance;
        _truckDots.Clear();
        for (int i = 0; i < trucks.Count; i++)
            _truckDots.Add(trucks[i]);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_mapTexture != null)
        {
            DrawTextureRect(_mapTexture, new Rect2(Vector2.Zero, Size), false);
        }

        // Borda nítida do minimapa
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("475569"), filled: false, width: 1.5f);

        float scaleX = Size.X / Math.Max(1, _gridWidth);
        float scaleY = Size.Y / Math.Max(1, _gridHeight);

        // Pontos dos caminhões ativos
        foreach (var (gp, manual) in _truckDots)
        {
            Vector2 pt = new(gp.X * scaleX, gp.Y * scaleY);
            DrawCircle(pt, 3.2f, new Color("0f172a"));
            DrawCircle(pt, 2.2f, manual ? new Color("facc15") : new Color("22d3ee"));
        }

        // Retângulo de foco da câmera
        float viewSpan = Mathf.Clamp(_cameraDistance * 0.45f, 5f, _gridWidth * 0.65f);
        Vector2 boxSize = new(viewSpan * scaleX, viewSpan * scaleY);
        Vector2 camCenter = new(_cameraGridPos.X * scaleX, _cameraGridPos.Y * scaleY);
        Rect2 camRect = new(camCenter - boxSize * 0.5f, boxSize);
        DrawRect(camRect, new Color(1f, 1f, 1f, 0.92f), filled: false, width: 1.5f);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.ButtonIndex == MouseButton.Left)
        {
            _isDragging = mb.Pressed;
            if (mb.Pressed)
            {
                DispatchNavigationAt(mb.Position);
                AcceptEvent();
            }
        }
        else if (@event is InputEventMouseMotion mm && _isDragging)
        {
            DispatchNavigationAt(mm.Position);
            AcceptEvent();
        }
    }

    private void DispatchNavigationAt(Vector2 localPos)
    {
        float nx = Mathf.Clamp(localPos.X / Math.Max(1f, Size.X), 0f, 0.999f);
        float ny = Mathf.Clamp(localPos.Y / Math.Max(1f, Size.Y), 0f, 0.999f);
        int gx = Mathf.Clamp((int)(nx * _gridWidth), 0, _gridWidth - 1);
        int gy = Mathf.Clamp((int)(ny * _gridHeight), 0, _gridHeight - 1);
        OnNavigateToCell?.Invoke(gx, gy);
    }
}

