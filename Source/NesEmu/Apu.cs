using System.Runtime.CompilerServices;

namespace NesEmu;

internal sealed class Apu
{
	private struct SquareWaveGenerator
	{
		private const double _sampleRate = 44100;
		public double Phase;

		public double GenerateSample(double freq, double dutyCycle)
		{
			Phase += 1.0 / _sampleRate * freq;
			Phase %= 1.0;
			var sample = Phase <= dutyCycle ? 1.0 : -1.0;
			return sample;
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

	// https://www.nesdev.org/wiki/APU_Pulse
	private struct PulseSequencerUnit
	{
		private static readonly int[][] _dutyCycles = [[0, 1, 0, 0, 0, 0, 0, 0], [0, 1, 1, 0, 0, 0, 0, 0], [0, 1, 1, 1, 1, 0, 0, 0], [1, 0, 0, 1, 1, 1, 1, 1]];
		//private static readonly int[][] _dutyCycles = [[1, 0, 0, 0, 0, 0, 0, 0], [1, 1, 0, 0, 0, 0, 0, 0], [1, 1, 1, 1, 0, 0, 0, 0], [1, 1, 1, 1, 1, 1, 0, 0]];

		public int DutyCycle;
		public int DutyIndex;

		public int TimerReload;
		public int Timer;

		public bool Output;

		public void Step()
		{
			if (Timer == 0)
			{
				Timer = TimerReload;

				DutyIndex++;
				DutyIndex %= 8;
			}
			else
				Timer--;

			Output = ((_dutyCycles[DutyCycle][DutyIndex]) & 1) != 0;
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

		private int _targetPeriod;

		public bool MuteChannel;

		public void UpdateTargetPeriod()
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
			_targetPeriod = currentPeriod + changeAmount;
			if (_targetPeriod < 0)
				_targetPeriod = 0;

			MuteChannel = currentPeriod < 8 || _targetPeriod > 0x7FF;
		}

		public void Step()
		{
			if (_divider == 0 && Enable && ShiftCount != 0) // If the divider's counter is zero, the sweep is enabled, the shift count is nonzero
			{
				if (!MuteChannel) // And the sweep unit is not muting the channel
					_setTimerReload(_targetPeriod); // The pulse's period is set to the target period
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

	private EnvelopeUnit _pulse1Envelope = new();
	private PulseSequencerUnit _pulse1Sequencer = new();
	private LengthCounter _pulse1LengthCounter = new();
	private SweepUnit _pulse1Sweep;
	private SquareWaveGenerator _pulse1Generator = new();

	private EnvelopeUnit _pulse2Envelope = new();
	private PulseSequencerUnit _pulse2Sequencer = new();
	private LengthCounter _pulse2LengthCounter = new();
	private SweepUnit _pulse2Sweep;
	private SquareWaveGenerator _pulse2Generator = new();

	private int _frameCounter = 0;
	private int _frameCounterMode = 1;
	private bool _frameCounterInterruptInhibit = false;
	private bool _frameCounterInterrupt = false;
	private int _frameCounterResetCounter = 0;

	private bool _dmcControlEnable = false;
	private bool _triangleLengthCounterEnable = false;
	private bool _noiseLengthCounterEnable = false;

	private ulong _cycles = 0;

	private static readonly byte[] _lengthCounterLookupTable = [10, 254, 20, 2, 40, 4, 80, 6, 160, 8, 60, 10, 14, 12, 26, 14, 12, 16, 24, 18, 48, 20, 96, 22, 192, 24, 72, 26, 16, 28, 32, 30];

	public Apu()
	{
		_pulse1Sweep = new(() => _pulse1Sequencer.TimerReload, value => _pulse1Sequencer.TimerReload = value, true);
		_pulse2Sweep = new(() => _pulse2Sequencer.TimerReload, value => _pulse2Sequencer.TimerReload = value, false);
	}

	public byte CpuReadByte(ushort address)
	{
		switch (address)
		{
			case 0x4015:
				return (byte)(
					(_pulse1LengthCounter.Enable ? 1 : 0) |
					((_pulse2LengthCounter.Enable ? 1 : 0) << 2) |
					((_triangleLengthCounterEnable ? 1 : 0) << 3) |
					((_noiseLengthCounterEnable ? 1 : 0) << 4) |
					((_dmcControlEnable ? 1 : 0) << 5)
				);
		}
		return 0;
	}

	public void CpuWriteByte(ushort address, byte value)
	{
		switch (address)
		{
			case 0x4000:
				_pulse1Sequencer.DutyCycle = (byte)((value >> 6) & 0b11);
				_pulse1LengthCounter.Halt = ((value >> 5) & 1) != 0;

				_pulse1Envelope.LoopFlag = _pulse1LengthCounter.Halt;
				_pulse1Envelope.ConstantVolumeFlag = ((value >> 4) & 1) != 0;
				_pulse1Envelope.VolumeOrDividerReload = value & 0b1111;
				break;
			case 0x4001:
				_pulse1Sweep.Enable = ((value >> 7) & 1) != 0;
				_pulse1Sweep.DividerReload = (value >> 4) & 0b111;
				_pulse1Sweep.NegateFlag = ((value >> 3) & 1) != 0;
				_pulse1Sweep.ShiftCount = value & 0b111;
				_pulse1Sweep.ReloadFlag = true;
				break;
			case 0x4002:
				_pulse1Sequencer.TimerReload &= 0xFF00;
				_pulse1Sequencer.TimerReload |= value;
				break;
			case 0x4003:
				_pulse1Sequencer.TimerReload &= 0x00FF;
				_pulse1Sequencer.TimerReload |= (value & 0b111) << 8;

				if (_pulse1LengthCounter.Enable)
					_pulse1LengthCounter.Value = _lengthCounterLookupTable[(value >> 3) & 0b11111];

				_pulse1Envelope.StartFlag = true;
				_pulse1Sequencer.DutyIndex = 0;
				_pulse1Sequencer.Timer = _pulse1Sequencer.TimerReload;
				_pulse1Generator.Phase = 0;
				break;
			case 0x4004:
				_pulse2Sequencer.DutyCycle = (byte)((value >> 6) & 0b11);
				_pulse2LengthCounter.Halt = ((value >> 5) & 1) != 0;

				_pulse2Envelope.LoopFlag = _pulse2LengthCounter.Halt;
				_pulse2Envelope.ConstantVolumeFlag = ((value >> 4) & 1) != 0;
				_pulse2Envelope.VolumeOrDividerReload = value & 0b1111;
				break;
			case 0x4005:
				_pulse2Sweep.Enable = ((value >> 7) & 1) != 0;
				_pulse2Sweep.DividerReload = (value >> 4) & 0b111;
				_pulse2Sweep.NegateFlag = ((value >> 3) & 1) != 0;
				_pulse2Sweep.ShiftCount = value & 0b111;
				_pulse2Sweep.ReloadFlag = true;
				break;
			case 0x4006:
				_pulse2Sequencer.TimerReload &= 0xFF00;
				_pulse2Sequencer.TimerReload |= value;
				break;
			case 0x4007:
				_pulse2Sequencer.TimerReload &= 0x00FF;
				_pulse2Sequencer.TimerReload |= (value & 0b111) << 8;

				if (_pulse2LengthCounter.Enable)
					_pulse2LengthCounter.Value = _lengthCounterLookupTable[(value >> 3) & 0b11111];

				_pulse2Envelope.StartFlag = true;
				_pulse2Sequencer.DutyIndex = 0;
				_pulse2Sequencer.Timer = _pulse2Sequencer.TimerReload;
				_pulse2Generator.Phase = 0;
				break;
			case 0x4015:
				_pulse1LengthCounter.Enable = (value & 1) != 0;
				_pulse2LengthCounter.Enable = ((value >> 1) & 1) != 0;
				_triangleLengthCounterEnable = ((value >> 2) & 1) != 0;
				_noiseLengthCounterEnable = ((value >> 3) & 1) != 0;
				_dmcControlEnable = ((value >> 4) & 1) != 0;

				if (!_pulse1LengthCounter.Enable)
					_pulse1LengthCounter.Value = 0;

				if (!_pulse2LengthCounter.Enable)
					_pulse2LengthCounter.Value = 0;
				break;
			case 0x4017:
				_frameCounterMode = (value >> 7) & 1;
				_frameCounterInterruptInhibit = ((value >> 6) & 1) != 0;

				if (_frameCounterInterruptInhibit)
					_frameCounterInterrupt = false;

				_frameCounterResetCounter = 4;
				break;
		}
	}

	public void Tick()
	{
		_pulse1Sweep.UpdateTargetPeriod();
		_pulse2Sweep.UpdateTargetPeriod();

		DoFrameCounter();

		if (_cycles % 2 == 1)
			DoSequencer();

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
			case 0:
				if (!_frameCounterInterruptInhibit)
					_frameCounterInterrupt = true;
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
			case (14914 * 2):
				if (!_frameCounterInterruptInhibit)
					_frameCounterInterrupt = true;
				break;
			case (14914 * 2) + 1:
				if (!_frameCounterInterruptInhibit)
					_frameCounterInterrupt = true;
				DoQuarterFrame();
				DoHalfFrame();
				break;
		}

		_frameCounter++;
		if (_frameCounter == 14915 * 2)
			_frameCounter = 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoFrameCounterMode1()
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
		_pulse1Envelope.Step();
		_pulse2Envelope.Step();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoTriangleLinearCounter()
	{
		// TODO
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoLengthCounters()
	{
		_pulse1LengthCounter.Step();
		_pulse2LengthCounter.Step();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoSweep()
	{
		_pulse1Sweep.Step();
		_pulse2Sweep.Step();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoSequencer()
	{
		_pulse1Sequencer.Step();
		_pulse2Sequencer.Step();
	}

	public float GetCurrentSample()
	{
		var pulseOut = 0.0;

		var pulse1 = 0.0;
		var pulse2 = 0.0;

		if (_pulse1LengthCounter.Value != 0 && !_pulse1Sweep.MuteChannel)
		{
			var freq = Emu.CyclesPerSecond / 12.0 / (16.0 * (_pulse1Sequencer.TimerReload + 1));
			var dutycycle = _pulse1Sequencer.DutyCycle switch
			{
				0 => 0.125,
				1 => 0.25,
				2 => 0.5,
				3 => 0.25,
				_ => 0.0
			};

			pulse1 = _pulse1Generator.GenerateSample(freq, dutycycle) * _pulse1Envelope.Volume;
		}

		if (_pulse2LengthCounter.Value != 0 && !_pulse2Sweep.MuteChannel)
		{
			var freq = Emu.CyclesPerSecond / 12.0 / (16.0 * (_pulse2Sequencer.TimerReload + 1));
			var dutycycle = _pulse2Sequencer.DutyCycle switch
			{
				0 => 0.125,
				1 => 0.25,
				2 => 0.5,
				3 => 0.75,
				_ => 0.0
			};

			pulse2 = _pulse2Generator.GenerateSample(freq, dutycycle) * _pulse2Envelope.Volume;
		}
		
		if (pulse1 != 0.0 || pulse2 != 0.0)
			pulseOut = 95.88 / ((8128.0 / (pulse1 + pulse2)) + 100.0);

		var tndOut = 0.0;
		var output = pulseOut + tndOut;
		return (float)output;
	}
}
