using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;

namespace NesEmu;

internal sealed class Ppu
{
	public const int ScreenWidth = 256;
	public const int ScreenHeight = 240;

	private const int _tileColumns = 32;
	private const int _cyclesPerScanline = 341;

	public readonly PpuBus Bus;
	private readonly CpuBus _cpuBus;

	private int _scanline = 261;
	private int _cycle = 0;
	private ushort _regAddr = 0;
	private byte _regData = 0;
	private byte _oamAddr;

	private int _step = 0;

	private bool _ctrlEnableVblankNmi = false;
	private bool _ctrlMasterSlaveSelect = false;
	private bool _ctrlSpriteSize = false;
	private bool _ctrlBackgroundPatternTable = false;
	private bool _ctrlSpritePatternTable = false;
	private bool _ctrlAddrIncrMode = false;
	private byte _ctrlBaseNametableAddr;

	private byte _bgTileColumn;
	private byte _bgTileRow;

	private int _nextPixelIndex = 0;

	private readonly Color[] _palette = new Color[64];
	private readonly byte[] _oam = new byte[256];

	public readonly byte[] Vram = new byte[0x800];
	public readonly Color[] Framebuffer = new Color[ScreenWidth * ScreenHeight];

	public bool StatusVblank { get; private set; } = false;

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
			((StatusVblank ? 1 : 0) << 7)
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
					byte ret;
					if (_regAddr is >= 0x3F00 and < 0x4000) // Palette
					{
						ret = _regData = Bus.ReadByte(_regAddr);
						_regData = Bus.ReadByte((ushort)(_regAddr - 0x1000));
					}
					else // VRAM
					{
						ret = _regData;
						_regData = Bus.ReadByte(_regAddr);
					}

					if (_ctrlAddrIncrMode)
					{
						_regAddr += 32;
					}
					else
						_regAddr++;

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
				Bus.WriteByte(_regAddr, value);
				if (_ctrlAddrIncrMode)
				{
					_regAddr += 32;
				}
				else
					_regAddr++;
				break;
			case 0x4014: // OAMDMA
				var address = (ushort)(value << 8);
				for (var i = 0; i < _oam.Length; i++)
				{
					_oam[(_oamAddr + i) & 0xFF] = _cpuBus.ReadByte(address);
					address++;
					if ((address & 0xFF) == 0)
						address -= 0x0100;
				}
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
		if (_cycle is >= 1 and <= 256)
		{
			_step++;

			var screenX = _cycle - 1;
			if (screenX is >= 0 and < ScreenWidth && _scanline < ScreenHeight)
				DrawPoint();
		}

		if (_step == 8)
		{
			_bgTileColumn++;
			if (_bgTileColumn >= _tileColumns)
				_bgTileColumn = 0;

			_step = 0;
		}

		_cycle++;
		if (_cycle >= _cyclesPerScanline)
		{
			_scanline++;
			if (_scanline % 8 == 0)
				_bgTileRow++;
			_cycle = 0;
			switch (_scanline)
			{
				case 241: // End of visible scanlines
					StatusVblank = true;
					if (_ctrlEnableVblankNmi)
						RequestVblankInterrupt = true;
					break;
				case 261: // Pre-render scanline
					break;
				case 262: // Wrap back to the start in the end
					_nextPixelIndex = 0;
					_scanline = 0;
					_bgTileRow = 0;
					StatusVblank = false;
					break;
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DrawPoint()
	{
		var spritePatternTable = _ctrlSpritePatternTable ? PpuBus.PatternTable1Address : PpuBus.PatternTable0Address;

		var screenX = _cycle - 1;

		Color color;

		{
			for (var i = 0; i < 64; i++)
			{
				var off = i * 4;
				var yPos = _oam[off + 0] + 1;

				var y = _scanline - yPos;

				if (y is < 0 or >= 8)
					continue;

				var tileId = _oam[off + 1];
				var attrib = _oam[off + 2];
				var xPos = _oam[off + 3];

				var x = screenX - xPos;

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

		var nametableAddress = _ctrlBaseNametableAddr switch
		{
			0 => PpuBus.Nametable0Address,
			1 => PpuBus.Nametable1Address,
			2 => PpuBus.Nametable2Address,
			3 => PpuBus.Nametable3Address,
			_ => throw new UnreachableException()
		};

		var tileColumn = _bgTileColumn;
		var tileRow = _bgTileRow;

		var tileIndex = tileColumn + (tileRow * _tileColumns);
		var bgNametableByte = Bus.ReadByte((ushort)(nametableAddress + tileIndex));

		var attribIndex = (tileRow / 4 * 8) + (tileColumn / 4);
		var bgAttribute = Bus.ReadByte((ushort)(nametableAddress + 0x03C0 + attribIndex));

		var bgTileY = _scanline & 0b111;
		var bgPatternTable = _ctrlBackgroundPatternTable ? PpuBus.PatternTable1Address : PpuBus.PatternTable0Address;
		var bgPatternOffset = bgPatternTable + (bgNametableByte * 16);

		var bgPatternLow = Bus.ReadByte((ushort)(bgPatternOffset + bgTileY));
		var bgPatternHigh = Bus.ReadByte((ushort)(bgPatternOffset + bgTileY + 8));

		var bgTileX = screenX & 0b111;

		{
			var paletteIndex = (_bgTileColumn % 4 / 2, _bgTileRow % 4 / 2) switch
			{
				(0, 0) => bgAttribute & 0b11,
				(1, 0) => (bgAttribute >> 2) & 0b11,
				(0, 1) => (bgAttribute >> 4) & 0b11,
				(1, 1) => (bgAttribute >> 6) & 0b11,
				_ => throw new UnreachableException()
			};

			var paletteOffset = PpuBus.PaletteRamAddress + 1 + (paletteIndex * 4);

			var colorIndex = (((bgPatternHigh >> (7 - bgTileX)) & 1) << 1) | ((bgPatternLow >> (7 - bgTileX)) & 1);
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
		Framebuffer[_nextPixelIndex++] = color;
	}
}
