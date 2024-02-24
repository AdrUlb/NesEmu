using System.Runtime.CompilerServices;

namespace NesEmu;

internal sealed class Apu
{
	// https://www.nesdev.org/wiki/APU_Envelope
	private struct EnvelopeUnit
	{
		public bool RegLoopFlag;
		public bool RegConstantVolumeFlag;
		public int RegVolumeOrDividerReload;

		public bool StartFlag;
		private int _decayLevelCounter;
		private int _divider;

		public int Volume { get; private set; }

		private void Divider()
		{
			// When the divider is clocked while at 0, it is loaded with V and clocks the decay level counter
			if (_divider == 0)
			{
				_divider = RegVolumeOrDividerReload;

				// Then one of two actions occurs:
				if (_decayLevelCounter != 0) // If the counter is non-zero, it is decremented
					_decayLevelCounter--;

				else if (RegLoopFlag) // otherwise if the loop flag is set, the decay level counter is loaded with 15.
				{
					_decayLevelCounter = 15;
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
				_decayLevelCounter = 15;
				_divider = RegVolumeOrDividerReload;
			}
			Volume = RegConstantVolumeFlag ? RegVolumeOrDividerReload : _decayLevelCounter;
		}
	}

	private readonly byte[] _dutyCycles = [0b00000001, 0b00000011, 0b00001111, 0b11111100];

	private EnvelopeUnit _pulse1Envelope = new();
	private EnvelopeUnit _pulse2Envelope = new();

	private int _frameCounter = 0;
	private int _frameCounterMode = 1;
	private bool _frameCounterInterruptInhibit = false;
	private bool _frameCounterInterrupt = false;
	private int _frameCounterResetCounter = 0;

	private int _pulse1DutyCycle = 0;
	private bool _pulse1LengthCounterHalt = false;
	private int _pulse1LengthCounter = 0;
	private int _pulse1TimerReload = 0;
	private int _pulse1Timer = 0;
	private int _pulse1DutyIndex = 0;

	private int _pulse2DutyCycle = 0;
	private bool _pulse2LengthCounterHalt = false;
	private int _pulse2LengthCounter = 0;
	private int _pulse2TimerReload = 0;
	private int _pulse2Timer = 0;
	private int _pulse2DutyIndex = 0;

	private bool _dmcControlEnable = false;
	private bool _pulse1LengthCounterEnable = false;
	private bool _pulse2LengthCounterEnable = false;
	private bool _triangleLengthCounterEnable = false;
	private bool _noiseLengthCounterEnable = false;

	private int _pulse1SequencerOutput = 0;
	private int _pulse2SequencerOutput = 0;

	private ulong _cycles = 0;

	private byte[] _lengthCounterLookupTable = [10, 254, 20, 2, 40, 4, 80, 6, 160, 8, 60, 10, 14, 12, 26, 14, 12, 16, 24, 18, 48, 20, 96, 22, 192, 24, 72, 26, 16, 28, 32, 30];

	public byte CpuReadByte(ushort address)
	{
		switch (address)
		{
			case 0x4015:
				return (byte)(
					(_pulse1LengthCounterEnable ? 1 : 0) |
					((_pulse2LengthCounterEnable ? 1 : 0) << 2) |
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
				_pulse1DutyCycle = (byte)((value >> 6) & 0b11);
				_pulse1LengthCounterHalt = ((value >> 5) & 1) != 0;

				_pulse1Envelope.RegLoopFlag = _pulse1LengthCounterHalt;
				_pulse1Envelope.RegConstantVolumeFlag = ((value >> 4) & 1) != 0;
				_pulse1Envelope.RegVolumeOrDividerReload = value & 0b1111;
				break;
			case 0x4002:
				_pulse1TimerReload &= 0xFF00;
				_pulse1TimerReload |= value;
				break;
			case 0x4003:
				_pulse1TimerReload &= 0x00FF;
				_pulse1TimerReload |= (value & 0b111) << 8;

				if (_pulse1LengthCounterEnable)
					_pulse1LengthCounter = _lengthCounterLookupTable[((value >> 3) & 0b11111)];

				_pulse1Timer = _pulse1TimerReload;
				_pulse1Envelope.StartFlag = true;
				//_pulse1EnvelopeStartFlag = true;
				// Restart envelope
				// Reset phase of pulse generator
				break;
			case 0x4004:
				_pulse2DutyCycle = (byte)((value >> 6) & 0b11);
				_pulse2LengthCounterHalt = ((value >> 5) & 1) != 0;

				_pulse2Envelope.RegLoopFlag = _pulse2LengthCounterHalt;
				_pulse2Envelope.RegConstantVolumeFlag = ((value >> 4) & 1) != 0;
				_pulse2Envelope.RegVolumeOrDividerReload = value & 0b1111;
				break;
			case 0x4006:
				_pulse2TimerReload &= 0xFF00;
				_pulse2TimerReload |= value;
				break;
			case 0x4007:
				_pulse2TimerReload &= 0x00FF;
				_pulse2TimerReload |= (value & 0b111) << 8;

				if (_pulse2LengthCounterEnable)
					_pulse2LengthCounter = _lengthCounterLookupTable[((value >> 3) & 0b11111)];

				_pulse2Timer = _pulse2TimerReload;
				_pulse2Envelope.StartFlag = true;
				// Restart envelope
				// Reset phase of pulse generator
				break;
			case 0x4015:
				_pulse1LengthCounterEnable = (value & 1) != 0;
				_pulse2LengthCounterEnable = ((value >> 1) & 1) != 0;
				_triangleLengthCounterEnable = ((value >> 2) & 1) != 0;
				_noiseLengthCounterEnable = ((value >> 3) & 1) != 0;
				_dmcControlEnable = ((value >> 4) & 1) != 0;

				if (!_pulse1LengthCounterEnable)
					_pulse1LengthCounter = 0;

				if (!_pulse2LengthCounterEnable)
					_pulse2LengthCounter = 0;
				// TODO
				break;
			case 0x4017:
				_frameCounterMode = (value >> 7) & 1;
				_frameCounterInterruptInhibit = ((value >> 6) & 1) != 0;

				if (_frameCounterInterruptInhibit)
					_frameCounterInterrupt = false;

				_frameCounterResetCounter = 4;

				/*if (_frameCounterMode == 1) ?????
 				{
 					DoQuarterFrame();
 					DoHalfFrame();
 				}*/
				break;
		}
	}

	public void Tick()
	{
		DoFrameCounter();
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
		if (_pulse1LengthCounter != 0 && !_pulse1LengthCounterHalt)
			_pulse1LengthCounter--;

		if (_pulse2LengthCounter != 0 && !_pulse2LengthCounterHalt)
			_pulse2LengthCounter--;

		// TODO
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoSweep()
	{
		// TODO
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DoSequencer()
	{
		if (_cycles % 2 == 0)
			return;

		if (_pulse1Timer == 0)
		{
			_pulse1Timer = _pulse1TimerReload;

			_pulse1SequencerOutput = (_dutyCycles[_pulse1DutyCycle] >> _pulse1DutyIndex) & 1;

			if (_pulse1DutyIndex == 0)
			{
				_pulse1DutyIndex = 7;
			}
			else
				_pulse1DutyIndex--;
		}
		else
			_pulse1Timer--;

		if (_pulse2Timer == 0)
		{
			_pulse2Timer = _pulse2TimerReload;

			_pulse2SequencerOutput = (_dutyCycles[_pulse2DutyCycle] >> _pulse2DutyIndex) & 1;

			if (_pulse2DutyIndex == 0)
			{
				_pulse2DutyIndex = 7;
			}
			else
				_pulse2DutyIndex--;
		}
		else
			_pulse2Timer--;
	}

	private float NiceSquare(float time, float freq, float dutycycle)
	{
		var a = 0.0f;
		var b = 0.0f;
		var p = dutycycle * 2.0f * float.Pi;

		for (float n = 1; n < 50.0f; n++)
		{
			var c = n * freq * 2.0f * float.Pi * time;
			a += float.Sin(c) / n;
			b += float.Sin(c - (p * n)) / n;
		}

		return (2.0f / float.Pi) * (a - b);
	}

	public float GetCurrentSample(float time)
	{
		var pulseOut = 0.0f;

		var pulse1 = 0.0f;
		var pulse2 = 0.0f;

		if (_pulse1LengthCounter != 0)
		{
			var freq = Emu.CyclesPerSecond / 12.0f / (16.0f * (_pulse1TimerReload + 1));
			var dutycycle = _pulse1DutyCycle switch
			{
				0 => 0.125f,
				1 => 0.25f,
				2 => 0.5f,
				3 => 0.25f,
				_ => 0.0f
			};

			pulse1 = NiceSquare(time, freq, dutycycle) * _pulse1Envelope.Volume;
		}

		if (_pulse2LengthCounter != 0)
		{
			var freq = Emu.CyclesPerSecond / 12.0f / (16.0f * (_pulse2TimerReload + 1));
			var dutycycle = _pulse2DutyCycle switch
			{
				0 => 0.125f,
				1 => 0.25f,
				2 => 0.5f,
				3 => 0.25f,
				_ => 0.0f
			};

			//pulse2 = NiceSquare(time, freq, dutycycle) * _pulse2Volume;
			pulse2 = NiceSquare(time, freq, dutycycle) * _pulse2Envelope.Volume;
		}
		
		if (pulse1 != 0.0f || pulse2 != 0.0f)
			pulseOut = 95.88f / ((8128.0f / (pulse1 + pulse2)) + 100.0f);

		var tndOut = 0.0f;
		var output = pulseOut + tndOut;
		return output;
	}
}
