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
    private readonly BenchmarkRenderer _renderer;
    private readonly Stopwatch _stopwatch;

    // FPS calculation — same atomic counter approach as GnollHack
    private long _frameCounter;
    private long _previousFrameCounter;
    private double _currentFps;

    // Reentrancy guard — same pattern as GnollHack's IsMainCanvasDrawing
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

        _renderer = new BenchmarkRenderer();
        _stopwatch = Stopwatch.StartNew();

        // Generate synthetic map data
        MapData.Generate();

        // Track button bar height for positioning messages
        ButtonRowGrid.SizeChanged += (s, e) =>
        {
            _buttonBarHeight = ButtonRowGrid.Height;
        };

        // Start the platform-native render loop
        StartPlatformRenderLoop();

        // FPS counter timer (runs every second)
        Dispatcher.StartTimer(TimeSpan.FromSeconds(1), OnFpsTick);

        // Message ticker — add a new random message every second
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
            SkGlView.InvalidateSurface();
        });
    }

    /// <summary>
    /// FPS calculation tick — runs every second.
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
    /// Message tick — add a new random message every second.
    /// </summary>
    private bool OnMessageTick()
    {
        string msg = MessagePool[_messageRng.Next(MessagePool.Length)];
        _renderer.AddMessage(msg);
        return true;
    }

    /// <summary>
    /// SKGLView paint handler — mirrors GnollHack's canvasView_PaintSurface.
    /// </summary>
    private void OnPaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
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

            SKCanvas canvas = e.Surface.Canvas;
            int width = e.BackendRenderTarget.Width;
            int height = e.BackendRenderTarget.Height;

            // Pass the device pixel density to convert XAML dp to canvas pixels
            float density = (float)DeviceDisplay.MainDisplayInfo.Density;
            _renderer.Fps = _currentFps;
            _renderer.ButtonBarHeightPx = (float)(_buttonBarHeight * density);
            _renderer.PaintFrame(canvas, width, height);

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

    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        e.Handled = true;
    }
}
