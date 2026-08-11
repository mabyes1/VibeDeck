using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;

namespace VibeDeck.Host.Windows
{
    public sealed class DxgiFrameCapturer : IDisposable
    {
        private readonly string _deviceName;
        private Device _d3dDevice;
        private OutputDuplication _outputDuplication;
        private Texture2D _stagingTexture;
        private int _width;
        private int _height;
        private bool _isInitialized;

        public DxgiFrameCapturer(string deviceName)
        {
            _deviceName = deviceName;
        }

        public bool TryCapture(Bitmap destinationBitmap, out bool hasNewFrame)
        {
            hasNewFrame = false;

            if (!_isInitialized)
            {
                if (!TryInitialize(destinationBitmap.Width, destinationBitmap.Height))
                {
                    return false;
                }
            }

            // If resolution requested has changed, re-initialize
            if (_width != destinationBitmap.Width || _height != destinationBitmap.Height)
            {
                Reset();
                if (!TryInitialize(destinationBitmap.Width, destinationBitmap.Height))
                {
                    return false;
                }
            }

            try
            {
                // TryAcquireNextFrame timeout 5ms to avoid blocking thread
                var result = _outputDuplication.TryAcquireNextFrame(5, out var frameInfo, out var desktopResource);

                if (result.Success)
                {
                    using (desktopResource)
                    using (var desktopTexture = desktopResource.QueryInterface<Texture2D>())
                    {
                        _d3dDevice.ImmediateContext.CopyResource(desktopTexture, _stagingTexture);
                    }

                    var dataBox = _d3dDevice.ImmediateContext.MapSubresource(_stagingTexture, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
                    try
                    {
                        var rect = new Rectangle(0, 0, destinationBitmap.Width, destinationBitmap.Height);
                        var data = destinationBitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                        try
                        {
                            var sourcePtr = dataBox.DataPointer;
                            var destPtr = data.Scan0;
                            var rowBytes = destinationBitmap.Width * 4;

                            // Fast path: RowPitch matches bitmap stride -> single bulk memory copy
                            if (dataBox.RowPitch == data.Stride)
                            {
                                Utilities.CopyMemory(destPtr, sourcePtr, rowBytes * destinationBitmap.Height);
                            }
                            else
                            {
                                // Otherwise, copy row-by-row
                                for (int y = 0; y < destinationBitmap.Height; y++)
                                {
                                    Utilities.CopyMemory(destPtr, sourcePtr, rowBytes);
                                    sourcePtr = IntPtr.Add(sourcePtr, dataBox.RowPitch);
                                    destPtr = IntPtr.Add(destPtr, data.Stride);
                                }
                            }
                        }
                        finally
                        {
                            destinationBitmap.UnlockBits(data);
                        }
                    }
                    finally
                    {
                        _d3dDevice.ImmediateContext.UnmapSubresource(_stagingTexture, 0);
                    }

                    _outputDuplication.ReleaseFrame();
                    hasNewFrame = true;
                    return true;
                }

                // If wait timed out, there is no new desktop frame (desktop hasn't changed).
                // We return true (success) but hasNewFrame = false, indicating the caller can reuse the cached bitmap.
                if (result.Code == SharpDX.DXGI.ResultCode.WaitTimeout.Result.Code)
                {
                    return true;
                }

                // If error is ACCESS_LOST or other failure, reset so we re-init next time
                Reset();
                return false;
            }
            catch (SharpDXException ex)
            {
                // Access lost or device removed/reset
                if (ex.ResultCode == SharpDX.DXGI.ResultCode.AccessLost ||
                    ex.ResultCode == SharpDX.DXGI.ResultCode.DeviceRemoved ||
                    ex.ResultCode == SharpDX.DXGI.ResultCode.DeviceReset)
                {
                    // Silent reset, will fall back to GDI this frame and re-init next frame
                    Reset();
                }
                else
                {
                    Reset();
                }
                return false;
            }
            catch
            {
                Reset();
                return false;
            }
        }

        private bool TryInitialize(int width, int height)
        {
            try
            {
                using (var factory = new Factory1())
                {
                    Adapter1 selectedAdapter = null;
                    Output selectedOutput = null;

                    // Enumerate adapters and find output matching _deviceName
                    foreach (var adapter in factory.Adapters1)
                    {
                        foreach (var output in adapter.Outputs)
                        {
                            if (string.Equals(output.Description.DeviceName, _deviceName, StringComparison.OrdinalIgnoreCase))
                            {
                                selectedAdapter = adapter;
                                selectedOutput = output;
                                break;
                            }
                        }
                        if (selectedOutput != null) break;
                    }

                    // Fallback to default output if adapter/output not found by exact device name
                    if (selectedOutput == null)
                    {
                        if (factory.Adapters1.Length > 0 && factory.Adapters1[0].Outputs.Length > 0)
                        {
                            selectedAdapter = factory.Adapters1[0];
                            selectedOutput = factory.Adapters1[0].Outputs[0];
                        }
                        else
                        {
                            return false;
                        }
                    }

                    // Create Direct3D11 Device
                    _d3dDevice = new Device(selectedAdapter, DeviceCreationFlags.None, SharpDX.Direct3D.FeatureLevel.Level_11_0);

                    using (var output1 = selectedOutput.QueryInterface<Output1>())
                    {
                        // Duplicate desktop output
                        _outputDuplication = output1.DuplicateOutput(_d3dDevice);
                    }

                    _width = width;
                    _height = height;

                    // Create staging texture to copy GPU texture to CPU-readable memory
                    var textureDesc = new Texture2DDescription
                    {
                        CpuAccessFlags = CpuAccessFlags.Read,
                        BindFlags = BindFlags.None,
                        Format = Format.B8G8R8A8_UNorm,
                        Width = _width,
                        Height = _height,
                        OptionFlags = ResourceOptionFlags.None,
                        MipLevels = 1,
                        ArraySize = 1,
                        SampleDescription = new SampleDescription(1, 0),
                        Usage = ResourceUsage.Staging
                    };
                    _stagingTexture = new Texture2D(_d3dDevice, textureDesc);

                    _isInitialized = true;
                    return true;
                }
            }
            catch
            {
                Reset();
                return false;
            }
        }

        public void Reset()
        {
            _isInitialized = false;
            _stagingTexture?.Dispose();
            _stagingTexture = null;
            _outputDuplication?.Dispose();
            _outputDuplication = null;
            _d3dDevice?.Dispose();
            _d3dDevice = null;
        }

        public void Dispose()
        {
            Reset();
        }
    }
}
