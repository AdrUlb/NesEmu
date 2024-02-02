using NesEmu;

internal static class Program
{
	private static void Main()
	{
		Cartridge cartridge;
		using (var fs = File.OpenRead(@"C:\Users\Adrian\Desktop\nestest.nes"))
			cartridge = new(fs);

		var bus = new Bus
		{
			Cartridge = cartridge
		};

		var cpu = new CPU(bus);

		var running = true;

		var emulationThread = new Thread(() =>
		{
			while (running)
				cpu.ExecuteCycle();
		});

		emulationThread.Start();

		var window = new MainWindow();
		window.Run();

		running = false;
	}
}
