using System;
using System.Collections.Generic;
using System.Threading;
using SkiaSharp;

namespace Net11FPSBenchmark
{
    // Mocked Structures from GnollHack
    public class MockGame
    {
        public object _petDataLock = new object();
        public List<object> _petData = new List<object>();
        public object _contextMenuDataLock = new object();
        public List<object> _contextMenuData = new List<object>();
    }

    public class MockMapData
    {
        public MockLayers Layers = new MockLayers();
        public string Symbol = "";
        public SKColor Color = SKColors.White;
        public ulong Special = 0;
        public long GlyphPrintMainCounterValue = 0;
        public long GlyphObjectPrintMainCounterValue = 0;
        public long GlyphGeneralPrintMainCounterValue = 0;
        public bool HasEnlargementOrAnimationOrSpecialHeight = false;
    }

    public class MockLayers
    {
        public int[] layer_glyphs = new int[7];
        public int[] layer_gui_glyphs = new int[7];
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

    public static class GHApp
    {
        public static object Glyph2TileLock = new object();
        public static int NoGlyph = -1;
        public static int[] Glyph2Tile = new int[100];
        public static int UsedTileSheets = 1;
        public static bool IsReplaySearching = false;
        public static SKTypeface LatoRegular = SKTypeface.Default;
        public static SKTypeface DejaVuSansMonoTypeface = SKTypeface.Default;
    }

    public static class GHConstants
    {
        public const int MapCols = 80;
        public const int MapRows = 22;
        public const int TileWidth = 32;
        public const int TileHeight = 32;
        public const float TileSizeAdjustmentModifier = 1.0f;
        public const float MapFontDefaultSize = 16f;
        public const int PIT_BOTTOM_BORDER = 4;
    }

    public class GnollHackRendererMock
    {
        private MockGame curGame = new MockGame();
        private MockMapData[,] _mapData = new MockMapData[GHConstants.MapCols, GHConstants.MapRows];
        private int[,] _draw_shadow = new int[GHConstants.MapCols, GHConstants.MapRows];
        private List<MockDrawOrder> _draw_order = new List<MockDrawOrder>();
        
        // Locks
        private object _savedCanvasLock = new object();
        private object _uLock = new object();
        private object _floatingTextLock = new object();
        private object _tileSizeLock = new object();
        private object _localWindowLock = new object();
        private object _drawOrderLock = new object();
        private object _guiEffectLock = new object();

        // Local state
        private float _savedCanvasWidth, _savedCanvasHeight;
        private int _ux, _uy;
        private float _usedTileWidth, _usedTileHeight, _mapWidth, _mapHeight;
        private List<object> _localFloatingTexts = new List<object>();
        private List<object> _localGuiEffects = new List<object>();
        private List<object> _localPetData = new List<object>();
        private List<SKRect> _localPetRects = new List<SKRect>();
        private List<object> _localContextMenuData = new List<object>();
        private List<SKRect> _localContextMenuRects = new List<SKRect>();

        private long maincountervalue = 0;

        public SKImage? TileSheet { get; set; }
        private SKSamplingOptions _samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);

        public GnollHackRendererMock()
        {
            Random rnd = new Random(12345);
            // Init mock map
            for (int x = 0; x < GHConstants.MapCols; x++)
            {
                for (int y = 0; y < GHConstants.MapRows; y++)
                {
                    _mapData[x, y] = new MockMapData();
                    for (int i = 0; i < 7; i++) 
                    {
                        if (rnd.NextDouble() > 0.5) {
                            _mapData[x, y].Layers.layer_glyphs[i] = rnd.Next(0, 3105);
                        } else {
                            _mapData[x, y].Layers.layer_glyphs[i] = -1;
                        }
                    }
                }
            }

            // Init draw order layers
            for (int i = 0; i < 7; i++)
            {
                _draw_order.Add(new MockDrawOrder { layer = i, enlargement_position = -1 });
            }
        }

        public void PaintMainGamePage(SKCanvas canvas, int width, int height)
        {
            float canvaswidth = width;
            float canvasheight = height;

            canvas.Clear(SKColors.Black);
            if (canvaswidth <= 16 || canvasheight <= 16) return;

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
            try
            {
                Monitor.TryEnter(_uLock, ref lockTaken);
                if (lockTaken) { _ux = 10; _uy = 10; }
            }
            finally { if (lockTaken) Monitor.Exit(_uLock); }

            lockTaken = false;
            try
            {
                Monitor.TryEnter(_floatingTextLock, ref lockTaken);
                if (lockTaken) { _localFloatingTexts.Clear(); }
            }
            finally { if (lockTaken) Monitor.Exit(_floatingTextLock); }

            lockTaken = false;
            try
            {
                Monitor.TryEnter(_guiEffectLock, ref lockTaken);
                if (lockTaken) { _localGuiEffects.Clear(); }
            }
            finally { if (lockTaken) Monitor.Exit(_guiEffectLock); }

            lockTaken = false;
            try
            {
                Monitor.TryEnter(curGame._petDataLock, ref lockTaken);
                if (lockTaken) { _localPetData.Clear(); }
            }
            finally { if (lockTaken) Monitor.Exit(curGame._petDataLock); }

            lockTaken = false;
            try
            {
                Monitor.TryEnter(curGame._contextMenuDataLock, ref lockTaken);
                if (lockTaken) { _localContextMenuData.Clear(); }
            }
            finally { if (lockTaken) Monitor.Exit(curGame._contextMenuDataLock); }

            maincountervalue++;

            float tileWidth = 32f, tileHeight = 32f;
            float mapwidth = tileWidth * GHConstants.MapCols;
            float mapheight = tileHeight * GHConstants.MapRows;

            lockTaken = false;
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

            float offsetX = 0, offsetY = 0;

            lock (GHApp.Glyph2TileLock)
            {
                using (SKPaint paint = new SKPaint { Color = SKColors.Green })
                {
                    int startX = 1, endX = GHConstants.MapCols - 1;
                    int startY = 0, endY = GHConstants.MapRows - 1;

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

                                    int source_x = mapx, source_y = mapy;
                                    if (source_x < 0 || source_x >= GHConstants.MapCols || source_y < 0 || source_y >= GHConstants.MapRows)
                                        continue;

                                    // Simulate struct fetching overhead
                                    bool loc_is_you = (layers.layer_flags & 1) != 0;
                                    sbyte monster_height = layers.special_monster_layer_height;
                                    sbyte feature_doodad_height = layers.special_feature_doodad_layer_height;
                                    short missile_special_quality = layers.missile_special_quality;
                                    sbyte monster_origin_x = layers.monster_origin_x;
                                    sbyte monster_origin_y = layers.monster_origin_y;
                                    long glyphprintmaincountervalue = _mapData[source_x, source_y].GlyphPrintMainCounterValue;
                                    int movediffx = (int)monster_origin_x - source_x;
                                    int movediffy = (int)monster_origin_y - source_y;

                                    // Mock Drawing Activity (A simple rect to tax the canvas slightly)
                                    float tx = offsetX + tileWidth * mapx;
                                    float ty = offsetY + tileHeight * mapy;
                                    
                                    // Normally GnollHack does canvas.DrawBitmap
                                    // To test loop overhead and identical drawing load, draw a real tile
                                    int tileIdx = layers.layer_glyphs[layer_idx];
                                    if (tileIdx >= 0 && TileSheet != null)
                                    {
                                        int srcX = (tileIdx % 69) * GHConstants.TileWidth;
                                        int srcY = (tileIdx / 69) * GHConstants.TileHeight;
                                        var sourceRect = new SKRect(srcX, srcY, srcX + GHConstants.TileWidth, srcY + GHConstants.TileHeight);
                                        var destRect = new SKRect(tx, ty, tx + GHConstants.TileWidth, ty + GHConstants.TileHeight);
                                        canvas.DrawImage(TileSheet, sourceRect, destRect, _samplingOptions, null);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
