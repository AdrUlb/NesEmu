using System.Diagnostics;

namespace NesEmu;

internal sealed class Emu
{
	private readonly Thread _emuThread;

	private volatile bool _running = false;

	private const int _cyclesPerSecond = 1789773;
	private const double _framesPerSecond = 60.0988;
	private readonly long _ticksPerFrame = (long)(Stopwatch.Frequency / _framesPerSecond);
	private readonly long _cyclesPerFrame = (long)(_cyclesPerSecond / _framesPerSecond);

	public event EventHandler? Vblank;
	public readonly Cpu Cpu;
	public readonly Ppu Ppu;
	public readonly Controller Controller;

	private readonly Stopwatch sw = new();

	public Emu()
	{
		Cpu = new();
		Ppu = new Ppu(Cpu.Bus);
		Controller = new();

		Cpu.Bus.Ppu = Ppu;
		Cpu.Bus.Controller = Controller;

		using (var fs = File.OpenRead(@"C:\Stuff\Roms\NES\tetris.nes"))
		{
			var cart = new Cartridge(Ppu, fs);
			Cpu.Bus.Cartridge = cart;
			Ppu.Bus.Cartridge = cart;
		}

		Cpu.Reset();

		_emuThread = new Thread(EmuThreadProc);
	}

	private void EmuThreadProc()
	{
		var lastTime = Stopwatch.GetTimestamp();

		while (_running)
		{
			uint cycles = 0;
			while (cycles < _cyclesPerFrame)
			{
				cycles++;

				if (Ppu.RequestVblankInterrupt)
				{
					Ppu.RequestVblankInterrupt = false;
					Cpu.RequestNmi();
				}

				if (Ppu.OamWaitCycles == 0)
					Cpu.Tick();
				var wasVblank = Ppu.StatusVblank;
				Ppu.Ticks(3);
				if (!wasVblank && Ppu.StatusVblank)
					Vblank?.Invoke(this, EventArgs.Empty);
			}

			long thisTime;
			do
			{
				thisTime = Stopwatch.GetTimestamp();
			}
			while (thisTime - lastTime < _ticksPerFrame);
			lastTime = thisTime;

			Console.WriteLine($"Frame took {sw.Elapsed.TotalMilliseconds}ms");
			sw.Restart();
		}
	}

	public void Start()
	{
		if (_running)
			return;

		_running = true;
		_emuThread.Start();
	}

	public void Stop()
	{
		if (!_running)
			return;

		_running = false;

		SpinWait.SpinUntil(() => !_emuThread.IsAlive);
	}
}
