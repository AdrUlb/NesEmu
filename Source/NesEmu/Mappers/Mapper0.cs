namespace NesEmu.Mappers;

internal sealed class Mapper0 : Mapper
{
	private readonly byte[] _prgRom;
	private readonly byte[] _chrRom;

	public Mapper0(int prgRomBanks, int chrRomBanks, MirroringMode mirroringMode, Stream data)
	{
		var prgRomSize = prgRomBanks * 0x4000; // 16384
		var chrRomSize = chrRomBanks * 0x2000; // 8192

		if (prgRomBanks is not (1 or 2))
			throw new NotImplementedException();

		if (chrRomBanks is not 1)
			throw new NotImplementedException();

		_prgRom = new byte[prgRomSize];
		_chrRom = new byte[chrRomSize];

		data.ReadExactly(_prgRom);
		data.ReadExactly(_chrRom);

		SetMirroringMode(mirroringMode);
	}

	public override byte CpuReadByte(ushort address)
	{
		if (address is >= 0x8000 and < 0xC000)
			return _prgRom[address - 0x8000];

		if (address >= 0x8000)
		{
			address -= 0x8000;
			address %= (ushort)_prgRom.Length;
			return _prgRom[address];
		}

		return 0xFF;
	}

	public override void CpuWriteByte(ushort address, byte value)
	{

	}

	public override byte PpuReadByte(Ppu ppu, ushort address)
	{
		if (address is >= 0x3000 and < 0x3F00)
			address -= 0x1000;

		return address switch
		{
			< PpuBus.Nametable0Address => _chrRom[address],
			>= PpuBus.Nametable0Address and < PpuBus.Nametable1Address => ppu.Vram[address - PpuBus.Nametable0Address + Nametable0Offset],
			>= PpuBus.Nametable1Address and < PpuBus.Nametable2Address => ppu.Vram[address - PpuBus.Nametable1Address + Nametable1Offset],
			>= PpuBus.Nametable2Address and < PpuBus.Nametable3Address => ppu.Vram[address - PpuBus.Nametable2Address + Nametable2Offset],
			>= PpuBus.Nametable3Address and < 0x3000 => ppu.Vram[address - PpuBus.Nametable3Address + Nametable3Offset],
			_ => 0xFF
		};
	}

	public override void PpuWriteByte(Ppu ppu, ushort address, byte value)
	{
		if (address is >= 0x3000 and < 0x3F00)
			address -= 0x1000;

		switch (address)
		{
			case >= PpuBus.Nametable0Address and < PpuBus.Nametable1Address: ppu.Vram[address - PpuBus.Nametable0Address + Nametable0Offset] = value; break;
			case >= PpuBus.Nametable1Address and < PpuBus.Nametable2Address: ppu.Vram[address - PpuBus.Nametable1Address + Nametable1Offset] = value; break;
			case >= PpuBus.Nametable2Address and < PpuBus.Nametable3Address: ppu.Vram[address - PpuBus.Nametable2Address + Nametable2Offset] = value; break;
			case >= PpuBus.Nametable3Address and < 0x3000: ppu.Vram[address - PpuBus.Nametable3Address + Nametable3Offset] = value; break;
		}
	}
}
