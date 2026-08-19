using IK_Auto_ADB.Core.Fruit2048;
using IK_Auto_ADB.Infrastructure.Fruit2048;
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IK_Auto_ADB.UI
{
    public partial class Fruit2048CalibrationWindow : Window
    {
        private readonly IFruit2048SeedCalibrationService calibration;
        private readonly string deviceName;
        private readonly System.Threading.CancellationToken cancellationToken;
        private Fruit2048CalibrationCapture capture;
        private int? emptyRow, emptyColumn, tier1Row, tier1Column;
        private bool selectingEmpty = true;

        public Fruit2048CalibrationWindow(IFruit2048SeedCalibrationService calibration, string deviceName,
            System.Threading.CancellationToken cancellationToken)
        {
            this.calibration = calibration; this.deviceName = deviceName; this.cancellationToken = cancellationToken;
            InitializeComponent(); Loaded += async (s, e) => await CaptureAsync();
        }

        private async System.Threading.Tasks.Task CaptureAsync()
        {
            InstructionText.Text = "Đang chụp bàn chơi...";
            capture = await calibration.CaptureAsync(deviceName, cancellationToken);
            if (!capture.Success) { InstructionText.Text = capture.Error; return; }
            BoardImage.Source = ToImage(capture.ScreenshotPng);
            BoardPreview.Width = capture.ScreenWidth; BoardPreview.Height = capture.ScreenHeight;
            emptyRow = emptyColumn = tier1Row = tier1Column = null; EmptyPreview.Source = Tier1Preview.Source = null;
            selectingEmpty = true; RefreshSelection();
        }

        private void BoardPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (capture == null || !capture.Success) return;
            Point point = e.GetPosition(BoardPreview);
            int row = Math.Min(3, Math.Max(0, (int)(point.Y * 4 / BoardPreview.ActualHeight)));
            int column = Math.Min(3, Math.Max(0, (int)(point.X * 4 / BoardPreview.ActualWidth)));
            if (selectingEmpty) { emptyRow = row; emptyColumn = column; EmptyPreview.Source = CropPreview(row, column); selectingEmpty = false; }
            else { tier1Row = row; tier1Column = column; Tier1Preview.Source = CropPreview(row, column); }
            RefreshSelection();
        }

        private void RefreshSelection()
        {
            foreach (object child in ((UniformGrid)BoardPreview.Children[1]).Children)
                ((Border)child).BorderBrush = new SolidColorBrush(Color.FromArgb(102, 255, 255, 255));
            Highlight(emptyRow, emptyColumn, Brushes.MediumSeaGreen);
            Highlight(tier1Row, tier1Column, Brushes.Goldenrod);
            InstructionText.Text = selectingEmpty ? "Chọn một ô TRỐNG" : tier1Row.HasValue ? "Đã chọn mẫu. Có thể lưu hiệu chỉnh." : "Chọn một ô có trái cây Tier 1";
            SaveButton.IsEnabled = emptyRow.HasValue && tier1Row.HasValue;
        }
        private void Highlight(int? row, int? column, Brush brush)
        {
            if (!row.HasValue || !column.HasValue) return;
            var cell = (Border)((UniformGrid)BoardPreview.Children[1]).Children[row.Value * 4 + column.Value];
            cell.BorderBrush = brush; cell.BorderThickness = new Thickness(4);
        }
        private async void CaptureAgain(object sender, RoutedEventArgs e) { await CaptureAsync(); }
        private async void Recapture_Click(object sender, RoutedEventArgs e) { await CaptureAsync(); }
        private void ReselectEmpty_Click(object sender, RoutedEventArgs e) { emptyRow = emptyColumn = null; selectingEmpty = true; RefreshSelection(); }
        private void ReselectTier1_Click(object sender, RoutedEventArgs e) { tier1Row = tier1Column = null; selectingEmpty = false; RefreshSelection(); }
        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            SaveButton.IsEnabled = false;
            Fruit2048CalibrationResult result = await calibration.SaveAsync(capture, emptyRow.Value, emptyColumn.Value, tier1Row.Value, tier1Column.Value, cancellationToken);
            MessageBox.Show(this, result.Message, "Fruit2048", MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            if (result.Success) { DialogResult = true; Close(); } else SaveButton.IsEnabled = true;
        }
        private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
        private BitmapImage CropPreview(int row, int column)
        {
            var region = Fruit2048ScreenProfile.GetCellRegion(capture.BoardBounds, row, column);
            using (var source = new System.Drawing.Bitmap(new MemoryStream(capture.ScreenshotPng, false)))
            using (var crop = source.Clone(new System.Drawing.Rectangle(region.X, region.Y, region.Width, region.Height), source.PixelFormat))
            using (var stream = new MemoryStream()) { crop.Save(stream, System.Drawing.Imaging.ImageFormat.Png); return ToImage(stream.ToArray()); }
        }
        private static BitmapImage ToImage(byte[] bytes) { var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = new MemoryStream(bytes, false); image.EndInit(); image.Freeze(); return image; }
    }
}
