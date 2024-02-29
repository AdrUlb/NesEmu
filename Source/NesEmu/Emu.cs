using AudioThing;
using System.Diagnostics;

namespace NesEmu;

internal sealed class Emu : IDisposable
{
	private readonly Thread _emuThread;

	private volatile bool _running = false;

	public const int CyclesPerSecond = 21477272;
	public const double FramesPerSecond = CyclesPerSecond / 4.0 / 262.5 / 341.0;
	private const double MillisPerFrame = 1000.0 / FramesPerSecond;
	public static readonly double TicksPerFrame = Stopwatch.Frequency / FramesPerSecond;
	private static int _cyclesPerAudioSample = CyclesPerSecond / 44100;

	public event EventHandler? Vblank;
	public readonly Cpu Cpu;
	public readonly Ppu Ppu;
	public readonly Apu Apu;
	public readonly Controller Controller;

	private readonly WasapiAudioClient _audioClient;

	public Emu()
	{
		Cpu = new(this);
		Ppu = new Ppu(this);
		Apu = new(this);
		Controller = new();

		_audioClient = new(AudioFormat.IeeeFloat, 44100, 32, 1);

		using (var fs = File.OpenRead(@"C:\Stuff\Roms\NES\tetris.nes"))
		{
			var cart = new Cartridge(Ppu, fs);
			Cpu.Bus.Cartridge = cart;
			Ppu.Bus.Cartridge = cart;
		}

		Cpu.Reset();

		_emuThread = new Thread(EmuThreadProc);
	}

	~Emu() => Dispose(false);

	private float _sample;

	private void EmuThreadProc()
	{
		uint cycles = 0;

		var maxAudioPadding = _audioClient.FramesPerSecond / 100;

		_audioClient.Start();

		while (_running)
		{
			cycles++;

			if (Ppu.RequestNmi)
			{
				Ppu.RequestNmi = false;
				Cpu.RequestNmi();
			}

			if (Apu.RequestFrameInterrupt || Apu.RequestDmcInterrupt)
				Cpu.RequestIrq();

			if (cycles % 12 == 0 && Ppu.OamWaitCycles == 0)
			{
				Cpu.Tick();
				Apu.Tick();
			}

			var wasVblank = Ppu.StatusVblank;

			if (cycles % 4 == 0)
				Ppu.Tick();

			if (!wasVblank && Ppu.StatusVblank)
			{
				Vblank?.Invoke(this, EventArgs.Empty);
			}

			if (cycles % _cyclesPerAudioSample == 0)
			{
				_sample = Apu.GetOutput();
				while (_audioClient.PaddingFrames > maxAudioPadding) { }
				if (_audioClient.TryGetBuffer<float>(1, out var buffer))
				{
					buffer[0] = _sample;
					_audioClient.ReleaseBuffer(1);
				}
			}
		}
		_audioClient.Stop();
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
		_audioClient.Dispose();
	}
}
