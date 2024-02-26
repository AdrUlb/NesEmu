using AudioThing;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace NesEmu;

internal sealed class Emu : IDisposable
{
	private readonly Thread _emuThread;

	private volatile bool _running = false;

	public const int CyclesPerSecond = 21477272;
	public const double FramesPerSecond = CyclesPerSecond / 4.0 / 262.0 / 341.0;
	public static readonly double TicksPerFrame = Stopwatch.Frequency / FramesPerSecond;
	private static int _cyclesPerAudioSample = CyclesPerSecond / 44100;

	public event EventHandler? Vblank;
	public readonly Cpu Cpu;
	public readonly Ppu Ppu;
	public readonly Apu Apu;
	public readonly Controller Controller;

	private readonly WasapiAudioClient _audioClient;

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

		_audioClient = new(AudioFormat.IeeeFloat, 44100, 32, 1);

		using (var fs = File.OpenRead(@"C:\Stuff\Roms\NES\tetris.nes"))
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

	bool condition = false;

	bool TryWhatever([MaybeNullWhen(false)] out Cpu value)
	{
		if (!condition)
		{
			value = null;
			return false;
		}

		value = new();
		return true;
	}

	private void EmuThreadProc()
	{
		long lastTime = Stopwatch.GetTimestamp();

		uint cycles = 0;

		var audioTimer = Stopwatch.StartNew();

		var maxAudioPadding = _audioClient.FramesPerSecond / 100;

		_audioClient.Start();
		while (_running)
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

			if (!wasVblank && Ppu.StatusVblank)
				Vblank?.Invoke(this, EventArgs.Empty);

			if (cycles % _cyclesPerAudioSample == 0)
			{
				var sample = Apu.GetOutput();

				while (_audioClient.PaddingFrames > maxAudioPadding) { }
				var buffer = _audioClient.GetBuffer<float>(1);
				buffer[0] = sample;
				_audioClient.ReleaseBuffer(1);
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
