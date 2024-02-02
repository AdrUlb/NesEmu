using RenderThing;

namespace NesEmu;

internal class MainWindow : Window
{
	public MainWindow() : base(resizable: false)
	{
		Size = new(256 * 3, 240 * 3);
	}

	protected override void OnCloseClicked()
	{
		IsVisible = false;
		Stop();
	}

	protected override void OnRender(Renderer renderer)
	{

	}

	protected override void OnRun()
	{

	}

	protected override void OnStop()
	{
	}
}
