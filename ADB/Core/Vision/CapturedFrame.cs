using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace IK_Auto_ADB.Core.Vision
{
    /// <summary>Owns one decoded screenshot for the lifetime of a single observation.</summary>
    public sealed class CapturedFrame : IDisposable
    {
        private Bitmap bitmap;
        private byte[] pngBytes;
        private readonly Action pngEncoded;
        private readonly object bitmapSync = new object();

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

        /// <summary>
        /// Runs one operation while this frame's owned bitmap is protected from
        /// concurrent GDI+ access and disposal.
        /// </summary>
        public T UseBitmap<T>(Func<Bitmap, T> operation)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            lock (bitmapSync)
            {
                if (bitmap == null) throw new ObjectDisposedException(nameof(CapturedFrame));
                return operation(bitmap);
            }
        }

        public byte[] GetPngBytes()
        {
            lock (bitmapSync)
            {
                if (pngBytes != null) return pngBytes;
                if (bitmap == null) throw new ObjectDisposedException(nameof(CapturedFrame));
                using (var stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    pngBytes = stream.ToArray();
                    pngEncoded?.Invoke();
                    return pngBytes;
                }
            }
        }

        public void Dispose()
        {
            lock (bitmapSync)
            {
                Bitmap owned = bitmap;
                bitmap = null;
                owned?.Dispose();
            }
        }
    }
}
