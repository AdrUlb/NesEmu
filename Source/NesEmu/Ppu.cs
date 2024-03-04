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
	private readonly Emu _emu;

	private int _scanline = 261;
	private int _cycle = 258;
	private byte _regData = 0;
	private byte _oamAddr;

	private bool _regW = false;
	private byte _regX = 0;
	private ushort _regV;
	private ushort _regT;

	private int _fetchStep = 0;

	private ushort _bgFetchAddress;
	private byte _bgFetchTile;
	private byte _bgFetchAttribute;
	private byte _bgFetchPatternLow;
	private byte _bgFetchPatternHigh;
	private ushort _bgPatternLows;
	private ushort _bgPatternHighs;
	private byte _bgPalette;

	private readonly Color[] _spritePixels = new Color[ScreenWidth];
	private readonly bool[] _sprite0Mask = new bool[ScreenWidth];
	private readonly bool[] _spritePriorities = new bool[ScreenWidth];
	private bool _spriteScanlineHasSprite0 = false;
	private byte _spriteEvalReadBuffer = 0;
	private int _spriteEvalN = 0;
	private int _spriteEvalM = 0;
	private bool _spriteEvalWrite = false;
	private int _spriteEvalWriteOffset = 0;
	private int _spriteEvalCount = 0;

	private bool _ctrlEnableVblankNmi = false;
	private bool _ctrlMasterSlaveSelect = false;
	private bool _ctrlSpriteSize = false;
	private byte _ctrlBackgroundPatternTable = 0;
	private bool _ctrlSpritePatternTable = false;
	private bool _ctrlAddrIncrMode = false;
	private byte _ctrlBaseNametableAddr;

	private bool _maskShowBackgroundInLeftmost8Pixels;
	private bool _maskShowSpritesInLeftmost8Pixels;
	private bool _maskShowBackground = true;
	private bool _maskShowSprites = true;

	private int _nextPixelIndex = 0;

	private bool _statusSprite0Hit = false;
	private bool _statusSpriteOverflow = false;

	private bool _wasSprite0HitThisFrame = false;

	private readonly Color[] _palette = new Color[64];
	private readonly byte[] _oam = new byte[256];
	private readonly byte[] _secondaryOam = new byte[32];

	public readonly byte[] Vram = new byte[0x800];
	public readonly Color[] Framebuffer = new Color[ScreenWidth * ScreenHeight];

	public bool StatusVblank { get; private set; } = false;

	public bool RequestNmi { get; set; } = false;

	public bool RenderingEnabled => _maskShowBackground || _maskShowSprites;

	private byte RegCtrl
	{
		get => (byte)
		(
			((_ctrlEnableVblankNmi ? 1 : 0) << 7) |
			((_ctrlMasterSlaveSelect ? 1 : 0) << 6) |
			((_ctrlSpriteSize ? 1 : 0) << 5) |
			((_ctrlBackgroundPatternTable & 1) << 4) |
			((_ctrlSpritePatternTable ? 1 : 0) << 3) |
			((_ctrlAddrIncrMode ? 1 : 0) << 2) |
			(_ctrlBaseNametableAddr & 0b11)
		);

		set
		{
			_ctrlEnableVblankNmi = ((value >> 7) & 1) != 0;
			_ctrlMasterSlaveSelect = ((value >> 6) & 1) != 0;
			_ctrlSpriteSize = ((value >> 5) & 1) != 0;
			_ctrlBackgroundPatternTable = (byte)((value >> 4) & 1);
			_ctrlSpritePatternTable = ((value >> 3) & 1) != 0;
			_ctrlAddrIncrMode = ((value >> 2) & 1) != 0;
			_ctrlBaseNametableAddr = (byte)(value & 0b11);
		}
	}

	private byte RegStatus
	{
		get => (byte)
		(
			((StatusVblank ? 1 : 0) << 7) |
			((_statusSprite0Hit ? 1 : 0) << 6) |
			((_statusSpriteOverflow ? 1 : 0) << 5)
		);
	}

	public int OamWaitCycles { get; private set; } = 0;

	public Ppu(Emu emu)
	{
		Bus = new(emu);
		_emu = emu;

		using var fs = File.OpenRead("palette.pal");

		Span<byte> pal = stackalloc byte[64 * 3];
		fs.ReadExactly(pal);
		for (var i = 0; i < 64; i++)
		{
			var ii = i * 3;
			_palette[i] = Color.FromArgb(pal[ii + 0], pal[ii + 1], pal[ii + 2]);
		}

		Array.Fill(Framebuffer, Color.White);
	}

	public byte CpuReadByte(int num)
	{
		switch (num)
		{
			case 0x2000: // PPUCTRL - write only
				return 0xFF;
			case 0x2001: // PPUMASK - write only
				return 0xFF;
			case 0x2002: // PPUSTATUS - read only
				{
					_regW = false; // Reset write latch
					var ret = RegStatus;
					StatusVblank = false;
					return ret;
				}
			case 0x2003: // OAMADDR - write only
				return 0xFF;
			case 0x2004: // OAMDATA - read/write
				return _oam[_oamAddr];
			case 0x2005: // PPUSCROLL - write only
				return 0xFF;
			case 0x2007: // PPUDATA
				{
					byte ret;
					if (_regV is >= 0x3F00 and < 0x4000) // Palette
					{
						ret = _regData = Bus.ReadByte(_regV);
						_regData = Bus.ReadByte((ushort)(_regV - 0x1000));
					}
					else // VRAM
					{
						ret = _regData;
						_regData = Bus.ReadByte(_regV);
					}

					if (_ctrlAddrIncrMode)
					{
						_regV += 32;
					}
					else
						_regV++;

					if ((_regV & (1 << 12)) != 0)
						_emu.Cartridge?.TickScanline();

					return ret;
				}

			case 0x2006:
			case 0x4014:
			default:
				return 0;
		}
	}

	public void CpuWriteByte(int num, byte value)
	{
		switch (num)
		{
			case 0x2000: // PPUCTRL - write only
				RegCtrl = value;
				_regT &= 0b1110011_11111111;
				_regT |= (ushort)((value & 0b11) << 10);
				break;
			case 0x2001: // PPUMASK - write only
				_maskShowBackgroundInLeftmost8Pixels = ((value >> 1) & 1) != 0;
				_maskShowSpritesInLeftmost8Pixels = ((value >> 2) & 1) != 0;
				_maskShowBackground = ((value >> 3) & 1) != 0;
				_maskShowSprites = ((value >> 4) & 1) != 0;
				break;
			case 0x2003: // OAMADDR - write only
				_oamAddr = value;
				break;
			case 0x2004: // OAMDATA - read/write
				_oam[_oamAddr++] = value;
				break;
			case 0x2005: // PPUSCROLL - write only
				if (!_regW)
				{
					_regT &= 0b1111111_11100000;
					_regT |= (ushort)((value >> 3) & 0b11111);
					_regX = (byte)(value & 0b111);
					_regW = true;
				}
				else
				{
					_regT &= 0b0001100_00011111;
					_regT |= (ushort)((value & 0b111) << 12);
					_regT |= (ushort)(((value >> 3) & 0b11111) << 5);
					_regW = false;
				}
				break;
			case 0x2006: // PPUADDR
				if (!_regW)
				{
					_regT &= 0b0000000_11111111;
					_regT |= (ushort)((value & 0b111111) << 8);
					_regW = true;
				}
				else
				{
					_regT &= 0b1111111_00000000;
					_regT |= value;
					_regV = _regT;
					_regW = false;
					if ((_regV & (1 << 12)) != 0)
						_emu.Cartridge?.TickScanline();
				}
				break;
			case 0x2007: // PPUDATA
				if ((_regV & (1 << 12)) != 0)
					_emu.Cartridge?.TickScanline();

				Bus.WriteByte(_regV, value);
				if (_ctrlAddrIncrMode)
				{
					_regV += 32;
				}
				else
					_regV++;

				if ((_regV & (1 << 12)) != 0)
					_emu.Cartridge?.TickScanline();

				break;
			case 0x4014: // OAMDMA
				var address = (ushort)(value << 8);
				for (var i = 0; i < _oam.Length; i++)
				{
					_oam[(_oamAddr + i) & 0xFF] = _emu.Cpu.Bus.ReadByte(address);
					address++;
				}
				OamWaitCycles = 514;
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
		if (OamWaitCycles > 0)
		{
			OamWaitCycles--;
		}

		if (_scanline is >= 0 and <= 239)
		{
			if (_scanline != 239) // Sprite evaluation does not happen on the prerender scanline
			{
				switch (_cycle)
				{
					case 0:
						_spriteEvalN = 0;
						_spriteEvalM = 0;
						_spriteEvalWriteOffset = 0;
						_spriteScanlineHasSprite0 = false;
						_spriteEvalCount = 0;
						Array.Fill<byte>(_secondaryOam, 0xFF);
						break;
					case >= 64 and <= 256:
						{
							if (_cycle % 2 == 1) // On odd cycles, data is read from (primary) OAM
							{
								// Read byte M of sprite N
								_spriteEvalReadBuffer = _oam[(4 * (_spriteEvalN % 64)) + _spriteEvalM];

								// If this is the first byte of the sprite
								if (_spriteEvalM == 0)
								{
									// Check if the sprite is part of this scanline
									var spriteInRange = _scanline >= _spriteEvalReadBuffer && _scanline < _spriteEvalReadBuffer + (_ctrlSpriteSize ? 16 : 8);
									if (spriteInRange)
									{
										_spriteEvalM++; // Next byte in the sprite
										_spriteEvalWrite = _spriteEvalN < 64 && _spriteEvalCount < 8; // Write this byte if OAM not full

										if (_spriteEvalN == 0)
											_spriteScanlineHasSprite0 = true;

										if (_spriteEvalN >= 64)
											_statusSpriteOverflow = true;
									}
									else
									{
										_spriteEvalN++; // Next sprite
										if (_spriteEvalN >= 64) // Apparently a hardware bug causes this to happen?
											_spriteEvalM++;
										_spriteEvalWrite = false; // Do not write read byte
									}
								}
								else
								{
									_spriteEvalM++; // Next byte
									_spriteEvalWrite = _spriteEvalN < 64 && _spriteEvalCount < 8; // Write read byte
								}

								if (_spriteEvalM >= 4) // Last byte of sprite was read
								{
									// Read first byte of next sprite next
									_spriteEvalM -= 4;
									_spriteEvalN++;
									_spriteEvalCount++;
								}
							}
							else // On even cycles, data is written to secondary OAM (unless secondary OAM is full, in which case it will read the value in secondary OAM instead)
							{
								if (_spriteEvalWrite)
									_secondaryOam[_spriteEvalWriteOffset++] = _spriteEvalReadBuffer;

								if (_spriteEvalWriteOffset >= _secondaryOam.Length)
									_spriteEvalWriteOffset = 0;
							}
							break;
						}
					case 257: // TOOD: proper sprite fetching, cycs 257-320
						{
							Array.Fill(_spritePixels, Color.Transparent);
							Array.Fill(_sprite0Mask, false);

							var spriteHeight = _ctrlSpriteSize ? 16 : 8;

							for (var screenX = 0; screenX < ScreenWidth; screenX++)
							{
								_spritePixels[screenX] = Color.Transparent;
								var patternTable = _ctrlSpritePatternTable ? PpuBus.PatternTable1Address : PpuBus.PatternTable0Address;
								if (_maskShowSprites)
								{
									for (var i = 0; i < 8; i++)
									{
										var off = i * 4;
										var yPos = _secondaryOam[off + 0];

										if (yPos == 0xFF)
											continue;

										// The y offset within the tile
										var y = _scanline - yPos;

										var tileId = _secondaryOam[off + 1];
										var attrib = _secondaryOam[off + 2];
										var xPos = _secondaryOam[off + 3];

										var flipX = (attrib & (1 << 6)) != 0;
										var flipY = (attrib & (1 << 7)) != 0;

										var x = screenX - xPos;

										if (x is < 0 or >= 8)
											continue;

										x = flipX ? x : 7 - x;
										y = flipY ? spriteHeight - 1 - y : y;

										var usePatternTable = patternTable;

										if (_ctrlSpriteSize) // 8x16
										{
											usePatternTable = (ushort)((tileId & 1) == 0 ? 0x0000 : 0x1000);
											tileId = (byte)(tileId & ~1);
											if (y >= 8)
											{
												tileId++;
												y -= 8;
											}
										}

										var paletteIndex = attrib & 0b11;
										var tileOff = (tileId * 16) + usePatternTable;

										var paletteOffset = PpuBus.PaletteRamAddress + 0x11 + (paletteIndex * 4);

										var msb = Bus.ReadByte((ushort)(tileOff + y + 8));
										var lsb = Bus.ReadByte((ushort)(tileOff + y));

										var colorIndex = (((msb >> x) & 1) << 1) | ((lsb >> x) & 1);
										var color = colorIndex switch
										{
											0 => Color.Transparent,
											1 => _palette[Bus.ReadByte((ushort)(paletteOffset + 0))],
											2 => _palette[Bus.ReadByte((ushort)(paletteOffset + 1))],
											3 => _palette[Bus.ReadByte((ushort)(paletteOffset + 2))],
											_ => throw new UnreachableException()
										};

										if (color == Color.Transparent)
											continue;

										_spritePixels[screenX] = color;
										_sprite0Mask[screenX] = i == 0 && _spriteScanlineHasSprite0;
										_spritePriorities[screenX] = ((attrib >> 5) & 1) != 0;
										break;
									}
								}
							}
							break;
						}
				}
			}
		}

		if (_scanline is >= 0 and <= 239 && _cycle is >= 1 and <= 256)
		{
			DrawPoint();
		}

		if (_scanline is (>= 0 and <= 239) or 261)
		{
			if (_cycle == 260 && RenderingEnabled)
				_emu.Cartridge?.TickScanline();
			if (_cycle is (>= 1 and <= 256) or (>= 321 and <= 336))
			{
				FetchBackground();

				_bgPatternHighs <<= 1;
				_bgPatternLows <<= 1;

				if (_fetchStep == 0)
				{
					var coarseX = _regV & 0b11111;
					var coarseY = (_regV >> 5) & 0b11111;

					var bgPaletteIndex = ((coarseX >> 1) & 1, (coarseY >> 1) & 1) switch
					{
						(0, 0) => (_bgFetchAttribute >> 0) & 0b11,
						(1, 0) => (_bgFetchAttribute >> 2) & 0b11,
						(0, 1) => (_bgFetchAttribute >> 4) & 0b11,
						(1, 1) => (_bgFetchAttribute >> 6) & 0b11,
						_ => throw new UnreachableException()
					};

					_bgPalette <<= 2;
					_bgPalette |= (byte)bgPaletteIndex;

					_bgPatternHighs |= _bgFetchPatternHigh;
					_bgPatternLows |= _bgFetchPatternLow;
				}
			}

			// If rendering is enabled, the PPU increments the horizontal position in v many times across the scanline, it begins at dots
			// 328 and 336, and will continue through the next scanline at 8, 16, 24... 240, 248, 256 (every 8 dots across the scanline until 256).
			// Across the scanline the effective coarse X scroll coordinate is incremented repeatedly, which will also wrap to the next nametable appropriately.
			// "Inc. hori(v)"
			if (_cycle is (>= 8 and < 256) or (>= 328 and <= 336) && _cycle % 8 == 0 && _cycle != 0 && RenderingEnabled)
			{
				if ((_regV & 0x001F) == 31) // If "coarse x" is 31
				{
					_regV &= unchecked((ushort)~0x001F); // Coarse X = 0
					_regV ^= 0x0400; // Switch horizontal nametable
				}
				else // Otherwise just increment coarse x
					_regV++;
			}

			// If rendering is enabled, the PPU increments the vertical position in v.
			// The effective Y scroll coordinate is incremented, which is a complex operation that will
			// correctly skip the attribute table memory regions, and wrap to the next nametable appropriately.
			// "Inc. vert(v)"
			if (_cycle == 256 && RenderingEnabled)
			{
				if ((_regV & 0x7000) != 0x7000) // If "fine y" is less than 7, increment it
				{
					_regV += 0x1000;
				}
				else // Otherwise
				{
					_regV &= unchecked((ushort)~0x7000); // Set fine y to 0
					var coarseY = (_regV & 0x03E0) >> 5;
					if (coarseY == 29)
					{
						coarseY = 0;
						_regV ^= 0x0800; // Switch vertical nametable
					}
					else if (coarseY == 31)
						coarseY = 0;
					else
						coarseY++;
					_regV = (ushort)((_regV & ~0x03E0) | (coarseY << 5));
				}
			}

			// If rendering is enabled, the PPU copies all bits related to horizontal position from t to v.
			// hori(v) = hori(t)
			if (_cycle == 257 && RenderingEnabled)
			{
				// v: ....A.. ...BCDEF <- t: ....A.. ...BCDEF
				ushort mask = 0b1111011_11100000;
				_regV &= mask;
				_regV |= (ushort)(_regT & (~mask & 0b1111111_11111111));
			}
		}

		// If rendering is enabled, at the end of vblank, shortly after the horizontal bits are copied from t to v at dot 257,
		// the PPU will repeatedly copy the vertical bits from t to v from dots 280 to 304, completing the full initialization of v from t.
		// vert(v) = vert(t)
		if (_scanline == 261 && (_cycle is >= 280 and <= 304) && RenderingEnabled) // PRERENDER SCANLINE
		{
			// v: GHIA.BC DEF..... <- t: GHIA.BC DEF.....
			ushort mask = 0b0000100_00011111;
			_regV &= mask;
			_regV |= (ushort)(_regT & (~mask & 0b1111111_11111111));
		}

		if (_scanline == 241 && _cycle == 1)
		{
			StatusVblank = true;
			if (_ctrlEnableVblankNmi)
				RequestNmi = true;
		}

		if (_scanline == 261 && _cycle == 1)
		{
			_statusSpriteOverflow = false;
			_statusSprite0Hit = false;
			StatusVblank = false;
		}

		_cycle++;
		if (_cycle >= _cyclesPerScanline)
		{
			_cycle = 0;

			_scanline++;
			if (_scanline == 262)
			{
				_scanline = 0;

				_nextPixelIndex = 0;
				_wasSprite0HitThisFrame = false;
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void FetchBackground()
	{
		switch (_fetchStep)
		{
			case 0:
				_bgFetchAddress = (ushort)(0x2000 | (_regV & 0x0FFF));
				break;
			case 1: // Nametable byte
				_bgFetchTile = Bus.ReadByte(_bgFetchAddress);
				break;
			case 2:
				_bgFetchAddress = (ushort)(0x23C0 | (_regV & 0x0C00) | ((_regV >> 4) & 0x38) | ((_regV >> 2) & 0x07));
				break;
			case 3: // Attribute byte
				_bgFetchAttribute = Bus.ReadByte(_bgFetchAddress);
				break;
			case 4:
				{
					var bgTileY = (_regV >> 12) & 0b111;
					var bgPatternTable = _ctrlBackgroundPatternTable << 12;
					var bgPatternOffset = (ushort)bgPatternTable | (ushort)(_bgFetchTile << 4) | bgTileY;
					_bgFetchAddress = (ushort)(bgPatternOffset);
					break;
				}
			case 5:
				_bgFetchPatternLow = Bus.ReadByte(_bgFetchAddress);
				break;
			case 6:
				{

					var bgTileY = (_regV >> 12) & 0b111;
					var bgPatternTable = _ctrlBackgroundPatternTable << 12;
					var bgPatternOffset = (ushort)bgPatternTable | (ushort)(_bgFetchTile << 4) | bgTileY;
					_bgFetchAddress = ((ushort)(bgPatternOffset + 8));
					break;
				}
			case 7:
				_bgFetchPatternHigh = Bus.ReadByte(_bgFetchAddress);
				break;
		}

		_fetchStep++;
		_fetchStep %= 8;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void DrawPoint()
	{
		// If rendering is disabled, render EXT color
		if (!RenderingEnabled)
		{
			var paletteIndex = Bus.ReadByte(PpuBus.PaletteRamAddress);
			paletteIndex %= (byte)_palette.Length;
			var color = _palette[paletteIndex];
			Framebuffer[_nextPixelIndex++] = color;
			return;
		}

		var screenX = _cycle - 1;

		var spriteColor = _maskShowSprites && (_maskShowSpritesInLeftmost8Pixels || screenX >= 8) ? _spritePixels[screenX] : Color.Transparent;
		var sprite0 = _sprite0Mask[screenX];

		var pal = (_bgPalette >> (((8 - _regX) > screenX % 8) ? 2 : 0)) & 0b11;
		var bgPaletteOffset = PpuBus.PaletteRamAddress + 1 + (pal * 4);

		var bgColorIndex = (((_bgPatternHighs >> (15 - _regX)) & 1) << 1) | ((_bgPatternLows >> (15 - _regX)) & 1);

		// If the background is disabled or rendering color index 0
		if (!_maskShowBackground || (!_maskShowBackgroundInLeftmost8Pixels && screenX < 8) || bgColorIndex == 0)
		{
			if (spriteColor == Color.Transparent) // No sprite pixel here, render EXT
				Framebuffer[_nextPixelIndex++] = _palette[Bus.ReadByte(PpuBus.PaletteRamAddress)];
			else // Sprite pixel here, render sprite
				Framebuffer[_nextPixelIndex++] = spriteColor;
			return;
		}

		var bgColor = bgColorIndex switch
		{
			1 => _palette[Bus.ReadByte((ushort)(bgPaletteOffset + 0))],
			2 => _palette[Bus.ReadByte((ushort)(bgPaletteOffset + 1))],
			3 => _palette[Bus.ReadByte((ushort)(bgPaletteOffset + 2))],
			_ => throw new UnreachableException()
		};

		// If sprite pixel is transparent, render background color
		if (spriteColor == Color.Transparent)
		{
			Framebuffer[_nextPixelIndex++] = bgColor;
			return;
		}

		// If sprite is sprite 0
		if (sprite0 && !_wasSprite0HitThisFrame)
		{
			_statusSprite0Hit = true;
			_wasSprite0HitThisFrame = true;
		}

		if (_spritePriorities[screenX]) // If the background priority bit is set, render background instead
			Framebuffer[_nextPixelIndex++] = bgColor;
		else
			Framebuffer[_nextPixelIndex++] = spriteColor;
	}
}
