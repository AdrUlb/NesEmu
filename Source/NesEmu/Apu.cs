using System.Runtime.CompilerServices;

namespace NesEmu;

internal sealed class Apu(Emu emu)
{
	private class LowPassFilter(double alpha)
	{
		private double _alpha = alpha;
		private double _lastOutput = 0.0;

		public double Filter(double input)
		{
			var output = (_alpha * input) + ((1 - _alpha) * _lastOutput);
			_lastOutput = output;
			return output;
		}
	}

	// https://www.nesdev.org/wiki/APU_Envelope
	private struct EnvelopeUnit
	{
		public bool LoopFlag;
		public bool ConstantVolumeFlag;
		public int VolumeOrDividerReload;

		public bool StartFlag;
		private int DecayLevelCounter;
		private int _divider;

		public int Volume;

		private void Divider()
		{
			// When the divider is clocked while at 0, it is loaded with V and clocks the decay level counter
			if (_divider == 0)
			{
				_divider = VolumeOrDividerReload;

				// Then one of two actions occurs:
				if (DecayLevelCounter != 0) // If the counter is non-zero, it is decremented
					DecayLevelCounter--;

				else if (LoopFlag) // otherwise if the loop flag is set, the decay level counter is loaded with 15.
				{
					DecayLevelCounter = 15;
				}
			}
			else
				_divider--;
		}

		public void Step()
		{
			if (!StartFlag) // If the start flag is clear, the divider is clocked
			{
				Divider();
			}
			else // Otherwise the start flag is cleared, the decay level counter is loaded with 15, and the divider's period is immediately reloaded
			{
				StartFlag = false;
				DecayLevelCounter = 15;
				_divider = VolumeOrDividerReload;
			}

			Volume = ConstantVolumeFlag ? VolumeOrDividerReload : DecayLevelCounter;
		}
	}

	// https://www.nesdev.org/wiki/APU_Length_Counter
	private struct LengthCounter
	{
		public bool Enable;
		public bool Halt;
		public int Value;

		public void Step()
		{
			if (Value != 0 && !Halt)
				Value--;
		}
	}

	// https://www.nesdev.org/wiki/APU_Sweep
	private struct SweepUnit(Func<int> getTimerReload, Action<int> setTimerReload, bool onesComplementNegate)
	{
		public bool Enable;
		public int ShiftCount;
		public bool ReloadFlag;
		public bool NegateFlag;
		public int DividerReload;

		private readonly Func<int> _getTimerReload = getTimerReload;
		private readonly Action<int> _setTimerReload = setTimerReload;
		private int _divider;
		private readonly bool _onesComplementNegate = onesComplementNegate;

		public bool MuteChannel;

		public void Step()
		{
			// A barrel shifter shifts the pulse channel's 11-bit raw timer period right by the shift count, producing the change amount.
			var currentPeriod = _getTimerReload();

			var changeAmount = currentPeriod >> ShiftCount;
			if (NegateFlag) // If the negate flag is true
			{
				changeAmount = -changeAmount; // The change amount is made negative
				if (_onesComplementNegate) // Pulse 1 adds the ones' complement (−c − 1). Making 20 negative produces a change amount of −21
					changeAmount--;
			}

			// The target period is the sum of the current period and the change amount, clamped to zero if this sum is negative.
			var targetPeriod = currentPeriod + changeAmount;
			if (targetPeriod < 0)
				targetPeriod = 0;

			MuteChannel = currentPeriod < 8 || targetPeriod > 0x7FF;

			if (Enable)
			{
				if (_divider == 0 && ShiftCount != 0) // If the divider's counter is zero, the sweep is enabled, the shift count is nonzero
				{
					if (!MuteChannel) // And the sweep unit is not muting the channel
						_setTimerReload(targetPeriod); // The pulse's period is set to the target period
				}

				if (_divider == 0 || ReloadFlag) // If the divider's counter is zero or the reload flag is true
				{
					// The divider counter is set to P and the reload flag is cleared.
					_divider = DividerReload;
					ReloadFlag = false;
				}
				else // Otherwise, the divider counter is decremented.
					_divider--;
			}
		}
	}

	private class PulseChannel
	{
		private const double _sampleRate = 44100;

		private static readonly int[][] _dutyCycles = [[0, 1, 0, 0, 0, 0, 0, 0], [0, 1, 1, 0, 0, 0, 0, 0], [0, 1, 1, 1, 1, 0, 0, 0], [1, 0, 0, 1, 1, 1, 1, 1]];

		public EnvelopeUnit Envelope = new();
		public LengthCounter LengthCounter = new();
		public SweepUnit Sweep;

		public int DutyCycle;
		public int DutyIndex;

		public int TimerReload;
		public int Timer;

		public bool Output;

		private double _sampleGeneratorState;

		public PulseChannel(bool isPulse1)
		{
			Sweep = new(() => TimerReload, value => TimerReload = value, isPulse1);
		}

		public void StepSequencer()
		{
			if (Timer == 0)
			{
				Timer = TimerReload;
				DutyIndex++;
				DutyIndex %= 8;
			}
			else
				Timer--;

			Output = _dutyCycles[DutyCycle][DutyIndex] != 0;
		}

		public double GenerateSample(double freq, double dutyCycle)
		{
			_sampleGeneratorState += freq;
			_sampleGeneratorState %= _sampleRate;

			var sample = _sampleGeneratorState / _sampleRate <= dutyCycle ? 1.0 : 0.0;

			return sample;
		}

	}

	// https://www.nesdev.org/wiki/APU_Triangle
	private struct TriangleChannel
	{
		public LengthCounter LengthCounter;

		private static readonly int[] _steps = [15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

		public int LinearCounter;

		public bool LinearCounterControl { readonly get => LengthCounter.Halt; set => LengthCounter.Halt = value; }
		public bool LinearCounterReloadFlag;
		public int LinearCounterReload;
		public int StepIndex;

		public int TimerReload;
		public int Timer;

		public int Output;

		public void StepLinearCounter()
		{
			if (LinearCounterReloadFlag)
			{
				LinearCounter = LinearCounterReload;
			}
			else if (LinearCounter != 0)
				LinearCounter--;

			if (!LinearCounterControl)
				LinearCounterReloadFlag = false;
		}

		public void StepSequencer()
		{
			if (Timer == 0)
			{
				Timer = TimerReload;
				if (LengthCounter.Value != 0 && LinearCounter != 0)
				{
					StepIndex++;
					StepIndex %= _steps.Length;

					Output = _steps[StepIndex];
				}
			}
			else
				Timer--;
		}
	}

	private struct NoiseChannel()
	{
		public LengthCounter LengthCounter;
		public EnvelopeUnit Envelope;

		public bool ModeFlag;
		public int TimerReload;
		public int Timer;

		private int _shiftRegister = 1;

		public bool Output;

		public void Step()
		{
			if (Timer == 0)
			{
				Timer = TimerReload;

				// Clock LFSR
				var feedback = (_shiftRegister & 1) ^ ((_shiftRegister >> (ModeFlag ? 6 : 1)) & 1);
				_shiftRegister >>= 1;
				_shiftRegister |= feedback << 14;
			}
			else
				Timer--;

			Output = (_shiftRegister & 1) != 0;
		}
	}

	// https://www.nesdev.org/wiki/APU_DMC
	private struct DmcChannel(Emu emu)
	{
		public bool IrqEnabledFlag;
		public bool RequestInterrupt = false;
		public bool LoopFlag;
		public int Rate;
		public int SampleAddress;
		public int SampleLength;

		public int Output = 0;

		public int CurrentAddress;
		public int BytesRemaining = 0;
		public bool SampleBufferEmpty = true;

		public bool SilenceFlag = true;

		private int _shiftRegister;

		private int _bitsRemaining = 0;

		private int _timer;
		private int _sampleBuffer;

		public void RestartSample()
		{
			CurrentAddress = SampleAddress;
			BytesRemaining = SampleLength;
		}

		public void Step()
		{
			if (SampleBufferEmpty && BytesRemaining != 0)
			{
				_sampleBuffer = emu.Cpu.Bus.ReadByte((ushort)CurrentAddress);
				SampleBufferEmpty = false;
				CurrentAddress++;

				if (CurrentAddress > 0xFFFF)
					CurrentAddress = 0x8000;

				BytesRemaining--;

				if (BytesRemaining == 0)
				{
					if (LoopFlag)
					{
						RestartSample();
					}
					else if (IrqEnabledFlag)
						RequestInterrupt = true;
				}
			}

			if (_timer <= 0)
			{
				_timer = Rate;

				if (!SilenceFlag)
				{
					var newOutput = Output;
					if ((_shiftRegister & 1) == 1)
					{
						newOutput += 2;
					}
					else
						newOutput -= 2;

					if (newOutput is >= 0 and <= 127)
						Output = newOutput;
				}

				_shiftRegister >>= 1;
				_bitsRemaining--;
				if (_bitsRemaining <= 0)
				{
					_bitsRemaining = 8;
					if (SampleBufferEmpty)
					{
						SilenceFlag = true;
					}
					else
					{
						SilenceFlag = false;
						_shiftRegister = _sampleBuffer;
						SampleBufferEmpty = true;
					}
				}
			}
			_timer--;
		}
	}

	private readonly PulseChannel _pulse1 = new(true);
	private readonly PulseChannel _pulse2 = new(false);
	private TriangleChannel _triangle = new();
	private NoiseChannel _noise = new();
	private DmcChannel _dmc = new(emu);
	private readonly LowPassFilter _lowPassFilter = new(0.8);

	private int _frameCounter = 0;
	private int _frameCounterMode = 1;
	private bool _frameCounterInterruptInhibit = false;
	private int _frameCounterResetCounter = 0;
	private bool _frameCounterClockOn0InMode5 = false;

	private ulong _cycles = 0;

	private static readonly byte[] _lengthCounterLookupTable = [10, 254, 20, 2, 40, 4, 80, 6, 160, 8, 60, 10, 14, 12, 26, 14, 12, 16, 24, 18, 48, 20, 96, 22, 192, 24, 72, 26, 16, 28, 32, 30];
	private static readonly int[] _dmcRates = [428, 380, 340, 320, 286, 254, 226, 214, 190, 160, 142, 128, 106, 84, 72, 54];
	private static readonly int[] _noiseTimerPeriods = [4, 8, 16, 32, 64, 96, 128, 160, 202, 254, 380, 508, 762, 1016, 2034, 4068];

	private bool _requestFrameInterrupt = false;

	public bool AcknowledgeInterrupt()
	{
		if (_dmc.RequestInterrupt)
		{
			_dmc.RequestInterrupt = false;
			return true;
		}

		if (_requestFrameInterrupt)
		{
			_requestFrameInterrupt = false;
			return true;
		}

		return false;
	}

	public byte CpuReadByte(ushort address)
	{
		switch (address)
		{
			case 0x4015:
				{
					var ret = (byte)(
						(_pulse1.LengthCounter.Value != 0 ? 1 : 0) |
						((_pulse2.LengthCounter.Value != 0 ? 1 : 0) << 1) |
						((_triangle.LengthCounter.Value != 0 ? 1 : 0) << 2) |
						((_noise.LengthCounter.Value != 0 ? 1 : 0) << 3) |
						((_dmc.BytesRemaining > 0 ? 1 : 0) << 4) |
						((_requestFrameInterrupt ? 1 : 0) << 6) |
						((_dmc.RequestInterrupt ? 1 : 0) << 7));

					_requestFrameInterrupt = false;

					return ret;
				}
			case 0x4017:
				return (byte)(
					(_frameCounterMode << 7) |
					((_frameCounterInterruptInhibit ? 1 : 0) << 6)
				);
		}
		return 0;
	}

	public void CpuWriteByte(ushort address, byte value)
	{
		switch (address)
		{
			case 0x4000:
				_pulse1.DutyCycle = (byte)((value >> 6) & 0b11);
				_pulse1.LengthCounter.Halt = ((value >> 5) & 1) != 0;

				_pulse1.Envelope.LoopFlag = _pulse1.LengthCounter.Halt;
				_pulse1.Envelope.ConstantVolumeFlag = ((value >> 4) & 1) != 0;
				_pulse1.Envelope.VolumeOrDividerReload = value & 0b1111;
				break;
			case 0x4001:
				_pulse1.Sweep.Enable = ((value >> 7) & 1) != 0;
				_pulse1.Sweep.DividerReload = (value >> 4) & 0b111;
				_pulse1.Sweep.NegateFlag = ((value >> 3) & 1) != 0;
				_pulse1.Sweep.ShiftCount = value & 0b111;
				_pulse1.Sweep.ReloadFlag = true;
				break;
			case 0x4002:
				_pulse1.TimerReload &= 0xFF00;
				_pulse1.TimerReload |= value;
				break;
			case 0x4003:
				_pulse1.TimerReload &= 0x00FF;
				_pulse1.TimerReload |= (value & 0b111) << 8;

				if (_pulse1.LengthCounter.Enable)
					_pulse1.LengthCounter.Value = _lengthCounterLookupTable[(value >> 3) & 0b11111];

				_pulse1.Envelope.StartFlag = true;
				_pulse1.Timer = _pulse1.TimerReload;
				break;
			case 0x4004:
				_pulse2.DutyCycle = (byte)((value >> 6) & 0b11);
				_pulse2.LengthCounter.Halt = ((value >> 5) & 1) != 0;

				_pulse2.Envelope.LoopFlag = _pulse2.LengthCounter.Halt;
				_pulse2.Envelope.ConstantVolumeFlag = ((value >> 4) & 1) != 0;
				_pulse2.Envelope.VolumeOrDividerReload = value & 0b1111;
				break;
			case 0x4005:
				_pulse2.Sweep.Enable = ((value >> 7) & 1) != 0;
				_pulse2.Sweep.DividerReload = (value >> 4) & 0b111;
				_pulse2.Sweep.NegateFlag = ((value >> 3) & 1) != 0;
				_pulse2.Sweep.ShiftCount = value & 0b111;
				_pulse2.Sweep.ReloadFlag = true;
				break;
			case 0x4006:
				_pulse2.TimerReload &= 0xFF00;
				_pulse2.TimerReload |= value;
				break;
			case 0x4007:
				_pulse2.TimerReload &= 0x00FF;
				_pulse2.TimerReload |= (value & 0b111) << 8;

				if (_pulse2.LengthCounter.Enable)
					_pulse2.LengthCounter.Value = _lengthCounterLookupTable[(value >> 3) & 0b11111];

				_pulse2.Envelope.StartFlag = true;
				_pulse2.Timer = _pulse2.TimerReload;
				break;
			case 0x4008:
				_triangle.LinearCounterControl = ((value >> 7) & 1) != 0;
				_triangle.LinearCounterReload = value & 0b1111111;
				break;
			case 0x400A:
				_triangle.TimerReload &= 0xFF00;
				_triangle.TimerReload |= value;
				break;
			case 0x400B:
				_triangle.TimerReload &= 0x00FF;
				_triangle.TimerReload |= (value & 0b111) << 8;

				if (_triangle.LengthCounter.Enable)
					_triangle.LengthCounter.Value = _lengthCounterLookupTable[(value >> 3) & 0b11111];

				_triangle.LinearCounterReloadFlag = true;
				break;
			case 0x400C:
				_noise.LengthCounter.Halt = ((value >> 5) & 1) != 0;
				_noise.Envelope.LoopFlag = _pulse2.LengthCounter.Halt;
				_noise.Envelope.ConstantVolumeFlag = ((value >> 4) & 1) != 0;
				_noise.Envelope.VolumeOrDividerReload = value & 0b1111;
				break;
			case 0x400E:
				_noise.ModeFlag = ((value >> 7) & 1) != 0;
				_noise.TimerReload = _noiseTimerPeriods[value & 0b1111];
				break;
			case 0x400F:
				if (_noise.LengthCounter.Enable)
					_noise.LengthCounter.Value = _lengthCounterLookupTable[(value >> 3) & 0b11111];

				_noise.Envelope.StartFlag = true;
				break;
			case 0x4010:
				_dmc.IrqEnabledFlag = ((value >> 7) & 1) != 0;
				_dmc.LoopFlag = ((value >> 6) & 1) != 0;
				_dmc.Rate = _dmcRates[value & 0b1111];

				if (!_dmc.IrqEnabledFlag)
					_dmc.RequestInterrupt = false;
				break;
			case 0x4011:
				_dmc.Output = value & 0b0111_1111;
				break;
			case 0x4012:
				_dmc.SampleAddress = 0xC000 + (value * 64);
				break;
			case 0x4013:
				_dmc.SampleLength = (value * 16) + 1;
				break;
			case 0x4015:
				_pulse1.LengthCounter.Enable = (value & 1) != 0;
				_pulse2.LengthCounter.Enable = ((value >> 1) & 1) != 0;
				_triangle.LengthCounter.Enable = ((value >> 2) & 1) != 0;
				_noise.LengthCounter.Enable = ((value >> 3) & 1) != 0;

				if (!_pulse1.LengthCounter.Enable)
					_pulse1.LengthCounter.Value = 0;

				if (!_pulse2.LengthCounter.Enable)
					_pulse2.LengthCounter.Value = 0;

				if (!_triangle.LengthCounter.Enable)
					_triangle.LengthCounter.Value = 0;

				if (!_noise.LengthCounter.Enable)
					_noise.LengthCounter.Value = 0;

				if (((value >> 4) & 1) != 0)
				{
					if (_dmc.BytesRemaining == 0)
						_dmc.RestartSample();
				}
				else
					_dmc.BytesRemaining = 0;

				_dmc.RequestInterrupt = false;
				break;
			case 0x4017:
				_frameCounterMode = (value >> 7) & 1;
				_frameCounterInterruptInhibit = ((value >> 6) & 1) != 0;

				if (_frameCounterInterruptInhibit)
					_requestFrameInterrupt = false;

				_frameCounterResetCounter = _cycles % 2 == 1 ? 3 : 4;
				_frameCounterClockOn0InMode5 = true;
				break;
		}
	}

	public void Tick()
	{
		DoFrameCounter();

		if (_cycles % 2 == 1)
		{
			_pulse1.StepSequencer();
			_pulse2.StepSequencer();
		}

		_noise.Step();
		_dmc.Step();

		_triangle.StepSequencer();

		_cycles++;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoFrameCounter()
	{
		if (_frameCounterResetCounter != 0)
		{
			_frameCounterResetCounter--;

			if (_frameCounterResetCounter == 0)
				_frameCounter = 0;
		}

		if (_frameCounterMode == 0)
			DoFrameCounterMode0();
		else
			DoFrameCounterMode1();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoFrameCounterMode0()
	{
		switch (_frameCounter)
		{
			case (3728 * 2) + 1:
				DoQuarterFrame();
				break;
			case (7456 * 2) + 1:
				DoQuarterFrame();
				DoHalfFrame();
				break;
			case (11185 * 2) + 1:
				DoQuarterFrame();
				break;
			case (14914 * 2):
				if (!_frameCounterInterruptInhibit)
					_requestFrameInterrupt = true;
				break;
			case (14914 * 2) + 1:
				if (!_frameCounterInterruptInhibit)
					_requestFrameInterrupt = true;
				DoQuarterFrame();
				DoHalfFrame();
				break;
			case 14915 * 2:
				if (!_frameCounterInterruptInhibit)
					_requestFrameInterrupt = true;
				_frameCounter = 0;
				break;
		}

		_frameCounter++;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoFrameCounterMode1()
	{
		switch (_frameCounter)
		{
			case 0 when _frameCounterClockOn0InMode5:
				DoQuarterFrame();
				DoHalfFrame();
				_frameCounterClockOn0InMode5 = false;
				break;
			case (3728 * 2) + 1:
				DoQuarterFrame();
				break;
			case (7456 * 2) + 1:
				DoQuarterFrame();
				DoHalfFrame();
				break;
			case (11185 * 2) + 1:
				DoQuarterFrame();
				break;
			case (18640 * 2) + 1:
				DoQuarterFrame();
				DoHalfFrame();
				break;
		}

		_frameCounter++;
		if (_frameCounter == 18641 * 2)
			_frameCounter = 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoQuarterFrame() // Envelopes and triangle's linear counter
	{
		DoEnvelopes();
		DoTriangleLinearCounter();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoHalfFrame()
	{
		DoLengthCounters();
		DoSweep();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoEnvelopes()
	{
		_pulse1.Envelope.Step();
		_pulse2.Envelope.Step();
		_noise.Envelope.Step();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoTriangleLinearCounter()
	{
		_triangle.StepLinearCounter();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoLengthCounters()
	{
		_pulse1.LengthCounter.Step();
		_pulse2.LengthCounter.Step();
		_triangle.LengthCounter.Step();
		_noise.LengthCounter.Step();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoSweep()
	{
		_pulse1.Sweep.Step();
		_pulse2.Sweep.Step();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private double GetPulse1()
	{
		//return _pulse1.Output ? _pulse1.Envelope.Volume : 0;

		var pulse1freq = (Emu.CyclesPerSecond / 12.0) / (16.0 * (_pulse1.TimerReload + 1));
		var pulse1dutyCycle = _pulse1.DutyCycle switch
		{
			0 => 0.125,
			1 => 0.25,
			2 => 0.5,
			3 => 0.25,
			_ => 0.0
		};

		if (_pulse1.LengthCounter.Value == 0 || _pulse1.Sweep.MuteChannel)
			return 0;

		return _pulse1.GenerateSample(pulse1freq, pulse1dutyCycle) * _pulse1.Envelope.Volume;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private double GetPulse2()
	{
		//return _pulse2.Output ? _pulse2.Envelope.Volume : 0;

		var pulse2freq = Emu.CyclesPerSecond / 12.0 / (16.0 * (_pulse2.TimerReload + 1));
		var pulse2dutyCycle = _pulse2.DutyCycle switch
		{
			0 => 0.125,
			1 => 0.25,
			2 => 0.5,
			3 => 0.25,
			_ => 0.0
		};

		if (_pulse2.LengthCounter.Value == 0 || _pulse2.Sweep.MuteChannel)
			return 0;

		return _pulse2.GenerateSample(pulse2freq, pulse2dutyCycle) * _pulse2.Envelope.Volume;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private double GetTriangle() => _triangle.Output;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private double GetNoise() => _noise.Output && _noise.LengthCounter.Value != 0 ? _noise.Envelope.Volume : 0;

	private double GetDmc() => _dmc.Output;

	public float GetOutput()
	{
		var pulse1 = GetPulse1();
		var pulse2 = GetPulse2();
		var triangle = GetTriangle();
		var noise = GetNoise();
		var dmc = GetDmc();

		var pulseOut = 0.0;
		var tndOut = 0.0;

		if (pulse1 != 0.0 || pulse2 != 0.0)
			pulseOut = 95.88 / ((8128.0 / (pulse1 + pulse2)) + 100.0);

		if (triangle != 0.0 || noise != 0.0 || dmc != 0.0)
			tndOut = 159.79 / ((1 / ((triangle / 8227.0) + (noise / 12241.0) + (dmc / 22638.0))) + 100.0);

		var sample = pulseOut + tndOut;
		return (float)_lowPassFilter.Filter(sample);
	}
}
