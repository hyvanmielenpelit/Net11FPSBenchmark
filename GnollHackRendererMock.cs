using System;
using System.Collections.Generic;
using System.Threading;
using SkiaSharp;

namespace Net11FPSBenchmark
{
    /* ================================================================
     * Mocked GnollHack Structures
     * These mirror the actual GnollHack data structures used by
     * PaintMainGamePage to ensure the mock renderer has the same
     * memory access patterns.
     * ================================================================ */
    public class MockGame
    {
        public object _petDataLock = new object();
        public List<object> _petData = new List<object>();
        public object _contextMenuDataLock = new object();
        public List<object> _contextMenuData = new List<object>();
        public object _weaponStyleObjDataItemLock = new object();
        public int[] _weaponStyleObjDataItem = new int[4];
        public object StatusFieldLock = new object();
        public int[] StatusFields = new int[32];
        public object AnimationTimerLock = new object();
        public long MainCounterValue = 0;
        public long GeneralAnimationCounter = 0;
    }

    public class MockMapData
    {
        public MockLayers Layers = new MockLayers();
        public MockEngraving Engraving = new MockEngraving();
        public string Symbol = "";
        public SKColor Color = SKColors.White;
        public ulong Special = 0;
        public long GlyphPrintMainCounterValue = 0;
        public long GlyphObjectPrintMainCounterValue = 0;
        public long GlyphGeneralPrintMainCounterValue = 0;
        public bool HasEnlargementOrAnimationOrSpecialHeight = false;
        public bool IsDarkened = false;
    }

    public class MockEngraving
    {
        public bool HasEngraving = false;
    }

    public class MockLayers
    {
        public int[] layer_glyphs = new int[12];
        public int[] layer_gui_glyphs = new int[12];
        public ulong layer_flags = 0;
        public ulong monster_flags = 0;
        public sbyte special_monster_layer_height = 0;
        public sbyte special_feature_doodad_layer_height = 0;
        public short missile_special_quality = 0;
        public sbyte monster_origin_x = 0;
        public sbyte monster_origin_y = 0;
        public short missile_height = 0;
        public short object_height = 0;
    }

    public class MockDrawOrder
    {
        public int enlargement_position;
        public int layer;
    }

    public struct MockDrawCommand
    {
        public SKMatrix Matrix;
        public SKRect SourceRect;
        public SKRect DestRect;
        public bool IsDark;
    }

    public static class GHApp
    {
        public static object Glyph2TileLock = new object();
        public static int NoGlyph = -1;
        public static int[] Glyph2Tile = new int[4000];
        public static int UsedTileSheets = 1;
        public static bool IsReplaySearching = false;
        public static SKTypeface LatoRegular = SKTypeface.Default;
        public static SKTypeface DejaVuSansMonoTypeface = SKTypeface.Default;
    }

    public static class GHConstants
    {
        public const int MapCols = 80;
        public const int MapRows = 22;
        public const int TileWidth = 64;
        public const int TileHeight = 96;
        public const int TilesPerRow = 69;
        public const int TilesPerSheet = 69 * 45;
        public const float TileSizeAdjustmentModifier = 1.0f;
        public const float MapFontDefaultSize = 16f;
        public const int PIT_BOTTOM_BORDER = 4;
        /* GnollHack has MAX_LAYERS=10 real layers + shadow layer + UI layer = 12 draw order entries */
        public const int MAX_LAYERS = 10;
    }

    /* ================================================================
     * GnollHackRendererMock
     *
     * Faithfully replicates the lock sequence, loop structure, and
     * drawing calls from GnollHack's PaintMainGamePage
     * (GamePage.xaml.cs lines 7399-8276+).
     *
     * Lock order matches GnollHack exactly:
     *   1. _savedCanvasLock
     *   2. AnimationTimerLock
     *   3. _clipLock
     *   4. _mapOffsetLock
     *   5. _statusOffsetLock
     *   6. _weaponStyleObjDataItemLock
     *   7. StatusFieldLock
     *   8. _uLock  (inside GetMapDataBuffer)
     *   9. _floatingTextLock
     *  10. _screenTextLock
     *  11. _conditionTextLock
     *  12. _screenFilterLock
     *  13. _guiEffectLock
     *  14. _petDataLock
     *  15. _contextMenuDataLock
     *  16. _canvasPointerLock (Windows only, skipped)
     *
     * Drawing loop:
     *   lock(Glyph2TileLock)
     *     lock(_drawOrderLock) : AlternativeLayerDrawing path
     *       for each mapy
     *         for each mapx
     *           for each draw_order entry
     *             canvas.DrawImage(...)
     * ================================================================ */
    public class GnollHackRendererMock
    {
        public bool SimulateArrayBottleneck = true;
        public bool SimulateCanvasTransform = true;
        public bool SimulateSplitDrawing = true;
        public bool SimulateManagedStruct = true;
        private MockGame curGame = new MockGame();
        private List<MockDrawCommand> _mockDrawCommands = new List<MockDrawCommand>(512);
        private MockMapData[,] _mapData = new MockMapData[GHConstants.MapCols, GHConstants.MapRows];
        private int[,] _draw_shadow = new int[GHConstants.MapCols, GHConstants.MapRows];
        private List<MockDrawOrder> _draw_order = new List<MockDrawOrder>();
        
        /* ---- Locks (matching GnollHack field names exactly) ---- */
        private object _savedCanvasLock = new object();
        private object _uLock = new object();
        private object _floatingTextLock = new object();
        private object _tileSizeLock = new object();
        private object _localWindowLock = new object();
        private object _drawOrderLock = new object();
        private object _guiEffectLock = new object();
        private object _clipLock = new object();
        private object _mapOffsetLock = new object();
        private object _statusOffsetLock = new object();
        private object _screenTextLock = new object();
        private object _conditionTextLock = new object();
        private object _screenFilterLock = new object();

        /* ---- Local state (mirrors GnollHack's _local* fields) ---- */
        private float _savedCanvasWidth, _savedCanvasHeight;
        private int _ux, _uy;
        private float _usedTileWidth, _usedTileHeight, _mapWidth, _mapHeight;
        private float _localClipX, _localClipY;
        private float _localMapOffsetX, _localMapOffsetY;
        private float _localMapMiniOffsetX, _localMapMiniOffsetY;
        private float _localStatusOffsetY;
        private int[] _localWeaponStyleObjDataItem = new int[4];
        private int[] _localStatusFields = new int[32];
        private string _localScreenText = "";
        private List<object> _localFloatingTexts = new List<object>();
        private List<object> _localGuiEffects = new List<object>();
        private List<object> _localPetData = new List<object>();
        private List<SKRect> _localPetRects = new List<SKRect>();
        private List<object> _localContextMenuData = new List<object>();
        private List<SKRect> _localContextMenuRects = new List<SKRect>();
        private List<object> _localConditionTexts = new List<object>();
        private List<object> _localScreenFilters = new List<object>();

        private long maincountervalue = 0;
        private long generalcountervalue = 0;

        public SKImage? TileSheet { get; set; }
        public bool MinimapMode { get; set; }
        private SKSamplingOptions _samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);

        /* Darkening paint: matches GnollHack's color filter for unlit areas */
        private readonly SKPaint _normalPaint;
        private readonly SKPaint _darkenedPaint;

        public GnollHackRendererMock()
        {
            _normalPaint = new SKPaint();
            var darkenMatrix = new float[]
            {
                0.40f, 0, 0, 0, 0,
                0, 0.40f, 0, 0, 0,
                0, 0, 0.40f, 0, 0,
                0, 0, 0, 1, 0
            };
            _darkenedPaint = new SKPaint
            {
                ColorFilter = SKColorFilter.CreateColorMatrix(darkenMatrix)
            };

            Random rnd = new Random(12345);

            /* Init mock map with realistic tile distribution:
             * - Most tiles: 1 layer (floor/background only): layer 0
             * - ~20 tiles per map: 2-3 layers (floor + monster or object)
             * - ~5 tiles per map: 5-10 layers (item piles)
             * This matches real GnollHack gameplay density. */
            int totalCells = GHConstants.MapCols * GHConstants.MapRows; /* 1760 */
            int multiLayerCount = 20;  /* tiles with 2-3 layers */
            int pileCount = 5;         /* tiles with 5-10 layers */

            /* First, pick which cells will be multi-layer or piles */
            HashSet<int> multiLayerCells = new HashSet<int>();
            HashSet<int> pileCells = new HashSet<int>();
            while (multiLayerCells.Count < multiLayerCount)
                multiLayerCells.Add(rnd.Next(totalCells));
            while (pileCells.Count < pileCount)
            {
                int idx = rnd.Next(totalCells);
                if (!multiLayerCells.Contains(idx))
                    pileCells.Add(idx);
            }

            for (int x = 0; x < GHConstants.MapCols; x++)
            {
                for (int y = 0; y < GHConstants.MapRows; y++)
                {
                    _mapData[x, y] = new MockMapData();
                    int cellIdx = x * GHConstants.MapRows + y;

                    /* Clear all layers to NoGlyph first */
                    for (int i = 0; i < _mapData[x, y].Layers.layer_glyphs.Length; i++)
                    {
                        _mapData[x, y].Layers.layer_glyphs[i] = GHApp.NoGlyph;
                        _mapData[x, y].Layers.layer_gui_glyphs[i] = GHApp.NoGlyph;
                    }

                    /* ~50% of tiles are darkened (unlit areas) */
                    _mapData[x, y].IsDarkened = rnd.NextDouble() < 0.5;

                    /* Layer 0 (floor): always present */
                    int floorTile = rnd.Next(0, 100);
                    _mapData[x, y].Layers.layer_glyphs[0] = floorTile;
                    _mapData[x, y].Layers.layer_gui_glyphs[0] = floorTile;

                    if (pileCells.Contains(cellIdx))
                    {
                        /* Item pile: 5-10 random layers filled */
                        int layerCount = rnd.Next(5, 11);
                        for (int i = 1; i < Math.Min(layerCount, _mapData[x, y].Layers.layer_glyphs.Length); i++)
                        {
                            int tile = rnd.Next(0, 3105);
                            _mapData[x, y].Layers.layer_glyphs[i] = tile;
                            _mapData[x, y].Layers.layer_gui_glyphs[i] = tile;
                        }
                    }
                    else if (multiLayerCells.Contains(cellIdx))
                    {
                        /* 2-3 layers: floor + monster/object */
                        int extraLayers = rnd.Next(1, 3);
                        for (int i = 0; i < extraLayers; i++)
                        {
                            /* Put on monster layer (6) or object layer (4) */
                            int layerSlot = (i == 0) ? 6 : 4;
                            int tile = rnd.Next(0, 3105);
                            _mapData[x, y].Layers.layer_glyphs[layerSlot] = tile;
                            _mapData[x, y].Layers.layer_gui_glyphs[layerSlot] = tile;
                        }
                    }
                    /* else: single floor tile only */
                }
            }

            /* Init draw order: GnollHack uses MAX_LAYERS + 2 entries
             * (10 real layers + shadow layer + UI layer = 12).
             * The AlternativeLayerDrawing path iterates all of them
             * per tile, with enlargement_position values -1..4. */
            for (int i = 0; i < GHConstants.MAX_LAYERS + 2; i++)
            {
                _draw_order.Add(new MockDrawOrder { layer = i, enlargement_position = -1 });
            }

            /* GnollHack also adds enlargement variants (positions 0-4)
             * for certain layers, making the real draw_order much larger.
             * Add some to match the workload. */
            for (int enl = 0; enl < 5; enl++)
            {
                /* Enlargement for monster layer and a few others */
                _draw_order.Add(new MockDrawOrder { layer = 6, enlargement_position = enl });
                _draw_order.Add(new MockDrawOrder { layer = 8, enlargement_position = enl });
            }
        }

        public void PaintMainGamePage(SKCanvas canvas, int width, int height)
        {
            float canvaswidth = width;
            float canvasheight = height;

            canvas.Clear(SKColors.Black);
            if (canvaswidth <= 16 || canvasheight <= 16) return;

            /* ============================================================
             * LOCK SEQUENCE: exact same order as GnollHack
             * GamePage.xaml.cs lines 7421-7878
             * ============================================================ */

            /* 1. _savedCanvasLock (line 7422-7435) */
            bool lockTaken = false;
            try
            {
                Monitor.TryEnter(_savedCanvasLock, ref lockTaken);
                if (lockTaken)
                {
                    _savedCanvasWidth = canvaswidth;
                    _savedCanvasHeight = canvasheight;
                }
            }
            finally { if (lockTaken) Monitor.Exit(_savedCanvasLock); }
            lockTaken = false;

            /* 2. AnimationTimerLock (line 7584-7597) */
            try
            {
                Monitor.TryEnter(curGame.AnimationTimerLock, ref lockTaken);
                if (lockTaken)
                {
                    /* GnollHack: curGame.AnimationTimers.CopyTo(_localAnimationTimers, false, false); */
                    generalcountervalue++;
                }
            }
            finally { if (lockTaken) Monitor.Exit(curGame.AnimationTimerLock); }
            lockTaken = false;

            /* 3. _clipLock (line 7606-7620) */
            try
            {
                Monitor.TryEnter(_clipLock, ref lockTaken);
                if (lockTaken)
                {
                    _localClipX = 0;
                    _localClipY = 0;
                }
            }
            finally { if (lockTaken) Monitor.Exit(_clipLock); }
            lockTaken = false;

            /* 4. _mapOffsetLock (line 7622-7638) */
            try
            {
                Monitor.TryEnter(_mapOffsetLock, ref lockTaken);
                if (lockTaken)
                {
                    _localMapOffsetX = 0;
                    _localMapOffsetY = 0;
                    _localMapMiniOffsetX = 0;
                    _localMapMiniOffsetY = 0;
                }
            }
            finally { if (lockTaken) Monitor.Exit(_mapOffsetLock); }
            lockTaken = false;

            /* 5. _statusOffsetLock (line 7640-7654) */
            try
            {
                Monitor.TryEnter(_statusOffsetLock, ref lockTaken);
                if (lockTaken)
                {
                    _localStatusOffsetY = 0;
                }
            }
            finally { if (lockTaken) Monitor.Exit(_statusOffsetLock); }
            lockTaken = false;

            /* 6. _weaponStyleObjDataItemLock (line 7656-7670) */
            try
            {
                Monitor.TryEnter(curGame._weaponStyleObjDataItemLock, ref lockTaken);
                if (lockTaken)
                {
                    curGame._weaponStyleObjDataItem.CopyTo(_localWeaponStyleObjDataItem, 0);
                }
            }
            finally { if (lockTaken) Monitor.Exit(curGame._weaponStyleObjDataItemLock); }
            lockTaken = false;

            /* 7. StatusFieldLock (line 7672-7686) */
            try
            {
                Monitor.TryEnter(curGame.StatusFieldLock, ref lockTaken);
                if (lockTaken)
                {
                    curGame.StatusFields.CopyTo(_localStatusFields, 0);
                }
            }
            finally { if (lockTaken) Monitor.Exit(curGame.StatusFieldLock); }
            lockTaken = false;

            /* 8. _uLock (inside GetMapDataBuffer, line 7717-7731) */
            try
            {
                Monitor.TryEnter(_uLock, ref lockTaken);
                if (lockTaken) { _ux = 10; _uy = 10; }
            }
            finally { if (lockTaken) Monitor.Exit(_uLock); }
            lockTaken = false;

            /* 9. _floatingTextLock (line 7737-7752) */
            try
            {
                Monitor.TryEnter(_floatingTextLock, ref lockTaken);
                if (lockTaken) { _localFloatingTexts.Clear(); }
            }
            finally { if (lockTaken) Monitor.Exit(_floatingTextLock); }
            lockTaken = false;

            /* 10. _screenTextLock (line 7754-7768) */
            try
            {
                Monitor.TryEnter(_screenTextLock, ref lockTaken);
                if (lockTaken) { _localScreenText = ""; }
            }
            finally { if (lockTaken) Monitor.Exit(_screenTextLock); }
            lockTaken = false;

            /* 11. _conditionTextLock (line 7770-7785) */
            try
            {
                Monitor.TryEnter(_conditionTextLock, ref lockTaken);
                if (lockTaken) { _localConditionTexts.Clear(); }
            }
            finally { if (lockTaken) Monitor.Exit(_conditionTextLock); }
            lockTaken = false;

            /* 12. _screenFilterLock (line 7787-7801) */
            try
            {
                Monitor.TryEnter(_screenFilterLock, ref lockTaken);
                if (lockTaken) { _localScreenFilters.Clear(); }
            }
            finally { if (lockTaken) Monitor.Exit(_screenFilterLock); }
            lockTaken = false;

            /* 13. _guiEffectLock (line 7805-7818) */
            try
            {
                Monitor.TryEnter(_guiEffectLock, ref lockTaken);
                if (lockTaken) { _localGuiEffects.Clear(); }
            }
            finally { if (lockTaken) Monitor.Exit(_guiEffectLock); }
            lockTaken = false;

            /* 14. _petDataLock (line 7821-7839) */
            try
            {
                Monitor.TryEnter(curGame._petDataLock, ref lockTaken);
                if (lockTaken) { _localPetData.Clear(); }
            }
            finally { if (lockTaken) Monitor.Exit(curGame._petDataLock); }
            lockTaken = false;

            /* 15. _contextMenuDataLock (line 7842-7861) */
            try
            {
                Monitor.TryEnter(curGame._contextMenuDataLock, ref lockTaken);
                if (lockTaken) { _localContextMenuData.Clear(); }
            }
            finally { if (lockTaken) Monitor.Exit(curGame._contextMenuDataLock); }
            lockTaken = false;

            maincountervalue++;

            /* ============================================================
             * TILE SIZE + VIEWPORT: mirrors GnollHack lines 7972-8109
             * GnollHack calculates usedFontSize, width, height, then
             * computes altStartX/altEndX/altStartY/altEndY for culling.
             * We replicate this with a scale+translate matrix like the
             * simple renderer to get equivalent visual output.
             * ============================================================ */
            float tileWidth = GHConstants.TileWidth;
            float tileHeight = GHConstants.TileHeight;
            float mapwidth = tileWidth * (GHConstants.MapCols - 1);
            float mapheight = tileHeight * GHConstants.MapRows;

            try
            {
                Monitor.TryEnter(_tileSizeLock, ref lockTaken);
                if (lockTaken)
                {
                    _usedTileWidth = tileWidth;
                    _usedTileHeight = tileHeight;
                    _mapWidth = mapwidth;
                    _mapHeight = mapheight;
                }
            }
            finally { if (lockTaken) Monitor.Exit(_tileSizeLock); }
            lockTaken = false;

            float targetscale = tileHeight / (float)GHConstants.TileHeight;
            float pit_border = (float)GHConstants.PIT_BOTTOM_BORDER * tileHeight / (float)GHConstants.TileHeight;

            /* Scale and center the map: supports minimap mode */
            float tileScale;
            float cameraX, cameraY;
            float offsetX, offsetY;

            if (MinimapMode)
            {
                float mapPixelWidth = GHConstants.MapCols * GHConstants.TileWidth;
                float mapPixelHeight = GHConstants.MapRows * GHConstants.TileHeight;
                float xScale = canvaswidth / mapPixelWidth;
                float yScale = canvasheight / mapPixelHeight;
                tileScale = Math.Min(xScale, yScale);
                float scaledMapWidth = mapPixelWidth * tileScale;
                float scaledMapHeight = mapPixelHeight * tileScale;
                offsetX = (canvaswidth - scaledMapWidth) / 2f;
                offsetY = (canvasheight - scaledMapHeight) / 2f;
                cameraX = 0;
                cameraY = 0;
            }
            else
            {
                tileScale = canvasheight / (12f * GHConstants.TileHeight);
                cameraX = 40 * GHConstants.TileWidth * tileScale - canvaswidth / 2f;
                cameraY = 10 * GHConstants.TileHeight * tileScale - canvasheight / 2f;
                offsetX = -cameraX;
                offsetY = -cameraY;
            }

            canvas.SetMatrix(SKMatrix.CreateScaleTranslation(tileScale, tileScale,
                offsetX, offsetY));

            /* Viewport culling */
            int startX, endX, startY, endY;
            if (MinimapMode)
            {
                startX = 0;
                endX = GHConstants.MapCols - 1;
                startY = 0;
                endY = GHConstants.MapRows - 1;
            }
            else
            {
                startX = Math.Max(1, (int)(cameraX / (GHConstants.TileWidth * tileScale)) - 1);
                endX = Math.Min(GHConstants.MapCols - 1,
                    (int)((cameraX + canvaswidth) / (GHConstants.TileWidth * tileScale)) + 2);
                startY = Math.Max(0, (int)(cameraY / (GHConstants.TileHeight * tileScale)) - 1);
                endY = Math.Min(GHConstants.MapRows - 1,
                    (int)((cameraY + canvasheight) / (GHConstants.TileHeight * tileScale)) + 2);
            }

            /* ============================================================
             * DRAWING LOOP: matches AlternativeLayerDrawing path
             * (GamePage.xaml.cs lines 8037-8276)
             *
             * Structure:
             *   lock(Glyph2TileLock)
             *     Array.Clear(_draw_shadow)
             *     lock(_drawOrderLock)
             *       for mapy in [startY..endY]
             *         for mapx in [startX..endX]
             *           null-check layers
             *           for draw_idx in _draw_order
             *             compute enlargement source
             *             fetch all layer properties
             *             canvas.DrawImage(...)
             * ============================================================ */
            lock (GHApp.Glyph2TileLock)
            {
                Array.Clear(_draw_shadow, 0, _draw_shadow.Length);
                _mockDrawCommands.Clear();

                lock (_drawOrderLock)
                {
                    for (int mapy = startY; mapy <= endY; mapy++)
                    {
                        for (int mapx = startX; mapx <= endX; mapx++)
                        {
                            var layers = _mapData[mapx, mapy].Layers;
                            if (layers.layer_glyphs == null || layers.layer_gui_glyphs == null)
                                continue;

                            int draw_cnt = _draw_order.Count;
                            for (int draw_idx = 0; draw_idx < draw_cnt; draw_idx++)
                            {
                                int enl_idx = _draw_order[draw_idx].enlargement_position;
                                int layer_idx = _draw_order[draw_idx].layer;

                                /* GnollHack: shadow layer skip check */
                                bool is_monster_or_shadow_layer = (layer_idx == 6 || layer_idx == GHConstants.MAX_LAYERS);
                                bool is_monster_like_layer = (is_monster_or_shadow_layer || layer_idx == 7);
                                bool is_object_like_layer = (layer_idx == 4 || layer_idx == 9);
                                bool is_missile_layer = (layer_idx == 5);

                                if (layer_idx == GHConstants.MAX_LAYERS
                                    && (_draw_shadow[mapx, mapy] == 0
                                        || (layer_idx < layers.layer_gui_glyphs.Length
                                            && layers.layer_gui_glyphs[6] == GHApp.NoGlyph)))
                                    continue;

                                /* Compute enlargement source tile */
                                int source_x = mapx, source_y = mapy;
                                switch (enl_idx)
                                {
                                    default:
                                    case -1:
                                        break;
                                    case 0:
                                        source_x = mapx + 1;
                                        source_y = mapy + 1;
                                        break;
                                    case 1:
                                        source_y = mapy + 1;
                                        break;
                                    case 2:
                                        source_x = mapx - 1;
                                        source_y = mapy + 1;
                                        break;
                                    case 3:
                                        source_x = mapx + 1;
                                        break;
                                    case 4:
                                        source_x = mapx - 1;
                                        break;
                                }
                                if (source_x < 0 || source_x >= GHConstants.MapCols
                                    || source_y < 0 || source_y >= GHConstants.MapRows)
                                    continue;

                                if (enl_idx >= 0 && !_mapData[source_x, source_y].HasEnlargementOrAnimationOrSpecialHeight)
                                    continue;

                                /* ---- Fetch all layer properties (matches lines 8164-8182) ---- */
                                bool loc_is_you, showing_detection, canspotself, obj_in_pit;
                                sbyte monster_height, feature_doodad_height, monster_origin_x, monster_origin_y;
                                short missile_special_quality, missile_height;
                                long glyphprintmaincountervalue, glyphobjectprintmaincountervalue, glyphgeneralprintmaincountervalue;
                                bool isDark;

                                if (SimulateManagedStruct)
                                {
                                    /* When ON: access the MANAGED struct array.
                                     * ManagedMapData contains reference-type fields
                                     * (int[], string, string[]). Each `ref` creates a
                                     * managed interior pointer — slow on CoreCLR. */
                                    ref MapData.ManagedMapData srcCell = ref MapData.ManagedData[source_x, source_y];
                                    ref MapData.ManagedLayerInfo srcLayers = ref MapData.ManagedData[source_x, source_y].Layers;
                                    loc_is_you = (srcLayers.layer_flags & 1) != 0;
                                    showing_detection = (srcLayers.layer_flags & 2) != 0;
                                    canspotself = (srcLayers.monster_flags & 1) != 0;
                                    monster_height = srcLayers.special_monster_layer_height;
                                    feature_doodad_height = srcLayers.special_feature_doodad_layer_height;
                                    missile_special_quality = srcLayers.missile_special_quality;
                                    monster_origin_x = srcLayers.monster_origin_x;
                                    monster_origin_y = srcLayers.monster_origin_y;
                                    glyphprintmaincountervalue = srcCell.GlyphPrintMainCounterValue;
                                    glyphobjectprintmaincountervalue = srcCell.GlyphObjectPrintMainCounterValue;
                                    glyphgeneralprintmaincountervalue = srcCell.GlyphGeneralPrintMainCounterValue;
                                    missile_height = srcLayers.missile_height;
                                    obj_in_pit = (srcLayers.layer_flags & 4) != 0;
                                    isDark = MapData.ManagedData[mapx, mapy].NeedsUpdate; /* Touch another cell */
                                    /* Additional accesses matching PaintMapTile's flag reads */
                                    bool transp = (srcLayers.monster_flags & 0x10) != 0;
                                    int hp = srcLayers.monster_hp;
                                    int maxhp = srcLayers.monster_maxhp;
                                    int gui_g = (srcLayers.layer_gui_glyphs != null && srcLayers.layer_gui_glyphs.Length > 6)
                                        ? srcLayers.layer_gui_glyphs[6] : -1;
                                    bool enlarg2 = srcCell.HasEnlargementOrAnimationOrSpecialHeight;

                                    /* Simulate PaintMapTile ref passing */
                                    ref MapData.ManagedMapData paintCell = ref MapData.ManagedData[source_x, source_y];
                                    ref MapData.ManagedLayerInfo paintLayers = ref MapData.ManagedData[source_x, source_y].Layers;
                                    bool touchPaint = paintCell.MapAnimated; /* Touch the ref */
                                }
                                else if (SimulateArrayBottleneck)
                                {
                                    /* When ON: access the ~300-byte STRUCT array via
                                     * repeated 2D indexing — this is what kills CoreCLR.
                                     * Each MapData.Data[x,y] forces address calc + bounds check
                                     * on the ~300-byte struct. 19 accesses per layer. */
                                    loc_is_you = (MapData.Data[source_x, source_y].Layers.layer_flags & 1) != 0;
                                    showing_detection = (MapData.Data[source_x, source_y].Layers.layer_flags & 2) != 0;
                                    canspotself = (MapData.Data[source_x, source_y].Layers.monster_flags & 1) != 0;
                                    monster_height = MapData.Data[source_x, source_y].Layers.special_monster_layer_height;
                                    feature_doodad_height = MapData.Data[source_x, source_y].Layers.special_feature_doodad_layer_height;
                                    missile_special_quality = MapData.Data[source_x, source_y].Layers.missile_special_quality;
                                    monster_origin_x = MapData.Data[source_x, source_y].Layers.monster_origin_x;
                                    monster_origin_y = MapData.Data[source_x, source_y].Layers.monster_origin_y;
                                    glyphprintmaincountervalue = MapData.Data[source_x, source_y].GlyphPrintMainCounterValue;
                                    glyphobjectprintmaincountervalue = MapData.Data[source_x, source_y].GlyphObjectPrintMainCounterValue;
                                    glyphgeneralprintmaincountervalue = MapData.Data[source_x, source_y].GlyphGeneralPrintMainCounterValue;
                                    missile_height = MapData.Data[source_x, source_y].Layers.missile_height;
                                    obj_in_pit = (MapData.Data[source_x, source_y].Layers.layer_flags & 4) != 0;
                                    isDark = MapData.Data[mapx, mapy].IsDarkened;
                                    /* Additional accesses matching PaintMapTile's flag reads */
                                    bool transp = (MapData.Data[source_x, source_y].Layers.monster_flags & 0x10) != 0;
                                    int hp = MapData.Data[source_x, source_y].Layers.monster_hp;
                                    int maxhp = MapData.Data[source_x, source_y].Layers.monster_maxhp;
                                    int gui_g = MapData.Data[source_x, source_y].Layers.layer_gui_glyph_6;
                                    bool enlarg2 = MapData.Data[source_x, source_y].HasEnlargementOrAnimationOrSpecialHeight;
                                }
                                else
                                {
                                    /* When OFF: use the class-based MockMapData (pointer lookups, fast) */
                                    loc_is_you = (layers.layer_flags & 1) != 0;
                                    showing_detection = (layers.layer_flags & 2) != 0;
                                    canspotself = (layers.monster_flags & 1) != 0;
                                    monster_height = layers.special_monster_layer_height;
                                    feature_doodad_height = layers.special_feature_doodad_layer_height;
                                    missile_special_quality = layers.missile_special_quality;
                                    monster_origin_x = layers.monster_origin_x;
                                    monster_origin_y = layers.monster_origin_y;
                                    glyphprintmaincountervalue = _mapData[source_x, source_y].GlyphPrintMainCounterValue;
                                    glyphobjectprintmaincountervalue = _mapData[source_x, source_y].GlyphObjectPrintMainCounterValue;
                                    glyphgeneralprintmaincountervalue = _mapData[source_x, source_y].GlyphGeneralPrintMainCounterValue;
                                    missile_height = layers.missile_height;
                                    obj_in_pit = (layers.layer_flags & 4) != 0;
                                    isDark = _mapData[mapx, mapy].IsDarkened;
                                }
                                int movediffx = (int)monster_origin_x - source_x;
                                int movediffy = (int)monster_origin_y - source_y;
                                long maincounterdiff = maincountervalue - glyphprintmaincountervalue;
                                long objectcounterdiff = maincountervalue - glyphobjectprintmaincountervalue;
                                long generalcounterdiff = generalcountervalue - glyphgeneralprintmaincountervalue;

                                /* ---- Draw tile ---- */
                                float tx = tileWidth * mapx;
                                float ty = tileHeight * mapy;
                                
                                int glyphIdx = (layer_idx < layers.layer_glyphs.Length)
                                    ? layers.layer_glyphs[layer_idx]
                                    : GHApp.NoGlyph;
                                if (glyphIdx >= 0 && TileSheet != null)
                                {
                                    int tileIdx = glyphIdx % GHConstants.TilesPerSheet;
                                    int srcX = (tileIdx % GHConstants.TilesPerRow) * GHConstants.TileWidth;
                                    int srcY = (tileIdx / GHConstants.TilesPerRow) * GHConstants.TileHeight;
                                    var sourceRect = new SKRect(srcX, srcY,
                                        srcX + GHConstants.TileWidth, srcY + GHConstants.TileHeight);
                                    var paint = isDark ? _darkenedPaint : _normalPaint;

                                    if (SimulateCanvasTransform)
                                    {
                                        /* Simulate GnollHack's PaintMapTile pattern:
                                         * using (new SKAutoCanvasRestore(canvas, true))
                                         * {
                                         *     canvas.Translate(tr_x, tr_y);
                                         *     canvas.Scale(sc_x, sc_y, 0, 0);
                                         *     canvas.DrawImage(..., localRect, ...);
                                         * }
                                         * Every tile in GnollHack goes through this. */
                                        canvas.Save();
                                        canvas.Translate(tx, ty);
                                        canvas.Scale(1.0f, 1.0f, 0, 0);
                                        var destRect = new SKRect(0, 0,
                                            GHConstants.TileWidth, GHConstants.TileHeight);
                                        canvas.DrawImage(TileSheet, sourceRect, destRect,
                                            _samplingOptions, paint);
                                        canvas.Restore();
                                    }
                                    else
                                    {
                                        var destRect = new SKRect(tx, ty,
                                            tx + GHConstants.TileWidth, ty + GHConstants.TileHeight);
                                        canvas.DrawImage(TileSheet, sourceRect, destRect,
                                            _samplingOptions, paint);
                                    }
                                    /* Store for delayed replay (split drawing):
                                     * In GnollHack, DrawSplitBitmap stores the top
                                     * portion of tall/enlarged tiles in _drawCommandList
                                     * for a second pass with per-command SetMatrix. */
                                    if (SimulateSplitDrawing && layer_idx > 0)
                                    {
                                        _mockDrawCommands.Add(new MockDrawCommand
                                        {
                                            Matrix = canvas.TotalMatrix,
                                            SourceRect = sourceRect,
                                            DestRect = new SKRect(tx, ty,
                                                tx + GHConstants.TileWidth, ty + GHConstants.TileHeight),
                                            IsDark = isDark
                                        });
                                    }
                                }
                            }
                        }
                    }
                }

                /* ============================================================
                 * DELAYED DRAW REPLAY PASS
                 * Matches GnollHack's second pass over _drawCommandList
                 * (GamePage.xaml.cs lines 8573-8803).
                 * Each command gets its own SetMatrix + DrawImage call.
                 * ============================================================ */
                if (SimulateSplitDrawing)
                {
                    for (int i = 0; i < _mockDrawCommands.Count; i++)
                    {
                        var dc = _mockDrawCommands[i];
                        canvas.SetMatrix(dc.Matrix);
                        var paint = dc.IsDark ? _darkenedPaint : _normalPaint;
                        canvas.DrawImage(TileSheet, dc.SourceRect, dc.DestRect,
                            _samplingOptions, paint);
                    }
                }
            }

            /* Reset matrix for any subsequent overlay drawing */
            canvas.ResetMatrix();
        }
    }
}
