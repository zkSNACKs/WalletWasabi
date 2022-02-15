using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace WalletWasabi.Fluent.Controls;

public class BlurBehind : Control
{
	private BlurBehindRenderOperation? _operation;

	public static readonly StyledProperty<Vector> BlurRadiusProperty =
		AvaloniaProperty.Register<BlurBehind, Vector>(
			nameof(BlurRadius), new Vector(10, 10));

	public Vector BlurRadius
	{
		get => GetValue(BlurRadiusProperty);
		set => SetValue(BlurRadiusProperty, value);
	}

	private class BlurBehindRenderOperation : ICustomDrawOperation
	{
		private readonly Rect _bounds;
		private readonly Vector _blurRadius;

		public BlurBehindRenderOperation(Rect bounds, Vector blurRadius)
		{
			_bounds = bounds;
			_blurRadius = blurRadius;
		}

		public void Dispose()
		{
		}

		public bool HitTest(Point p) => _bounds.Contains(p);

		public void Render(IDrawingContextImpl context)
		{
			if (context is not ISkiaDrawingContextImpl skia)
			{
				return;
			}

			if (!skia.SkCanvas.TotalMatrix.TryInvert(out var currentInvertedTransform))
			{
				return;
			}

			using var backgroundSnapshot = skia.SkSurface.Snapshot();
			using var backdropShader = SKShader.CreateImage(backgroundSnapshot, SKShaderTileMode.Clamp,
				SKShaderTileMode.Clamp, currentInvertedTransform);

			using var blurred = SKSurface.Create(skia.GrContext, false, new SKImageInfo(
				(int)Math.Ceiling(_bounds.Width),
				(int)Math.Ceiling(_bounds.Height), SKImageInfo.PlatformColorType, SKAlphaType.Premul));
			using (var filter =
			       SKImageFilter.CreateBlur((int)_blurRadius.X, (int)_blurRadius.Y, SKShaderTileMode.Clamp))
			using (var blurPaint = new SKPaint
			       {
				       Shader = backdropShader,
				       ImageFilter = filter
			       })
			{
				blurred.Canvas.DrawRect(0, 0, (float)_bounds.Width, (float)_bounds.Height, blurPaint);
			}

			using (var blurSnap = blurred.Snapshot())
			using (var blurSnapShader = SKShader.CreateImage(blurSnap))
			using (var blurSnapPaint = new SKPaint
			       {
				       Shader = blurSnapShader,
				       IsAntialias = true
			       })
			{
				skia.SkCanvas.DrawRect(0, 0, (float)_bounds.Width, (float)_bounds.Height, blurSnapPaint);
			}
		}

		public Rect Bounds => _bounds.Inflate(_blurRadius.X);

		public bool Equals(ICustomDrawOperation? other)
		{
			return other is BlurBehindRenderOperation op && op._bounds == _bounds;
		}
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		_operation = new BlurBehindRenderOperation(new Rect(new Point(), finalSize), BlurRadius);

		return base.ArrangeOverride(finalSize);
	}

	public override void Render(DrawingContext context)
	{
		if (_operation is { })
		{
			context.Custom(_operation);
		}
	}
}