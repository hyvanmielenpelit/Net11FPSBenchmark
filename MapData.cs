namespace Net11FPSBenchmark;

/// <summary>
/// Synthetic map data that mimics a real GnollHack dungeon layout.
/// Each cell has up to 22 layer tile indices (20 core + shadow + UI overlay).
/// A tile index of -1 means the layer is empty (skip drawing).
/// </summary>
public static class MapData
{
    /// <summary>
    /// Per-cell layer data: which tile to draw at each of the 22 render passes.
    /// </summary>
    public struct MapCell
    {
        /// <summary>Tile index for each render pass. -1 = empty/transparent.</summary>
        public int[] LayerTiles;

        /// <summary>Whether this cell should be drawn with a darkening color filter.</summary>
        public bool IsDarkened;
    }

    /// <summary>
    /// Pre-generated map grid [col, row] with realistic layer population.
    /// </summary>
    public static MapCell[,] Cells { get; private set; } = new MapCell[Constants.MapCols, Constants.MapRows];

    /// <summary>
    /// Total number of draw calls per frame (for diagnostics).
    /// </summary>
    public static int DrawCallsPerFrame { get; private set; }

    /// <summary>
    /// Generate a synthetic dungeon map with realistic layer density.
    /// Uses a fixed seed for reproducible benchmarks.
    /// </summary>
    public static void Generate()
    {
        var rng = new Random(42); // Fixed seed for reproducibility
        int drawCalls = 0;

        // Define tile index ranges for each layer type.
        // These sample from different regions of the tile sheet to exercise
        // diverse source rectangles (like real gameplay does).
        int floorTileBase = 0;          // Floor tiles start at index 0
        int featureTileBase = 200;      // Doors, walls, stairs
        int objectTileBase = 1000;      // Items on the ground
        int monsterTileBase = 2000;     // Creatures
        int effectTileBase = 5000;      // Effects, environment
        int uiTileBase = 7000;          // UI elements

        for (int col = 0; col < Constants.MapCols; col++)
        {
            for (int row = 0; row < Constants.MapRows; row++)
            {
                var cell = new MapCell
                {
                    LayerTiles = new int[Constants.TotalRenderPasses],
                    IsDarkened = false
                };

                // Initialize all layers to empty
                for (int i = 0; i < Constants.TotalRenderPasses; i++)
                    cell.LayerTiles[i] = -1;

                // Border walls (col 0 or row 0 are typically empty in GnollHack)
                bool isBorder = col == 0 || col == Constants.MapCols - 1 ||
                                row == 0 || row == Constants.MapRows - 1;

                // Layer 0: FLOOR — every non-border cell gets a floor tile
                if (!isBorder)
                {
                    cell.LayerTiles[0] = floorTileBase + rng.Next(20); // 20 floor variants
                    drawCalls++;
                }

                // Layer 1: CARPET — ~10% of cells
                if (!isBorder && rng.NextDouble() < 0.10)
                {
                    cell.LayerTiles[1] = floorTileBase + 20 + rng.Next(10);
                    drawCalls++;
                }

                // Layer 2: FLOOR_DOODAD — ~15%
                if (!isBorder && rng.NextDouble() < 0.15)
                {
                    cell.LayerTiles[2] = floorTileBase + 40 + rng.Next(15);
                    drawCalls++;
                }

                // Layer 3: FEATURE — ~20% (doors, walls, stairs, fountains)
                if (!isBorder && rng.NextDouble() < 0.20)
                {
                    cell.LayerTiles[3] = featureTileBase + rng.Next(50);
                    drawCalls++;
                }
                else if (isBorder)
                {
                    // Border walls
                    cell.LayerTiles[3] = featureTileBase + 50 + rng.Next(10);
                    drawCalls++;
                }

                // Layer 4: TRAP — ~3%
                if (!isBorder && rng.NextDouble() < 0.03)
                {
                    cell.LayerTiles[4] = featureTileBase + 100 + rng.Next(20);
                    drawCalls++;
                }

                // Layer 5: FEATURE_DOODAD — ~10%
                if (!isBorder && rng.NextDouble() < 0.10)
                {
                    cell.LayerTiles[5] = featureTileBase + 150 + rng.Next(20);
                    drawCalls++;
                }

                // Layer 6: BACKGROUND_EFFECT — ~5%
                if (!isBorder && rng.NextDouble() < 0.05)
                {
                    cell.LayerTiles[6] = effectTileBase + rng.Next(30);
                    drawCalls++;
                }

                // Layer 7: CHAIN — ~2%
                if (!isBorder && rng.NextDouble() < 0.02)
                {
                    cell.LayerTiles[7] = objectTileBase + 500 + rng.Next(10);
                    drawCalls++;
                }

                // Layer 8: OBJECT — ~15% (items on the ground)
                if (!isBorder && rng.NextDouble() < 0.15)
                {
                    cell.LayerTiles[8] = objectTileBase + rng.Next(200);
                    drawCalls++;
                }

                // Layer 9: MONSTER — ~8% (creatures)
                bool hasMonster = !isBorder && rng.NextDouble() < 0.08;
                if (hasMonster)
                {
                    cell.LayerTiles[9] = monsterTileBase + rng.Next(300);
                    drawCalls++;
                }

                // Layer 10: MISSILE — ~2%
                if (!isBorder && rng.NextDouble() < 0.02)
                {
                    cell.LayerTiles[10] = objectTileBase + 300 + rng.Next(30);
                    drawCalls++;
                }

                // Layers 11-14: COVER layers — sparse (~1-3% each)
                for (int coverLayer = 11; coverLayer <= 14; coverLayer++)
                {
                    if (!isBorder && rng.NextDouble() < 0.02)
                    {
                        cell.LayerTiles[coverLayer] = featureTileBase + 200 + rng.Next(30);
                        drawCalls++;
                    }
                }

                // Layer 15: ENVIRONMENT — ~30% (fog, lighting, darkness overlays)
                if (!isBorder && rng.NextDouble() < 0.30)
                {
                    cell.LayerTiles[15] = effectTileBase + 100 + rng.Next(40);
                    drawCalls++;
                }

                // Layers 16-18: ZAP, GENERAL_EFFECT, MONSTER_EFFECT — sparse
                for (int fxLayer = 16; fxLayer <= 18; fxLayer++)
                {
                    if (!isBorder && rng.NextDouble() < 0.01)
                    {
                        cell.LayerTiles[fxLayer] = effectTileBase + 200 + rng.Next(30);
                        drawCalls++;
                    }
                }

                // Layer 19: GENERAL_UI — ~5% (status marks on monsters)
                if (hasMonster && rng.NextDouble() < 0.60)
                {
                    cell.LayerTiles[19] = uiTileBase + rng.Next(20);
                    drawCalls++;
                }

                // Pass 20: SHADOW — wherever monsters exist
                if (hasMonster)
                {
                    cell.LayerTiles[20] = uiTileBase + 50 + rng.Next(5);
                    drawCalls++;
                }

                // Pass 21: UI OVERLAY — ~3% (HP bars, player marker)
                if (hasMonster && rng.NextDouble() < 0.30)
                {
                    cell.LayerTiles[21] = uiTileBase + 60 + rng.Next(10);
                    drawCalls++;
                }

                // ~40% of non-border cells are "explored but not visible" (darkened)
                if (!isBorder && rng.NextDouble() < 0.40)
                {
                    cell.IsDarkened = true;
                }

                Cells[col, row] = cell;
            }
        }

        DrawCallsPerFrame = drawCalls;
    }
}
