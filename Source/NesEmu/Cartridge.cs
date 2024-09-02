using NesEmu.Mappers;

namespace NesEmu;

internal sealed class Cartridge
{
	private readonly Mapper _mapper;

	public bool AcknowledgeInterrupt() => _mapper.AcknowledgeInterrupt();

	public Cartridge(Ppu ppu, Stream data)
	{
		Span<byte> signature = [(byte)'N', (byte)'E', (byte)'S', 0x1A];

		Span<byte> header = stackalloc byte[16];
		data.ReadExactly(header);

		if (!header[..4].SequenceEqual(signature))
			throw new("Not a valid iNES file.");

		var prgRomBanks = header[4];
		var chrRomBanks = header[5];

		var flags6 = header[6];
		var flags7 = header[7];
		var flags8 = header[8];
		var flags9 = header[9];
		var flags10 = header[10];

		var mirroringMode = ((flags6 & 1) == 0) ? MirroringMode.Horizontal : MirroringMode.Vertical;
		var hasBattery = ((flags6 >> 1) & 1) == 1; // TODO: use
		var hasTrainer = ((flags6 >> 2) & 1) == 1;
		var hasAltNametables = ((flags6 >> 3) & 1) == 1;
		var mapperNumber = flags6 >> 4;

		if (hasTrainer)
			throw new NotImplementedException("Trainer.");

		var consoleType = flags7 & 0b11;
		var isNes2 = ((flags7 >> 2) & 0b11) == 0b10;
		mapperNumber |= flags7 & 0xF0;

		if (consoleType != 0)
			throw new NotImplementedException("Only NES/Famicom ROMs are supported.");

		// By default assume there is one PRG RAM bank
		var prgRamBanks = 1;

		if (isNes2) // NES 2.0
		{
			// TODO: mapper high byte and submapper
			// TODO: PRG ROM size MSB
			// TODO: CHR ROM size MSB
			var prgNvramShifts = flags10 >> 4;
			var prgRamShifts = flags10 & 0x0F;

			var prgRamSize = 0;

			if (hasBattery)
			{
				if (prgNvramShifts == 0)
					throw new NotSupportedException("ROM has battery but no NVRAM??");

				if (prgRamShifts != 0)
					throw new NotSupportedException("ROM has battery but volatile RAM??");

				prgRamSize = 64 << prgNvramShifts;
			}

			if (!hasBattery)
			{
				if (prgNvramShifts != 0)
					throw new NotSupportedException("ROM has no battery but has NVRAM??");

				if (prgRamShifts != 0)
					prgRamSize = 64 << prgRamShifts;
			}

			if ((prgRamSize % 0x2000) != 0)
				throw new NotSupportedException($"Only whole PRG RAM banks (sizes that are multiples of 0x2000) are supported (requested 0x{prgRamSize:X4}).");

			prgRamBanks = prgRamSize / 0x2000;
		}
		else // iNES
		{
			// Detect older ROM dumps
			if (header[^1] != 0 || header[^2] != 0 || header[^3] != 0 || header[^4] != 0)
			{
				// Ignore high mapper nibble, fixing older ROM files where there's garbage in bytes 7-15
				mapperNumber &= 0b1111;
			}
			else // This is a newer ROM, "extended iNES"
			{
				if (flags8 != 0) // If flags8 is not 0 assume it is valid
					prgRamBanks = flags8;
			}
		}

		Console.WriteLine($"Format: {(isNes2 ? "NES 2.0" : "iNES")}");
		Console.WriteLine($"Mapper: {mapperNumber}");
		Console.WriteLine($"Mirroring: {mirroringMode}");
		Console.WriteLine($"PRG: {prgRomBanks}*16K");
		Console.WriteLine($"CHR: {chrRomBanks}*8K");
		Console.WriteLine($"PRG RAM: {prgRamBanks}*8K");
		Console.WriteLine($"Battery: {(hasBattery ? "yes" : "no")}");

		_mapper = mapperNumber switch
		{
			0 => new Mapper0(prgRomBanks, chrRomBanks, mirroringMode, data),
			1 => new Mapper1(prgRomBanks, chrRomBanks, prgRamBanks, mirroringMode, hasBattery, data),
			2 => new Mapper2(prgRomBanks, chrRomBanks, mirroringMode, data), // TODO: PRG RAM? battery?
			4 => new Mapper4(prgRomBanks, chrRomBanks, mirroringMode, data), // TODO: PRG RAM? battery?
			_ => throw new NotImplementedException($"Mapper {mapperNumber} not implemented.")
		};
	}

	public byte CpuReadByte(ushort address) => _mapper.CpuReadByte(address);

	public void CpuWriteByte(ushort address, byte value) => _mapper.CpuWriteByte(address, value);

	public byte PpuReadByte(Ppu ppu, ushort address) => _mapper.PpuReadByte(ppu, address);

	public void PpuWriteByte(Ppu ppu, ushort address, byte value) => _mapper.PpuWriteByte(ppu, address, value);

	public void TickScanline() => _mapper.TickScanline();
}
