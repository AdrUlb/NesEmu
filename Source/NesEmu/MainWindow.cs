using RenderThing;
using System.Drawing;

namespace NesEmu;

internal class MainWindow : Window
{
	private readonly Emu _emu = new();

	private Texture _tex = null!;

	public MainWindow() : base(resizable: false)
	{
		Size = new(256 * 2, 240 * 2);
	}

	protected override void OnCloseClicked()
	{
		IsVisible = false;
		Stop();
	}

	protected override void OnRender(Renderer renderer)
	{
		for (var y = 0; y < _tex.Height; y++)
		{
			for (var x = 0; x < _tex.Width; x++)
			{
				var i = x + (y * 256);
				_tex[x, y] = _emu.Ppu.Framebuffer[i];
			}
		}

		renderer.DrawTexture(_tex, new(0, 0), new(Size.Width, Size.Height), Color.White);
	}

	protected override void OnRun()
	{
		_tex = new();
		_tex.SetSize(256, 240);
		for (var y = 0; y < _tex.Height; y++)
			for (var x = 0; x < _tex.Width; x++)
				_tex[x, y] = Color.Orange;

		_emu.Start();
	}

	protected override void OnStop()
	{
		_emu.Stop();
		_tex.Dispose();
	}
}
