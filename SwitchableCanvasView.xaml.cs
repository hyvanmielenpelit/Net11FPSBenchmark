using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System.Threading;

namespace Net11FPSBenchmark
{
    public partial class SwitchableCanvasView : ContentView
    {
        private int _useGL = 0;
        private readonly SKGLView _internalGLView = null;

        public bool UseGL 
        {   get
            {
                return Interlocked.CompareExchange(ref _useGL, 0, 0) != 0;
            }
            set
            {
                Interlocked.Exchange(ref _useGL, value ? 1 : 0);
                if(HasGL)
                {
                    internalCanvasView.IsVisible = !value;
                    _internalGLView.IsVisible = value;
                }
                else
                    internalCanvasView.IsVisible = true;
            }
        }

        public bool HasGL {  get { return _internalGLView != null; } }

        public SwitchableCanvasView()
        {
            bool gpuAvailable = true; // Hardcoded true to match GnollHack on standard platforms
            if (gpuAvailable)
            {
                _internalGLView = new SKGLView()
                {
                    IsVisible = false,
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill,
                };
                _internalGLView.PaintSurface += internalGLView_PaintSurface;
                _internalGLView.Touch += internalGLView_Touch;
            }
            InitializeComponent();
            if(gpuAvailable)
            {
                RootGrid.Children.Add(_internalGLView);
            }
        }

        public event EventHandler<SKPaintSurfaceEventArgs> PaintSurface;
        public event EventHandler<SKPaintGLSurfaceEventArgs> PaintSurfaceGL;
        public event EventHandler<SKTouchEventArgs> Touch;

        private bool _firstCanvasDraw = true;

        private void internalCanvasView_PaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            if (UseGL && HasGL)
                return; /* Insurance in the case both canvases mistakenly are updated */

            if(_firstCanvasDraw)
            {
                _firstCanvasDraw = false;
                SKCanvas canvas = e?.Surface?.Canvas;
                if (canvas != null)
                    canvas.Clear(SKColors.Black);
            }
            PaintSurface?.Invoke(this, e);
        }

        private void internalCanvasView_Touch(object sender, SKTouchEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Touch?.Invoke(this, e);
            });
        }

        private bool _firstDraw = true;

        private void internalGLView_PaintSurface(object sender, SKPaintGLSurfaceEventArgs e)
        {
            if (!UseGL || !HasGL)
                return; /* Insurance in the case both canvases mistakenly are updated */

            var grContext = _internalGLView?.GRContext;
            if (_firstDraw)
            {
                _firstDraw = false;
                if (grContext != null)
                {
                    grContext.SetResourceCacheLimit(128 * 1024 * 1024); // Same as GnollHack
                }
            }

            if (grContext != null)
            {
                // GnollHack clears resource cache occasionally, but we skip that here for benchmark simplicity
            }

            PaintSurfaceGL?.Invoke(this, e);
        }

        private void internalGLView_Touch(object sender, SKTouchEventArgs e)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                Touch?.Invoke(this, e);
            });
        }

        public void InvalidateSurface()
        {
            if (UseGL && HasGL)
            {
                try
                {
                    _internalGLView.InvalidateSurface();
                }
                catch (Exception ex) 
                {
                    System.Diagnostics.Debug.WriteLine(ex);    
                }
            }
            else
            {
                try
                {
                    internalCanvasView.InvalidateSurface();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                }
            }
        }
    }
}
