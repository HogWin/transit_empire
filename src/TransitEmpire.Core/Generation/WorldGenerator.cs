using System;
using System.Collections.Generic;
using System.Linq;
using TransitEmpire.Core.Domain;

namespace TransitEmpire.Core.Generation;

public enum WorldScenario
{
    Balanced = 0,
    CoastalArchipelago = 1,
    AlpineRange = 2
}

public enum MountainDensity
{
    Few = 0,
    Normal = 1,
    Many = 2
}

public record WorldGenerationConfig(
    int Width = 100,
    int Height = 100,
    int CityCount = 6,
    int IndustryCount = 10,
    int Seed = 1337,
    WorldScenario Scenario = WorldScenario.Balanced,
    bool HasRivers = true,
    MountainDensity Mountains = MountainDensity.Normal
);

public class GeneratedWorld
{
    public const int MaxRegionsCount = 5;

    public GridCell[,] Grid { get; }
    public List<City> Cities { get; } = new();
    public List<Industry> Industries { get; } = new();
    public List<SeaportTerminal> Seaports { get; } = new();
    public List<RegionalLandmark> Landmarks { get; } = new();
    public List<RoadNode> RoadNodes { get; } = new();
    public List<RoadEdge> RoadEdges { get; } = new();
    public int UnlockedRegionsCount { get; set; } = 1;

    /// <summary>Marca células pertencentes ao oceano aberto (separando continentes).</summary>
    public bool[,] OceanMask { get; }

    /// <summary>Identificador do continente/ilha de cada célula terrestre (0 = água oceânica).</summary>
    public int[,] ContinentMap { get; }

    public int MainlandContinentId { get; internal set; } = 1;
    public int OverseasContinentId { get; internal set; } = 0;

    public GeneratedWorld(int width, int height)
    {
        Grid = new GridCell[width, height];
        OceanMask = new bool[width, height];
        ContinentMap = new int[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Grid[x, y] = new GridCell(x, y, TerrainType.Plains);
            }
        }
    }

    public bool AreSameContinent(int x1, int y1, int x2, int y2)
    {
        int w = Grid.GetLength(0);
        int h = Grid.GetLength(1);
        if (x1 < 0 || x1 >= w || y1 < 0 || y1 >= h || x2 < 0 || x2 >= w || y2 < 0 || y2 >= h)
            return false;

        int c1 = ContinentMap[x1, y1];
        int c2 = ContinentMap[x2, y2];
        if (c1 == 0 || c2 == 0)
            return true;
        return c1 == c2;
    }
}

/// <summary>
/// Gerador procedural lógico do mundo:
/// cria continentes separados por oceano navegável, cordilheiras, rios que deságuam no mar,
/// biomas coerentes, cidades/indústrias posicionadas por geografia e malha viária sem sobreposição de prédios.
/// </summary>
public class WorldGenerator
{
    private readonly WorldGenerationConfig _config;
    private readonly Random _rng;

    public WorldGenerator(WorldGenerationConfig? config = null)
    {
        _config = config ?? new WorldGenerationConfig();
        _rng = new Random(_config.Seed);
    }

    public GeneratedWorld Generate()
    {
        var world = new GeneratedWorld(_config.Width, _config.Height);

        // 1. Geografia lógica: Oceano + Continentes, Cordilheiras, Rios até o mar e Biomas coerentes
        GenerateLogicalGeography(world);

        // 2. Rotula massas continentais separadas pelo oceano aberto
        LabelContinents(world);

        // 3. Posiciona Cidades espaçadas em planícies férteis/costeiras ou fluviais
        GenerateCities(world);

        // 4. Posiciona Indústrias por lógica geográfica e cadeia logística
        GenerateIndustries(world);

        // 5. Terminais Portuários conectados ao Oceano ligando continentes distintos + Mega-Obras lógicas
        if (world.Grid.GetLength(0) >= 25 && world.Grid.GetLength(1) >= 25)
        {
            GenerateSeaports(world);
            GenerateRegionalLandmarks(world);
        }

        // 6. Malha viária inicial por continente (nunca sobre prédios centrais nem cruzando o oceano aberto)
        GenerateInitialRoadNetwork(world);

        return world;
    }

    private void GenerateLogicalGeography(GeneratedWorld world)
    {
        int w = _config.Width;
        int h = _config.Height;
        bool hasOceanChannel = w >= 65 && h >= 65;
        bool isArchipelago = _config.Scenario == WorldScenario.CoastalArchipelago;
        bool isAlpine = _config.Scenario == WorldScenario.AlpineRange;

        float phaseA = (float)_rng.NextDouble() * MathF.Tau;
        float phaseB = (float)_rng.NextDouble() * MathF.Tau;
        float phaseC = (float)_rng.NextDouble() * MathF.Tau;

        // A. Canal Oceânico navegável dividindo Continente Principal (Oeste) e Continente Ultramarino (Leste)
        if (hasOceanChannel)
        {
            float baseSeaX = w * ((isArchipelago ? 0.56f : 0.61f) + (float)_rng.NextDouble() * 0.05f);
            float seaWidthScale = isArchipelago ? 1.45f : (isAlpine ? 0.85f : 1.0f);
            for (int y = 0; y < h; y++)
            {
                float ny = (float)y / Math.Max(1, h - 1);
                float seaCenter = baseSeaX
                    + MathF.Sin(ny * MathF.Tau * 1.15f + phaseA) * (w * (isArchipelago ? 0.085f : 0.055f))
                    + MathF.Cos(ny * MathF.Tau * 2.4f + phaseB) * (w * 0.025f);
                float halfWidth = Math.Max(4.5f, (w * 0.055f + MathF.Sin(ny * MathF.Tau * 1.8f + phaseC) * 1.6f) * seaWidthScale);

                int minSeaX = Math.Clamp((int)MathF.Floor(seaCenter - halfWidth), 12, w - 18);
                int maxSeaX = Math.Clamp((int)MathF.Ceiling(seaCenter + halfWidth), minSeaX + 7, w - 14);

                for (int x = minSeaX; x <= maxSeaX; x++)
                {
                    world.Grid[x, y].Terrain = TerrainType.Water;
                    world.OceanMask[x, y] = true;
                }
            }

            if (isArchipelago)
            {
                // Enseadas e braços costeiros adicionais para criar ilhas e penínsulas navegáveis
                int inletY = Math.Clamp((int)(h * 0.48f), 10, h - 11);
                for (int x = Math.Max(6, (int)(w * 0.24f)); x < (int)(w * 0.62f); x++)
                {
                    float wv = MathF.Sin(x * 0.22f + phaseB) * 2.2f;
                    int cy = Math.Clamp(inletY + (int)MathF.Round(wv), 6, h - 7);
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        int yy = cy + dy;
                        if (yy >= 4 && yy < h - 4)
                        {
                            world.Grid[x, yy].Terrain = TerrainType.Water;
                            world.OceanMask[x, yy] = true;
                        }
                    }
                }
            }
        }
        else
        {
            // Mapas compactos (ex.: testes ou 50x50): enseada oceânica conectada à borda Leste/Sul
            int bayWidth = isArchipelago ? Math.Max(6, w / 5) : Math.Max(3, w / 9);
            int bayHeight = isArchipelago ? Math.Max(10, (int)(h * 0.78f)) : Math.Max(6, h / 2);
            int bayStartY = Math.Max(2, (h - bayHeight) / 2);
            for (int y = bayStartY; y < Math.Min(h, bayStartY + bayHeight); y++)
            {
                float wave = MathF.Sin(y * 0.35f + phaseA) * (isArchipelago ? 2.0f : 1.1f);
                int startX = Math.Clamp(w - bayWidth + (int)MathF.Round(wave), w / 2, w - 2);
                for (int x = startX; x < w; x++)
                {
                    world.Grid[x, y].Terrain = TerrainType.Water;
                    world.OceanMask[x, y] = true;
                }
            }
        }

        // B. Cordilheiras & Colinas (sopés em volta das montanhas para relevo natural)
        int[,] elevation = new int[w, h]; // 0 = planície, 1 = colina, 2 = montanha
        int baseRadius = isAlpine ? (hasOceanChannel ? 4 : 3) : (isArchipelago ? 1 : (hasOceanChannel ? 3 : 2));
        int ridgeRadius = _config.Mountains switch
        {
            MountainDensity.Few => Math.Max(1, baseRadius - 1),
            MountainDensity.Many => baseRadius + 1,
            _ => baseRadius
        };
        float peakChanceScale = _config.Mountains switch
        {
            MountainDensity.Few => 0.45f,
            MountainDensity.Many => 1.35f,
            _ => 1.0f
        };

        CarveMountainRidge(
            world,
            elevation,
            startX: Math.Clamp((int)(w * 0.16f), 4, w - 6),
            startY: Math.Clamp((int)(h * 0.16f), 4, h - 6),
            endX: Math.Clamp((int)(w * (hasOceanChannel ? 0.48f : 0.68f)), 6, w - 6),
            endY: Math.Clamp((int)(h * 0.28f), 6, h - 6),
            halfRadius: ridgeRadius,
            phase: phaseA,
            peakScale: peakChanceScale);

        if ((hasOceanChannel && !isArchipelago && _config.Mountains != MountainDensity.Few) ||
            _config.Mountains == MountainDensity.Many)
        {
            // Segunda cadeia de colinas ao centro-sul do continente principal
            CarveMountainRidge(
                world,
                elevation,
                startX: (int)(w * 0.12f),
                startY: (int)(h * 0.62f),
                endX: (int)(w * 0.28f),
                endY: (int)(h * 0.78f),
                halfRadius: _config.Mountains == MountainDensity.Many ? 3 : (isAlpine ? 3 : 2),
                phase: phaseB,
                peakScale: peakChanceScale);

            if (hasOceanChannel)
            {
                // Cadeia de colinas minerais no continente ultramarino (Leste)
                CarveMountainRidge(
                    world,
                    elevation,
                    startX: Math.Clamp((int)(w * 0.84f), 6, w - 5),
                    startY: Math.Clamp((int)(h * 0.24f), 6, h - 6),
                    endX: Math.Clamp((int)(w * 0.90f), 6, w - 4),
                    endY: Math.Clamp((int)(h * 0.56f), 6, h - 6),
                    halfRadius: _config.Mountains == MountainDensity.Many ? 3 : (isAlpine ? 3 : 2),
                    phase: phaseC,
                    peakScale: peakChanceScale);
            }
        }

        if (isAlpine || _config.Mountains == MountainDensity.Many)
        {
            // Maciço central elevado com desfiladeiros para túneis
            CarveMountainRidge(
                world,
                elevation,
                startX: Math.Clamp((int)(w * 0.22f), 4, w - 6),
                startY: Math.Clamp((int)(h * 0.42f), 4, h - 6),
                endX: Math.Clamp((int)(w * 0.46f), 6, w - 6),
                endY: Math.Clamp((int)(h * 0.50f), 6, h - 6),
                halfRadius: hasOceanChannel ? 3 : 2,
                phase: phaseC + 1.1f,
                peakScale: peakChanceScale);
        }

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                if (world.OceanMask[x, y]) continue;
                world.Grid[x, y].Terrain = elevation[x, y] switch
                {
                    >= 2 => TerrainType.Mountain,
                    1 => TerrainType.Hills,
                    _ => TerrainType.Plains
                };
            }
        }

        // C. Rios sinuosos nascendo nas serras e desaguando no Oceano
        if (_config.HasRivers)
        {
            CarveRiverToSea(
                world,
                startX: Math.Clamp((int)(w * 0.20f), 4, w - 6),
                startY: Math.Clamp((int)(h * 0.24f), 4, h - 6),
                hasOceanChannel: hasOceanChannel,
                meanderPhase: phaseB);

            if (hasOceanChannel)
            {
                CarveRiverToSea(
                    world,
                    startX: Math.Clamp((int)(w * 0.18f), 4, w - 6),
                    startY: Math.Clamp((int)(h * 0.72f), 4, h - 6),
                    hasOceanChannel: true,
                    meanderPhase: phaseC);
            }
        }

        // D. Biomas coerentes com relevo, latitude e sombra de chuva
        int desertCenterX = Math.Clamp((int)(w * (hasOceanChannel ? 0.16f : 0.24f)), 4, w - 5);
        int desertCenterY = Math.Clamp((int)(h * 0.78f), 4, h - 5);
        float desertRadius = isAlpine ? 0f : Math.Max(7f, Math.Min(w, h) * 0.22f);
        float alpineHillCutoff = isAlpine ? 0.56f : 0.30f;
        float alpinePlainCutoff = isAlpine ? 0.36f : 0.20f;

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                float warp = MathF.Sin(x * 0.14f + phaseA) * 2.2f + MathF.Cos(y * 0.14f + phaseB) * 2.2f;
                ref var cell = ref world.Grid[x, y];

                if (cell.Terrain == TerrainType.Mountain || (y + warp < h * alpineHillCutoff && (cell.Terrain == TerrainType.Hills || elevation[x, y] > 0)))
                {
                    cell.Biome = BiomeType.Alpine;
                    continue;
                }

                float dx = x - desertCenterX;
                float dy = y - desertCenterY;
                float distDesert = MathF.Sqrt(dx * dx + dy * dy) + warp * 0.7f;

                // Bacia árida fora da calha imediata de rios/oceano
                bool onWaterEdge = HasWaterWithin(world, x, y, 1);
                if (!isArchipelago && !onWaterEdge && distDesert <= desertRadius && cell.Terrain != TerrainType.Water)
                {
                    cell.Biome = BiomeType.Desert;
                }
                else
                {
                    cell.Biome = (y + warp < h * alpinePlainCutoff && !world.OceanMask[x, y])
                        ? BiomeType.Alpine
                        : BiomeType.Temperate;
                }
            }
        }
    }

    private void CarveMountainRidge(
        GeneratedWorld world,
        int[,] elevation,
        int startX,
        int startY,
        int endX,
        int endY,
        int halfRadius,
        float phase,
        float peakScale = 1.0f)
    {
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        int steps = Math.Max(12, Math.Abs(endX - startX) + Math.Abs(endY - startY));
        double mountainProb = Math.Clamp(0.78 * peakScale, 0.25, 0.92);
        double hillProb = Math.Clamp(0.80 * peakScale, 0.40, 0.94);

        for (int s = 0; s <= steps; s++)
        {
            float t = (float)s / steps;
            float px = startX + (endX - startX) * t + MathF.Sin(t * MathF.PI * 2f + phase) * 2.1f;
            float py = startY + (endY - startY) * t + MathF.Cos(t * MathF.PI * 1.5f + phase) * 1.8f;
            int cx = Math.Clamp((int)MathF.Round(px), 2, w - 3);
            int cy = Math.Clamp((int)MathF.Round(py), 2, h - 3);

            for (int dx = -halfRadius; dx <= halfRadius; dx++)
            {
                for (int dy = -halfRadius; dy <= halfRadius; dy++)
                {
                    int nx = cx + dx;
                    int ny = cy + dy;
                    if (nx < 1 || nx >= w - 1 || ny < 1 || ny >= h - 1 || world.OceanMask[nx, ny])
                        continue;

                    float d = MathF.Sqrt(dx * dx + dy * dy);
                    if (d <= 0.8f * peakScale && _rng.NextDouble() < mountainProb)
                        elevation[nx, ny] = Math.Max(elevation[nx, ny], 2);
                    else if (d <= halfRadius + 0.35f && _rng.NextDouble() < hillProb)
                        elevation[nx, ny] = Math.Max(elevation[nx, ny], 1);
                }
            }
        }
    }

    private void CarveRiverToSea(GeneratedWorld world, int startX, int startY, bool hasOceanChannel, float meanderPhase)
    {
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        int rx = startX;
        int ry = startY;

        // Garante que a nascente comece em terra firme e avance em passos ortogonais 4-conectados
        for (int safety = 0; safety < w * 2; safety++)
        {
            if (rx < 1 || rx >= w - 1 || ry < 1 || ry >= h - 1)
                break;

            if (world.OceanMask[rx, ry])
                break;

            world.Grid[rx, ry].Terrain = TerrainType.Water;
            if (!hasOceanChannel && rx >= w - 2)
                break;

            // Suaviza curvas meandrantes ao redor da latitude do vale rumo ao oceano Leste
            float targetY = startY + MathF.Sin(safety * 0.28f + meanderPhase) * Math.Max(2.5f, h * 0.06f);
            int stepX = _rng.NextDouble() < 0.78 ? 1 : 0;
            int stepY = Math.Sign(targetY - ry);
            if (stepX == 1 && MathF.Abs(targetY - ry) < 0.75f)
                stepY = 0;
            if (stepX == 0 && stepY == 0)
                stepX = 1;

            int nextX = Math.Clamp(rx + stepX, 1, w - 1);
            int nextY = Math.Clamp(ry + stepY, 2, h - 3);

            // Mantém rio afastado de montanhas altas contornando quando possível
            if (world.Grid[nextX, nextY].Terrain == TerrainType.Mountain && rx + 1 < w)
            {
                nextX = rx + 1;
                nextY = ry;
            }

            // Nunca pula diagonalmente: preenche tile intermediário ortogonal antes de dobrar
            if (nextX != rx && nextY != ry)
            {
                int midX = nextX;
                int midY = ry;
                if (world.Grid[midX, midY].Terrain == TerrainType.Mountain)
                {
                    midX = rx;
                    midY = nextY;
                }
                if (midX >= 1 && midX < w - 1 && midY >= 1 && midY < h - 1 && !world.OceanMask[midX, midY])
                    world.Grid[midX, midY].Terrain = TerrainType.Water;
            }

            if (nextX == rx && nextY == ry)
                nextX = Math.Min(w - 1, rx + 1);

            rx = nextX;
            ry = nextY;
        }
    }

    public static void LabelContinents(GeneratedWorld world)
    {
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        var sizes = new Dictionary<int, int>();
        var queue = new Queue<(int X, int Y)>();
        int nextId = 1;
        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                if (world.OceanMask[x, y] || world.ContinentMap[x, y] != 0)
                    continue;

                int cid = nextId++;
                int count = 0;
                world.ContinentMap[x, y] = cid;
                queue.Enqueue((x, y));

                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    count++;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = cx + dx[d];
                        int ny = cy + dy[d];
                        if (nx >= 0 && nx < w && ny >= 0 && ny < h && !world.OceanMask[nx, ny] && world.ContinentMap[nx, ny] == 0)
                        {
                            world.ContinentMap[nx, ny] = cid;
                            queue.Enqueue((nx, ny));
                        }
                    }
                }
                sizes[cid] = count;
            }
        }

        if (sizes.Count == 0)
            return;

        // Continente principal = maior massa oeste; Ultramarino = maior massa separada no lado leste
        var ordered = sizes.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
        world.MainlandContinentId = ordered[0];
        world.OverseasContinentId = 0;

        if (ordered.Count > 1)
        {
            // Escolhe segunda maior massa com área suficiente para abrigar cidade/porto (>= 80 tiles)
            foreach (int cid in ordered.Skip(1))
            {
                if (sizes[cid] >= 80)
                {
                    world.OverseasContinentId = cid;
                    break;
                }
            }
        }
    }

    private void GenerateCities(GeneratedWorld world)
    {
        int w = _config.Width;
        int h = _config.Height;
        var usedCityNames = new HashSet<string>();

        bool hasOverseas = world.OverseasContinentId > 0 && _config.CityCount >= 3;
        int overseasTarget = !hasOverseas ? 0 : (_config.CityCount >= 5 ? 2 : 1);
        int mainlandTarget = _config.CityCount - overseasTarget;

        int baseMinDist = Math.Clamp(Math.Min(w, h) / 4, 14, 28);

        for (int i = 0; i < _config.CityCount; i++)
        {
            int targetContinent = (i >= mainlandTarget && overseasTarget > 0)
                ? world.OverseasContinentId
                : world.MainlandContinentId;

            int minDist = baseMinDist;
            (int X, int Y)? chosen = null;

            for (int relax = 0; relax < 3 && chosen == null; relax++)
            {
                chosen = PickBestCitySite(world, targetContinent, minDist, requireTemperate: i == 0 && relax == 0);
                minDist = Math.Max(10, (int)(minDist * 0.75f));
            }

            // Fallback caso o continente alvo esteja cheio em mapas customizados pequenos
            if (chosen == null)
                chosen = PickBestCitySite(world, 0, Math.Max(8, minDist / 2), requireTemperate: false);

            if (chosen == null)
                continue;

            int cx = chosen.Value.X;
            int cy = chosen.Value.Y;
            string name = GenerateRandomCityName(usedCityNames);
            var city = new City(world.Cities.Count + 1, name, cx, cy);
            world.Cities.Add(city);

            foreach (var (ux, uy) in city.UrbanTiles)
            {
                if (ux >= 0 && ux < w && uy >= 0 && uy < h && !world.OceanMask[ux, uy] && world.Grid[ux, uy].Terrain != TerrainType.Water)
                {
                    world.Grid[ux, uy].Terrain = TerrainType.Plains;
                    world.Grid[ux, uy].OccupantId = city.Id;
                    world.Grid[ux, uy].RoadLevel = RoadTier.None;
                }
            }
            // Garante que o núcleo central da cidade sempre esteja em planície ocupada e livre de estrada
            world.Grid[cx, cy].Terrain = TerrainType.Plains;
            world.Grid[cx, cy].OccupantId = city.Id;
            world.Grid[cx, cy].RoadLevel = RoadTier.None;
        }
    }

    private (int X, int Y)? PickBestCitySite(GeneratedWorld world, int requiredContinentId, int minCityDist, bool requireTemperate)
    {
        int w = _config.Width;
        int h = _config.Height;
        int marginX = Math.Clamp(w / 14, 4, 7);
        int marginY = Math.Clamp(h / 14, 4, 7);

        (int X, int Y)? best = null;
        float bestScore = float.MinValue;

        for (int attempt = 0; attempt < 260; attempt++)
        {
            int cx = _rng.Next(marginX, Math.Max(marginX + 1, w - marginX));
            int cy = _rng.Next(marginY, Math.Max(marginY + 1, h - marginY));

            if (requiredContinentId > 0 && world.ContinentMap[cx, cy] != requiredContinentId)
                continue;

            var cell = world.Grid[cx, cy];
            if (cell.Terrain != TerrainType.Plains || cell.OccupantId != 0 || world.OceanMask[cx, cy])
                continue;

            if (requireTemperate && cell.Biome != BiomeType.Temperate)
                continue;

            // Garante área urbana 3x3 firme (sem água oceânica ou montanha debaixo dos quarteirões)
            bool areaClear = true;
            int waterNeighbors = 0;
            for (int dx = -1; dx <= 1 && areaClear; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    var u = world.Grid[cx + dx, cy + dy];
                    if (u.OccupantId != 0 || u.Terrain == TerrainType.Mountain || u.Terrain == TerrainType.Water)
                    {
                        areaClear = false;
                        break;
                    }
                    if (HasWaterWithin(world, cx + dx, cy + dy, 2))
                        waterNeighbors++;
                }
            }
            if (!areaClear) continue;

            // Garante ao menos 1 ponto de acesso em terra firme adjacente ao núcleo urbano
            bool hasDryStreet = HasDryAccessAround(world, cx, cy);
            if (!hasDryStreet) continue;

            bool tooClose = world.Cities.Any(c => Math.Abs(c.CenterX - cx) + Math.Abs(c.CenterY - cy) < minCityDist);
            if (tooClose) continue;

            float score = 0f;
            if (cell.Biome == BiomeType.Temperate) score += 18f;
            if (HasWaterWithin(world, cx, cy, 6) && !HasWaterWithin(world, cx, cy, 1)) score += 16f; // Perto de rio/costa é atrativo
            score += Math.Min(waterNeighbors, 4) * 2f;
            if (world.Cities.Count > 0)
            {
                float nearestCity = world.Cities.Min(c => MathF.Abs(c.CenterX - cx) + MathF.Abs(c.CenterY - cy));
                score += Math.Min(nearestCity, 45f) * 0.4f;
            }
            score += (float)_rng.NextDouble() * 6f;

            if (score > bestScore)
            {
                bestScore = score;
                best = (cx, cy);
            }
        }

        return best;
    }

    private void GenerateIndustries(GeneratedWorld world)
    {
        int w = _config.Width;
        int h = _config.Height;

        // Primárias primeiro (para que processadoras secundárias sejam alocadas entre fonte e cidades!)
        var primaryTemplates = new (ResourceType Output, ResourceType Input, ResourceType SecondaryInput, ResourceType TertiaryInput)[]
        {
            (ResourceType.Grain, ResourceType.None, ResourceType.Fuel, ResourceType.None),
            (ResourceType.IronOre, ResourceType.None, ResourceType.None, ResourceType.None),
            (ResourceType.Coal, ResourceType.None, ResourceType.None, ResourceType.None),
            (ResourceType.CrudeOil, ResourceType.None, ResourceType.None, ResourceType.None),
            (ResourceType.Lumber, ResourceType.None, ResourceType.None, ResourceType.None)
        };

        var secondaryTemplates = new (ResourceType Output, ResourceType Input, ResourceType SecondaryInput, ResourceType TertiaryInput)[]
        {
            (ResourceType.Food, ResourceType.Grain, ResourceType.None, ResourceType.None),
            (ResourceType.Steel, ResourceType.IronOre, ResourceType.Coal, ResourceType.None),
            (ResourceType.Fuel, ResourceType.CrudeOil, ResourceType.None, ResourceType.None),
            (ResourceType.Machinery, ResourceType.Steel, ResourceType.Lumber, ResourceType.Fuel),
            (ResourceType.Goods, ResourceType.Lumber, ResourceType.Steel, ResourceType.None)
        };

        var orderedTemplates = new List<(ResourceType Output, ResourceType Input, ResourceType SecondaryInput, ResourceType TertiaryInput)>();
        int total = _config.IndustryCount;
        int primaryCount = (total + 1) / 2;
        for (int i = 0; i < primaryCount; i++)
            orderedTemplates.Add(primaryTemplates[i % primaryTemplates.Length]);
        for (int i = 0; i < total - primaryCount; i++)
            orderedTemplates.Add(secondaryTemplates[i % secondaryTemplates.Length]);

        bool hasOverseas = world.OverseasContinentId > 0 && world.Cities.Any(c => world.ContinentMap[c.CenterX, c.CenterY] == world.OverseasContinentId);
        int overseasTargetCount = !hasOverseas ? 0 : Math.Max(2, total / 4);

        var usedNames = new HashSet<string>();
        int minCityDist = Math.Clamp(Math.Min(w, h) / 8, 6, 13);
        int minIndDist = Math.Clamp(Math.Min(w, h) / 9, 5, 11);

        int placedOverseas = 0;
        for (int i = 0; i < orderedTemplates.Count; i++)
        {
            var tmpl = orderedTemplates[i];

            // Distribui parte das indústrias raras/secundárias no continente ultramarino
            bool wantOverseas = hasOverseas && placedOverseas < overseasTargetCount && (i == 2 || i == 3 || i == orderedTemplates.Count - 1 || i % 4 == 3);
            int targetContinent = wantOverseas ? world.OverseasContinentId : world.MainlandContinentId;

            (int X, int Y)? chosen = PickLogicalIndustrySite(world, tmpl.Output, tmpl.Input, targetContinent, minCityDist, minIndDist)
                ?? PickLogicalIndustrySite(world, tmpl.Output, tmpl.Input, targetContinent, Math.Max(4, minCityDist - 3), Math.Max(4, minIndDist - 3))
                ?? PickLogicalIndustrySite(world, tmpl.Output, tmpl.Input, 0, Math.Max(4, minCityDist - 4), Math.Max(4, minIndDist - 4));

            if (chosen == null)
                continue;

            int ix = chosen.Value.X;
            int iy = chosen.Value.Y;
            if (world.ContinentMap[ix, iy] == world.OverseasContinentId)
                placedOverseas++;

            string name = GenerateEstablishmentName(tmpl.Output, usedNames);
            var ind = new Industry(100 + i, name, ix, iy, tmpl.Output, tmpl.Input, secondaryInput: tmpl.SecondaryInput, tertiaryInput: tmpl.TertiaryInput);
            world.Industries.Add(ind);
            world.Grid[ix, iy].OccupantId = ind.Id;
            world.Grid[ix, iy].RoadLevel = RoadTier.None; // Lote da indústria NUNCA é estrada
        }
    }

    private (int X, int Y)? PickLogicalIndustrySite(
        GeneratedWorld world,
        ResourceType output,
        ResourceType input,
        int requiredContinentId,
        int minCityDist,
        int minIndDist)
    {
        int w = _config.Width;
        int h = _config.Height;
        int margin = Math.Clamp(Math.Min(w, h) / 16, 3, 6);

        (int X, int Y)? best = null;
        float bestScore = float.MinValue;

        for (int attempt = 0; attempt < 260; attempt++)
        {
            int ix = _rng.Next(margin, Math.Max(margin + 1, w - margin));
            int iy = _rng.Next(margin, Math.Max(margin + 1, h - margin));

            if (requiredContinentId > 0 && world.ContinentMap[ix, iy] != requiredContinentId)
                continue;

            var cell = world.Grid[ix, iy];
            if (cell.Terrain != TerrainType.Plains || cell.OccupantId != 0 || world.OceanMask[ix, iy])
                continue;

            if (!HasDryAccessAround(world, ix, iy))
                continue;

            bool tooClose = world.Cities.Any(c => Math.Abs(c.CenterX - ix) + Math.Abs(c.CenterY - iy) < minCityDist)
                || world.Industries.Any(other => Math.Abs(other.GridX - ix) + Math.Abs(other.GridY - iy) < minIndDist);
            if (tooClose) continue;

            float score = ScoreIndustryGeography(world, ix, iy, output, input, cell.Biome);
            score += (float)_rng.NextDouble() * 5f;

            if (score > bestScore)
            {
                bestScore = score;
                best = (ix, iy);
            }
        }

        return best;
    }

    private static float ScoreIndustryGeography(GeneratedWorld world, int x, int y, ResourceType output, ResourceType input, BiomeType biome)
    {
        float score = 0f;
        bool nearWater = HasWaterWithin(world, x, y, 8);
        bool nearHillsOrMtn = HasTerrainWithin(world, x, y, 4, TerrainType.Hills) || HasTerrainWithin(world, x, y, 5, TerrainType.Mountain);
        bool onOceanCoast = HasOceanWithin(world, x, y, 6);

        switch (output)
        {
            case ResourceType.Grain:
                if (biome == BiomeType.Temperate) score += 24f;
                if (biome == BiomeType.Desert) score -= 20f;
                if (nearWater) score += 16f;
                break;

            case ResourceType.Lumber:
                if (biome == BiomeType.Temperate) score += 16f;
                if (biome == BiomeType.Alpine) score += 12f;
                if (biome == BiomeType.Desert) score -= 24f;
                if (HasTerrainWithin(world, x, y, 4, TerrainType.Hills)) score += 15f;
                break;

            case ResourceType.IronOre:
                if (nearHillsOrMtn) score += 32f;
                else score -= 12f;
                if (biome == BiomeType.Alpine) score += 10f;
                break;

            case ResourceType.Coal:
                if (nearHillsOrMtn) score += 26f;
                if (biome == BiomeType.Alpine || biome == BiomeType.Temperate) score += 12f;
                break;

            case ResourceType.CrudeOil:
                if (biome == BiomeType.Desert) score += 30f;
                if (onOceanCoast) score += 14f;
                break;

            case ResourceType.Fuel:
                if (onOceanCoast) score += 16f;
                score += CorridorScore(world, x, y, ResourceType.CrudeOil);
                break;

            case ResourceType.Food:
                if (biome == BiomeType.Temperate) score += 12f;
                score += CorridorScore(world, x, y, input);
                break;

            case ResourceType.Steel:
                score += CorridorScore(world, x, y, input);
                if (nearHillsOrMtn) score += 6f;
                break;

            case ResourceType.Goods:
            case ResourceType.Machinery:
                // Fábricas e Complexos Tier 3 gostam de ficar em corredores industriais a média distância das cidades
                score += CorridorScore(world, x, y, input);
                if (world.Cities.Count > 0)
                {
                    float dCity = world.Cities.Where(c => world.ContinentMap[c.CenterX, c.CenterY] == world.ContinentMap[x, y])
                        .Select(c => MathF.Abs(c.CenterX - x) + MathF.Abs(c.CenterY - y))
                        .DefaultIfEmpty(30f)
                        .Min();
                    score += Math.Max(0f, 20f - MathF.Abs(dCity - 16f));
                }
                break;
        }

        return score;
    }

    private static float CorridorScore(GeneratedWorld world, int x, int y, ResourceType inputType)
    {
        if (inputType == ResourceType.None) return 0f;
        int cid = world.ContinentMap[x, y];
        var source = world.Industries.FirstOrDefault(i => i.OutputResource == inputType && world.ContinentMap[i.GridX, i.GridY] == cid)
            ?? world.Industries.FirstOrDefault(i => i.OutputResource == inputType);
        if (source == null) return 0f;

        float distSource = MathF.Abs(source.GridX - x) + MathF.Abs(source.GridY - y);
        // Ideal: nem colado (<10) nem nos confins (>42)
        return Math.Max(0f, 22f - MathF.Abs(distSource - 22f) * 0.6f);
    }

    private string GenerateRandomCityName(HashSet<string> usedNames) => GenerateUniqueCityName(usedNames, _rng);

    public static string GenerateUniqueCityName(HashSet<string> usedNames, Random rng)
    {
        string[] prefixes = { "Porto", "Vila", "Nova", "São", "Santa", "Belo", "Monte", "Serra", "Vale", "Ribeirão", "Campos", "Mirassol", "Pinheiral", "Rio", "Alto", "Alvorada" };
        string[] suffixes = { "Belo", "Verde", "Dourado", "do Sul", "do Norte", "da Serra", "das Pedras", "Horizonte", "Alegre", "Claro", "Real", "das Flores", "do Pinhal", "da Mata", "Novo", "Esperança" };
        string[] singleNames = { "Petrópolis", "Campinas", "Maringá", "Joinville", "Blumenau", "Uberlândia", "Londrina", "Piracicaba", "Cascavel" };

        for (int attempts = 0; attempts < 100; attempts++)
        {
            string candidate = rng.NextDouble() < 0.25
                ? singleNames[rng.Next(singleNames.Length)]
                : $"{prefixes[rng.Next(prefixes.Length)]} {suffixes[rng.Next(suffixes.Length)]}";

            if (usedNames.Add(candidate))
                return candidate;
        }

        return $"Cidade #{usedNames.Count + 1}";
    }

    private string GenerateEstablishmentName(ResourceType output, HashSet<string> usedNames) =>
        GenerateUniqueEstablishmentName(output, usedNames, _rng);

    public static string GenerateUniqueEstablishmentName(ResourceType output, HashSet<string> usedNames, Random rng)
    {
        (string[] Types, string[] Qualifiers) data = output switch
        {
            ResourceType.Grain => (
                new[] { "Fazenda", "Sítio", "Granja", "Estância", "Agropecuária", "Rancho" },
                new[] { "Boa Esperança", "Santa Fé", "Sol Nascente", "Vale Dourado", "Terra Roxa", "Campo Verde", "Bela Vista", "Recanto Feliz", "Três Meninas", "São Bento" }
            ),
            ResourceType.Food => (
                new[] { "Moinho", "Moagem", "Cerealista", "Farinheira", "Panificadora & Moinho" },
                new[] { "Central", "Progresso", "Farinha Real", "Santo André", "Vale do Trigo", "Pão de Ouro", "Imperial", "Serra Nobre", "Aliança" }
            ),
            ResourceType.Lumber => (
                new[] { "Serraria", "Madeireira", "Manejo Florestal", "Horto Madeireiro" },
                new[] { "Araucária", "Pinheiral do Sul", "Mata Atlântica", "Serra Verde", "Rio Claro", "Tronco Forte", "Cedro Real", "Jequitibá" }
            ),
            ResourceType.Steel or ResourceType.IronOre => (
                output == ResourceType.IronOre
                    ? new[] { "Mineração", "Mina de Ferro", "Complexo Extrativo", "Lavra Mineral" }
                    : new[] { "Siderúrgica", "Aciaria", "Metalúrgica", "Alto-Forno" },
                new[] { "São Pedro", "Serra Negra", "Minas Gerais", "Monte Alto", "Pico de Ferro", "Vale do Aço" }
            ),
            ResourceType.CrudeOil => (
                new[] { "Poço Petrolífero", "Campo de Petróleo", "Extração Petrolífera", "Jazida" },
                new[] { "Bacia do Sul", "Serra Alta", "Águas Claras", "Vale Fundo", "Estrela", "Horizonte" }
            ),
            ResourceType.Fuel => (
                new[] { "Refinaria", "Terminal de Petróleo", "Polo Petroquímico", "Distribuidora" },
                new[] { "Planalto", "Vale Azul", "Alvorada", "PetroSul", "Horizonte", "Nacional", "Litorânea" }
            ),
            ResourceType.Coal => (
                new[] { "Mina de Carvão", "Carvoaria", "Jazida Hulha", "Extração Carbonífera" },
                new[] { "Serra Negra", "Criciúma", "Monte Escuro", "Vale do Carvão", "Pedra Alta", "Santa Bárbara" }
            ),
            ResourceType.Machinery => (
                new[] { "Complexo Industrial", "Fábrica de Maquinário", "Construtora Pesada", "Metalomecânica" },
                new[] { "Vanguard", "São Paulo", "Indústria Forte", "Horizonte", "Titan", "Progresso" }
            ),
            _ => (
                new[] { "Fábrica de Móveis", "Manufatura & Marcenaria", "Oficina Central", "Indústria Moveleira" },
                new[] { "Arte & Madeira", "São Jorge", "Padrão Real", "Continental", "Alvorada", "Paulista", "Progresso" }
            )
        };

        for (int attempts = 0; attempts < 100; attempts++)
        {
            string candidate = $"{data.Types[rng.Next(data.Types.Length)]} {data.Qualifiers[rng.Next(data.Qualifiers.Length)]}";
            if (usedNames.Add(candidate))
                return candidate;
        }

        return $"{data.Types[0]} #{usedNames.Count + 1}";
    }

    private void GenerateSeaports(GeneratedWorld world)
    {
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        // Garante que OceanMask identifique exatamente águas conectadas à borda oceânica do mapa
        bool[,] navigableOcean = BuildBorderConnectedOceanMask(world);

        // Porto 1 no Continente Principal (Oeste) voltado para o Oceano aberto
        var mainSpot = FindCoastalSeaportSpot(world, navigableOcean, world.MainlandContinentId, dx, dy);
        if (mainSpot == null && world.OverseasContinentId == 0)
        {
            // Fallback seguro para mapas customizados pequenos sem borda oceânica
            mainSpot = CarveBorderOceanBayAndPortSpot(world);
        }

        if (mainSpot == null)
            return;

        var (px1, py1, wx1, wy1) = mainSpot.Value;
        world.Grid[px1, py1].Terrain = TerrainType.Plains;
        world.Grid[px1, py1].OccupantId = 200;
        world.Grid[px1, py1].RoadLevel = RoadTier.None;

        var port1 = new SeaportTerminal(1, "Porto Atlântico Sul", px1, py1, wx1, wy1);
        world.Seaports.Add(port1);

        // Porto 2 no Continente Ultramarino (Leste) — ligado EXCLUSIVAMENTE pelo mesmo Oceano navegável!
        if (world.OverseasContinentId > 0)
        {
            bool[,] reachableSea = BuildReachableWaterMask(world, wx1, wy1);
            var overseasSpot = FindCoastalSeaportSpot(world, reachableSea, world.OverseasContinentId, dx, dy, targetNearY: py1);
            if (overseasSpot != null)
            {
                var (px2, py2, wx2, wy2) = overseasSpot.Value;
                world.Grid[px2, py2].Terrain = TerrainType.Plains;
                world.Grid[px2, py2].OccupantId = 201;
                world.Grid[px2, py2].RoadLevel = RoadTier.None;

                var port2 = new SeaportTerminal(2, "Porto Costa Ultramarina", px2, py2, wx2, wy2, partnerSeaportId: port1.Id);
                // Alterna o ciclo para que o navio saia de um porto rumo ao outro em sincronia marítima
                port2.RestoreState(
                    isShipDocked: false,
                    cycleHoursRemaining: SeaportTerminal.SeaVoyageDurationHours * 0.5f,
                    currentShipCargo: 0,
                    maxShipCapacity: port2.MaxShipCapacity,
                    totalExportedUnits: 0,
                    totalExportRevenue: 0,
                    partnerSeaportId: port1.Id);
                port1.PartnerSeaportId = port2.Id;
                world.Seaports.Add(port2);
            }
        }
    }

    private static bool[,] BuildBorderConnectedOceanMask(GeneratedWorld world)
    {
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        var mask = new bool[w, h];
        var queue = new Queue<(int X, int Y)>();
        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        void EnqueueIfOceanWater(int x, int y)
        {
            if (x < 0 || x >= w || y < 0 || y >= h || mask[x, y]) return;
            if (world.Grid[x, y].Terrain == TerrainType.Water && world.OceanMask[x, y])
            {
                mask[x, y] = true;
                queue.Enqueue((x, y));
            }
        }

        for (int x = 0; x < w; x++)
        {
            EnqueueIfOceanWater(x, 0);
            EnqueueIfOceanWater(x, h - 1);
        }
        for (int y = 0; y < h; y++)
        {
            EnqueueIfOceanWater(0, y);
            EnqueueIfOceanWater(w - 1, y);
        }

        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + dx[d];
                int ny = cy + dy[d];
                if (nx >= 0 && nx < w && ny >= 0 && ny < h && !mask[nx, ny] && world.Grid[nx, ny].Terrain == TerrainType.Water && world.OceanMask[nx, ny])
                {
                    mask[nx, ny] = true;
                    queue.Enqueue((nx, ny));
                }
            }
        }
        return mask;
    }

    private static bool[,] BuildReachableWaterMask(GeneratedWorld world, int startWx, int startWy)
    {
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        var mask = new bool[w, h];
        if (world.Grid[startWx, startWy].Terrain != TerrainType.Water)
            return mask;

        var queue = new Queue<(int X, int Y)>();
        mask[startWx, startWy] = true;
        queue.Enqueue((startWx, startWy));
        int[] dx = { 1, -1, 0, 0 };
        int[] dy = { 0, 0, 1, -1 };

        while (queue.Count > 0)
        {
            var (cx, cy) = queue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + dx[d];
                int ny = cy + dy[d];
                if (nx >= 0 && nx < w && ny >= 0 && ny < h && !mask[nx, ny] && world.Grid[nx, ny].Terrain == TerrainType.Water && world.OceanMask[nx, ny])
                {
                    mask[nx, ny] = true;
                    queue.Enqueue((nx, ny));
                }
            }
        }
        return mask;
    }

    private static (int Px, int Py, int Wx, int Wy)? FindCoastalSeaportSpot(
        GeneratedWorld world,
        bool[,] oceanWaterMask,
        int targetContinentId,
        int[] dx,
        int[] dy,
        int? targetNearY = null)
    {
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);

        int bestPx = -1, bestPy = -1, bestWx = -1, bestWy = -1;
        float bestScore = float.MaxValue;

        for (int x = 2; x < w - 2; x++)
        {
            for (int y = 3; y < h - 3; y++)
            {
                if (world.ContinentMap[x, y] != targetContinentId)
                    continue;

                ref var cell = ref world.Grid[x, y];
                if (cell.Terrain == TerrainType.Water || cell.Terrain == TerrainType.Mountain || cell.OccupantId != 0)
                    continue;

                bool tooClose = world.Cities.Any(c => MathF.Abs(c.CenterX - x) + MathF.Abs(c.CenterY - y) < 5)
                    || world.Industries.Any(i => MathF.Abs(i.GridX - x) + MathF.Abs(i.GridY - y) < 4)
                    || world.Seaports.Any(s => MathF.Abs(s.GridX - x) + MathF.Abs(s.GridY - y) < 6);
                if (tooClose) continue;

                // Exige ao menos um tile terrestre livre adjacente para entrada da rodovia
                if (!HasDryAccessAround(world, x, y))
                    continue;

                for (int d = 0; d < 4; d++)
                {
                    int nx = x + dx[d];
                    int ny = y + dy[d];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    if (!oceanWaterMask[nx, ny]) continue;

                    // Prefere costa próxima à cidade do mesmo continente (ou alinhada em latitude com porto parceiro)
                    var continentCities = world.Cities.Where(c => world.ContinentMap[c.CenterX, c.CenterY] == targetContinentId).ToList();
                    float distCity = continentCities.Count > 0
                        ? continentCities.Min(c => MathF.Abs(c.CenterX - x) + MathF.Abs(c.CenterY - y))
                        : 15f;

                    float latPenalty = targetNearY.HasValue ? MathF.Abs(y - targetNearY.Value) * 0.45f : 0f;
                    float flatBonus = cell.Terrain == TerrainType.Plains ? 0f : 6f;
                    float score = distCity + latPenalty + flatBonus;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestPx = x; bestPy = y; bestWx = nx; bestWy = ny;
                    }
                    break;
                }
            }
        }

        return bestPx != -1 ? (bestPx, bestPy, bestWx, bestWy) : null;
    }

    private static (int Px, int Py, int Wx, int Wy)? CarveBorderOceanBayAndPortSpot(GeneratedWorld world)
    {
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        int py = Math.Clamp(h - 4, 3, h - 4);
        int px = Math.Clamp(w / 2, 3, w - 4);
        for (int tryX = 3; tryX < w - 3; tryX++)
        {
            if (world.Grid[tryX, py].OccupantId == 0 &&
                !world.Cities.Any(c => MathF.Abs(c.CenterX - tryX) + MathF.Abs(c.CenterY - py) < 4))
            {
                px = tryX;
                break;
            }
        }

        for (int bx = Math.Max(1, px - 2); bx <= Math.Min(w - 1, px + 2); bx++)
        {
            for (int by = py + 1; by < h; by++)
            {
                if (world.Grid[bx, by].OccupantId == 0)
                {
                    world.Grid[bx, by].Terrain = TerrainType.Water;
                    world.Grid[bx, by].RoadLevel = RoadTier.None;
                    world.OceanMask[bx, by] = true;
                }
            }
        }

        return (px, py, px, py + 1);
    }

    private void GenerateRegionalLandmarks(GeneratedWorld world)
    {
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);

        var specs = new (LandmarkKind Kind, string Name)[]
        {
            (LandmarkKind.HydroelectricDam, "Usina Hidrelétrica Rio Claro"),
            (LandmarkKind.WindFarmPark, "Parque Eólico Serra Alta"),
            (LandmarkKind.MunicipalStadium, "Estádio Regional Horizonte")
        };

        int secondIdx = 1 + _rng.Next(2);
        var selected = new[] { specs[0], specs[secondIdx] };

        int nextId = 1;
        foreach (var (kind, name) in selected)
        {
            int bestX = -1, bestY = -1;
            float bestScore = float.MinValue;

            for (int attempt = 0; attempt < 260; attempt++)
            {
                int cx = _rng.Next(4, w - 4);
                int cy = _rng.Next(4, h - 4);
                if (world.ContinentMap[cx, cy] != world.MainlandContinentId)
                    continue;

                var cell = world.Grid[cx, cy];
                if (cell.Terrain == TerrainType.Water || cell.Terrain == TerrainType.Mountain || cell.OccupantId != 0)
                    continue;
                if (!HasDryAccessAround(world, cx, cy))
                    continue;

                bool tooClose = world.Cities.Any(c => MathF.Abs(c.CenterX - cx) + MathF.Abs(c.CenterY - cy) < 6)
                    || world.Industries.Any(i => MathF.Abs(i.GridX - cx) + MathF.Abs(i.GridY - cy) < 5)
                    || world.Seaports.Any(s => MathF.Abs(s.GridX - cx) + MathF.Abs(s.GridY - cy) < 6)
                    || world.Landmarks.Any(l => MathF.Abs(l.GridX - cx) + MathF.Abs(l.GridY - cy) < 10);
                if (tooClose) continue;

                float score = 0f;
                if (kind == LandmarkKind.HydroelectricDam)
                {
                    // Hidrelétrica faz sentido lógico junto a um rio (água NÃO oceânica)
                    bool onRiver = HasRiverWithin(world, cx, cy, 1);
                    bool nearWater = HasWaterWithin(world, cx, cy, 2);
                    if (onRiver) score += 40f;
                    else if (nearWater) score += 16f;
                    if (HasTerrainWithin(world, cx, cy, 3, TerrainType.Hills)) score += 10f;
                }
                else if (kind == LandmarkKind.WindFarmPark)
                {
                    if (cell.Terrain == TerrainType.Hills || HasTerrainWithin(world, cx, cy, 2, TerrainType.Hills))
                        score += 28f;
                    if (HasOceanWithin(world, cx, cy, 5))
                        score += 14f;
                }
                else // MunicipalStadium
                {
                    if (cell.Terrain == TerrainType.Plains) score += 20f;
                    float dCity = world.Cities.Where(c => world.ContinentMap[c.CenterX, c.CenterY] == world.MainlandContinentId)
                        .Select(c => MathF.Abs(c.CenterX - cx) + MathF.Abs(c.CenterY - cy))
                        .DefaultIfEmpty(12f)
                        .Min();
                    score += Math.Max(0f, 18f - MathF.Abs(dCity - 9f));
                }

                score += (float)_rng.NextDouble() * 4f;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestX = cx;
                    bestY = cy;
                }
            }

            if (bestX == -1) continue;

            world.Grid[bestX, bestY].Terrain = TerrainType.Plains;
            world.Grid[bestX, bestY].OccupantId = 220 + nextId;
            world.Grid[bestX, bestY].RoadLevel = RoadTier.None;

            world.Landmarks.Add(new RegionalLandmark(nextId, name, kind, bestX, bestY));
            nextId++;
        }
    }

    private void GenerateInitialRoadNetwork(GeneratedWorld world)
    {
        int nextNodeId = 1;
        int nextEdgeId = 1;
        var createdNodes = new Dictionary<(int X, int Y), RoadNode>();

        // Reserva estrita de lotes de prédios centrais para que estradas nunca passem sobre prédios
        var cityCenterLots = new HashSet<(int X, int Y)>(world.Cities.Select(c => (c.CenterX, c.CenterY)));

        void LinkPoints(int x1, int y1, int x2, int y2) =>
            CarveSmartDirtRoadAndLinkNodes(world, x1, y1, x2, y2, ref nextNodeId, ref nextEdgeId, createdNodes, cityCenterLots);

        // 1. Espinha Dorsal POR CONTINENTE (MST separada: nunca cruza o oceano entre continentes!)
        foreach (var continentGroup in world.Cities.GroupBy(c => world.ContinentMap[c.CenterX, c.CenterY]))
        {
            var continentCities = continentGroup.ToList();
            if (continentCities.Count <= 1) continue;

            var connected = new List<City> { continentCities[0] };
            var unvisited = continentCities.Skip(1).ToList();

            while (unvisited.Count > 0)
            {
                float bestDist = float.MaxValue;
                City? bestFrom = null;
                int bestToIdx = -1;

                foreach (var c1 in connected)
                {
                    for (int j = 0; j < unvisited.Count; j++)
                    {
                        var c2 = unvisited[j];
                        float d = MathF.Abs(c2.CenterX - c1.CenterX) + MathF.Abs(c2.CenterY - c1.CenterY);
                        if (d < bestDist)
                        {
                            bestDist = d;
                            bestFrom = c1;
                            bestToIdx = j;
                        }
                    }
                }

                if (bestFrom != null && bestToIdx != -1)
                {
                    var bestTo = unvisited[bestToIdx];
                    unvisited.RemoveAt(bestToIdx);
                    var a1 = GetCityAccessPoint(world, bestFrom);
                    var a2 = GetCityAccessPoint(world, bestTo);
                    LinkPoints(a1.X, a1.Y, a2.X, a2.Y);
                    connected.Add(bestTo);
                }
            }
        }

        // 2. Ramais das Indústrias para a cidade mais próxima DO MESMO CONTINENTE
        foreach (var ind in world.Industries)
        {
            int cid = world.ContinentMap[ind.GridX, ind.GridY];
            var nearestCity = world.Cities
                .Where(c => world.ContinentMap[c.CenterX, c.CenterY] == cid)
                .OrderBy(c => MathF.Abs(c.CenterX - ind.GridX) + MathF.Abs(c.CenterY - ind.GridY))
                .FirstOrDefault();

            if (nearestCity != null)
            {
                var indAccess = GetAccessPointForIndustry(world, ind.GridX, ind.GridY);
                var cityAccess = GetCityAccessPoint(world, nearestCity);
                LinkPoints(indAccess.X, indAccess.Y, cityAccess.X, cityAccess.Y);
            }
        }

        // 3. Ramais para Portos e Mega-Obras para a cidade mais próxima DO MESMO CONTINENTE
        foreach (var sp in world.Seaports)
        {
            int cid = world.ContinentMap[sp.GridX, sp.GridY];
            var nearestCity = world.Cities
                .Where(c => world.ContinentMap[c.CenterX, c.CenterY] == cid)
                .OrderBy(c => MathF.Abs(c.CenterX - sp.GridX) + MathF.Abs(c.CenterY - sp.GridY))
                .FirstOrDefault();

            if (nearestCity != null)
            {
                var spAccess = GetAccessPointForIndustry(world, sp.GridX, sp.GridY);
                var cityAccess = GetCityAccessPoint(world, nearestCity);
                LinkPoints(spAccess.X, spAccess.Y, cityAccess.X, cityAccess.Y);
            }
        }

        foreach (var lm in world.Landmarks)
        {
            int cid = world.ContinentMap[lm.GridX, lm.GridY];
            var nearestCity = world.Cities
                .Where(c => world.ContinentMap[c.CenterX, c.CenterY] == cid)
                .OrderBy(c => MathF.Abs(c.CenterX - lm.GridX) + MathF.Abs(c.CenterY - lm.GridY))
                .FirstOrDefault();

            if (nearestCity != null)
            {
                var lmAccess = GetAccessPointForIndustry(world, lm.GridX, lm.GridY);
                var cityAccess = GetCityAccessPoint(world, nearestCity);
                LinkPoints(lmAccess.X, lmAccess.Y, cityAccess.X, cityAccess.Y);
            }
        }

        // Blindagem estrita: lotes de prédios centrais, indústrias, portos e mega-obras NUNCA têm estrada por baixo
        foreach (var c in world.Cities)
        {
            world.Grid[c.CenterX, c.CenterY].RoadLevel = RoadTier.None;
        }
        foreach (var ind in world.Industries)
        {
            world.Grid[ind.GridX, ind.GridY].RoadLevel = RoadTier.None;
        }
        foreach (var sp in world.Seaports)
        {
            world.Grid[sp.GridX, sp.GridY].RoadLevel = RoadTier.None;
        }
        foreach (var lm in world.Landmarks)
        {
            world.Grid[lm.GridX, lm.GridY].RoadLevel = RoadTier.None;
        }

        // Remove blocos 2x2 maciços e pequenos anéis/paralelas redundantes ("quadrados sem sentido")
        // preservando 100% da conectividade entre cidades, indústrias, portos e mega-obras.
        PruneRedundantRoadSquares(world);
    }

    public static (int X, int Y) GetCityAccessPoint(GeneratedWorld world, City city)
    {
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        var offsets = new[] { (0, 1), (1, 0), (0, -1), (-1, 0) };

        // Se já houver alguma rua urbana conectada adjacente ao centro, reutiliza como nó de acesso
        foreach (var (dx, dy) in offsets)
        {
            int ax = city.CenterX + dx;
            int ay = city.CenterY + dy;
            if (ax >= 0 && ax < w && ay >= 0 && ay < h && world.Grid[ax, ay].HasRoad)
                return (ax, ay);
        }

        foreach (var (dx, dy) in offsets)
        {
            int ax = city.CenterX + dx;
            int ay = city.CenterY + dy;
            if (ax >= 0 && ax < w && ay >= 0 && ay < h
                && !world.OceanMask[ax, ay]
                && world.Grid[ax, ay].Terrain == TerrainType.Plains
                && world.Grid[ax, ay].OccupantId == city.Id)
            {
                return (ax, ay);
            }
        }

        return GetAccessPointForIndustry(world, city.CenterX, city.CenterY);
    }

    public static (int X, int Y) GetAccessPointForIndustry(GeneratedWorld world, int ix, int iy)
    {
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        var offsets = new[] { (0, 1), (1, 0), (0, -1), (-1, 0) };
        foreach (var (dx, dy) in offsets)
        {
            int ax = ix + dx;
            int ay = iy + dy;
            if (ax >= 0 && ax < w && ay >= 0 && ay < h && !world.OceanMask[ax, ay])
            {
                if (world.Grid[ax, ay].Terrain == TerrainType.Plains && (world.Grid[ax, ay].OccupantId == 0 || world.Grid[ax, ay].RoadLevel != RoadTier.None))
                {
                    return (ax, ay);
                }
            }
        }
        foreach (var (dx, dy) in offsets)
        {
            int ax = ix + dx;
            int ay = iy + dy;
            if (ax >= 0 && ax < w && ay >= 0 && ay < h && !world.OceanMask[ax, ay] && world.Grid[ax, ay].Terrain != TerrainType.Water)
            {
                if (world.Grid[ax, ay].OccupantId < 100)
                {
                    world.Grid[ax, ay].Terrain = TerrainType.Plains;
                    return (ax, ay);
                }
            }
        }
        return (ix, iy);
    }

    public static void CarveDirtRoadAndLinkNodes(
        GeneratedWorld world,
        int x1,
        int y1,
        int x2,
        int y2,
        ref int nextNodeId,
        ref int nextEdgeId,
        Dictionary<(int X, int Y), RoadNode>? nodeLookup = null)
    {
        var cityCenterLots = new HashSet<(int X, int Y)>(world.Cities.Select(c => (c.CenterX, c.CenterY)));
        CarveSmartDirtRoadAndLinkNodes(world, x1, y1, x2, y2, ref nextNodeId, ref nextEdgeId, nodeLookup, cityCenterLots);
    }

    private static void CarveSmartDirtRoadAndLinkNodes(
        GeneratedWorld world,
        int x1,
        int y1,
        int x2,
        int y2,
        ref int nextNodeId,
        ref int nextEdgeId,
        Dictionary<(int X, int Y), RoadNode>? nodeLookup,
        HashSet<(int X, int Y)> cityCenterLots)
    {
        var path = FindLogicalRoadPath(world, x1, y1, x2, y2, cityCenterLots);
        foreach (var (px, py) in path)
        {
            ApplyDirtRoad(world, px, py, cityCenterLots);
        }

        var n1 = GetOrCreateRoadNode(world, x1, y1, ref nextNodeId, nodeLookup);
        var n2 = GetOrCreateRoadNode(world, x2, y2, ref nextNodeId, nodeLookup);
        if (n1 == n2) return;

        float dist = MathF.Max(1f, path.Count - 1);
        var edgeForward = new RoadEdge(nextEdgeId++, n1, n2, RoadTier.Dirt, dist);
        var edgeBackward = new RoadEdge(nextEdgeId++, n2, n1, RoadTier.Dirt, dist);
        n1.OutgoingEdges.Add(edgeForward);
        n2.OutgoingEdges.Add(edgeBackward);
        world.RoadEdges.Add(edgeForward);
        world.RoadEdges.Add(edgeBackward);
    }

    private static List<(int X, int Y)> FindLogicalRoadPath(
        GeneratedWorld world,
        int startX,
        int startY,
        int endX,
        int endY,
        HashSet<(int X, int Y)> cityCenterLots)
    {
        const float roadReuseCost = 0.35f;
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        int totalStates = w * h * 5;

        var open = new PriorityQueue<int, float>();
        var cameFrom = new int[totalStates];
        var gScore = new float[totalStates];
        Array.Fill(cameFrom, -1);
        Array.Fill(gScore, float.PositiveInfinity);

        int Encode(int x, int y, int dir) => ((y * w + x) * 5) + dir;

        int startState = Encode(startX, startY, 4);
        gScore[startState] = 0f;
        open.Enqueue(startState, roadReuseCost * (MathF.Abs(endX - startX) + MathF.Abs(endY - startY)));

        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { 1, 0, -1, 0 };

        while (open.Count > 0)
        {
            int state = open.Dequeue();
            int currDir = state % 5;
            int cellIdx = state / 5;
            int cx = cellIdx % w;
            int cy = cellIdx / w;

            if (cx == endX && cy == endY)
            {
                var path = new List<(int X, int Y)>();
                int trace = state;
                while (trace != -1)
                {
                    int cIdx = trace / 5;
                    path.Add((cIdx % w, cIdx / w));
                    trace = cameFrom[trace];
                }
                path.Reverse();
                return path;
            }

            float currG = gScore[state];
            bool currHasRoad = world.Grid[cx, cy].HasRoad;

            for (int d = 0; d < 4; d++)
            {
                int nx = cx + dx[d];
                int ny = cy + dy[d];
                if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;

                bool isGoal = nx == endX && ny == endY;
                if (!isGoal)
                {
                    // Nunca passa sobre o oceano aberto, centros de cidade nem lotes de indústrias/portos/obras
                    if (world.OceanMask[nx, ny]) continue;
                    if (cityCenterLots.Contains((nx, ny))) continue;
                    if (world.Grid[nx, ny].OccupantId >= 100) continue;
                }

                var cell = world.Grid[nx, ny];
                float stepCost;
                if (cell.HasRoad)
                    stepCost = roadReuseCost;
                else if (cell.OccupantId > 0 && !isGoal)
                    stepCost = 6.0f; // Evita atravessar quarteirões urbanos vizinhos quando houver campo livre
                else
                    stepCost = cell.Terrain switch
                    {
                        TerrainType.Water => 16.0f,    // Ponte apenas quando preciso em rios
                        TerrainType.Hills => 4.2f,
                        TerrainType.Mountain => 48.0f,
                        _ => 1.0f
                    };

                if (currDir < 4 && d != currDir)
                    stepCost += cell.HasRoad ? 0.03f : 0.22f;

                if (!cell.HasRoad && !isGoal)
                {
                    int distStart = Math.Abs(nx - startX) + Math.Abs(ny - startY);
                    int distEnd = Math.Abs(nx - endX) + Math.Abs(ny - endY);
                    if (distStart > 1 && distEnd > 1)
                    {
                        int px = dy[d];
                        int py = -dx[d];

                        // Penaliza fortemente pavimentar faixa paralela colada em estrada existente (evita blocos 2x2 "##")
                        bool side1 = nx + px >= 0 && nx + px < w && ny + py >= 0 && ny + py < h && world.Grid[nx + px, ny + py].HasRoad;
                        bool side2 = nx - px >= 0 && nx - px < w && ny - py >= 0 && ny - py < h && world.Grid[nx - px, ny - py].HasRoad;
                        if (side1 || side2)
                            stepCost += 5.5f;

                        // Penaliza correr paralelo a 1 tile de distância de outra estrada (evita quadrados "# . #")
                        if (!currHasRoad && distStart > 2 && distEnd > 2)
                        {
                            bool far1 = nx + px * 2 >= 0 && nx + px * 2 < w && ny + py * 2 >= 0 && ny + py * 2 < h && world.Grid[nx + px * 2, ny + py * 2].HasRoad;
                            bool far2 = nx - px * 2 >= 0 && nx - px * 2 < w && ny - py * 2 >= 0 && ny - py * 2 < h && world.Grid[nx - px * 2, ny - py * 2].HasRoad;
                            if (far1 || far2)
                                stepCost += 3.0f;
                        }
                    }
                }

                float tentativeG = currG + stepCost;
                int nextState = Encode(nx, ny, d);
                if (tentativeG < gScore[nextState])
                {
                    cameFrom[nextState] = state;
                    gScore[nextState] = tentativeG;
                    float hEst = roadReuseCost * (MathF.Abs(endX - nx) + MathF.Abs(endY - ny));
                    open.Enqueue(nextState, tentativeG + hEst);
                }
            }
        }

        // Fallback L contornando centros urbanos
        var fallback = new List<(int X, int Y)>();
        int fx = startX, fy = startY;
        fallback.Add((fx, fy));
        while (fx != endX) { fx += Math.Sign(endX - fx); fallback.Add((fx, fy)); }
        while (fy != endY) { fy += Math.Sign(endY - fy); fallback.Add((fx, fy)); }
        return fallback;
    }

    public static void PruneRedundantRoadSquares(GeneratedWorld world)
    {
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        if (w < 4 || h < 4) return;

        var protectedTiles = new HashSet<(int X, int Y)>();
        foreach (var node in world.RoadNodes)
            protectedTiles.Add((node.GridX, node.GridY));

        void ProtectAdjacentAccess(int cx, int cy)
        {
            for (int d = 0; d < 4; d++)
            {
                int ax = cx + (d == 1 ? 1 : d == 3 ? -1 : 0);
                int ay = cy + (d == 0 ? 1 : d == 2 ? -1 : 0);
                if (ax >= 0 && ax < w && ay >= 0 && ay < h && world.Grid[ax, ay].HasRoad)
                    protectedTiles.Add((ax, ay));
            }
        }

        foreach (var c in world.Cities) ProtectAdjacentAccess(c.CenterX, c.CenterY);
        foreach (var ind in world.Industries) ProtectAdjacentAccess(ind.GridX, ind.GridY);
        foreach (var sp in world.Seaports) ProtectAdjacentAccess(sp.GridX, sp.GridY);
        foreach (var lm in world.Landmarks) ProtectAdjacentAccess(lm.GridX, lm.GridY);

        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { 1, 0, -1, 0 };

        int RoadDegree(int x, int y)
        {
            int deg = 0;
            for (int d = 0; d < 4; d++)
            {
                int nx = x + dx[d];
                int ny = y + dy[d];
                if (nx >= 0 && nx < w && ny >= 0 && ny < h && world.Grid[nx, ny].HasRoad)
                    deg++;
            }
            return deg;
        }

        bool CanRemoveSafely(int tx, int ty)
        {
            if (protectedTiles.Contains((tx, ty))) return false;
            var cell = world.Grid[tx, ty];
            if (!cell.HasRoad || cell.RoadLevel != RoadTier.Dirt) return false;
            if (cell.Terrain == TerrainType.Water) return false;

            var neighbors = new List<(int X, int Y)>(4);
            for (int d = 0; d < 4; d++)
            {
                int nx = tx + dx[d];
                int ny = ty + dy[d];
                if (nx >= 0 && nx < w && ny >= 0 && ny < h && world.Grid[nx, ny].HasRoad)
                    neighbors.Add((nx, ny));
            }

            if (neighbors.Count <= 1) return true;

            const int localRadius = 10;
            int minX = Math.Max(0, tx - localRadius);
            int maxX = Math.Min(w - 1, tx + localRadius);
            int minY = Math.Max(0, ty - localRadius);
            int maxY = Math.Min(h - 1, ty + localRadius);

            var visited = new HashSet<(int X, int Y)> { (tx, ty), neighbors[0] };
            var queue = new Queue<(int X, int Y)>();
            queue.Enqueue(neighbors[0]);

            int remainingTargets = neighbors.Count - 1;
            while (queue.Count > 0 && remainingTargets > 0)
            {
                var curr = queue.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    int nx = curr.X + dx[d];
                    int ny = curr.Y + dy[d];
                    if (nx < minX || nx > maxX || ny < minY || ny > maxY) continue;
                    if (!world.Grid[nx, ny].HasRoad || !visited.Add((nx, ny))) continue;

                    for (int k = 1; k < neighbors.Count; k++)
                    {
                        if (neighbors[k].X == nx && neighbors[k].Y == ny)
                            remainingTargets--;
                    }
                    queue.Enqueue((nx, ny));
                }
            }

            return remainingTargets == 0;
        }

        bool IsEnclosedEmptyPocket(int x0, int y0, int pw, int ph)
        {
            if (x0 <= 0 || y0 <= 0 || x0 + pw >= w || y0 + ph >= h) return false;
            for (int x = x0; x < x0 + pw; x++)
            {
                for (int y = y0; y < y0 + ph; y++)
                {
                    if (world.Grid[x, y].HasRoad || world.Grid[x, y].OccupantId != 0)
                        return false;
                }
            }
            for (int x = x0; x < x0 + pw; x++)
            {
                if (!world.Grid[x, y0 - 1].HasRoad || !world.Grid[x, y0 + ph].HasRoad) return false;
            }
            for (int y = y0; y < y0 + ph; y++)
            {
                if (!world.Grid[x0 - 1, y].HasRoad || !world.Grid[x0 + pw, y].HasRoad) return false;
            }
            return true;
        }

        for (int pass = 0; pass < 8; pass++)
        {
            bool changed = false;

            // 1. Elimina blocos 2x2 maciços de estrada ("## / ##")
            for (int x = 0; x < w - 1; x++)
            {
                for (int y = 0; y < h - 1; y++)
                {
                    if (!world.Grid[x, y].HasRoad || !world.Grid[x + 1, y].HasRoad ||
                        !world.Grid[x, y + 1].HasRoad || !world.Grid[x + 1, y + 1].HasRoad)
                        continue;

                    (int X, int Y)[] quad = { (x, y), (x + 1, y), (x, y + 1), (x + 1, y + 1) };
                    Array.Sort(quad, (a, b) => RoadDegree(a.X, a.Y).CompareTo(RoadDegree(b.X, b.Y)));
                    foreach (var (qx, qy) in quad)
                    {
                        if (CanRemoveSafely(qx, qy))
                        {
                            world.Grid[qx, qy].RoadLevel = RoadTier.None;
                            changed = true;
                            break;
                        }
                    }
                }
            }

            // 2. Quebra pequenos anéis/quadrados vazios cercados por estrada (1x1, 1x2, 2x1, 2x2, 1x3, 3x1, 2x3, 3x2, 1x4, 4x1)
            (int Pw, int Ph)[] pocketSizes = { (1, 1), (2, 1), (1, 2), (2, 2), (3, 1), (1, 3), (3, 2), (2, 3), (4, 1), (1, 4) };
            foreach (var (pw, ph) in pocketSizes)
            {
                for (int x = 1; x < w - pw; x++)
                {
                    for (int y = 1; y < h - ph; y++)
                    {
                        if (!IsEnclosedEmptyPocket(x, y, pw, ph)) continue;

                        var ringCandidates = new List<(int X, int Y, int Deg)>();
                        for (int rx = x; rx < x + pw; rx++)
                        {
                            ringCandidates.Add((rx, y - 1, RoadDegree(rx, y - 1)));
                            ringCandidates.Add((rx, y + ph, RoadDegree(rx, y + ph)));
                        }
                        for (int ry = y; ry < y + ph; ry++)
                        {
                            ringCandidates.Add((x - 1, ry, RoadDegree(x - 1, ry)));
                            ringCandidates.Add((x + pw, ry, RoadDegree(x + pw, ry)));
                        }

                        ringCandidates.Sort((a, b) => a.Deg.CompareTo(b.Deg));
                        foreach (var (rx, ry, _) in ringCandidates)
                        {
                            if (CanRemoveSafely(rx, ry))
                            {
                                world.Grid[rx, ry].RoadLevel = RoadTier.None;
                                changed = true;
                                break;
                            }
                        }
                    }
                }
            }

            // 3. Remove pontas soltas (grau 1) não-protegidas deixadas pela quebra das paralelas
            bool stubPruned;
            do
            {
                stubPruned = false;
                for (int x = 0; x < w; x++)
                {
                    for (int y = 0; y < h; y++)
                    {
                        if (world.Grid[x, y].HasRoad && RoadDegree(x, y) <= 1 && !protectedTiles.Contains((x, y)) && world.Grid[x, y].RoadLevel == RoadTier.Dirt)
                        {
                            world.Grid[x, y].RoadLevel = RoadTier.None;
                            stubPruned = true;
                            changed = true;
                        }
                    }
                }
            } while (stubPruned);

            if (!changed) break;
        }
    }

    private static RoadNode GetOrCreateRoadNode(
        GeneratedWorld world,
        int x,
        int y,
        ref int nextNodeId,
        Dictionary<(int X, int Y), RoadNode>? nodeLookup)
    {
        if (nodeLookup != null && nodeLookup.TryGetValue((x, y), out var cached))
            return cached;

        foreach (var existing in world.RoadNodes)
        {
            if (existing.GridX == x && existing.GridY == y)
            {
                if (nodeLookup != null) nodeLookup[(x, y)] = existing;
                return existing;
            }
        }

        var created = new RoadNode(nextNodeId++, x, y);
        world.RoadNodes.Add(created);
        if (nodeLookup != null) nodeLookup[(x, y)] = created;
        return created;
    }

    private static void ApplyDirtRoad(GeneratedWorld world, int x, int y, HashSet<(int X, int Y)>? cityCenterLots = null)
    {
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        if (x < 0 || x >= w || y < 0 || y >= h) return;

        if (world.OceanMask[x, y]) return;
        if (world.Grid[x, y].OccupantId >= 100) return;
        if (cityCenterLots != null && cityCenterLots.Contains((x, y))) return;

        if (world.Grid[x, y].RoadLevel == RoadTier.None)
            world.Grid[x, y].RoadLevel = RoadTier.Dirt;
    }

    private static bool HasDryAccessAround(GeneratedWorld world, int x, int y)
    {
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        int[] dx = { 0, 1, 0, -1 };
        int[] dy = { 1, 0, -1, 0 };
        for (int d = 0; d < 4; d++)
        {
            int ax = x + dx[d];
            int ay = y + dy[d];
            if (ax >= 0 && ax < w && ay >= 0 && ay < h && !world.OceanMask[ax, ay] && world.Grid[ax, ay].Terrain != TerrainType.Water && world.Grid[ax, ay].Terrain != TerrainType.Mountain)
                return true;
        }
        return false;
    }

    private static bool HasWaterWithin(GeneratedWorld world, int x, int y, int radius)
    {
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx >= 0 && nx < w && ny >= 0 && ny < h && world.Grid[nx, ny].Terrain == TerrainType.Water)
                    return true;
            }
        }
        return false;
    }

    private static bool HasOceanWithin(GeneratedWorld world, int x, int y, int radius)
    {
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx >= 0 && nx < w && ny >= 0 && ny < h && world.OceanMask[nx, ny])
                    return true;
            }
        }
        return false;
    }

    private static bool HasRiverWithin(GeneratedWorld world, int x, int y, int radius)
    {
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx >= 0 && nx < w && ny >= 0 && ny < h && world.Grid[nx, ny].Terrain == TerrainType.Water && !world.OceanMask[nx, ny])
                    return true;
            }
        }
        return false;
    }

    private static bool HasTerrainWithin(GeneratedWorld world, int x, int y, int radius, TerrainType terrain)
    {
        int w = world.Grid.GetLength(0);
        int h = world.Grid.GetLength(1);
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int nx = x + dx;
                int ny = y + dy;
                if (nx >= 0 && nx < w && ny >= 0 && ny < h && world.Grid[nx, ny].Terrain == terrain)
                    return true;
            }
        }
        return false;
    }
}
