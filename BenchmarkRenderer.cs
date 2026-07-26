using SkiaSharp;
using System.Text;

namespace Net11FPSBenchmark;

/// <summary>
/// Core rendering class that mirrors GnollHack's PaintMainGamePage pipeline.
/// Includes:
/// - Multi-layer tile rendering (22 passes)
/// - Word-wrapped scrolling message area (above bottom buttons, going upward)
/// - Status bar with HP/Mana orbs (drawn on canvas)
/// - FPS overlay
/// </summary>
public class BenchmarkRenderer
{
    public bool SimulateArrayBottleneck = true;
    private SKImage? _tileSheet;
    private readonly SKPaint _normalPaint;
    private readonly SKPaint _darkenedPaint;

    // HUD paints and fonts
    private readonly SKPaint _fpsPaint;
    private readonly SKPaint _fpsBackgroundPaint;
    private readonly SKPaint _infoPaint;
    private readonly SKFont _fpsFont;
    private readonly SKFont _infoFont;

    // Message area paints and fonts (mirrors GnollHack's message window)
    private readonly SKPaint _msgFillPaint;
    private readonly SKPaint _msgStrokePaint;
    private readonly SKFont _msgFont;

    // Status bar paints
    private readonly SKPaint _statusBgPaint;
    private readonly SKPaint _statusTextPaint;
    private readonly SKPaint _hpOrbPaint;
    private readonly SKPaint _hpOrbBgPaint;
    private readonly SKPaint _manaOrbPaint;
    private readonly SKPaint _manaOrbBgPaint;
    private readonly SKPaint _orbOutlinePaint;
    private readonly SKFont _statusFont;
    private readonly SKFont _statusSmallFont;

    private float _cameraX;
    private float _cameraY;

    // Message buffer — scrolling list with word-wrapped lines
    // Same approach as GnollHack's _msgHistory / GHMsgHistoryItem
    private readonly object _messageLock = new object();
    private readonly List<MessageItem> _messages = new List<MessageItem>();
    private const int MaxVisibleMessages = 10;
    private const int MaxMessageBuffer = 30;

    // Reusable StringBuilder for word wrapping (same as GnollHack's _lineBuilder)
    private readonly StringBuilder _lineBuilder = new StringBuilder();

    // Flag to force re-wrap (same as GnollHack's RefreshMsgHistoryRowCounts)
    private bool _refreshWrapping = true;
    private float _lastWrapWidth = 0;

    // Simulated player stats for the status bar
    private int _playerHp = 78;
    private int _playerMaxHp = 100;
    private int _playerMana = 42;
    private int _playerMaxMana = 65;

    public bool MinimapMode { get; set; }
    public double Fps { get; set; }
    public float ButtonBarHeightPx { get; set; } = 280;
    public bool IsLoaded => _tileSheet != null;

    public BenchmarkRenderer()
    {
        _normalPaint = new SKPaint();

        // Darkening color filter
        var darkenMatrix = new float[]
        {
            0.45f, 0, 0, 0, 0,
            0, 0.45f, 0, 0, 0,
            0, 0, 0.45f, 0, 0,
            0, 0, 0, 1, 0
        };
        _darkenedPaint = new SKPaint
        {
            ColorFilter = SKColorFilter.CreateColorMatrix(darkenMatrix)
        };

        // FPS overlay
        var monoTypeface = SKTypeface.FromFamilyName("monospace", SKFontStyleWeight.Bold,
            SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        _fpsFont = new SKFont(monoTypeface, 44);
        _infoFont = new SKFont(SKTypeface.FromFamilyName("monospace"), 28);
        _fpsPaint = new SKPaint { Color = SKColors.Lime, IsAntialias = true };
        _fpsBackgroundPaint = new SKPaint
        {
            Color = new SKColor(0, 0, 0, 160),
            Style = SKPaintStyle.Fill
        };
        _infoPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };

        // Message area — text with stroke outline + fill (same as GnollHack)
        // Font 10% bigger (was 26, now ~29)
        var serifTypeface = SKTypeface.FromFamilyName("serif");
        _msgFont = new SKFont(serifTypeface, 29);
        _msgFillPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        _msgStrokePaint = new SKPaint
        {
            Color = SKColors.Black,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.5f
        };

        // Status bar
        _statusBgPaint = new SKPaint
        {
            Color = new SKColor(20, 15, 10, 200),
            Style = SKPaintStyle.Fill
        };
        _statusTextPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        _statusFont = new SKFont(monoTypeface, 28);
        _statusSmallFont = new SKFont(SKTypeface.FromFamilyName("monospace"), 22);

        // HP orb (red)
        _hpOrbBgPaint = new SKPaint { Color = new SKColor(60, 0, 0), Style = SKPaintStyle.Fill };
        _hpOrbPaint = new SKPaint { Color = new SKColor(200, 30, 30), Style = SKPaintStyle.Fill };

        // Mana orb (blue)
        _manaOrbBgPaint = new SKPaint { Color = new SKColor(0, 0, 60), Style = SKPaintStyle.Fill };
        _manaOrbPaint = new SKPaint { Color = new SKColor(40, 80, 220), Style = SKPaintStyle.Fill };

        // Orb outline
        _orbOutlinePaint = new SKPaint
        {
            Color = new SKColor(180, 160, 100),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            IsAntialias = true
        };

        // Seed initial messages
        _messages.Add(new MessageItem("Welcome to Net11 FPS Benchmark! This app reproduces GnollHack's rendering pipeline for performance testing."));
        _messages.Add(new MessageItem("Rendering 64x96 tiles from an 8192-tile atlas using SkiaSharp SKGLView with layer-first iteration."));
        _messages.Add(new MessageItem("The benchmark uses the same word wrapping, stroke+fill text rendering, and platform render loop as the real game."));
    }

    /// <summary>
    /// Add a message to the scrolling buffer (thread-safe).
    /// Same as GnollHack's putstr → _msgHistory.Add.
    /// </summary>
    public void AddMessage(string message)
    {
        lock (_messageLock)
        {
            _messages.Add(new MessageItem(message));
            _refreshWrapping = true;
            while (_messages.Count > MaxMessageBuffer)
                _messages.RemoveAt(0);
        }
    }

    public void LoadTileSheet(Stream stream)
    {
        var bitmap = SKBitmap.Decode(stream);
        bitmap.SetImmutable();
        _tileSheet = SKImage.FromBitmap(bitmap);
    }

    public SKImage? TileSheet => _tileSheet;

    /// <summary>
    /// Paint one complete frame. Full pipeline matching GnollHack's PaintMainGamePage.
    /// </summary>
    public void PaintFrame(SKCanvas canvas, int canvasWidth, int canvasHeight, bool drawMapAndText = true)
    {
        if (drawMapAndText)
        {
            canvas.Clear(SKColors.Black);

            if (_tileSheet == null)
            {
                canvas.DrawText("Loading tile sheet...", 50, canvasHeight / 2,
                    SKTextAlign.Left, _fpsFont, _fpsPaint);
                return;
            }

            // ================================================================
            // TILE RENDERING — 22-pass layer-first iteration
            // ================================================================
            float tileScale;
            float offsetX, offsetY;

            if (MinimapMode)
        {
            float mapPixelWidth = Constants.MapCols * Constants.TileWidth;
            float mapPixelHeight = Constants.MapRows * Constants.TileHeight;
            float xScale = canvasWidth / mapPixelWidth;
            float yScale = canvasHeight / mapPixelHeight;
            tileScale = Math.Min(xScale, yScale);
            float scaledMapWidth = mapPixelWidth * tileScale;
            float scaledMapHeight = mapPixelHeight * tileScale;
            offsetX = (canvasWidth - scaledMapWidth) / 2f;
            offsetY = (canvasHeight - scaledMapHeight) / 2f;
        }
        else
        {
            tileScale = canvasHeight / (12f * Constants.TileHeight);
            _cameraX = 40 * Constants.TileWidth * tileScale - canvasWidth / 2f;
            _cameraY = 10 * Constants.TileHeight * tileScale - canvasHeight / 2f;
            offsetX = -_cameraX;
            offsetY = -_cameraY;
        }

        canvas.SetMatrix(SKMatrix.CreateScaleTranslation(tileScale, tileScale,
            offsetX, offsetY));

        int startCol = 0, endCol = Constants.MapCols;
        int startRow = 0, endRow = Constants.MapRows;

        if (!MinimapMode)
        {
            startCol = Math.Max(0, (int)(_cameraX / (Constants.TileWidth * tileScale)) - 1);
            endCol = Math.Min(Constants.MapCols,
                (int)((_cameraX + canvasWidth) / (Constants.TileWidth * tileScale)) + 2);
            startRow = Math.Max(0, (int)(_cameraY / (Constants.TileHeight * tileScale)) - 1);
            endRow = Math.Min(Constants.MapRows,
                (int)((_cameraY + canvasHeight) / (Constants.TileHeight * tileScale)) + 2);
        }

        var samplingOptions = new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);

        for (int layerIdx = 0; layerIdx < Constants.TotalRenderPasses; layerIdx++)
        {
            for (int row = startRow; row < endRow; row++)
            {
                for (int col = startCol; col < endCol; col++)
                {
                    ref readonly var cell = ref MapData.Cells[col, row];
                    if (SimulateArrayBottleneck)
                    {
                        /* Simulate GnollHack's drawing loop: ~19 separate
                         * _mapData[x,y] accesses per draw-order layer.
                         * Each access on CoreCLR re-computes the 2D address
                         * and performs bounds checks on the ~300-byte struct. */
                        bool loc_is_you = (MapData.Data[col, row].Layers.layer_flags & 1) != 0;
                        bool showing_det = (MapData.Data[col, row].Layers.layer_flags & 2) != 0;
                        bool canspotself = (MapData.Data[col, row].Layers.monster_flags & 1) != 0;
                        sbyte mh = MapData.Data[col, row].Layers.special_monster_layer_height;
                        sbyte fdh = MapData.Data[col, row].Layers.special_feature_doodad_layer_height;
                        short msq = MapData.Data[col, row].Layers.missile_special_quality;
                        sbyte mox = MapData.Data[col, row].Layers.monster_origin_x;
                        sbyte moy = MapData.Data[col, row].Layers.monster_origin_y;
                        long gpv = MapData.Data[col, row].GlyphPrintMainCounterValue;
                        long opv = MapData.Data[col, row].GlyphObjectPrintMainCounterValue;
                        long genv = MapData.Data[col, row].GlyphGeneralPrintMainCounterValue;
                        short missile_h = MapData.Data[col, row].Layers.missile_height;
                        bool obj_in_pit = (MapData.Data[col, row].Layers.layer_flags & 4) != 0;
                        bool enlarg = MapData.Data[col, row].HasEnlargementOrAnimationOrSpecialHeight;
                        bool dark = MapData.Data[col, row].IsDarkened;
                        int gui_g = MapData.Data[col, row].Layers.layer_gui_glyph_6;
                        bool transp = (MapData.Data[col, row].Layers.monster_flags & 0x10) != 0;
                        int hp = MapData.Data[col, row].Layers.monster_hp;
                        int maxhp = MapData.Data[col, row].Layers.monster_maxhp;
                        /* Use results to prevent dead-code elimination */
                        if (loc_is_you && mox == 127 && gpv == long.MaxValue) continue;
                    }
                    int tileIdx = cell.LayerTiles[layerIdx];
                    if (tileIdx < 0)
                        continue;

                    tileIdx = tileIdx % Constants.TilesPerSheet;

                    int srcX = (tileIdx % Constants.TilesPerRow) * Constants.TileWidth;
                    int srcY = (tileIdx / Constants.TilesPerRow) * Constants.TileHeight;
                    var sourceRect = new SKRect(srcX, srcY,
                        srcX + Constants.TileWidth, srcY + Constants.TileHeight);

                    float destX = col * Constants.TileWidth;
                    float destY = row * Constants.TileHeight;
                    var destRect = new SKRect(destX, destY,
                        destX + Constants.TileWidth, destY + Constants.TileHeight);

                    // Darken top 7 rows and bottom 7 rows (mimics GnollHack's
                    // lit center / dark periphery lighting model)
                    bool isDarkened = row < 7 || row >= Constants.MapRows - 7;
                    var paint = isDarkened ? _darkenedPaint : _normalPaint;

                    canvas.DrawImage(_tileSheet, sourceRect, destRect,
                        samplingOptions, paint);
                }
            }
        }

        // Reset matrix for HUD overlays (screen space)
        canvas.ResetMatrix();
        }

        // ================================================================
        // STATUS BAR — drawn on canvas above the bottom button bar
        // ================================================================
        DrawStatusBar(canvas, canvasWidth, canvasHeight);

        // ================================================================
        // MESSAGE AREA — above button bar, going upward
        // Word-wrapped text with stroke+fill rendering (same as GnollHack)
        // ================================================================
        DrawMessages(canvas, canvasWidth, canvasHeight);

        // ================================================================
        // FPS OVERLAY — top-left corner
        // ================================================================
        DrawFpsOverlay(canvas, canvasWidth);
    }

    /// <summary>
    /// Draw the scrolling message area above the bottom buttons.
    /// Uses GnollHack's exact word-wrapping algorithm:
    /// - MeasureText per word
    /// - Break at 0.85 * canvasWidth
    /// - Store wrapped rows in MessageItem.WrappedRows (same as GHMsgHistoryItem.WrappedTextRows)
    /// - Draw newest messages at bottom (just above status bar), oldest scroll upward
    /// - Text rendered with stroke outline + fill (same as GnollHack)
    /// </summary>
    private void DrawMessages(SKCanvas canvas, int canvasWidth, int canvasHeight)
    {
        float lineLengthLimit = 0.85f * canvasWidth;
        float lineHeight = 36f;

        // Measure space width (same as GnollHack: spaceLength = textPaint.MeasureText(" "))
        float spaceLength = _msgFont.MeasureText(" ");

        // Check if we need to re-wrap (canvas width changed or new messages)
        if (Math.Abs(_lastWrapWidth - lineLengthLimit) > 1f)
        {
            _refreshWrapping = true;
            _lastWrapWidth = lineLengthLimit;
        }

        MessageItem[] messageCopy;
        lock (_messageLock)
        {
            messageCopy = _messages.ToArray();
        }

        // ================================================================
        // WORD WRAPPING — same algorithm as GnollHack's PaintMainGamePage
        // (RefreshMsgHistoryRowCounts / msgHistoryItem.WrappedTextRows)
        // ================================================================
        if (_refreshWrapping)
        {
            for (int idx = 0; idx < messageCopy.Length; idx++)
            {
                MessageItem item = messageCopy[idx];
                item.WrappedRows.Clear();
                _lineBuilder.Clear();
                float lineLength = 0f;
                bool firstOnLine = true;

                for (int widx = 0; widx < item.TextSplit.Length; widx++)
                {
                    string word = item.TextSplit[widx];
                    float wordLength = _msgFont.MeasureText(word);
                    float wordWithSpaceLength = wordLength + spaceLength;

                    if (lineLength + wordLength > lineLengthLimit && !firstOnLine)
                    {
                        // Line full — commit and start new line
                        // (same logic as GnollHack's wrapping)
                        item.WrappedRows.Add(_lineBuilder.ToString());
                        _lineBuilder.Clear();
                        _lineBuilder.Append(word);
                        _lineBuilder.Append(' ');
                        lineLength = wordWithSpaceLength;
                        firstOnLine = true;
                    }
                    else
                    {
                        _lineBuilder.Append(word);
                        _lineBuilder.Append(' ');
                        lineLength += wordWithSpaceLength;
                        firstOnLine = false;
                    }
                }
                // Commit last line
                item.WrappedRows.Add(_lineBuilder.ToString());
            }
            _refreshWrapping = false;
        }

        // ================================================================
        // RENDERING — draw from bottom upward (newest at bottom)
        // Position: just above the button bar
        // Same rendering as GnollHack: iterate from newest to oldest,
        // decrementing the row index per wrapped line
        // ================================================================
        float bottomY = canvasHeight - ButtonBarHeightPx;
        int maxRows = MaxVisibleMessages;
        int j = maxRows - 1; // Row index counting down from bottom

        for (int idx = messageCopy.Length - 1; idx >= 0 && j >= 0; idx--)
        {
            MessageItem item = messageCopy[idx];
            if (item.WrappedRows.Count == 0)
                continue;

            for (int lineIdx = item.WrappedRows.Count - 1; lineIdx >= 0 && j >= 0; lineIdx--)
            {
                string wrappedLine = item.WrappedRows[lineIdx];
                float ty = bottomY - (maxRows - 1 - j) * lineHeight;

                // Skip if off-screen top
                if (ty < 0)
                {
                    j = -1; // Stop drawing
                    break;
                }

                float tx = 16;

                // Stroke outline (black) — same as GnollHack's message rendering
                canvas.DrawText(wrappedLine, tx, ty, SKTextAlign.Left, _msgFont, _msgStrokePaint);

                // Fill (white)
                canvas.DrawText(wrappedLine, tx, ty, SKTextAlign.Left, _msgFont, _msgFillPaint);

                j--;
            }
        }
    }

    /// <summary>
    /// Draw the status bar above the bottom button bar.
    /// Mimics GnollHack's DrawExtendedStatusBar with HP/Mana orbs.
    /// </summary>
    private void DrawStatusBar(SKCanvas canvas, int canvasWidth, int canvasHeight)
    {
        float barHeight = 120;
        // Move to top, just below the FPS/Info overlay
        float barTop = 140;

        // Semi-transparent dark background
        canvas.DrawRect(0, barTop, canvasWidth, barHeight, _statusBgPaint);

        // Simulate slowly changing stats
        _playerHp = 50 + (int)(28 * Math.Sin(Environment.TickCount64 / 3000.0));
        _playerMana = 30 + (int)(20 * Math.Cos(Environment.TickCount64 / 4000.0));

        // HP Orb (left side)
        float orbRadius = 38;
        float hpOrbCx = 60;
        float hpOrbCy = barTop + barHeight / 2;
        DrawOrb(canvas, hpOrbCx, hpOrbCy, orbRadius,
            _playerHp, _playerMaxHp, _hpOrbBgPaint, _hpOrbPaint, "HP");

        // Mana Orb
        float manaOrbCx = 160;
        float manaOrbCy = barTop + barHeight / 2;
        DrawOrb(canvas, manaOrbCx, manaOrbCy, orbRadius,
            _playerMana, _playerMaxMana, _manaOrbBgPaint, _manaOrbPaint, "MP");

        // Stats text
        float statsX = 240;
        float statsY1 = barTop + 35;
        float statsY2 = barTop + 65;
        float statsY3 = barTop + 95;

        canvas.DrawText("Str:18/42  Dex:16  Con:17  Int:14  Wis:18  Cha:11",
            statsX, statsY1, SKTextAlign.Left, _statusSmallFont, _statusTextPaint);
        canvas.DrawText("Dlvl:8  AC:-3  Xp:12/48000  T:15432  $:2847",
            statsX, statsY2, SKTextAlign.Left, _statusSmallFont, _statusTextPaint);
        canvas.DrawText("Satiated  Burdened  Hallu",
            statsX, statsY3, SKTextAlign.Left, _statusSmallFont, _statusTextPaint);
    }

    /// <summary>
    /// Draw a circular stat orb — mirrors GnollHack's DrawOrb.
    /// </summary>
    private void DrawOrb(SKCanvas canvas, float cx, float cy, float radius,
        int current, int max, SKPaint bgPaint, SKPaint fillPaint, string label)
    {
        canvas.DrawCircle(cx, cy, radius, bgPaint);

        float fillRatio = max > 0 ? Math.Clamp((float)current / max, 0f, 1f) : 0f;
        float fillHeight = radius * 2 * fillRatio;
        float fillTop = cy + radius - fillHeight;

        canvas.Save();
        var pathBuilder = new SKPathBuilder();
        pathBuilder.AddCircle(cx, cy, radius);
        using (var clipPath = pathBuilder.Snapshot())
        {
            canvas.ClipPath(clipPath);
            canvas.DrawRect(cx - radius, fillTop, radius * 2, fillHeight, fillPaint);
        }
        canvas.Restore();

        canvas.DrawCircle(cx, cy, radius, _orbOutlinePaint);

        string valueText = $"{current}/{max}";
        canvas.DrawText(valueText, cx, cy + 5, SKTextAlign.Center, _statusSmallFont, _statusTextPaint);
        canvas.DrawText(label, cx, cy + radius + 18, SKTextAlign.Center, _statusSmallFont, _statusTextPaint);
    }

    /// <summary>
    /// Draw the FPS counter and runtime info overlay.
    /// </summary>
    private void DrawFpsOverlay(SKCanvas canvas, int canvasWidth)
    {
        string fpsText = $"FPS: {Fps:F1}";
        string infoText = $"Draws/frame: {MapData.DrawCallsPerFrame}";
        string runtimeText = $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}";

        canvas.DrawRect(0, 0, canvasWidth, 140, _fpsBackgroundPaint);
        canvas.DrawText(fpsText, 20, 42, SKTextAlign.Left, _fpsFont, _fpsPaint);
        canvas.DrawText(infoText, 20, 78, SKTextAlign.Left, _infoFont, _infoPaint);
        canvas.DrawText(runtimeText, 20, 115, SKTextAlign.Left, _infoFont, _infoPaint);
    }
}

/// <summary>
/// A single message entry — mirrors GnollHack's GHMsgHistoryItem.
/// Stores the original text, pre-split words, and wrapped display rows.
/// </summary>
public class MessageItem
{
    /// <summary>Original message text.</summary>
    public string Text { get; }

    /// <summary>
    /// Pre-split words — same as GnollHack's GHMsgHistoryItem.TextSplit.
    /// Split once on construction to avoid per-frame string allocations.
    /// </summary>
    public string[] TextSplit { get; }

    /// <summary>
    /// Word-wrapped display rows — same as GnollHack's GHMsgHistoryItem.WrappedTextRows.
    /// Recalculated when canvas width changes or new messages arrive.
    /// </summary>
    public List<string> WrappedRows { get; } = new List<string>();

    public MessageItem(string text)
    {
        Text = text;
        TextSplit = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }
}
