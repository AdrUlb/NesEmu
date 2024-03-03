using RenderThing;
using System.Drawing;

namespace NesEmu;

internal class MainWindow : Window
{
	private readonly Emu _emu = new();

	private Texture _tex = null!;

	private volatile bool _waitForVblank = true;

	public MainWindow() : base(resizable: false)
	{
		Title = "Adrian's NES Emulator";
		Size = new(256 * 3, 240 * 3);
		_emu.Vblank += (_, _) => _waitForVblank = false;
	}

	protected override void OnCloseClicked()
	{
		IsVisible = false;
		Stop();
	}

	protected override void OnRender(Renderer renderer)
	{
		while (_waitForVblank) { }

		_waitForVblank = true;

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
		_emu.Start();
	}

	protected override void OnStop()
	{
		_emu.Stop();
		_emu.Dispose();
		_tex.Dispose();
	}

	protected override void OnKeyDown(KeyboardKey key, ModifierKeys modifiers)
	{
		switch (key)
		{
			case KeyboardKey.Escape:
				Stop();
				break;
			case KeyboardKey.Up:
				_emu.Controller.UpPressed = true;
				break;
			case KeyboardKey.Down:
				_emu.Controller.DownPressed = true;
				break;
			case KeyboardKey.Left:
				_emu.Controller.LeftPressed = true;
				break;
			case KeyboardKey.Right:
				_emu.Controller.RightPressed = true;
				break;
			case KeyboardKey.Enter:
				_emu.Controller.StartPressed = true;
				break;
			case KeyboardKey.Space:
				_emu.Controller.SelectPressed = true;
				break;
			case KeyboardKey.S:
				_emu.Controller.APressed = true;
				break;
			case KeyboardKey.A:
				_emu.Controller.BPressed = true;
				break;
		}
	}

	protected override void OnKeyUp(KeyboardKey key, ModifierKeys modifiers)
	{
		switch (key)
		{
			case KeyboardKey.Up:
				_emu.Controller.UpPressed = false;
				break;
			case KeyboardKey.Down:
				_emu.Controller.DownPressed = false;
				break;
			case KeyboardKey.Left:
				_emu.Controller.LeftPressed = false;
				break;
			case KeyboardKey.Right:
				_emu.Controller.RightPressed = false;
				break;
			case KeyboardKey.Enter:
				_emu.Controller.StartPressed = false;
				break;
			case KeyboardKey.Space:
				_emu.Controller.SelectPressed = false;
				break;
			case KeyboardKey.S:
				_emu.Controller.APressed = false;
				break;
			case KeyboardKey.A:
				_emu.Controller.BPressed = false;
				break;
		}
	}
}
