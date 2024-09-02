namespace NesEmu.Mappers;

internal sealed class Mapper2 : Mapper
{
	private readonly byte[] _prgRom;
	private readonly byte[] _chrRom;
	private readonly byte[] _prgRam;

	private readonly bool _chrRam = false;

	private int _prgBank0 = 0;
	private readonly int _prgBank1;

	private readonly int _prgRomBanks;

	public Mapper2(int prgRomBanks, int chrRomBanks, MirroringMode mirroringMode, Stream data)
	{
		if (chrRomBanks == 0)
		{
			chrRomBanks = 1;
			_chrRam = true;
		}

		_prgRomBanks = prgRomBanks; // Number of 16k banks
		_prgRam = new byte[0x2000];

		var prgRomSize = prgRomBanks * 0x4000; // 16384
		var chrRomSize = chrRomBanks * 0x2000; // 8192

		_prgBank1 = prgRomBanks - 1;

		_prgRom = new byte[prgRomSize];
		_chrRom = new byte[chrRomSize];

		data.ReadExactly(_prgRom);
		if (!_chrRam)
			data.ReadExactly(_chrRom);

		SetMirroringMode(mirroringMode);
	}

	public override byte CpuReadByte(ushort address) => address switch
	{
		>= 0x6000 and <= 0x7FFF => _prgRam[address - 0x6000],
		>= 0x8000 and <= 0xBFFF => _prgRom[address - 0x8000 + (_prgBank0 * 0x4000)],
		>= 0xC000 => _prgRom[address - 0xC000 + (_prgBank1 * 0x4000)],
		_ => 0xFF
	};

	public override void CpuWriteByte(ushort address, byte value)
	{
		if (address < 0x8000)
			return;

		_prgBank0 = value % _prgRomBanks;
	}

	public override byte PpuReadByte(Ppu ppu, ushort address)
	{
		if (address is >= 0x3000 and < 0x3F00)
			address -= 0x1000;

		return address switch
		{
			< PpuBus.Nametable0Address => _chrRom[address],
			< PpuBus.Nametable1Address => ppu.Vram[address - PpuBus.Nametable0Address + Nametable0Offset],
			< PpuBus.Nametable2Address => ppu.Vram[address - PpuBus.Nametable1Address + Nametable1Offset],
			< PpuBus.Nametable3Address => ppu.Vram[address - PpuBus.Nametable2Address + Nametable2Offset],
			< 0x3000 => ppu.Vram[address - PpuBus.Nametable3Address + Nametable3Offset],
			_ => 0xFF
		};
	}

	public override void PpuWriteByte(Ppu ppu, ushort address, byte value)
	{
		if (address is >= 0x3000 and < 0x3F00)
			address -= 0x1000;

		switch (address)
		{
			case < PpuBus.Nametable0Address when _chrRam:
				_chrRom[address] = value;
				break;
			case >= PpuBus.Nametable0Address and < PpuBus.Nametable1Address:
				ppu.Vram[address - PpuBus.Nametable0Address + Nametable0Offset] = value;
				break;
			case >= PpuBus.Nametable1Address and < PpuBus.Nametable2Address:
				ppu.Vram[address - PpuBus.Nametable1Address + Nametable1Offset] = value;
				break;
			case >= PpuBus.Nametable2Address and < PpuBus.Nametable3Address:
				ppu.Vram[address - PpuBus.Nametable2Address + Nametable2Offset] = value;
				break;
			case >= PpuBus.Nametable3Address and < 0x3000:
				ppu.Vram[address - PpuBus.Nametable3Address + Nametable3Offset] = value;
				break;
		}
	}
}
