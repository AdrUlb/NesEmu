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

		var prgRomSize = header[4] * 0x4000;
		var chrRomSize = header[5] * 0x2000;

		var flags6 = header[6];
		var mirroringMode = ((flags6 & 1) == 0) ? MirroringMode.Vertical : MirroringMode.Horizontal;
		var mapperNumber = flags6 >> 4;
		
		_mapper = mapperNumber switch
		{
			0 => new Mapper0(prgRomSize, chrRomSize, mirroringMode, data),
			_ => throw new NotImplementedException()
		};
	}

	public byte CpuReadByte(ushort address) => _mapper.CpuReadByte(address);

	public void CpuWriteByte(ushort address, byte value) => _mapper.CpuWriteByte(address, value);

	public byte PpuReadByte(Ppu ppu, ushort address) => _mapper.PpuReadByte(ppu, address);

	public void PpuWriteByte(Ppu ppu, ushort address, byte value) => _mapper.PpuWriteByte(ppu, address, value);
}
