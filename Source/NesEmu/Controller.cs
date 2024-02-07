using System.Diagnostics;

namespace NesEmu;

internal sealed class Controller
{
	public bool UpPressed = false;
	public bool DownPressed = false;
	public bool LeftPressed = false;
	public bool RightPressed = false;
	public bool SelectPressed = false;
	public bool StartPressed = false;
	public bool APressed = false;
	public bool BPressed = false;

	private bool _strobe = false;
	private int _index = 0;

	private byte GetNext()
	{
		if (_index > 7)
			return 1;

		var ret = _index switch
		{
			0 => APressed,
			1 => BPressed,
			2 => SelectPressed,
			3 => StartPressed,
			4 => UpPressed,
			5 => DownPressed,
			6 => LeftPressed,
			7 => RightPressed,
			_ => throw new UnreachableException()
		} ? (byte)1 : (byte)0;

		if (!_strobe)
			_index++;

		return ret;
	}

	public byte CpuReadByte(ushort address) => address switch
	{
		0x4016 => GetNext(),
		_ => 0
	};

	public void CpuWriteByte(ushort address, byte value)
	{
		switch (address)
		{
			case 0x4016:
				_strobe = (value & 1) != 0;
				if (_strobe)
					_index = 0;
				break;
		}
	}
}
