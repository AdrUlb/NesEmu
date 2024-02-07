using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;

namespace NesEmu;

internal sealed class Ppu
{
	public const int ScreenWidth = 256;
	public const int ScreenHeight = 240;

	public readonly PpuBus Bus;
	private readonly CpuBus _cpuBus;

	private int _scanline;
	private int _point;
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

	public readonly byte[] Vram = new byte[0x800];
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

	public Ppu(CpuBus cpuBus)
	{
		Bus = new(this);
		_cpuBus = cpuBus;

		using var fs = File.OpenRead("palette.pal");

		Span<byte> pal = stackalloc byte[64 * 3];
		fs.ReadExactly(pal);
		for (var i = 0; i < 64; i++)
		{
			var ii = i * 3;
			_palette[i] = Color.FromArgb(pal[ii + 0], pal[ii + 1], pal[ii + 2]);
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
						return _regData = Bus.ReadByte(_regAddr++);

					var ret = _regData;
					_regData = Bus.ReadByte(_regAddr++);
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
				Bus.WriteByte(_regAddr++, value);
				break;
			case 0x4014: // OAMDMA
				for (var i = 0; i < _oam.Length; i++)
					_oam[i] = _cpuBus.ReadByte((ushort)((value << 8) + ((i + _oamAddr) & 0xFF)));
				break;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Ticks(int count)
	{
		for (var i = 0; i < count; i++)
			Tick();
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public void Tick()
	{
		_point++;

		if (_point < ScreenWidth && _scanline < ScreenHeight)
		{
			DrawPoint();
			return;
		}

		if (_point == 341)
		{
			_scanline++;
			_point = 0;
			if (_scanline == 241)
			{
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

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DrawPoint()
	{
		var nametableAddress = _ctrlBaseNametableAddr switch
		{
			0 => PpuBus.Nametable0Address,
			1 => PpuBus.Nametable1Address,
			2 => PpuBus.Nametable2Address,
			3 => PpuBus.Nametable3Address,
			_ => throw new UnreachableException()
		};

		var spritePatternTable = _ctrlSpritePatternTable ? PpuBus.PatternTable1Address : PpuBus.PatternTable0Address;

		Color color;

		{
			for (var i = 0; i < 64; i++)
			{
				var off = i * 4;
				var yPos = _oam[off + 0];

				var y = _scanline - yPos;

				if (y is < 0 or >= 8)
					continue;

				var tileId = _oam[off + 1];
				var attrib = _oam[off + 2];
				var xPos = _oam[off + 3];

				var x = _point - xPos;

				if (x is < 0 or >= 8)
					continue;

				var paletteIndex = attrib & 0b11;
				var tileOff = (tileId * 16) + spritePatternTable;

				var paletteOffset = PpuBus.PaletteRamAddress + 0x11 + (paletteIndex * 4);

				var flipX = (attrib & (1 << 6)) != 0;
				var flipY = (attrib & (1 << 7)) != 0;

				var flippedX = flipX ? x : 7 - x;
				var flippedY = flipY ? 7 - y : y;

				var msb = Bus.ReadByte((ushort)(tileOff + flippedY + 8));
				var lsb = Bus.ReadByte((ushort)(tileOff + flippedY));

				var colorIndex = (((msb >> flippedX) & 1) << 1) | ((lsb >> flippedX) & 1);
				color = colorIndex switch
				{
					0 => Color.Transparent,
					1 => _palette[Bus.ReadByte((ushort)(paletteOffset + 0))],
					2 => _palette[Bus.ReadByte((ushort)(paletteOffset + 1))],
					3 => _palette[Bus.ReadByte((ushort)(paletteOffset + 2))],
					_ => throw new UnreachableException()
				};

				if (color == Color.Transparent)
					continue;

				goto end;
			}
		}

		var bgPatternTable = _ctrlBackgroundPatternTable ? PpuBus.PatternTable1Address : PpuBus.PatternTable0Address;
		var bgTileColumn = _point >> 3;
		var bgTileRow = _scanline >> 3;
		var bgTileX = _point & 0b111;
		var bgTileY = _scanline & 0b111;
		var bgTileIndex = bgTileColumn + (bgTileRow * 32);
		var bgTileId = Bus.ReadByte((ushort)(nametableAddress + bgTileIndex));
		var bgPatternOffset = (bgTileId * 16) + bgPatternTable;

		{
			var attribIndex = (bgTileRow / 4 * 8) + (bgTileColumn >> 2);
			var attrib = Bus.ReadByte((ushort)(nametableAddress + 0x03C0 + attribIndex));

			var paletteIndex = (bgTileColumn % 4 / 2, bgTileRow % 4 / 2) switch
			{
				(0, 0) => attrib & 0b11,
				(1, 0) => (attrib >> 2) & 0b11,
				(0, 1) => (attrib >> 4) & 0b11,
				(1, 1) => (attrib >> 6) & 0b11,
				_ => throw new UnreachableException()
			};

			var paletteOffset = PpuBus.PaletteRamAddress + 1 + (paletteIndex * 4);

			var msb = Bus.ReadByte((ushort)(bgPatternOffset + bgTileY + 8));
			var lsb = Bus.ReadByte((ushort)(bgPatternOffset + bgTileY));

			var colorIndex = (((msb >> (7 - bgTileX)) & 1) << 1) | ((lsb >> (7 - bgTileX)) & 1);
			color = colorIndex switch
			{
				0 => _palette[Bus.ReadByte(PpuBus.PaletteRamAddress)],
				1 => _palette[Bus.ReadByte((ushort)(paletteOffset + 0))],
				2 => _palette[Bus.ReadByte((ushort)(paletteOffset + 1))],
				3 => _palette[Bus.ReadByte((ushort)(paletteOffset + 2))],
				_ => throw new UnreachableException()
			};
		}

	end:
		Framebuffer[_point + (_scanline * ScreenWidth)] = color;
	}
}
