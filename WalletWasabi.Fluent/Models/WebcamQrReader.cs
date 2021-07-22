using Avalonia.Media.Imaging;
using System;
using System.Threading.Tasks;
using OpenCvSharp;
using Avalonia;
using Avalonia.Media;
using System.Runtime.InteropServices;
using WalletWasabi.Logging;
using WalletWasabi.Userfacing;
using NBitcoin;
using Nito.AsyncEx;
using Avalonia.Platform;

namespace WalletWasabi.Fluent.Models
{
	public class WebcamQrReader
	{
		private const byte DefaultCameraId = 0;

		public WebcamQrReader(Network network)
		{
			ScanningTaskLock = new();
			Network = network;
		}

		public event EventHandler<WriteableBitmap>? NewImageArrived;

		public event EventHandler<string>? CorrectAddressFound;

		public event EventHandler<string>? InvalidAddressFound;

		public event EventHandler<Exception>? ErrorOccured;

		private AsyncLock ScanningTaskLock { get; set; }
		public bool RequestEnd { get; set; }
		public Network Network { get; }
		public Task? ScanningTask { get; set; }
		public bool IsRunning => ScanningTask is not null;

		public async Task StartScanningAsync()
		{
			using (await ScanningTaskLock.LockAsync().ConfigureAwait(false))
			{
				if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
				{
					throw new NotImplementedException("This operating system is not supported.");
				}
				ScanningTask = Task.Run(() =>
				{
					VideoCapture? camera = null;
					try
					{
						camera = OpenCamera();
						RequestEnd = false;
						Scan(camera);
					}
					catch (Exception exc)
					{
						Logger.LogError("QR scanning stopped. Reason:", exc);
						ErrorOccured?.Invoke(this, exc);
					}
					finally
					{
						camera?.Release();
						camera?.Dispose();
					}
				});
			}
		}

		public async Task StopScanningAsync()
		{
			using (await ScanningTaskLock.LockAsync().ConfigureAwait(false))
			{
				if (ScanningTask is { } task)
				{
					RequestEnd = true;
					await task;

					ScanningTask = null;
				}
			}
		}

		private void Scan(VideoCapture camera)
		{
			WriteableBitmap? lastBitmap = null;
			WriteableBitmap? currentBitmap = null;
			using QRCodeDetector qRCodeDetector = new();
			while (!RequestEnd)
			{
				try
				{
					using Mat frame = new();
					bool gotBackFrame = camera.Read(frame);
					if (!gotBackFrame || frame.Width == 0 || frame.Height == 0)
					{
						continue;
					}
					currentBitmap = ConvertMatToWriteableBitmap(frame);

					NewImageArrived?.Invoke(this, currentBitmap);
					lastBitmap?.Dispose();
					lastBitmap = currentBitmap;

					if (qRCodeDetector.Detect(frame, out Point2f[] points))
					{
						using Mat tmpMat = new();
						string qrCode = qRCodeDetector.Decode(frame, points, tmpMat);
						if (string.IsNullOrWhiteSpace(qrCode))
						{
							continue;
						}
						if (AddressStringParser.TryParse(qrCode, Network, out _))
						{
							CorrectAddressFound?.Invoke(this, qrCode);
							break;
						}
						else
						{
							InvalidAddressFound?.Invoke(this, qrCode);
						}
					}
				}
				catch (OpenCVException exc)
				{
					Logger.LogWarning(exc);
					currentBitmap?.Dispose();
				}
			}
			lastBitmap?.Dispose();
			currentBitmap?.Dispose();
		}

		private VideoCapture OpenCamera()
		{
			VideoCapture camera = new();
			if (!camera.Open(DefaultCameraId))
			{
				throw new InvalidOperationException("Could not open webcam.");
			}
			return camera;
		}

		private WriteableBitmap ConvertMatToWriteableBitmap(Mat frame)
		{
			PixelSize pixelSize = new(frame.Width, frame.Height);
			Vector dpi = new(96, 96);
			var writeableBitmap = new WriteableBitmap(pixelSize, dpi, PixelFormat.Rgba8888, AlphaFormat.Unpremul);

			using (var fb = writeableBitmap.Lock())
			{
				var indexer = frame.GetGenericIndexer<Vec3b>();
				int[] data = new int[fb.Size.Width * fb.Size.Height];
				for (int y = 0; y < frame.Height; y++)
				{
					for (int x = 0; x < frame.Width; x++)
					{
						Vec3b pixel = indexer[y, x];
						byte r = pixel.Item0;
						byte g = pixel.Item1;
						byte b = pixel.Item2;
						var color = new Color(255, r, g, b);
						data[y * fb.Size.Width + x] = (int)color.ToUint32();
					}
				}
				Marshal.Copy(data, 0, fb.Address, fb.Size.Width * fb.Size.Height);
			}
			return writeableBitmap;
		}
	}
}
