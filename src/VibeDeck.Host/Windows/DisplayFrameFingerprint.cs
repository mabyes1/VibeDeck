using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace VibeDeck.Host.Windows
{
    public sealed class DisplayFrameFingerprint
    {
        private const int SampleWidth = 32;
        private const int SampleHeight = 18;

        public byte[] Samples { get; }

        private DisplayFrameFingerprint(byte[] samples)
        {
            Samples = samples;
        }

        public static DisplayFrameFingerprint Create(Bitmap bitmap)
        {
            if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
            {
                return new DisplayFrameFingerprint(new byte[SampleWidth * SampleHeight]);
            }

            var samples = new byte[SampleWidth * SampleHeight];
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var stride = data.Stride;
                var height = bitmap.Height;
                var width = bitmap.Width;
                var rowBuffer = new byte[Math.Abs(stride)];
                var index = 0;

                for (var sampleY = 0; sampleY < SampleHeight; sampleY++)
                {
                    var sourceY = (sampleY * height) / SampleHeight;
                    if (sourceY >= height)
                    {
                        sourceY = height - 1;
                    }

                    var rowPtr = IntPtr.Add(data.Scan0, sourceY * stride);
                    Marshal.Copy(rowPtr, rowBuffer, 0, Math.Min(rowBuffer.Length, width * 4));

                    for (var sampleX = 0; sampleX < SampleWidth; sampleX++)
                    {
                        var sourceX = (sampleX * width) / SampleWidth;
                        if (sourceX >= width)
                        {
                            sourceX = width - 1;
                        }

                        var offset = sourceX * 4;
                        // Format32bppArgb is stored as BGRA in memory.
                        var b = rowBuffer[offset];
                        var g = rowBuffer[offset + 1];
                        var r = rowBuffer[offset + 2];
                        samples[index++] = (byte)((r * 77 + g * 150 + b * 29) >> 8);
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return new DisplayFrameFingerprint(samples);
        }

        public double DifferenceFrom(DisplayFrameFingerprint other)
        {
            if (other == null || other.Samples == null || Samples == null || other.Samples.Length != Samples.Length)
            {
                return 1;
            }

            var total = 0d;
            for (var index = 0; index < Samples.Length; index++)
            {
                total += Math.Abs(Samples[index] - other.Samples[index]) / 255d;
            }

            return total / Samples.Length;
        }
    }
}
