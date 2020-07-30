using SharpAvi;
using SharpAvi.Codecs;
using SharpAvi.Output;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace GifSnipper
{
    // https://stackoverflow.com/questions/4068414/how-to-capture-screen-to-be-video-using-c-sharp-net
    public class RecorderParams
    {
        public string Filename { get; set; }
        public int FrameRate { get; set; }
        public FourCC Encoder { get; set; }
        public int Quality { get; set; }
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public AviWriter CreateAviWriter()
        {
            return new AviWriter(Filename)
            {
                FramesPerSecond = FrameRate,
                EmitIndex1 = true,
            };
        }

        public IAviVideoStream CreateVideoStream(AviWriter writer)
        {
            if (Encoder == KnownFourCCs.Codecs.Uncompressed)
            {
                return writer.AddUncompressedVideoStream(Width, Height);
            }
            else if (Encoder == KnownFourCCs.Codecs.MotionJpeg)
            {
                return writer.AddMotionJpegVideoStream(Width, Height, Quality);
            }
            else
            {
                return writer.AddMpeg4VideoStream(
                    width: Width,
                    height: Height,
                    fps: (double)writer.FramesPerSecond,
                    quality: Quality,
                    codec: Encoder,
                    forceSingleThreadedAccess: true);
            }
        }
    }

    public class Recorder
    {
        private const Int32 CURSOR_SHOWING = 0x0001;
        private const Int32 DI_NORMAL = 0x0003;
        private readonly AviWriter _writer;
        private readonly RecorderParams _params;
        private readonly IAviVideoStream _videoStream;
        private readonly Thread _screenThread;
        private readonly ManualResetEvent _stopThread = new ManualResetEvent(false);

        public string Filepath { get; set; }

        public Recorder(RecorderParams Params)
        {
            _params = Params;
            Filepath = _params.Filename;

            _writer = Params.CreateAviWriter();

            _videoStream = Params.CreateVideoStream(_writer);
            _videoStream.Name = "GifSnipper";

            _screenThread = new Thread(RecordScreen)
            {
                Name = typeof(Recorder).Name + ".RecordScreen",
                IsBackground = true
            };
        }

        public void StartRecording()
        {
            _screenThread.Start();
        }

        public void StopRecording()
        {
            _stopThread.Set();
            _screenThread.Join();

            _writer.Close();

            _stopThread.Dispose();
        }

        void RecordScreen()
        {
            var frameInterval = TimeSpan.FromSeconds(1 / (double)_writer.FramesPerSecond);
            var buffer = new byte[_params.Width * _params.Height * 4];
            Task videoWriteTask = null;
            var timeTillNextFrame = TimeSpan.Zero;

            while (!_stopThread.WaitOne(timeTillNextFrame))
            {
                var timestamp = DateTime.Now;

                Screenshot(buffer);

                videoWriteTask?.Wait();
                videoWriteTask = _videoStream.WriteFrameAsync(true, buffer, 0, buffer.Length);

                timeTillNextFrame = timestamp + frameInterval - DateTime.Now;
                if (timeTillNextFrame < TimeSpan.Zero)
                {
                    timeTillNextFrame = TimeSpan.Zero;
                }
            }

            videoWriteTask?.Wait();
        }

        public void Screenshot(byte[] Buffer)
        {
            var bounds = new Rectangle(_params.X, _params.Y, _params.Width, _params.Height);

            using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
            using (var g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(new Point(bounds.Left, bounds.Top), Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);

                CURSORINFO pci;
                pci.cbSize = Marshal.SizeOf(typeof(CURSORINFO));
                if (GetCursorInfo(out pci))
                {
                    if (pci.flags == CURSOR_SHOWING)
                    {
                        var hdc = g.GetHdc();
                        DrawIconEx(hdc, pci.ptScreenPos.x - _params.X, pci.ptScreenPos.y - _params.Y, pci.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL);
                        g.ReleaseHdc();
                    }
                }

                g.Flush();

                var bits = bitmap.LockBits(new Rectangle(0, 0, _params.Width, _params.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                Marshal.Copy(bits.Scan0, Buffer, 0, Buffer.Length);
                bitmap.UnlockBits(bits);
            }
        }

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct CURSORINFO
        {
            public Int32 cbSize;
            public Int32 flags;
            public IntPtr hCursor;
            public POINTAPI ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINTAPI
        {
            public int x;
            public int y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(out CURSORINFO pci);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyHeight, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        static extern int GetDpiForWindow(IntPtr hWnd);
    }
}
