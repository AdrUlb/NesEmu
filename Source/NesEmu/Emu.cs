using AudioThing;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace NesEmu;

internal sealed class Emu : IDisposable
{
	private readonly Thread _emuThread;

	private volatile bool _running = false;

	public const int CyclesPerSecond = 21477272;
	public const long CyclesPerSample = CyclesPerSecond / 44100;
	public const double FramesPerSecond = CyclesPerSecond / 4.0 / 262.0 / 341.0;
	public static readonly double TicksPerFrame = Stopwatch.Frequency / FramesPerSecond;

	public event EventHandler? Vblank;
	public readonly Cpu Cpu;
	public readonly Ppu Ppu;
	public readonly Apu Apu;
	public readonly Controller Controller;

	ConcurrentQueue<float> _audioSamples = new();
	private readonly AudioPlayer<float> _audioPlayer;

	private readonly Stopwatch _sw = new();

	public Emu()
	{
		Cpu = new();
		Ppu = new Ppu(Cpu.Bus);
		Apu = new();
		Controller = new();

		Cpu.Bus.Ppu = Ppu;
		Cpu.Bus.Apu = Apu;
		Cpu.Bus.Controller = Controller;

		_audioPlayer = new(44100, 32, 1, AudioFormat.Float, AudioDataCallback);

		using (var fs = File.OpenRead(@"C:\Stuff\Roms\NES\mario.nes"))
		{
			var cart = new Cartridge(Ppu, fs);
			Cpu.Bus.Cartridge = cart;
			Ppu.Bus.Cartridge = cart;
		}

		Cpu.Reset();

		_emuThread = new Thread(EmuThreadProc);
		_sw.Start();
	}

	~Emu() => Dispose(false);

	private void EmuThreadProc()
	{
		long lastTime = Stopwatch.GetTimestamp();

		_audioPlayer.Play();

		uint cycles = 0;
		while (_running)
		{
			while (true)
			{
				cycles++;

				if (Ppu.RequestVblankInterrupt)
				{
					Ppu.RequestVblankInterrupt = false;
					Cpu.RequestNmi();
				}

				if (cycles % 12 == 0 && Ppu.OamWaitCycles == 0)
				{
					Cpu.Tick();
					Apu.Tick();
				}

				var wasVblank = Ppu.StatusVblank;

				if (cycles % 4 == 0)
					Ppu.Tick();

				if (cycles % CyclesPerSample == 0)
				{
					_audioSamples.Enqueue(Apu.GetCurrentSample());
				}

				if (!wasVblank && Ppu.StatusVblank)
				{
					Vblank?.Invoke(this, EventArgs.Empty);
					break;
				}
			}

			long thisTime;
			do
			{
				thisTime = Stopwatch.GetTimestamp();
			}
			while (thisTime - lastTime < TicksPerFrame);
			lastTime = thisTime;
		}

		_audioPlayer.Stop();
	}

	private int AudioDataCallback(Span<float> buffer)
	{
		if (buffer.Length == 0)
			return 0;

		for (var i = 0; i < buffer.Length; i++)
			buffer[i] = _audioSamples.TryDequeue(out var sample) ? sample : 1.0f;

		var dropCount = _audioSamples.Count - 1000;
		while (dropCount > 0)
		{
			_audioSamples.TryDequeue(out var _);
			dropCount--;
		}

		return buffer.Length;
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

	public void Dispose() => Dispose(true);

	private void Dispose(bool disposing)
	{
		_audioPlayer.Dispose();
	}
}
