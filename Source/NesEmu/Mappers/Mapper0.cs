namespace NesEmu.Mappers;

internal sealed class Mapper0 : Mapper
{
	private readonly byte[] _prgRom;
	private readonly byte[] _chrRom;
	private readonly bool _chrRam = false;

	private readonly int _nametable0Offset;
	private readonly int _nametable1Offset;
	private readonly int _nametable2Offset;
	private readonly int _nametable3Offset;

	public Mapper0(int prgRomBanks, int chrRomBanks, MirroringMode mirroringMode, Stream data)
	{
		var prgRomSize = prgRomBanks * 0x4000;
		var chrRomSize = chrRomBanks * 0x2000;

		if (chrRomBanks > 1)
			throw new InvalidOperationException();

		if (chrRomBanks == 0)
			throw new NotImplementedException();

		_prgRom = new byte[prgRomSize];
		_chrRom = new byte[chrRomSize];

		if (prgRomSize is not 0x4000 and not 0x8000)
			throw new NotSupportedException($"Program ROM size {prgRomSize} bytes invalid.");

		data.ReadExactly(_prgRom);
		data.ReadExactly(_chrRom);

		switch (mirroringMode)
		{
			case MirroringMode.Horizontal:
				_nametable0Offset = 0;
				_nametable1Offset = 0;
				_nametable2Offset = 0x400;
				_nametable3Offset = 0x400;
				break;
			case MirroringMode.Vertical:
				_nametable0Offset = 0;
				_nametable1Offset = 0x400;
				_nametable2Offset = 0;
				_nametable3Offset = 0x400;
				break;
		}
	}

	public override byte CpuReadByte(ushort address) => address switch
	{
		>= 0x8000 and < 0xC000 => _prgRom[address - 0x8000],
		>= 0xC000 => _prgRom[address - (_prgRom.Length == 0x4000 ? 0xC000 : 0x8000)],
		_ => 0xFF,
	};

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
			>= PpuBus.Nametable0Address and < PpuBus.Nametable1Address => ppu.Vram[address - PpuBus.Nametable0Address + _nametable0Offset],
			>= PpuBus.Nametable1Address and < PpuBus.Nametable2Address => ppu.Vram[address - PpuBus.Nametable1Address + _nametable1Offset],
			>= PpuBus.Nametable2Address and < PpuBus.Nametable3Address => ppu.Vram[address - PpuBus.Nametable2Address + _nametable2Offset],
			>= PpuBus.Nametable3Address and < 0x3000 => ppu.Vram[address - PpuBus.Nametable3Address + _nametable3Offset],
			_ => 0xFF
		};
	}

	public override void PpuWriteByte(Ppu ppu, ushort address, byte value)
	{
		if (address is >= 0x3000 and < 0x3F00)
			address -= 0x1000;

		switch (address)
		{
			case < PpuBus.Nametable0Address when _chrRam: _chrRom[address] = value; break;
			case >= PpuBus.Nametable0Address and < PpuBus.Nametable1Address: ppu.Vram[address - PpuBus.Nametable0Address + _nametable0Offset] = value; break;
			case >= PpuBus.Nametable1Address and < PpuBus.Nametable2Address: ppu.Vram[address - PpuBus.Nametable1Address + _nametable1Offset] = value; break;
			case >= PpuBus.Nametable2Address and < PpuBus.Nametable3Address: ppu.Vram[address - PpuBus.Nametable2Address + _nametable2Offset] = value; break;
			case >= PpuBus.Nametable3Address and < 0x3000: ppu.Vram[address - PpuBus.Nametable3Address + _nametable3Offset] = value; break;
		}
	}
}
