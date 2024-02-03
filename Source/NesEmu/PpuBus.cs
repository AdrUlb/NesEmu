namespace NesEmu;

// https://www.nesdev.org/wiki/PPU_memory_map
internal sealed class PpuBus
{
	private readonly byte[] _paletteRam = new byte[0x20];
	public Cartridge? Cartridge = null;

	public byte ReadByte(ushort address)
	{
		return address is >= 0x3F00 and < 0x4000
			? _paletteRam[(address - 0x3F00) % 0x20]
			: Cartridge?.PpuReadByte(address) ?? 0xFF;
	}

	public void WriteByte(ushort address, byte value)
	{
		if (address is >= 0x3F00 and < 0x4000)
		{
			_paletteRam[(address - 0x3F00) % 0x20] = value;
			return;
		}

		Cartridge?.PpuWriteByte(address, value);
	}
}
