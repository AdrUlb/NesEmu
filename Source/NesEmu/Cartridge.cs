namespace NesEmu;

internal sealed class Cartridge
{
	private readonly byte[] _rom = new byte[0x4000];

	public Cartridge(Stream data)
	{
		Span<byte> signature = [(byte)'N', (byte)'E', (byte)'S', 0x1A];

		Span<byte> header = stackalloc byte[16];
		data.ReadExactly(header);

		if (!header[..4].SequenceEqual(signature))
			throw new("Not a valid iNES file.");

		data.ReadExactly(_rom);
	}

	public byte ReadByte(ushort address) => address switch
	{
		>= 0x8000 and < 0xC000 => _rom[address - 0x8000],
		>= 0xC000 => _rom[address - 0xC000],
		_ => 0xFF,
	};

	public void WriteByte(ushort address, byte value)
	{
		
	}
}
