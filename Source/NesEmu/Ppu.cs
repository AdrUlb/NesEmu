using System.Diagnostics;
using System.Drawing;
using System.Net;
using static System.Net.Mime.MediaTypeNames;

namespace NesEmu;

internal sealed class Ppu
{
	public const int ScreenWidth = 256;
	public const int ScreenHeight = 240;

	private readonly PpuBus _bus;
	private readonly CpuBus _cpuBus;

	private int _scanline;
	private int _cycles;
	private ushort _regAddr = 0;
	private byte _regData = 0;
	private byte _oamAddr;

	private bool _ctrlEnableVblankNmi = false;
	private bool _ctrlMasterSlaveSelect = false;
	private bool _ctrlSpriteSize = false;
	private bool _ctrlBackgroundPatternTable = false;
	private bool _ctrlSpritePatternTable = false;
	private bool _ctrlAddrIncrMode = false;
	private byte _ctrlBaseNametableAddr;

	private bool _statusVblank = false;

	private readonly Color[] _palette = new Color[64];
	private readonly byte[] _oam = new byte[256];

	public readonly Color[] Framebuffer = new Color[ScreenWidth * ScreenHeight];


	public bool RequestVblankInterrupt { get; set; } = false;

	private byte RegCtrl
	{
		get => (byte)
		(
			((_ctrlEnableVblankNmi ? 1 : 0) << 7) |
			((_ctrlMasterSlaveSelect ? 1 : 0) << 6) |
			((_ctrlSpriteSize ? 1 : 0) << 5) |
			((_ctrlBackgroundPatternTable ? 1 : 0) << 4) |
			((_ctrlSpritePatternTable ? 1 : 0) << 3) |
			((_ctrlAddrIncrMode ? 1 : 0) << 2) |
			(_ctrlBaseNametableAddr & 0b11)
		);

		set
		{
			_ctrlEnableVblankNmi = ((value >> 7) & 1) != 0;
			_ctrlMasterSlaveSelect = ((value >> 6) & 1) != 0;
			_ctrlSpriteSize = ((value >> 7) & 5) != 0;
			_ctrlBackgroundPatternTable = ((value >> 4) & 1) != 0;
			_ctrlSpritePatternTable = ((value >> 3) & 1) != 0;
			_ctrlAddrIncrMode = ((value >> 2) & 1) != 0;
			_ctrlBaseNametableAddr = (byte)(value & 0b11);
		}
	}

	private byte RegStatus
	{
		get => (byte)
		(
			((_statusVblank ? 1 : 0) << 7)
		);
	}

	public Ppu(PpuBus bus, CpuBus cpuBus)
	{
		_bus = bus;
		_cpuBus = cpuBus;
		using (var fs = File.OpenRead("palette.pal"))
		{
			Span<byte> pal = stackalloc byte[64 * 3];
			fs.ReadExactly(pal);
			for (var i = 0; i < 64; i++)
			{
				var ii = i * 3;
				_palette[i] = Color.FromArgb(pal[ii + 0], pal[ii + 1], pal[ii + 2]);
			}
		}
	}

	public byte ReadReg(int num)
	{
		switch (num)
		{
			case 0x2000: // PPUCTRL
				return RegCtrl;
			case 0x2002: // PPUSTATUS
				return RegStatus;
			case 0x2004: // OAMDATA
				return _oam[_oamAddr];
			case 0x2007: // PPUDATA
				{
					if (_regAddr is >= 0x3F00 and < 0x4000) // no dummy read required for palette ram
						return _regData = _bus.ReadByte(_regAddr++);

					var ret = _regData;
					_regData = _bus.ReadByte(_regAddr++);
					return ret;
				}

			case 0x2001:
			case 0x2003:
			case 0x2005:
			case 0x2006:
			case 0x4014:
			default:
				return 0;
		}
	}

	public void WriteReg(int num, byte value)
	{
		switch (num)
		{
			case 0x2000: // PPUCTRL
				RegCtrl = value;
				break;
			case 0x2003: // OAMADDR
				_oamAddr = value;
				break;
			case 0x2004: // OAMDATA
				_oam[_oamAddr++] = value;
				break;
			case 0x2006: // PPUADDR
				_regAddr = (ushort)((_regAddr << 8) | value);
				break;
			case 0x2007: // PPUDATA
				_regData = value;
				_bus.WriteByte(_regAddr++, value);
				break;
			case 0x4014: // OAMDMA
				for (var i = 0; i < _oam.Length; i++)
					_oam[i] = _cpuBus.ReadByte((ushort)((value << 8) + ((i + _oamAddr) & 0xFF)));
				break;
		}
	}

	public void Tick()
	{
		_cycles++;

		if (_cycles == 341)
		{
			_scanline++;
			_cycles = 0;
			if (_scanline == 241)
			{
				var nametableAddress = _ctrlBaseNametableAddr switch
				{
					0 => 0x2000,
					1 => 0x2400,
					2 => 0x2800,
					3 => 0x2C00,
					_ => throw new UnreachableException()
				};

				var backgroundPatternTable = _ctrlBackgroundPatternTable ? 0x1000 : 0x0000;
				var spritePatternTable = _ctrlSpritePatternTable ? 0x1000 : 0x0000;

				for (var tileY = 0; tileY < 30; tileY++)
				{
					for (var tileX = 0; tileX < 32; tileX++)
					{
						var i = tileX + (tileY * 32);

						var tileId = _bus.ReadByte((ushort)(nametableAddress + i));
						var tileOff = (tileId * 16) + backgroundPatternTable;

						var attribIndex = (tileY / 4 * 8) + (tileX / 4);
						var attrib = _bus.ReadByte((ushort)(nametableAddress + 0x03C0 + attribIndex));

						var paletteIndex = (tileX % 4 / 2, tileY % 4 / 2) switch
						{
							(0, 0) => attrib & 0b11,
							(1, 0) => (attrib >> 2) & 0b11,
							(0, 1) => (attrib >> 4) & 0b11,
							(1, 1) => (attrib >> 6) & 0b11,
							_ => throw new UnreachableException()
						};

						var paletteOffset = 0x3F01 + (paletteIndex * 4);

						for (var y = 0; y < 8; y++)
						{
							var msb = _bus.ReadByte((ushort)(tileOff + y + 8));
							var lsb = _bus.ReadByte((ushort)(tileOff + y));
							for (var x = 0; x < 8; x++)
							{
								var colorIndex = ((msb >> 7) << 1) | (lsb >> 7);
								msb <<= 1;
								lsb <<= 1;
								var color = colorIndex switch
								{
									0 => _palette[_bus.ReadByte(0x3F00)],
									1 => _palette[_bus.ReadByte((ushort)(paletteOffset + 0))],
									2 => _palette[_bus.ReadByte((ushort)(paletteOffset + 1))],
									3 => _palette[_bus.ReadByte((ushort)(paletteOffset + 2))],
									_ => throw new UnreachableException()
								};
								var xx = (tileX * 8) + x;
								var yy = (tileY * 8) + y;
								Framebuffer[xx + (yy * 256)] = color;
							}
						}
					}
				}

				for (var i = 0; i < 64; i++)
				{
					var off = i * 4;
					var yPos = _oam[off + 0];
					var tileId = _oam[off + 1];
					var attrib = _oam[off + 2];
					var xPos = _oam[off + 3];

					var paletteIndex = attrib & 0b11;
					var tileOff = (tileId * 16) + spritePatternTable;

					var paletteOffset = 0x3F11 + (paletteIndex * 4);

					var flipX = (attrib & (1 << 6)) != 0;
					var flipY = (attrib & (1 << 7)) != 0;

					for (var y = 0; y < 8; y++)
					{
						var msb = _bus.ReadByte((ushort)(tileOff + y + 8));
						var lsb = _bus.ReadByte((ushort)(tileOff + y));
						for (var x = 0; x < 8; x++)
						{
							var colorIndex = ((msb >> 7) << 1) | (lsb >> 7);
							msb <<= 1;
							lsb <<= 1;
							var color = colorIndex switch
							{
								0 => Color.Transparent,
								1 => _palette[_bus.ReadByte((ushort)(paletteOffset + 0))],
								2 => _palette[_bus.ReadByte((ushort)(paletteOffset + 1))],
								3 => _palette[_bus.ReadByte((ushort)(paletteOffset + 2))],
								_ => throw new UnreachableException()
							};
							if (color == Color.Transparent)
								continue;
							var xx = xPos + (flipX ? 7 - x : x);
							var yy = yPos + (flipY ? 7 - y : y);
							if (xx < 0 || yy < 0 || xx >= ScreenWidth || yy >= ScreenHeight)
								continue;
							Framebuffer[xx + (yy * 256)] = color;
						}
					}
				}

				_statusVblank = true;
				if (_ctrlEnableVblankNmi)
					RequestVblankInterrupt = true;
			}
			else if (_scanline == 262)
			{
				_scanline = 0;
				_statusVblank = false;
			}
		}
	}
}
