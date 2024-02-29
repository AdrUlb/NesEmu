using System.Diagnostics;

namespace NesEmu.Mappers;

internal sealed class Mapper1 : Mapper
{
	private enum ChrRomBankMode
	{
		Switch8k = 0,
		Switch4k = 1
	}

	private enum PrgRomBankMode
	{
		Switch32k = 0,
		Switch32kAlt = 1,
		FixFirstSwitchLast = 2,
		FixLastSwitchFirst = 3,
	}

	private readonly int _prgRomBanks;
	private readonly int _chrRomBanks;

	private readonly byte[] _prgRom;
	private readonly byte[] _chrRom;
	private readonly byte[] _prgRam;

	private int _prgBank0 = 0;
	private int _prgBank1 = 1;

	private int _chrBank0 = 0;
	private int _chrBank1 = 1;

	private ChrRomBankMode _chrRomBankMode = ChrRomBankMode.Switch4k;
	private PrgRomBankMode _prgRomBankMode = PrgRomBankMode.Switch32k;

	private byte _shiftRegisterValue = 0;
	private byte _shiftRegisterCount = 0;

	private readonly bool _chrRam = false;

	public Mapper1(int prgRomBanks, int chrRomBanks, MirroringMode mirroringMode, Stream data)
	{
		if (chrRomBanks == 0)
		{
			chrRomBanks = 1;
			_chrRam = true;
		}

		_prgRomBanks = prgRomBanks; // Number of 16k banks
		_chrRomBanks = chrRomBanks; // Number of 8k banks
		_prgRam = new byte[0x2000];

		var prgRomSize = prgRomBanks * 0x4000; // 16384
		var chrRomSize = chrRomBanks * 0x2000; // 8192

		if (prgRomBanks is not >= 1)
			throw new NotImplementedException();

		_prgRom = new byte[prgRomSize];
		_chrRom = new byte[chrRomSize];

		_prgBank1 = prgRomBanks - 1;

		data.ReadExactly(_prgRom);
		if (!_chrRam)
			data.ReadExactly(_chrRom);

		SetMirroringMode(mirroringMode);
	}

	public override byte CpuReadByte(ushort address) => address switch
	{
		>= 0x6000 and <= 0x7FFF => _prgRam[address - 0x6000],
		>= 0x8000 and <= 0xBFFF => _prgRom[address - 0x8000 + (_prgBank0 * 0x4000)],
		>= 0xC000 and <= 0xFFFF => _prgRom[address - 0xC000 + (_prgBank1 * 0x4000)],
		_ => 0xFF
	};

	public override void CpuWriteByte(ushort address, byte value)
	{
		if (address < 0x8000)
			return;

		if (((value >> 7) & 1) != 0) // Reset shift register
		{
			_shiftRegisterCount = 0;
			_shiftRegisterValue = 0;
			return;
		}

		_shiftRegisterValue |= (byte)((value & 1) << _shiftRegisterCount);
		_shiftRegisterCount++;

		if (_shiftRegisterCount != 5)
			return;

		switch (address)
		{
			case >= 0x6000 and <= 0x7FFF: // PRGRAM
				_prgRam[address - 0x6000] = value;
				break;
			case >= 0x8000 and <= 0x9FFF: // Control register
				{
					var mirroringMode = (_shiftRegisterValue & 0b11) switch
					{
						0 => MirroringMode.OneScreenLowerBank,
						1 => MirroringMode.OneScreenUpperBank,
						2 => MirroringMode.Vertical,
						3 => MirroringMode.Horizontal,
						_ => throw new UnreachableException()
					};

					_prgRomBankMode = (PrgRomBankMode)((_shiftRegisterValue >> 2) & 0b11);
					_chrRomBankMode = (ChrRomBankMode)((_shiftRegisterValue >> 4) & 1);

					SetMirroringMode(mirroringMode);
					break;
				}
			case >= 0xA000 and <= 0xBFFF: // CHR bank 0
				if (_chrRomBankMode == ChrRomBankMode.Switch8k)
				{
					_chrBank0 = _shiftRegisterValue & 0b11110;
					_chrBank1 = _chrBank0 + 1;
					_chrBank0 %= _chrRomBanks * 2;
					_chrBank1 %= _chrRomBanks * 2;
					break;
				}
				_chrBank0 = _shiftRegisterValue;
				_chrBank0 %= _chrRomBanks * 2;
				break;
			case >= 0xC000 and <= 0xDFFF: // CHR bank 1
				_chrBank1 = (_shiftRegisterValue & 0b11111);
				_chrBank1 %= _chrRomBanks * 2;
				break;
			case >= 0xE000 and <= 0xFFFF: // PRG bank
				switch (_prgRomBankMode)
				{
					case PrgRomBankMode.FixLastSwitchFirst:
						_prgBank0 = (_shiftRegisterValue & 0b1111) % _prgRomBanks;
						break;
					case PrgRomBankMode.FixFirstSwitchLast:
						_prgBank1 = (_shiftRegisterValue & 0b1111) % _prgRomBanks;
						break;
					default:
						Console.WriteLine($"TODO: Mapper1: PRG=0x{_shiftRegisterValue:X2},MODE:{_prgRomBankMode}");
						break;
				}
				break;
		}

		_shiftRegisterCount = 0;
		_shiftRegisterValue = 0;
	}

	public override byte PpuReadByte(Ppu ppu, ushort address)
	{
		if (address is >= 0x3000 and < 0x3F00)
			address -= 0x1000;

		return address switch
		{
			>= 0x0000 and <= 0x0FFF => _chrRom[address + (_chrBank0 * 0x1000)],
			>= 0x1000 and <= 0x1FFF => _chrRom[address - 0x1000 + (_chrBank1 * 0x1000)],
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
			case >= 0x0000 and <= 0x0FFF when _chrRam: _chrRom[address + (_chrBank0 * 0x1000)] = value; break;
			case >= 0x1000 and <= 0x1FFF when _chrRam: _chrRom[address - 0x1000 + (_chrBank1 * 0x1000)] = value; break;
			case >= PpuBus.Nametable0Address and < PpuBus.Nametable1Address: ppu.Vram[address - PpuBus.Nametable0Address + Nametable0Offset] = value; break;
			case >= PpuBus.Nametable1Address and < PpuBus.Nametable2Address: ppu.Vram[address - PpuBus.Nametable1Address + Nametable1Offset] = value; break;
			case >= PpuBus.Nametable2Address and < PpuBus.Nametable3Address: ppu.Vram[address - PpuBus.Nametable2Address + Nametable2Offset] = value; break;
			case >= PpuBus.Nametable3Address and < 0x3000: ppu.Vram[address - PpuBus.Nametable3Address + Nametable3Offset] = value; break;
		}
	}
}
