namespace NesEmu.Mappers;

internal abstract class Mapper
{
	public abstract byte CpuReadByte(ushort address);

	public abstract void CpuWriteByte(ushort address, byte value);

	public abstract byte PpuReadByte(Ppu ppu, ushort address);

	public abstract void PpuWriteByte(Ppu ppu, ushort address, byte value);
}
