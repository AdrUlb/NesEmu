using AudioThing;
using System.Diagnostics;

namespace NesEmu;

internal sealed class Emu : IDisposable
{
	private readonly Thread _emuThread;

	private volatile bool _running = false;

	public const int CyclesPerSecond = 21477272;
	private const double _framesPerSecond = CyclesPerSecond / 4.0 / 262.5 / 341.0;
	private const double _cyclesPerFrame = CyclesPerSecond / _framesPerSecond;
	public static readonly double TicksPerFrame = Stopwatch.Frequency / _framesPerSecond;
	private static readonly int _cyclesPerAudioSample = CyclesPerSecond / 44100;

	public event EventHandler? Vblank;
	public readonly Cpu Cpu;
	public readonly Ppu Ppu;
	public readonly Apu Apu;
	public readonly Controller Controller;
	public readonly Cartridge Cartridge;

	private readonly AudioClient? _audioClient;

	public Emu(string romFilePath)
	{
		Cpu = new(this);
		Ppu = new Ppu(this);
		Apu = new(this);
		Controller = new();

		if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
			_audioClient = new(AudioFormat.IeeeFloat, 44100, 32, 1);

		//using (var fs = File.OpenRead(@"C:\Stuff\Roms\NES\nes-test-roms-master\mmc3_test_2\rom_singles\5-MMC3.nes"))
		using (var fs = File.OpenRead(romFilePath))
			Cartridge = new Cartridge(Ppu, fs);

		Cpu.Reset();

		_emuThread = new Thread(EmuThreadProc);
	}

	~Emu() => Dispose(false);

	private float _sample;

	private void EmuThreadProc()
	{
		uint cycles = 0;

		var maxAudioPadding = (_audioClient?.FramesPerSecond ?? 0) / 100;
		_audioClient?.Start();

		var stopwatch = Stopwatch.StartNew();

		while (_running)
		{
			cycles++;

			if (cycles % 12 == 0 && Ppu.OamWaitCycles == 0)
			{
				if (Ppu.RequestNmi)
				{
					Ppu.RequestNmi = false;
					Cpu.RequestNmi();
				}
				else if (!Cpu.FlagInterruptDisable && Apu.AcknowledgeInterrupt() || Cartridge.AcknowledgeInterrupt())
					Cpu.RequestIrq();

				Cpu.Tick();
				Apu.Tick();
			}

			var wasVblank = Ppu.StatusVblank;

			if (cycles % 4 == 0)
				Ppu.Tick();

			if (!wasVblank && Ppu.StatusVblank)
				Vblank?.Invoke(this, EventArgs.Empty);

			if (_audioClient != null)
			{
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
			else if (cycles >= _cyclesPerFrame)
			{
				while (stopwatch.Elapsed.TotalMilliseconds < 1000 / _framesPerSecond) { }
				cycles -= (uint)_cyclesPerFrame;
				stopwatch.Restart();
			}
		}
		_audioClient?.Stop();
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

	public void Dispose()
	{
		GC.SuppressFinalize(this);
		Dispose(true);
	}

	private void Dispose(bool disposing)
	{
		if (_audioClient != null)
			_audioClient.Dispose();
	}
}
