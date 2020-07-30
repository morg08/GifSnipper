using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace GifSnipper
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool _isLeftMouseHeldDown = false;
        private bool _isDraggingSelection = false;
        private Point _startingPoint;
        private bool _isCapturing = false;
        private readonly string BASE_PATH;
        private Recorder _recorder;

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        public MainWindow()
        {
            BASE_PATH = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + "\\gifsnipper\\";
            if (!Directory.Exists(BASE_PATH)) Directory.CreateDirectory(BASE_PATH);

            SetProcessDPIAware();
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            mainOverlay.Rect = new Rect(1, 1, this.Width - 1, this.Height - 1);
            this.KeyDown += new KeyEventHandler(MainWindow_KeyDown);
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (!_isDraggingSelection && !_isCapturing)
                {
                    _isLeftMouseHeldDown = true;
                    _startingPoint = e.GetPosition(this);
                    CaptureMouse();
                }
                else if (_isCapturing && !dragSelection.Rect.Contains(e.GetPosition(this)))
                {
                    StopRecording();
                }

                e.Handled = true;
            }
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (_isDraggingSelection)
                {
                    _isDraggingSelection = false;
                    _isLeftMouseHeldDown = false;
                    _isCapturing = true;
                    window.Cursor = Cursors.Hand;

                    // https://stackoverflow.com/questions/1438283/find-coordinates-for-point-on-screen
                    var m = PresentationSource.FromVisual(Application.Current.MainWindow).CompositionTarget.TransformToDevice;
                    var dpiScaleX = m.M11;
                    var dpiScaleY = m.M22;
                    var startPoint = PointToScreen(new Point(dragSelection.Rect.X, dragSelection.Rect.Y));
                    e.Handled = true;

                    path.Stroke = new SolidColorBrush(Colors.Red);

                    var recorderParams = new RecorderParams
                    {
                        Filename = BASE_PATH + Guid.NewGuid().ToString() + ".avi",
                        FrameRate = 8,
                        Encoder = SharpAvi.KnownFourCCs.Codecs.MotionJpeg,
                        Quality = 100,
                        X = (int)startPoint.X,
                        Y = (int)startPoint.Y,
                        Width = (int)Math.Floor(dragSelection.Rect.Width * dpiScaleX) - 2,
                        Height = (int)Math.Floor(dragSelection.Rect.Height * dpiScaleY) - 2
                    };

                    _recorder = new Recorder(recorderParams);
                    _recorder.StartRecording();
                }
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingSelection)
            {
                Point curMouseDownPoint = e.GetPosition(this);
                UpdateDragSelection(_startingPoint, curMouseDownPoint);
            }
            else if (_isLeftMouseHeldDown)
            {
                var curMouseDownPoint = e.GetPosition(this);
                var dragDelta = curMouseDownPoint - _startingPoint;
                var dragDistance = Math.Abs(dragDelta.Length);

                if (dragDistance > 10)
                {
                    _isDraggingSelection = true;
                    UpdateDragSelection(_startingPoint, curMouseDownPoint);
                }
            }

            e.Handled = true;
        }

        private void UpdateDragSelection(Point p1, Point p2)
        {
            dragSelection.Rect = new Rect(p1, p2);
        }

        private void StopRecording()
        {
            window.Hide();
            _recorder.StopRecording();

            var aviFilePath = _recorder.Filepath;
            var gifFilePath = BASE_PATH + "GifSnipper " + DateTime.Now.ToString("yyyy-MM-dd HHmmss") + ".gif";

            var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            var ffmpegProcess = Process.Start(new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-i \"{aviFilePath}\" -filter_complex \"[0:v] fps=8,scale=w=720:h=-1,split[a][b];[a] palettegen[p];[b][p] paletteuse\" \"{gifFilePath}\" -y",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            });
            ffmpegProcess.WaitForExit();

            FileSystem.DeleteFile(aviFilePath, UIOption.OnlyErrorDialogs, RecycleOption.DeletePermanently);

            CopyGifToClipboard(gifFilePath);
            ShowToastNotification(gifFilePath);

            this.Close();
        }

        public void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && !_isCapturing)
            {
                this.Close();
            }
            else if (e.Key == Key.Escape && _isCapturing)
            {
                StopRecording();
            }
        }

        private void CopyGifToClipboard(string gifPath)
        {
            var files = new StringCollection { gifPath };
            Clipboard.SetFileDropList(files);
        }

        private void ShowToastNotification(string gifPath)
        {
            XmlDocument toastXml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastImageAndText01);

            XmlNodeList stringElements = toastXml.GetElementsByTagName("text");
            stringElements[0].AppendChild(toastXml.CreateTextNode("Snip saved to clipboard"));

            XmlNodeList imageElements = toastXml.GetElementsByTagName("image");
            imageElements[0].Attributes.GetNamedItem("src").NodeValue = gifPath;

            var toast = new ToastNotification(toastXml);
            ToastNotificationManager.CreateToastNotifier("GifSnipper").Show(toast);
        }
    }
}
