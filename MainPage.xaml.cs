using System.Diagnostics;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace Net11FPSBenchmark;

/// <summary>
/// Main page hosting the SKGLView benchmark.
/// Uses platform-native render loops for vsync-aligned frame timing:
/// - Android: Choreographer.IFrameCallback (hardware vsync)
/// - iOS: CADisplayLink (display refresh-synced)
/// Same approach as GnollHack's PlatformRenderLoop.
/// </summary>
public partial class MainPage : ContentPage
{
    private readonly BenchmarkRenderer _renderer = new BenchmarkRenderer();
    private readonly GnollHackRendererMock _ghRenderer = new GnollHackRendererMock();
    private bool _useGnollHackRenderer = true;
    private bool _simulateArrayBottleneck = true;
    private bool _simulateCanvasTransform = true;
    private bool _simulateSplitDrawing = true;
    private bool _simulateManagedStruct = true;

    private readonly Stopwatch _stopwatch;

    // FPS calculation: same atomic counter approach as GnollHack
    private long _frameCounter;
    private long _previousFrameCounter;
    private double _currentFps;

    // Reentrancy guard: same pattern as GnollHack's IsMainCanvasDrawing
    private int _isDrawing;

    private bool _tileSheetLoaded;
    private bool _buttonImagesLoaded;

    // Platform render loop ticker references
#if ANDROID
    private ChoreographerFrameTicker? _platformTicker;
#elif IOS
    private DisplayLinkTicker? _platformTicker;
#endif

    // Message pool for random messages added every second
    private static readonly string[] MessagePool = new[]
    {
        "You see a long corridor stretching to the east.",
        "The walls are damp with moisture. A faint glow illuminates the passage.",
        "You hear the distant sound of water dripping somewhere in the dungeon.",
        "A flickering torch casts dancing shadows on the ancient stone walls.",
        "The air is thick with the scent of moss and crumbling masonry.",
        "Something stirs in the darkness ahead. You grip your weapon tightly.",
        "A faint glow emanates from a crack in the northern wall.",
        "The floor here is covered with strange runes and symbols.",
        "You feel a cold draft coming from somewhere below.",
        "An eerie silence fills the chamber. The torches flicker nervously.",
        "You notice scratch marks on the floor leading into the shadows.",
        "The ceiling here is unusually high, lost in darkness above.",
        "A pile of bones lies in the corner. They look very old.",
        "You find a set of footprints in the dust, leading north.",
        "The stone here has a different texture, smoother and darker.",
        "A distant rumble echoes through the corridors. The walls tremble.",
        "You spot a glint of metal partially buried in the rubble.",
        "The passage narrows here. You must squeeze through sideways.",
        "Cobwebs stretch across the doorway. No one has passed this way recently.",
        "A pool of still water reflects the faint torchlight overhead.",
        "You hear a faint scratching noise behind the eastern wall.",
        "The temperature drops noticeably as you move deeper into the level.",
        "Faded murals on the walls depict scenes of an ancient battle.",
        "A rusted iron gate blocks the passage to the south.",
        "Strange fungi grow along the base of the walls here.",
        "You sense a presence watching you from the shadows.",
        "The dungeon level continues with more twisting corridors and hidden chambers.",
        "A tattered banner hangs from a corroded bracket on the wall.",
        "The echo of your footsteps sounds unusually loud in this chamber.",
        "You stumble upon an old campsite. The ashes are long cold.",
    };

    private readonly Random _messageRng = new Random();

    // Button bar height for the renderer (measured from XAML layout)
    private double _buttonBarHeight = 140; // Default, updated on SizeChanged

    public MainPage()
    {
        InitializeComponent();

        _stopwatch = Stopwatch.StartNew();

        // Generate synthetic map data
        MapData.Generate();

        // Attach tap gesture to ALL buttons : each toggles minimap mode
        // (makes it easy to test both modes from any button)
        var allButtons = new CachedImageButton[]
        {
            StoneMenuBtn, StoneCancelBtn, StoneAutoCenterBtn,
            StoneMinimapBtn, StoneLookBtn, StoneTravelBtn,
            BtnInventory, BtnSearch, BtnWait, BtnDropMany,
            BtnChat, BtnKick, BtnRepeat,
            BtnSwap, BtnFire, BtnThrow, BtnCast,
            BtnZap, BtnApply, BtnMore
        };

        // Initialize toggle texts
        ToggleRendererBtn.Text = "Renderer: GnollHack (Mock)";
        ToggleRendererBtn.BackgroundColor = Colors.DarkGreen;
        foreach (var btn in allButtons)
        {
            var tapGesture = new TapGestureRecognizer();
            tapGesture.Tapped += (s, e) => ToggleMinimap();
            btn.GestureRecognizers.Add(tapGesture);
        }

        // Track button bar height for positioning messages
        ButtonRowGrid.SizeChanged += (s, e) =>
        {
            _buttonBarHeight = ButtonRowGrid.Height;
        };

        // Start the platform-native render loop
        StartPlatformRenderLoop();

        // FPS counter timer (runs every second)
        Dispatcher.StartTimer(TimeSpan.FromSeconds(1), OnFpsTick);

        // Message ticker : add a new random message every second
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(1000), OnMessageTick);
    }

    /// <summary>
    /// Start the platform-native render loop.
    /// </summary>
    private void StartPlatformRenderLoop()
    {
#if ANDROID
        _platformTicker = new ChoreographerFrameTicker();
        _platformTicker.Start(frameTimeNanos =>
        {
            OnRenderFrame();
        });
#elif IOS
        _platformTicker = new DisplayLinkTicker();
        _platformTicker.Start(deltaTime =>
        {
            OnRenderFrame();
        });
#else
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(1), () =>
        {
            OnRenderFrame();
            return true;
        });
#endif
    }

    /// <summary>
    /// Called on each platform vsync/frame callback.
    /// </summary>
    private void OnRenderFrame()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            MainCanvasView.InvalidateSurface();
        });
    }

    /// <summary>
    /// FPS calculation tick : runs every second.
    /// </summary>
    private bool OnFpsTick()
    {
        long currentCounter = Interlocked.Read(ref _frameCounter);
        long delta = currentCounter - _previousFrameCounter;
        _previousFrameCounter = currentCounter;

        double elapsed = _stopwatch.Elapsed.TotalSeconds;
        _stopwatch.Restart();

        if (elapsed > 0)
            _currentFps = delta / elapsed;

        return true;
    }

    /// <summary>
    /// Message tick : add a new random message every second.
    /// </summary>
    private bool OnMessageTick()
    {
        string msg = MessagePool[_messageRng.Next(MessagePool.Length)];
        _renderer.AddMessage(msg);
        return true;
    }

    /// <summary>
    /// SKCanvasView paint handler (fallback)
    /// </summary>
    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        PaintInternal(e.Surface.Canvas, e.Info.Width, e.Info.Height);
    }

    /// <summary>
    /// SKGLView paint handler : mirrors GnollHack's canvasView_PaintSurface.
    /// </summary>
    private void OnPaintSurfaceGL(object? sender, SKPaintGLSurfaceEventArgs e)
    {
        PaintInternal(e.Surface.Canvas, e.BackendRenderTarget.Width, e.BackendRenderTarget.Height);
    }

    private void PaintInternal(SKCanvas canvas, int width, int height)
    {
        // Reentrancy guard
        if (Interlocked.CompareExchange(ref _isDrawing, 1, 0) != 0)
            return;

        try
        {
            if (!_tileSheetLoaded)
            {
                _tileSheetLoaded = true;
                _ = LoadTileSheetAsync();
            }

            if (!_buttonImagesLoaded)
            {
                _buttonImagesLoaded = true;
                _ = LoadButtonImagesAsync();
            }

            // Pass the device pixel density to convert XAML dp to canvas pixels
            float density = (float)DeviceDisplay.MainDisplayInfo.Density;
            
            if (_useGnollHackRenderer)
            {
                _ghRenderer.PaintMainGamePage(canvas, width, height);
                // Also paint the benchmark UI overlays on top so we can still see FPS
                _renderer.Fps = _currentFps;
                _renderer.ButtonBarHeightPx = (float)(_buttonBarHeight * density);
                _renderer.PaintFrame(canvas, width, height, drawMapAndText: false); 
            }
            else
            {
                _renderer.Fps = _currentFps;
                _renderer.ButtonBarHeightPx = (float)(_buttonBarHeight * density);
                _renderer.PaintFrame(canvas, width, height, drawMapAndText: true);
            }

            Interlocked.Increment(ref _frameCounter);
            canvas.Flush();
        }
        finally
        {
            Interlocked.Exchange(ref _isDrawing, 0);
        }
    }

    private async Task LoadTileSheetAsync()
    {
        try
        {
            using var stream = await FileSystem.OpenAppPackageFileAsync(
                "tileset/benchmark_tiles.png");
            _renderer.LoadTileSheet(stream);
            _ghRenderer.TileSheet = _renderer.TileSheet;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load tile sheet: {ex.Message}");
        }
    }

    /// <summary>
    /// Load all button images from app package assets.
    /// Same as GnollHack's LabeledImageButton loading from embedded resources.
    /// </summary>
    private async Task LoadButtonImagesAsync()
    {
        try
        {
            // Stone buttons (top-right)
            await StoneMenuBtn.LoadImageAsync("ui/stone-menu.png");
            await StoneCancelBtn.LoadImageAsync("ui/stone-cancel.png");
            await StoneAutoCenterBtn.LoadImageAsync("ui/stone-autocenter-on.png");
            await StoneMinimapBtn.LoadImageAsync("ui/stone-minimap-off.png");
            await StoneLookBtn.LoadImageAsync("ui/stone-look-off.png");
            await StoneTravelBtn.LoadImageAsync("ui/stone-travel-off.png");

            // Upper command row
            await BtnInventory.LoadImageAsync("ui/inventory.png");
            BtnInventory.SetLabel("Inv");
            await BtnSearch.LoadImageAsync("ui/search.png");
            BtnSearch.SetLabel("Search");
            await BtnWait.LoadImageAsync("ui/wait.png");
            BtnWait.SetLabel("Wait");
            await BtnDropMany.LoadImageAsync("ui/dropmany.png");
            BtnDropMany.SetLabel("Drop");
            await BtnChat.LoadImageAsync("ui/chat.png");
            BtnChat.SetLabel("Chat");
            await BtnKick.LoadImageAsync("ui/kick.png");
            BtnKick.SetLabel("Kick");
            await BtnRepeat.LoadImageAsync("ui/repeat.png");
            BtnRepeat.SetLabel("Repeat");

            // Lower command row
            await BtnSwap.LoadImageAsync("ui/swap.png");
            BtnSwap.SetLabel("Swap");
            await BtnFire.LoadImageAsync("ui/fire.png");
            BtnFire.SetLabel("Fire");
            await BtnThrow.LoadImageAsync("ui/throw.png");
            BtnThrow.SetLabel("Throw");
            await BtnCast.LoadImageAsync("ui/cast.png");
            BtnCast.SetLabel("Cast");
            await BtnZap.LoadImageAsync("ui/zap.png");
            BtnZap.SetLabel("Zap");
            await BtnApply.LoadImageAsync("ui/apply.png");
            BtnApply.SetLabel("Apply");
            await BtnMore.LoadImageAsync("ui/more.png");
            BtnMore.SetLabel("More");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load button images: {ex.Message}");
        }
    }

    /// <summary>
    /// Toggle minimap mode - called by all button taps.
    /// </summary>
    private void ToggleMinimap()
    {
        _renderer.MinimapMode = !_renderer.MinimapMode;
        _ghRenderer.MinimapMode = _renderer.MinimapMode;
    }

    private void ToggleRendererBtn_Clicked(object sender, EventArgs e)
    {
        _useGnollHackRenderer = !_useGnollHackRenderer;
        ToggleRendererBtn.Text = _useGnollHackRenderer ? "Renderer: GnollHack (Mock)" : "Renderer: Simple";
        ToggleRendererBtn.BackgroundColor = _useGnollHackRenderer ? Colors.DarkGreen : Colors.DarkRed;
    }

    private void ToggleArrayBottleneckBtn_Clicked(object sender, EventArgs e)
    {
        _simulateArrayBottleneck = !_simulateArrayBottleneck;
        ToggleArrayBottleneckBtn.Text = _simulateArrayBottleneck ? "Array Bottleneck: ON" : "Array Bottleneck: OFF";
        ToggleArrayBottleneckBtn.BackgroundColor = _simulateArrayBottleneck ? Colors.Purple : Colors.Gray;
        _renderer.SimulateArrayBottleneck = _simulateArrayBottleneck;
        _ghRenderer.SimulateArrayBottleneck = _simulateArrayBottleneck;
    }

    private void ToggleCanvasTransformBtn_Clicked(object sender, EventArgs e)
    {
        _simulateCanvasTransform = !_simulateCanvasTransform;
        ToggleCanvasTransformBtn.Text = _simulateCanvasTransform ? "Canvas Transform: ON" : "Canvas Transform: OFF";
        ToggleCanvasTransformBtn.BackgroundColor = _simulateCanvasTransform ? Colors.DarkOrange : Colors.Gray;
        _renderer.SimulateCanvasTransform = _simulateCanvasTransform;
        _ghRenderer.SimulateCanvasTransform = _simulateCanvasTransform;
    }

    private void ToggleSplitDrawingBtn_Clicked(object sender, EventArgs e)
    {
        _simulateSplitDrawing = !_simulateSplitDrawing;
        ToggleSplitDrawingBtn.Text = _simulateSplitDrawing ? "Split Drawing: ON" : "Split Drawing: OFF";
        ToggleSplitDrawingBtn.BackgroundColor = _simulateSplitDrawing ? Colors.DarkCyan : Colors.Gray;
        _renderer.SimulateSplitDrawing = _simulateSplitDrawing;
        _ghRenderer.SimulateSplitDrawing = _simulateSplitDrawing;
    }

    private void ToggleManagedStructBtn_Clicked(object sender, EventArgs e)
    {
        _simulateManagedStruct = !_simulateManagedStruct;
        ToggleManagedStructBtn.Text = _simulateManagedStruct ? "Managed Struct: ON" : "Managed Struct: OFF";
        ToggleManagedStructBtn.BackgroundColor = _simulateManagedStruct ? Colors.DarkMagenta : Colors.Gray;
        _renderer.SimulateManagedStruct = _simulateManagedStruct;
        _ghRenderer.SimulateManagedStruct = _simulateManagedStruct;
    }

    private void OnButtonClicked(object sender, EventArgs e)
    {
    }

    private void OnTouch(object sender, SKTouchEventArgs e)
    {
        e.Handled = true;
    }

    private async void RunArrayBenchmarkBtn_Clicked(object sender, EventArgs e)
    {
        RunArrayBenchmarkBtn.IsEnabled = false;
        RunArrayBenchmarkBtn.Text = "Running...";
        
        string result = await Task.Run(() => 
        {
            var sw = new Stopwatch();
            long sum = 0;
            int Iterations = 1000;
            int Cols = Constants.MapCols;
            int Rows = Constants.MapRows;
            int LayersCount = 12;

            var data2D = MapData.Data;

            var data1D = new MapData.RealisticMapData[Cols * Rows];
            for (int y = 0; y < Rows; y++)
                for (int x = 0; x < Cols; x++)
                    data1D[y * Cols + x] = data2D[x, y];

            string output = $"Array Benchmark ({Iterations} frames, {Cols}x{Rows}, {LayersCount} layers)\n";

            sw.Restart();
            for (int i = 0; i < Iterations; i++)
            {
                for (int y = 0; y < Rows; y++)
                {
                    for (int x = 0; x < Cols; x++)
                    {
                        for (int l = 0; l < LayersCount; l++)
                        {
                            if ((data2D[x, y].Layers.layer_flags & 0x1) != 0) sum++;
                            if ((data2D[x, y].Layers.monster_flags & 0x1) != 0) sum++;
                            sum += data2D[x, y].Layers.special_monster_layer_height;
                            sum += data2D[x, y].Layers.monster_origin_x;
                            sum += data2D[x, y].GlyphPrintMainCounterValue;
                        }
                    }
                }
            }
            sw.Stop();
            output += $"\n1. 2D[x,y] Direct: {sw.ElapsedMilliseconds} ms";

            sw.Restart();
            for (int i = 0; i < Iterations; i++)
            {
                for (int y = 0; y < Rows; y++)
                {
                    for (int x = 0; x < Cols; x++)
                    {
                        for (int l = 0; l < LayersCount; l++)
                        {
                            var cell = data2D[x, y];
                            if ((cell.Layers.layer_flags & 0x1) != 0) sum++;
                            if ((cell.Layers.monster_flags & 0x1) != 0) sum++;
                            sum += cell.Layers.special_monster_layer_height;
                            sum += cell.Layers.monster_origin_x;
                            sum += cell.GlyphPrintMainCounterValue;
                        }
                    }
                }
            }
            sw.Stop();
            output += $"\n2. Struct Copy: {sw.ElapsedMilliseconds} ms";

            sw.Restart();
            for (int i = 0; i < Iterations; i++)
            {
                for (int y = 0; y < Rows; y++)
                {
                    for (int x = 0; x < Cols; x++)
                    {
                        for (int l = 0; l < LayersCount; l++)
                        {
                            ref var cell = ref data2D[x, y];
                            if ((cell.Layers.layer_flags & 0x1) != 0) sum++;
                            if ((cell.Layers.monster_flags & 0x1) != 0) sum++;
                            sum += cell.Layers.special_monster_layer_height;
                            sum += cell.Layers.monster_origin_x;
                            sum += cell.GlyphPrintMainCounterValue;
                        }
                    }
                }
            }
            sw.Stop();
            output += $"\n3. Ref Local: {sw.ElapsedMilliseconds} ms";

            sw.Restart();
            for (int i = 0; i < Iterations; i++)
            {
                for (int y = 0; y < Rows; y++)
                {
                    for (int x = 0; x < Cols; x++)
                    {
                        for (int l = 0; l < LayersCount; l++)
                        {
                            if ((data1D[y * Cols + x].Layers.layer_flags & 0x1) != 0) sum++;
                            if ((data1D[y * Cols + x].Layers.monster_flags & 0x1) != 0) sum++;
                            sum += data1D[y * Cols + x].Layers.special_monster_layer_height;
                            sum += data1D[y * Cols + x].Layers.monster_origin_x;
                            sum += data1D[y * Cols + x].GlyphPrintMainCounterValue;
                        }
                    }
                }
            }
            sw.Stop();
            output += $"\n4. 1D Array: {sw.ElapsedMilliseconds} ms";

            /* Test 5: Managed struct 2D array with ref (the slow path) */
            var managed2D = MapData.ManagedData;
            sw.Restart();
            for (int i = 0; i < Iterations; i++)
            {
                for (int y = 0; y < Rows; y++)
                {
                    for (int x = 0; x < Cols; x++)
                    {
                        for (int l = 0; l < LayersCount; l++)
                        {
                            ref MapData.ManagedMapData cell = ref managed2D[x, y];
                            ref MapData.ManagedLayerInfo layers = ref managed2D[x, y].Layers;
                            if ((layers.layer_flags & 0x1) != 0) sum++;
                            if ((layers.monster_flags & 0x1) != 0) sum++;
                            sum += layers.special_monster_layer_height;
                            sum += layers.monster_origin_x;
                            sum += cell.GlyphPrintMainCounterValue;
                        }
                    }
                }
            }
            sw.Stop();
            output += $"\n5. Managed 2D Ref: {sw.ElapsedMilliseconds} ms";

            /* Test 6: Blittable struct 2D array with ref (should be fast) */
            sw.Restart();
            for (int i = 0; i < Iterations; i++)
            {
                for (int y = 0; y < Rows; y++)
                {
                    for (int x = 0; x < Cols; x++)
                    {
                        for (int l = 0; l < LayersCount; l++)
                        {
                            ref MapData.RealisticMapData cell = ref data2D[x, y];
                            ref MapData.RealisticLayerInfo layers = ref data2D[x, y].Layers;
                            if ((layers.layer_flags & 0x1) != 0) sum++;
                            if ((layers.monster_flags & 0x1) != 0) sum++;
                            sum += layers.special_monster_layer_height;
                            sum += layers.monster_origin_x;
                            sum += cell.GlyphPrintMainCounterValue;
                        }
                    }
                }
            }
            sw.Stop();
            output += $"\n6. Blittable 2D Ref: {sw.ElapsedMilliseconds} ms";

            output += $"\nSum: {sum}";
            return output;
        });

        await DisplayAlert("Benchmark Results", result, "OK");
        
        RunArrayBenchmarkBtn.IsEnabled = true;
        RunArrayBenchmarkBtn.Text = "Run Array Benchmark";
    }
}
