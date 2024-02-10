using System.Diagnostics;

namespace NesEmu.Mappers;

internal sealed class Mapper1 : Mapper
{
	private readonly byte[] _prgRom;
	private readonly byte[] _chrRom;

	private int _nametable0Offset;
	private int _nametable1Offset;
	private int _nametable2Offset;
	private int _nametable3Offset;
	private int _selectedChrBank = 0;

	private int _chrBank1Offset;
	private int _chrBank2Offset;

	private bool _chr8kbMode = false;

	private int _shiftRegister = 0;
	private int _writeCount = 0;

	public Mapper1(int prgRomBanks, int chrRomBanks, MirroringMode mirroringMode, Stream data)
	{
		var prgRomSize = prgRomBanks * 0x4000;
		var chrRomSize = chrRomBanks * 0x2000;

		if (chrRomBanks == 0)
			chrRomSize = 0x2000;

		_prgRom = new byte[prgRomSize];
		_chrRom = new byte[chrRomSize];

		data.ReadExactly(_prgRom);

		if (chrRomBanks != 0)
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
		>= 0x8000 => _prgRom[address - 0x8000],
		_ => 0xFF,
	};

	public override void CpuWriteByte(ushort address, byte value)
	{
		if (address < 0x8000)
			return;

		if ((value >> 7) != 0)
		{
			_writeCount = 0;
			_shiftRegister = 0;
			return;
		}

		_shiftRegister |= (value & 1) << _writeCount;
		_writeCount++;

		if (_writeCount < 5)
			return;

		switch (address)
		{
			case >= 0x8000 and < 0xA000: // Control register
				{
					var mirroringMode = (_shiftRegister & 0b11) switch
					{
						0 => MirroringMode.OneScreenLowerBank,
						1 => MirroringMode.OneScreenUpperBank,
						2 => MirroringMode.Vertical,
						3 => MirroringMode.Horizontal,
						_ => throw new UnreachableException()
					};

					var prgMode = (_shiftRegister >> 2) & 0b11;
					_chr8kbMode = ((_shiftRegister >> 4) & 1) == 0;

					switch (mirroringMode)
					{
						case MirroringMode.OneScreenLowerBank:
							_nametable0Offset = 0;
							_nametable1Offset = 0x400;
							_nametable2Offset = 0x400;
							_nametable3Offset = 0x400;
							_selectedChrBank = 0;
							break;
						case MirroringMode.OneScreenUpperBank:
							_nametable0Offset = 0;
							_nametable1Offset = 0x400;
							_nametable2Offset = 0x400;
							_nametable3Offset = 0x400;
							_selectedChrBank = 1;
							break;
					}
					break;
				}
			case >= 0xA000 and < 0xC000: // CHR bank 0
				_chrBank1Offset = _shiftRegister * 0x1000;
				break;
			case >= 0xC000 and < 0xE000: // CHR bank 1
				_chrBank2Offset = _shiftRegister * 0x1000;
				break;
		}

		_writeCount = 0;
		_shiftRegister = 0;
	}

	public override byte PpuReadByte(Ppu ppu, ushort address)
	{
		if (address is >= 0x3000 and < 0x3F00)
			address -= 0x1000;

		return address switch
		{
			< 0x1000 => _chrRom[address + (_selectedChrBank * 0x1000) + _chrBank1Offset],
			< 0x2000 => _chrRom[address - 0x1000 + (_selectedChrBank * 0x1000) + _chrBank2Offset],
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
			case >= PpuBus.Nametable0Address and < PpuBus.Nametable1Address: ppu.Vram[address - PpuBus.Nametable0Address + _nametable0Offset] = value; break;
			case >= PpuBus.Nametable1Address and < PpuBus.Nametable2Address: ppu.Vram[address - PpuBus.Nametable1Address + _nametable1Offset] = value; break;
			case >= PpuBus.Nametable2Address and < PpuBus.Nametable3Address: ppu.Vram[address - PpuBus.Nametable2Address + _nametable2Offset] = value; break;
			case >= PpuBus.Nametable3Address and < 0x3000: ppu.Vram[address - PpuBus.Nametable3Address + _nametable3Offset] = value; break;
		}
	}
}
