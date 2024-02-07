namespace NesEmu;

// https://www.nesdev.org/wiki/CPU_memory_map
internal sealed class CpuBus
{
	private readonly byte[] _ram = new byte[0x0800];

	public Ppu? Ppu;
	public Cartridge? Cartridge = null;
	public Controller? Controller = null;

	public CpuBus()
	{

	}

	public byte ReadByte(ushort address)
	{
		// The cartridge can map to any addresses, in the case of a bus conflict 0s "win" over 1s.
		// As such whatever the cartridge reads is effectively logically ANDed with whatever else is on the bus.

		var value = Cartridge?.CpuReadByte(address) ?? 0xFF;

		value &= address switch
		{
			< 0x0800 => _ram[address],
			>= 0x0800 and < 0x1000 => _ram[address - 0x0800],
			>= 0x1000 and < 0x1800 => _ram[address - 0x1000],
			>= 0x1800 and < 0x2000 => _ram[address - 0x1800],
			>= 0x2000 and < 0x4000 => Ppu?.ReadReg(((address - 0x2000) % 8) + 0x2000) ?? 0xFF,
			0x4014 => Ppu?.ReadReg(0x4014) ?? 0xFF,
			0x4016 => Controller?.CpuReadByte(address) ?? 0,
			0x4017 => 0,
			0x4018 => 0,
			_ => 0xFF
		};

		return value;
	}

	public void WriteByte(ushort address, byte value)
	{
		Cartridge?.CpuWriteByte(address, value);

		switch (address)
		{
			case < 0x0800: _ram[address] = value; break;
			case 0x0800 and < 0x1000: _ram[address - 0x0800] = value; break;
			case 0x1000 and < 0x1800: _ram[address - 0x1000] = value; break;
			case 0x1800 and < 0x2000: _ram[address - 0x1800] = value; break;
			case >= 0x2000 and < 0x4000: Ppu?.WriteReg(((address - 0x2000) % 8) + 0x2000, value); break;
			case 0x4014: Ppu?.WriteReg(0x4014, value); break;
			case 0x4016: Controller?.CpuWriteByte(address, value); break;
		}
	}
}
