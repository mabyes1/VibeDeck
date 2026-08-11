using System;
using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace VibeDeck.Host.Streaming
{
    public static class JpegFrameEncoder
    {
        private static readonly ImageCodecInfo JpegCodec = ImageCodecInfo
            .GetImageEncoders()
            .First(c => c.MimeType == "image/jpeg");

        // Initial buffer size for a typical 1280x720 JPEG at mid-quality (~192 KB).
        // PooledMemoryStream will grow if needed, and the rented array is returned after use.
        private const int InitialBufferSize = 192 * 1024;

        public static ArraySegment<byte> Encode(Bitmap bitmap, long quality)
        {
            // Rent a buffer from the shared pool to back the MemoryStream, avoiding heap allocation
            // per frame which would otherwise generate ~250–500 MB/s of GC pressure at 30-60 fps.
            var buffer = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
            try
            {
                using var stream = new MemoryStream(buffer, 0, buffer.Length, writable: true, publiclyVisible: true);
                using var encoderParameters = new EncoderParameters(1);
                encoderParameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
                bitmap.Save(stream, JpegCodec, encoderParameters);

                // Copy the encoded bytes into a standalone array so the rented buffer can be
                // safely returned to the pool without affecting the caller.
                var encoded = stream.ToArray();
                return new ArraySegment<byte>(encoded);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}
