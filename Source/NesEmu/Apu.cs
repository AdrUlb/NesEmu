using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace NesEmu;

internal sealed class Apu
{
	private readonly byte[] _dutyCycles = [0b00000001, 0b00000011, 0b00001111, 0b11111100];

	private int _frameCounter = 0;
	private int _frameCounterMode = 1;
	private bool _frameCounterInterruptInhibit = false;
	private bool _frameCounterInterrupt = false;
	private int _frameCounterResetCounter = 0;
	private double _pulse1Sample = 0;

	private int _pulse1DutyCycle = 0;
	private bool _pulse1LengthCounterHalt = false;
	private bool _pulse1ConstantEnvelope = false;
	private int _pulse1EnvelopeDividerPeriod = 0;
	private int _pulse1LengthCounter = 0;
	private int _pulse1Timer = 0;
	private int _pulse1TimerCounter = 0;
	private int _pulse1DutyIndex = 0;

	private bool _dmcControlEnable = false;
	private bool _pulse1LengthCounterEnable = false;
	private bool _pulse2LengthCounterEnable = false;
	private bool _triangleLengthCounterEnable = false;
	private bool _noiseLengthCounterEnable = false;

	private ulong _cycles = 0;

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
				_pulse1ConstantEnvelope = ((value >> 4) & 1) != 0;
				_pulse1EnvelopeDividerPeriod = value & 0b1111;
				break;
			case 0x4002:
				_pulse1Timer &= 0xFF00;
				_pulse1Timer |= value;
				break;
			case 0x4003:
				_pulse1Timer &= 0x00FF;
				_pulse1Timer |= (value & 0b111) << 8;
				if (_pulse1LengthCounterEnable)
					_pulse1LengthCounter = (value >> 3) & 0b11111;
				_pulse1TimerCounter = _pulse1Timer;
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
		// TODO
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
		if (_pulse1TimerCounter == 0)
		{
			_pulse1TimerCounter = _pulse1Timer;

			_pulse1Sample = (_dutyCycles[_pulse1DutyCycle] >> _pulse1DutyIndex) & 1;

			if (_pulse1DutyIndex == 0)
			{
				_pulse1DutyIndex = 7;
			}
			else
				_pulse1DutyIndex--;
		}
		else
			_pulse1TimerCounter--;
	}

	public double GetCurrentSample()
	{
		if (_pulse1LengthCounter == 0 || _pulse1Timer < 8)
			return 0;

		return _pulse1Sample;
	}
}
