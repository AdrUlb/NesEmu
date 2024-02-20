namespace NesEmu;

internal sealed class Apu
{
	double count = 0;

	public void Tick()
	{

	}

	public double GetCurrentSample()
	{
		count += 0.1;
		return Math.Sin(count);
	}

	public byte CpuReadByte(ushort address)
	{
		return 0;
	}

	public void CpuWriteByte(ushort address, byte value) { }
}
