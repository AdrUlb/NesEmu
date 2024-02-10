namespace NesEmu;

// https://www.nesdev.org/wiki/PPU_memory_map
internal sealed class PpuBus(Ppu ppu)
{
	public const ushort PatternTable0Address = 0x0000;
	public const ushort PatternTable1Address = 0x1000;

	public const ushort Nametable0Address = 0x2000;
	public const ushort Nametable1Address = 0x2400;
	public const ushort Nametable2Address = 0x2800;
	public const ushort Nametable3Address = 0x2C00;

	public const ushort PaletteRamAddress = 0x3F00;

	private readonly byte[] _paletteRam = new byte[0x20];
	public Cartridge? Cartridge = null;
	private readonly Ppu _ppu = ppu;

	public byte ReadByte(ushort address)
	{
		if (address is >= 0x3F00 and < 0x4000)
		{
			address -= 0x3F00;
			address %= 0x20;

			if (address == 0x10)
				address = 0;

			return _paletteRam[address];
		}

		return Cartridge?.PpuReadByte(_ppu, address) ?? 0xFF;
	}

	public void WriteByte(ushort address, byte value)
	{
		if (address is >= PaletteRamAddress and < 0x4000)
		{
			address -= 0x3F00;
			address %= 0x20;

			if (address == 0x10)
				address = 0;

			_paletteRam[address] = value;
			return;
		}

		Cartridge?.PpuWriteByte(_ppu, address, value);
	}
}
