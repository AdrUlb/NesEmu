using System.Diagnostics;

namespace NesEmu;

internal sealed class Emu
{
	private readonly Thread _emuThread;
	public readonly Cpu Cpu;
	public readonly Ppu Ppu;
	public readonly CpuBus CpuBus;
	public readonly PpuBus PpuBus;

	private volatile bool _running = false;

	private const int _cyclesPerSecond = 1789773;
	private const double _framesPerSecond = 60.0988;
	private readonly long _ticksPerFrame = (long)(Stopwatch.Frequency / _framesPerSecond);
	private readonly long _cyclesPerFrame = (long)(_cyclesPerSecond / _framesPerSecond);

	public event EventHandler? VblankInterrupt;

	public Emu()
	{
		CpuBus = new CpuBus();
		PpuBus = new PpuBus();

		using (var fs = File.OpenRead(@"C:\Users\Adrian\Desktop\pacman.nes"))
		{
			var cart = new Cartridge(fs);
			CpuBus.Cartridge = cart;
			PpuBus.Cartridge = cart;
		}

		Cpu = new Cpu(CpuBus);
		Ppu = new Ppu(PpuBus, CpuBus);

		CpuBus.Ppu = Ppu;

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
					VblankInterrupt?.Invoke(this, EventArgs.Empty);
				}

				Cpu.Tick();
				Ppu.Tick();
				Ppu.Tick();
				Ppu.Tick();
			}

			long thisTime;
			do
			{
				thisTime = Stopwatch.GetTimestamp();
			}
			while (thisTime - lastTime < _ticksPerFrame);
			lastTime = thisTime;
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
