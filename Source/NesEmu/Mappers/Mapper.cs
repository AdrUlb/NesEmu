namespace NesEmu.Mappers;

internal abstract class Mapper
{
	protected int Nametable0Offset;
	protected int Nametable1Offset;
	protected int Nametable2Offset;
	protected int Nametable3Offset;

	public bool RequestInterrupt;

	protected void SetMirroringMode(MirroringMode mirroringMode)
	{
		switch (mirroringMode)
		{
			case MirroringMode.Horizontal:
				Nametable0Offset = 0;
				Nametable1Offset = 0;
				Nametable2Offset = 0x400;
				Nametable3Offset = 0x400;
				break;
			case MirroringMode.Vertical:
				Nametable0Offset = 0;
				Nametable1Offset = 0x400;
				Nametable2Offset = 0;
				Nametable3Offset = 0x400;
				break;
			case MirroringMode.OneScreenLowerBank:
				Nametable0Offset = 0;
				Nametable1Offset = 0;
				Nametable2Offset = 0;
				Nametable3Offset = 0;
				break;
			case MirroringMode.OneScreenUpperBank:
				Nametable0Offset = 0x400;
				Nametable1Offset = 0x400;
				Nametable2Offset = 0x400;
				Nametable3Offset = 0x400;
				break;
			default:
				throw new NotImplementedException();
		}
	}

	public abstract byte CpuReadByte(ushort address);

	public abstract void CpuWriteByte(ushort address, byte value);

	public abstract byte PpuReadByte(Ppu ppu, ushort address);

	public abstract void PpuWriteByte(Ppu ppu, ushort address, byte value);

	public virtual void TickScanline() { }

	public bool AcknowledgeInterrupt()
	{
		if (!RequestInterrupt)
			return false;

		//RequestInterrupt = false;
		return true;
	}
}
