using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace ADB_Tool_Automation_Post_FB.Core.Vision
{
    /// <summary>Owns one decoded screenshot for the lifetime of a single observation.</summary>
    public sealed class CapturedFrame : IDisposable
    {
        private Bitmap bitmap;
        private byte[] pngBytes;
        private readonly Action pngEncoded;

        public CapturedFrame(Bitmap bitmap, DateTimeOffset capturedAt,
            Action pngEncoded = null)
        {
            this.bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
            this.pngEncoded = pngEncoded;
            Width = bitmap.Width;
            Height = bitmap.Height;
            CapturedAt = capturedAt;
        }

        public int Width { get; }
        public int Height { get; }
        public DateTimeOffset CapturedAt { get; }
        public bool HasEncodedPng => pngBytes != null;

        public Bitmap Bitmap
        {
            get
            {
                if (bitmap == null) throw new ObjectDisposedException(nameof(CapturedFrame));
                return bitmap;
            }
        }

        public byte[] GetPngBytes()
        {
            if (pngBytes != null) return pngBytes;
            using (var stream = new MemoryStream())
            {
                Bitmap.Save(stream, ImageFormat.Png);
                pngBytes = stream.ToArray();
                pngEncoded?.Invoke();
                return pngBytes;
            }
        }

        public void Dispose()
        {
            Bitmap owned = bitmap;
            bitmap = null;
            owned?.Dispose();
        }
    }
}
