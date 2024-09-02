namespace NesEmu.Mappers;

internal sealed class Mapper4 : Mapper
{
	private const int PrgBankSize = 0x2000;
	private const int ChrBankSize = 0x0400;

	private readonly byte[] _prgRom;
	private readonly byte[] _chrRom;
	private readonly byte[] _prgRam;

	private readonly bool _chrRam = false;

	private int[] _banks = new int[8];
	private readonly int _prgLast;
	private readonly int _prgSecondLast;

	private readonly int _prgRomBanks;
	private readonly int _chrRomBanks;

	private int _bankSelect;
	private bool _prgRomBankMode;
	private bool _chrInvert;

	private byte _irqLatch;
	private int _irqCounter;

	private bool _irqEnabled = true;

	public Mapper4(int prgRomBanks, int chrRomBanks, MirroringMode mirroringMode, Stream data)
	{
		if (chrRomBanks == 0)
		{
			chrRomBanks = 2;
			_chrRam = true;
		}

		_prgRam = new byte[0x2000];

		var prgRomSize = prgRomBanks * 0x4000; // 16384
		var chrRomSize = chrRomBanks * 0x2000; // 8192

		_prgRomBanks = prgRomSize / PrgBankSize;
		_chrRomBanks = chrRomSize / ChrBankSize;

		_prgLast = _prgRomBanks - 1;
		_prgSecondLast = _prgRomBanks - 2;

		_banks[0] = 0;
		_banks[1] = 2 % _chrRomBanks;
		_banks[2] = 4 % _chrRomBanks;
		_banks[3] = 5 % _chrRomBanks;
		_banks[4] = 6 % _chrRomBanks;
		_banks[5] = 7 % _chrRomBanks;

		_banks[6] = _prgSecondLast;

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

		>= 0xA000 and <= 0xBFFF => _prgRom[address - 0xA000 + (_banks[7] * PrgBankSize)],

		>= 0x8000 and <= 0x9FFF when !_prgRomBankMode => _prgRom[address - 0x8000 + (_banks[6] * PrgBankSize)],
		>= 0xC000 and <= 0xDFFF when !_prgRomBankMode => _prgRom[address - 0xC000 + (_prgSecondLast * PrgBankSize)],

		>= 0x8000 and <= 0x9FFF when _prgRomBankMode => _prgRom[address - 0x8000 + (_prgSecondLast * PrgBankSize)],
		>= 0xC000 and <= 0xDFFF when _prgRomBankMode => _prgRom[address - 0xC000 + (_banks[6] * PrgBankSize)],

		>= 0xE000 => _prgRom[address - 0xE000 + (_prgLast * PrgBankSize)],

		_ => 0xFF
	};

	public override void CpuWriteByte(ushort address, byte value)
	{
		switch (address)
		{
			case >= 0x6000 and <= 0x7FFF:
				_prgRam[address - 0x6000] = value;
				break;
			case >= 0x8000 and <= 0x9FFF:
				if (address % 2 == 0)
				{
					_bankSelect = value & 0b111;
					_prgRomBankMode = ((value >> 6) & 1) != 0;
					_chrInvert = ((value >> 7) & 1) != 0;
				}
				else
				{
					if (_bankSelect is 0 or 1)
						value &= 0b11111110;

					if (_bankSelect is 6 or 7)
					{
						value = (byte)(value % _prgRomBanks);
						value = (byte)(value & 0b0011_1111);
					}
					else
					{
						value = (byte)(value % _chrRomBanks);
					}

					_banks[_bankSelect] = value;
				}
				break;
			case >= 0xA000 and <= 0xBFFE:
				if (address % 2 == 0)
				{
					SetMirroringMode((value & 1) == 0 ? MirroringMode.Vertical : MirroringMode.Horizontal);
				}
				break;
			case >= 0xC000 and <= 0xDFFF:
				if (address % 2 == 0)
				{
					_irqLatch = value;
				}
				else
				{
					_irqCounter = 0;
				}
				break;
			case >= 0xE000:
				if (address % 2 == 0)
				{
					_irqEnabled = false;
					RequestInterrupt = false;
				}
				else
				{
					_irqEnabled = true;
				}
				break;
		}
	}

	public override byte PpuReadByte(Ppu ppu, ushort address)
	{
		switch (address)
		{
			case <= 0x1FFF when _chrInvert:
				address ^= 0x1000;
				break;
			case >= 0x3000 and < 0x3F00:
				address -= 0x1000;
				break;
		}

		return address switch
		{
			<= 0x03FF => _chrRom[address - 0x0000 + (_banks[0] * ChrBankSize)],
			<= 0x07FF => _chrRom[address - 0x0400 + (_banks[0] * ChrBankSize) + ChrBankSize],
			<= 0x0BFF => _chrRom[address - 0x0800 + (_banks[1] * ChrBankSize)],
			<= 0x0FFF => _chrRom[address - 0x0C00 + (_banks[1] * ChrBankSize) + ChrBankSize],

			<= 0x13FF => _chrRom[address - 0x1000 + (_banks[2] * ChrBankSize)],
			<= 0x17FF => _chrRom[address - 0x1400 + (_banks[3] * ChrBankSize)],
			<= 0x1BFF => _chrRom[address - 0x1800 + (_banks[4] * ChrBankSize)],
			<= 0x1FFF => _chrRom[address - 0x1C00 + (_banks[5] * ChrBankSize)],

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

		if (address <= 0x1FFF && _chrInvert)
			address ^= 0x1000;

		switch (address)
		{
			case <= 0x03FF:
				_chrRom[address - 0x0000 + (_banks[0] * ChrBankSize)] = value;
				break;
			case <= 0x07FF:
				_chrRom[address - 0x0400 + (_banks[0] * ChrBankSize) + ChrBankSize] = value;
				break;
			case <= 0x0BFF:
				_chrRom[address - 0x0800 + (_banks[1] * ChrBankSize)] = value;
				break;
			case <= 0x0FFF:
				_chrRom[address - 0x0C00 + (_banks[1] * ChrBankSize) + ChrBankSize] = value;
				break;

			case <= 0x13FF:
				_chrRom[address - 0x1000 + (_banks[2] * ChrBankSize)] = value;
				break;
			case <= 0x17FF:
				_chrRom[address - 0x1400 + (_banks[3] * ChrBankSize)] = value;
				break;
			case <= 0x1BFF:
				_chrRom[address - 0x1800 + (_banks[4] * ChrBankSize)] = value;
				break;
			case <= 0x1FFF:
				_chrRom[address - 0x1C00 + (_banks[5] * ChrBankSize)] = value;
				break;

			case < PpuBus.Nametable1Address:
				ppu.Vram[address - PpuBus.Nametable0Address + Nametable0Offset] = value;
				break;
			case < PpuBus.Nametable2Address:
				ppu.Vram[address - PpuBus.Nametable1Address + Nametable1Offset] = value;
				break;
			case < PpuBus.Nametable3Address:
				ppu.Vram[address - PpuBus.Nametable2Address + Nametable2Offset] = value;
				break;
			case < 0x3000:
				ppu.Vram[address - PpuBus.Nametable3Address + Nametable3Offset] = value;
				break;
		}
	}

	public override void TickScanline()
	{
		if (_irqCounter == 0)
		{
			_irqCounter = _irqLatch;
		}
		else
			_irqCounter--;

		if (_irqCounter == 0 && _irqEnabled)
			RequestInterrupt = true;
	}
}
