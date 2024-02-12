using NesEmu.Mappers;

namespace NesEmu;

internal sealed class Cartridge
{
	private readonly Mapper _mapper;

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
		var mirroringMode = ((flags6 & 1) == 0) ? MirroringMode.Horizontal : MirroringMode.Vertical;
		var mapperNumber = flags6 >> 4;

		Console.WriteLine($"Mapper: {mapperNumber}");
		Console.WriteLine($"Mirroring: {mirroringMode}");

		_mapper = mapperNumber switch
		{
			0 => new Mapper0(prgRomBanks, chrRomBanks, mirroringMode, data),
			1 => new Mapper1(prgRomBanks, chrRomBanks, mirroringMode, data),
			_ => throw new NotImplementedException($"Mapper {mapperNumber} not implemented.")
		};
	}

	public byte CpuReadByte(ushort address) => _mapper.CpuReadByte(address);

	public void CpuWriteByte(ushort address, byte value) => _mapper.CpuWriteByte(address, value);

	public byte PpuReadByte(Ppu ppu, ushort address) => _mapper.PpuReadByte(ppu, address);

	public void PpuWriteByte(Ppu ppu, ushort address, byte value) => _mapper.PpuWriteByte(ppu, address, value);
}
