using AudioThing;
using System.Diagnostics;

namespace NesEmu;

internal sealed class Emu : IDisposable
{
	private readonly Thread _emuThread;

	private volatile bool _running = false;

	private const int _cyclesPerSecond = 21477272;
	private const double _framesPerSecond = _cyclesPerSecond / 4.0 / 262.0 / 341.0;
	private readonly double _ticksPerFrame = Stopwatch.Frequency / _framesPerSecond;
	private const long _cyclesPerSample = _cyclesPerSecond / 44100;

	public event EventHandler? Vblank;
	public readonly Cpu Cpu;
	public readonly Ppu Ppu;
	public readonly Apu Apu;
	public readonly Controller Controller;

	private readonly Queue<short> _audioSamples = new();
	private readonly object _audioSamplesLock = new();
	private readonly PcmPlayer _audioPlayer;

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

		_audioPlayer = new(44100 - 100, 16, 1, AudioDataCallback, 2048);

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
		double lastTime = Stopwatch.GetTimestamp();
		_audioPlayer.Play();
		while (_running)
		{
			uint cycles = 0;

			while (true)
			{
				cycles++;

				if (Ppu.RequestVblankInterrupt)
				{
					Ppu.RequestVblankInterrupt = false;
					Cpu.RequestNmi();
				}

				if (cycles % 12 == 0 && Ppu.OamWaitCycles == 0)
					Cpu.Tick();

				var wasVblank = Ppu.StatusVblank;

				if (cycles % 4 == 0)
					Ppu.Tick();

				if (cycles % 24 == 0)
					Apu.Tick();

				if (cycles % _cyclesPerSample == 0)
				{
					lock (_audioSamplesLock)
						_audioSamples.Enqueue((short)(Apu.GetCurrentSample() * short.MaxValue));
				}

				if (!wasVblank && Ppu.StatusVblank)
				{
					Vblank?.Invoke(this, EventArgs.Empty);
					break;
				}

			}

			if (Console.KeyAvailable)
			{
				Console.ReadKey();
				_audioSamples.Clear();
			}
			double thisTime;
			do
			{
				thisTime = Stopwatch.GetTimestamp();
			}
			while (thisTime - lastTime < _ticksPerFrame);
			//Console.WriteLine(Stopwatch.GetElapsedTime((long)lastTime).TotalMilliseconds);
			lastTime = thisTime;
		}


		_audioPlayer.Stop();
		_audioSamples.Clear();
	}

	private int AudioDataCallback(Span<byte> buffer)
	{
		int i;
		lock (_audioSamplesLock)
		{
			while (_audioSamples.Count > 4096 * 3)
				_audioSamples.Dequeue();

			int sample = 0;
			for (i = 0; i * 2 < buffer.Length; i++)
			{
				if (i < _audioSamples.Count)
					sample = _audioSamples.Dequeue();
				var lo = (byte)(sample >> 8);
				var hi = (byte)sample;
				buffer[i * 2] = hi;
				buffer[i * 2 + 1] = lo;
			}
		}

		return i * 2;
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
