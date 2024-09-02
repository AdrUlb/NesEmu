namespace NesEmu;

// https://www.nesdev.org/wiki/CPU_memory_map
internal sealed class CpuBus(Emu emu)
{
	private readonly byte[] _ram = new byte[0x0800];

	public byte ReadByte(ushort address)
	{
		// The cartridge can map to any addresses, in the case of a bus conflict 0s "win" over 1s.
		// As such whatever the cartridge reads is effectively logically ANDed with whatever else is on the bus.

		var value = emu.Cartridge?.CpuReadByte(address) ?? 0xFF;

		value &= address switch
		{
			< 0x0800 => _ram[address],
			< 0x1000 => _ram[address - 0x0800],
			< 0x1800 => _ram[address - 0x1000],
			< 0x2000 => _ram[address - 0x1800],
			< 0x4000 => emu.Ppu.CpuReadByte(((address - 0x2000) % 8) + 0x2000),
			<= 0x4008 => emu.Apu.CpuReadByte(address),
			>= 0x400A and <= 0x400C => emu.Apu.CpuReadByte(address),
			>= 0x400E and <= 0x4013 => emu.Apu.CpuReadByte(address),
			0x4014 => emu.Ppu.CpuReadByte(address),
			0x4015 => emu.Apu.CpuReadByte(address),
			0x4016 => emu.Controller.CpuReadByte(address),
			0x4017 => emu.Apu.CpuReadByte(address),
			0x4018 => 0,
			_ => 0xFF
		};

		return value;
	}

	public void WriteByte(ushort address, byte value)
	{
		emu.Cartridge?.CpuWriteByte(address, value);

		switch (address)
		{
			case < 0x0800:
				_ram[address] = value;
				break;
			case < 0x1000:
				_ram[address - 0x0800] = value;
				break;
			case < 0x1800:
				_ram[address - 0x1000] = value;
				break;
			case < 0x2000:
				_ram[address - 0x1800] = value;
				break;
			case < 0x4000:
				emu.Ppu.CpuWriteByte(((address - 0x2000) % 8) + 0x2000, value);
				break;
			case <= 0x4008:
				emu.Apu.CpuWriteByte(address, value);
				break;
			case >= 0x400A and <= 0x400C:
				emu.Apu.CpuWriteByte(address, value);
				break;
			case >= 0x400E and <= 0x4013:
				emu.Apu.CpuWriteByte(address, value);
				break;
			case 0x4014:
				emu.Ppu.CpuWriteByte(address, value);
				break;
			case 0x4015:
				emu.Apu.CpuWriteByte(address, value);
				break;
			case 0x4016:
				emu.Controller.CpuWriteByte(address, value);
				break;
			case 0x4017:
				emu.Apu.CpuWriteByte(address, value);
				break;
		}
	}
}
