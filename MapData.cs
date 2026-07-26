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

    /* ================================================================
     * GnollHack-Realistic Struct Data for Array Access Bottleneck
     *
     * GnollHack's real MapData struct is ~300+ bytes and includes:
     * - LayerInfo (~200 bytes): 24 inline int fields for glyph indices,
     *   64-bit flag fields, height/origin fields, HP counters, etc.
     * - Engraving (~32 bytes): type, flags, padding
     * - Counter fields (3x long = 24 bytes)
     * - Boolean flags, color data
     *
     * The struct size matters because every _mapData[x,y] access on
     * CoreCLR's 2D array performs bounds checking + address calculation.
     * If assigned by value (not ref), the entire struct is copied.
     *
     * In the real drawing loop, _mapData[x,y] is re-indexed ~19 times
     * per draw-order layer, totaling ~350 accesses per tile per frame.
     * ================================================================ */

    /// <summary>
    /// Mirrors GnollHack's LayerInfo struct (include/layer.h).
    /// Contains per-layer rendering metadata: glyph indices, flags,
    /// height offsets, animation origins, HP data.
    /// ~200 bytes (matches real struct layout).
    /// </summary>
    public struct RealisticLayerInfo
    {
        /* 12 layer glyph indices = 48 bytes */
        public int layer_glyph_0, layer_glyph_1, layer_glyph_2, layer_glyph_3;
        public int layer_glyph_4, layer_glyph_5, layer_glyph_6, layer_glyph_7;
        public int layer_glyph_8, layer_glyph_9, layer_glyph_10, layer_glyph_11;
        /* 12 GUI glyph indices = 48 bytes */
        public int layer_gui_glyph_0, layer_gui_glyph_1, layer_gui_glyph_2, layer_gui_glyph_3;
        public int layer_gui_glyph_4, layer_gui_glyph_5, layer_gui_glyph_6, layer_gui_glyph_7;
        public int layer_gui_glyph_8, layer_gui_glyph_9, layer_gui_glyph_10, layer_gui_glyph_11;

        /* Bit-flag fields (heavily tested in drawing loop) = 16 bytes */
        public ulong layer_flags;      /* LFLAGS_UXUY, LFLAGS_SHOWING_DETECTION, etc. */
        public ulong monster_flags;    /* LMFLAGS_RADIAL_TRANSPARENCY, etc. */

        /* Height and animation fields = 10 bytes */
        public sbyte special_monster_layer_height;
        public sbyte special_feature_doodad_layer_height;
        public short missile_special_quality;
        public sbyte monster_origin_x;
        public sbyte monster_origin_y;
        public short missile_height;
        public short object_height;

        /* HP data for UI overlay = 28 bytes */
        public int monster_hp;
        public int monster_maxhp;
        public ulong status_bits;
        public ulong condition_bits;
        public int hit_tile;
    }

    /// <summary>
    /// Mirrors GnollHack's Engraving sub-struct. ~32 bytes.
    /// </summary>
    public struct RealisticEngraving
    {
        public int EngrType;
        public ulong GeneralFlags;
        public long Padding1, Padding2;  /* text/rowsplit refs in real code */
    }

    /// <summary>
    /// Mirrors GnollHack's MapData struct — the element type of _mapData[,].
    /// ~300 bytes total. Every _mapData[x,y] access without ref copies all of this.
    /// In real GnollHack, the drawing loop accesses this ~350 times per tile.
    /// </summary>
    public struct RealisticMapData
    {
        public RealisticLayerInfo Layers;
        public RealisticEngraving Engraving;
        public ulong Special;
        public long GlyphPrintMainCounterValue;
        public long GlyphObjectPrintMainCounterValue;
        public long GlyphGeneralPrintMainCounterValue;
        public bool HasEnlargementOrAnimationOrSpecialHeight;
        public bool IsDarkened;
        public int Glyph;
        public int Color;
    }

    /// <summary>
    /// The 2D struct array that simulates GnollHack's _mapData[,].
    /// Accessing this without ref forces a full struct copy on CoreCLR.
    /// </summary>
    /* Sized to GHConstants.MapRows (22) since GnollHackRendererMock iterates
     * 0..21 while BenchmarkRenderer only uses 0..20. Must fit both. */
    public const int DataCols = 80;
    public const int DataRows = 22;
    public static RealisticMapData[,] Data { get; private set; } = new RealisticMapData[DataCols, DataRows];

    /// <summary>
    /// Total number of draw calls per frame (for diagnostics).
    /// </summary>
    public static int DrawCallsPerFrame { get; private set; }

    /// <summary>
    /// Initialize the realistic struct data with random values.
    /// </summary>
    public static void GenerateRealisticData()
    {
        var rng = new Random(42);
        for (int col = 0; col < DataCols; col++)
        {
            for (int row = 0; row < DataRows; row++)
            {
                Data[col, row].Layers.layer_flags = (ulong)rng.NextInt64();
                Data[col, row].Layers.monster_flags = (ulong)rng.NextInt64();
                Data[col, row].Layers.layer_glyph_0 = rng.Next(3000);
                Data[col, row].Layers.layer_glyph_6 = rng.Next(3000);
                Data[col, row].Layers.layer_gui_glyph_0 = rng.Next(3000);
                Data[col, row].Layers.layer_gui_glyph_6 = rng.Next(3000);
                Data[col, row].Layers.special_monster_layer_height = (sbyte)rng.Next(0, 3);
                Data[col, row].Layers.monster_origin_x = (sbyte)(col % 20);
                Data[col, row].Layers.monster_origin_y = (sbyte)(row % 20);
                Data[col, row].Layers.missile_height = (short)rng.Next(0, 10);
                Data[col, row].Layers.monster_hp = rng.Next(1, 100);
                Data[col, row].Layers.monster_maxhp = 100;
                Data[col, row].GlyphPrintMainCounterValue = rng.Next(0, 1000);
                Data[col, row].GlyphObjectPrintMainCounterValue = rng.Next(0, 1000);
                Data[col, row].GlyphGeneralPrintMainCounterValue = rng.Next(0, 1000);
                Data[col, row].HasEnlargementOrAnimationOrSpecialHeight = rng.NextDouble() < 0.1;
                Data[col, row].IsDarkened = rng.NextDouble() < 0.4;
            }
        }
    }

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

        // Also generate the realistic struct data
        GenerateRealisticData();
    }
}
