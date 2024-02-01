namespace NesEmu;

internal sealed class Bus
{
	private readonly byte[] _ram = new byte[0x0800];

	public Cartridge? Cartridge = null;

	public byte ReadByte(ushort address)
	{
		// The cartridge can map to any addresses, in the case of a bus conflict 0s "win" over 1s.
		// As such whatever the cartridge reads is effectively logically ANDed with whatever else is on the bus.

		var value = Cartridge?.ReadByte(address) ?? 0xFF;

		value &= address switch
		{
			< 0x0800 => _ram[address],
			>= 0x0800 and < 0x1000 => _ram[address - 0x0800],
			>= 0x1000 and < 0x1800 => _ram[address - 0x1000],
			>= 0x1800 and < 0x2000 => _ram[address - 0x1800],
			_ => 0xFF
		};

		return value;
	}

	public void WriteByte(ushort address, byte value)
	{
		Cartridge?.WriteByte(address, value);

		switch (address)
		{
			case < 0x0800: _ram[address] = value; break;
			case 0x0800 and < 0x1000: _ram[address - 0x0800] = value; break;
			case 0x1000 and < 0x1800: _ram[address - 0x1000] = value; break;
			case 0x1800 and < 0x2000: _ram[address - 0x1800] = value; break;
		}
	}
}
