using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NesEmu;

// https://www.pagetable.com/?p=410
// https://www.masswerk.at/6502/6502_instruction_set.html
// https://www.nesdev.org/obelisk-6502-guide/reference.html
// https://www.zimmers.net/anonftp/pub/cbm/documents/chipdata/64doc
internal sealed class Cpu
{
	private enum AddressingMode
	{
		Implied,
		Immediate,
		Relative,
		Accumulator,

		Zeropage,
		ZeropageXIndexed,
		ZeropageYIndexed,

		Absolute,
		AbsoluteXIndexed,
		AbsoluteYIndexed,

		Indirect,
		XIndexedIndirect,
		IndirectYIndexed,
	}

	private enum Instruction
	{
		Unknown,
		Adc,
		And,
		Asl,
		Bcc,
		Bcs,
		Beq,
		Bit,
		Bmi,
		Bne,
		Bpl,
		Brk,
		Bvc,
		Bvs,
		Clc,
		Cld,
		Cli,
		Clv,
		Cmp,
		Cpx,
		Cpy,
		Dec,
		Dex,
		Dey,
		Eor,
		Inc,
		Inx,
		Iny,
		Jam,
		Jmp,
		Jsr,
		Lda,
		Ldx,
		Ldy,
		Lsr,
		Nop,
		Ora,
		Php,
		Pha,
		Pla,
		Plp,
		Rol,
		Ror,
		Rti,
		Rts,
		Sbc,
		Sec,
		Sed,
		Sei,
		Sta,
		Stx,
		Sty,
		Tax,
		Tay,
		Tsx,
		Txa,
		Txs,
		Tya,
		IllDcp,
		IllIsc,
		IllLax,
		IllRla,
		IllRra,
		IllSax,
		IllSlo,
		IllSre
	}

	private enum Category
	{
		Stack,
		Implied,
		Read,
		ReadModifyWrite,
		Write,
		InstJmp,
		InstJsr,
		Unknown
	}

	private readonly struct Operation(Instruction instruction, AddressingMode addressingMode, Category type = Category.Unknown)
	{
		public readonly Instruction Instruction = instruction;
		public readonly AddressingMode AddressingMode = addressingMode;
		public readonly Category Category = type;

	}

	#region Opcodes

	private readonly Operation[] _operations =
	[
		// 0x00 - 0x0F
		new(Instruction.Brk, AddressingMode.Implied, Category.Stack),
		new(Instruction.Ora, AddressingMode.XIndexedIndirect, Category.Read),
		new(Instruction.Jam, AddressingMode.Implied, Category.Implied),
		new(Instruction.IllSlo, AddressingMode.XIndexedIndirect, Category.ReadModifyWrite),
		new(Instruction.Nop, AddressingMode.Zeropage, Category.Read),
		new(Instruction.Ora, AddressingMode.Zeropage, Category.Read),
		new(Instruction.Asl, AddressingMode.Zeropage, Category.ReadModifyWrite),
		new(Instruction.IllSlo, AddressingMode.Zeropage, Category.ReadModifyWrite),
		new(Instruction.Php, AddressingMode.Implied, Category.Stack),
		new(Instruction.Ora, AddressingMode.Immediate),
		new(Instruction.Asl, AddressingMode.Accumulator),
		new(Instruction.Unknown, AddressingMode.Immediate),
		new(Instruction.Nop, AddressingMode.Absolute, Category.Read),
		new(Instruction.Ora, AddressingMode.Absolute, Category.Read),
		new(Instruction.Asl, AddressingMode.Absolute, Category.ReadModifyWrite),
		new(Instruction.IllSlo, AddressingMode.Absolute, Category.ReadModifyWrite),
		// 0x10 - 0x1F
		new(Instruction.Bpl, AddressingMode.Relative),
		new(Instruction.Ora, AddressingMode.IndirectYIndexed, Category.Read),
		new(Instruction.Jam, AddressingMode.Implied, Category.Implied),
		new(Instruction.IllSlo, AddressingMode.IndirectYIndexed, Category.ReadModifyWrite),
		new(Instruction.Nop, AddressingMode.ZeropageXIndexed, Category.Read),
		new(Instruction.Ora, AddressingMode.ZeropageXIndexed, Category.Read),
		new(Instruction.Asl, AddressingMode.ZeropageXIndexed, Category.ReadModifyWrite),
		new(Instruction.IllSlo, AddressingMode.ZeropageXIndexed, Category.ReadModifyWrite),
		new(Instruction.Clc, AddressingMode.Implied, Category.Implied),
		new(Instruction.Ora, AddressingMode.AbsoluteYIndexed, Category.Read),
		new(Instruction.Nop, AddressingMode.Implied, Category.Implied),
		new(Instruction.IllSlo, AddressingMode.AbsoluteYIndexed, Category.ReadModifyWrite),
		new(Instruction.Nop, AddressingMode.AbsoluteXIndexed, Category.Read),
		new(Instruction.Ora, AddressingMode.AbsoluteXIndexed, Category.Read),
		new(Instruction.Asl, AddressingMode.AbsoluteXIndexed, Category.ReadModifyWrite),
		new(Instruction.IllSlo, AddressingMode.AbsoluteXIndexed, Category.ReadModifyWrite),
		// 0x20 - 0x2F
		new(Instruction.Jsr, AddressingMode.Absolute, Category.InstJsr),
		new(Instruction.And, AddressingMode.XIndexedIndirect, Category.Read),
		new(Instruction.Jam, AddressingMode.Implied, Category.Implied),
		new(Instruction.IllRla, AddressingMode.XIndexedIndirect, Category.ReadModifyWrite),
		new(Instruction.Bit, AddressingMode.Zeropage, Category.Read),
		new(Instruction.And, AddressingMode.Zeropage, Category.Read),
		new(Instruction.Rol, AddressingMode.Zeropage, Category.ReadModifyWrite),
		new(Instruction.IllRla, AddressingMode.Zeropage, Category.ReadModifyWrite),
		new(Instruction.Plp, AddressingMode.Implied, Category.Stack),
		new(Instruction.And, AddressingMode.Immediate),
		new(Instruction.Rol, AddressingMode.Accumulator),
		new(Instruction.Unknown, AddressingMode.Immediate),
		new(Instruction.Bit, AddressingMode.Absolute, Category.Read),
		new(Instruction.And, AddressingMode.Absolute, Category.Read),
		new(Instruction.Rol, AddressingMode.Absolute, Category.ReadModifyWrite),
		new(Instruction.IllRla, AddressingMode.Absolute, Category.ReadModifyWrite),
		// 0x30 - 0x3F
		new(Instruction.Bmi, AddressingMode.Relative),
		new(Instruction.And, AddressingMode.IndirectYIndexed, Category.Read),
		new(Instruction.Jam, AddressingMode.Implied, Category.Implied),
		new(Instruction.IllRla, AddressingMode.IndirectYIndexed, Category.ReadModifyWrite),
		new(Instruction.Nop, AddressingMode.ZeropageXIndexed, Category.Read),
		new(Instruction.And, AddressingMode.ZeropageXIndexed, Category.Read),
		new(Instruction.Rol, AddressingMode.ZeropageXIndexed, Category.ReadModifyWrite),
		new(Instruction.IllRla, AddressingMode.ZeropageXIndexed, Category.ReadModifyWrite),
		new(Instruction.Sec, AddressingMode.Implied, Category.Implied),
		new(Instruction.And, AddressingMode.AbsoluteYIndexed, Category.Read),
		new(Instruction.Nop, AddressingMode.Implied, Category.Implied),
		new(Instruction.IllRla, AddressingMode.AbsoluteYIndexed, Category.ReadModifyWrite),
		new(Instruction.Nop, AddressingMode.AbsoluteXIndexed, Category.Read),
		new(Instruction.And, AddressingMode.AbsoluteXIndexed, Category.Read),
		new(Instruction.Rol, AddressingMode.AbsoluteXIndexed, Category.ReadModifyWrite),
		new(Instruction.IllRla, AddressingMode.AbsoluteXIndexed, Category.ReadModifyWrite),
		// 0x40 - 0x4F
		new(Instruction.Rti, AddressingMode.Implied, Category.Stack),
		new(Instruction.Eor, AddressingMode.XIndexedIndirect, Category.Read),
		new(Instruction.Jam, AddressingMode.Implied, Category.Implied),
		new(Instruction.IllSre, AddressingMode.XIndexedIndirect, Category.ReadModifyWrite),
		new(Instruction.Nop, AddressingMode.Zeropage, Category.Read),
		new(Instruction.Eor, AddressingMode.Zeropage, Category.Read),
		new(Instruction.Lsr, AddressingMode.Zeropage, Category.ReadModifyWrite),
		new(Instruction.IllSre, AddressingMode.Zeropage, Category.ReadModifyWrite),
		new(Instruction.Pha, AddressingMode.Implied, Category.Stack),
		new(Instruction.Eor, AddressingMode.Immediate),
		new(Instruction.Lsr, AddressingMode.Accumulator),
		new(Instruction.Unknown, AddressingMode.Immediate),
		new(Instruction.Jmp, AddressingMode.Absolute, Category.InstJmp),
		new(Instruction.Eor, AddressingMode.Absolute, Category.Read),
		new(Instruction.Lsr, AddressingMode.Absolute, Category.ReadModifyWrite),
		new(Instruction.IllSre, AddressingMode.Absolute, Category.ReadModifyWrite),
		// 0x50 - 0x5F
		new(Instruction.Bvc, AddressingMode.Relative),
		new(Instruction.Eor, AddressingMode.IndirectYIndexed, Category.Read),
		new(Instruction.Jam, AddressingMode.Implied, Category.Implied),
		new(Instruction.IllSre, AddressingMode.IndirectYIndexed, Category.ReadModifyWrite),
		new(Instruction.Nop, AddressingMode.ZeropageXIndexed, Category.Read),
		new(Instruction.Eor, AddressingMode.ZeropageXIndexed, Category.Read),
		new(Instruction.Lsr, AddressingMode.ZeropageXIndexed, Category.ReadModifyWrite),
		new(Instruction.IllSre, AddressingMode.ZeropageXIndexed, Category.ReadModifyWrite),
		new(Instruction.Cli, AddressingMode.Implied, Category.Implied),
		new(Instruction.Eor, AddressingMode.AbsoluteYIndexed, Category.Read),
		new(Instruction.Nop, AddressingMode.Implied, Category.Implied),
		new(Instruction.IllSre, AddressingMode.AbsoluteYIndexed, Category.ReadModifyWrite),
		new(Instruction.Nop, AddressingMode.AbsoluteXIndexed, Category.Read),
		new(Instruction.Eor, AddressingMode.AbsoluteXIndexed, Category.Read),
		new(Instruction.Lsr, AddressingMode.AbsoluteXIndexed, Category.ReadModifyWrite),
		new(Instruction.IllSre, AddressingMode.AbsoluteXIndexed, Category.ReadModifyWrite),
		// 0x60 - 0x6F
		new(Instruction.Rts, AddressingMode.Implied, Category.Stack),
		new(Instruction.Adc, AddressingMode.XIndexedIndirect, Category.Read),
		new(Instruction.Jam, AddressingMode.Implied, Category.Implied),
		new(Instruction.IllRra, AddressingMode.XIndexedIndirect, Category.ReadModifyWrite),
		new(Instruction.Nop, AddressingMode.Zeropage, Category.Read),
		new(Instruction.Adc, AddressingMode.Zeropage, Category.Read),
		new(Instruction.Ror, AddressingMode.Zeropage, Category.ReadModifyWrite),
		new(Instruction.IllRra, AddressingMode.Zeropage, Category.ReadModifyWrite),
		new(Instruction.Pla, AddressingMode.Implied, Category.Stack),
		new(Instruction.Adc, AddressingMode.Immediate),
		new(Instruction.Ror, AddressingMode.Accumulator),
		new(Instruction.Unknown, AddressingMode.Immediate),
		new(Instruction.Jmp, AddressingMode.Indirect),
		new(Instruction.Adc, AddressingMode.Absolute, Category.Read),
		new(Instruction.Ror, AddressingMode.Absolute, Category.ReadModifyWrite),
		new(Instruction.IllRra, AddressingMode.Absolute, Category.ReadModifyWrite),
		// 0x70 - 0x7F
		new(Instruction.Bvs, AddressingMode.Relative),
		new(Instruction.Adc, AddressingMode.IndirectYIndexed, Category.Read),
		new(Instruction.Jam, AddressingMode.Implied, Category.Implied),
		new(Instruction.IllRra, AddressingMode.IndirectYIndexed, Category.ReadModifyWrite),
		new(Instruction.Nop, AddressingMode.ZeropageXIndexed, Category.Read),
		new(Instruction.Adc, AddressingMode.ZeropageXIndexed, Category.Read),
		new(Instruction.Ror, AddressingMode.ZeropageXIndexed, Category.ReadModifyWrite),
		new(Instruction.IllRra, AddressingMode.ZeropageXIndexed, Category.ReadModifyWrite),
		new(Instruction.Sei, AddressingMode.Implied, Category.Implied),
		new(Instruction.Adc, AddressingMode.AbsoluteYIndexed, Category.Read),
		new(Instruction.Nop, AddressingMode.Implied, Category.Implied),
		new(Instruction.IllRra, AddressingMode.AbsoluteYIndexed, Category.ReadModifyWrite),
		new(Instruction.Nop, AddressingMode.AbsoluteXIndexed, Category.Read),
		new(Instruction.Adc, AddressingMode.AbsoluteXIndexed, Category.Read),
		new(Instruction.Ror, AddressingMode.AbsoluteXIndexed, Category.ReadModifyWrite),
		new(Instruction.IllRra, AddressingMode.AbsoluteXIndexed, Category.ReadModifyWrite),
		// 0x80 - 0x8F
		new(Instruction.Nop, AddressingMode.Immediate),
		new(Instruction.Sta, AddressingMode.XIndexedIndirect, Category.Write),
		new(Instruction.Nop, AddressingMode.Immediate),
		new(Instruction.IllSax, AddressingMode.XIndexedIndirect, Category.Write),
		new(Instruction.Sty, AddressingMode.Zeropage, Category.Write),
		new(Instruction.Sta, AddressingMode.Zeropage, Category.Write),
		new(Instruction.Stx, AddressingMode.Zeropage, Category.Write),
		new(Instruction.IllSax, AddressingMode.Zeropage, Category.Write),
		new(Instruction.Dey, AddressingMode.Implied, Category.Implied),
		new(Instruction.Nop, AddressingMode.Immediate),
		new(Instruction.Txa, AddressingMode.Implied, Category.Implied),
		new(Instruction.Unknown, AddressingMode.Immediate),
		new(Instruction.Sty, AddressingMode.Absolute, Category.Write),
		new(Instruction.Sta, AddressingMode.Absolute, Category.Write),
		new(Instruction.Stx, AddressingMode.Absolute, Category.Write),
		new(Instruction.IllSax, AddressingMode.Absolute, Category.Write),
		// 0x90 - 0x9F
		new(Instruction.Bcc, AddressingMode.Relative),
		new(Instruction.Sta, AddressingMode.IndirectYIndexed, Category.Write),
		new(Instruction.Jam, AddressingMode.Implied, Category.Implied),
		new(Instruction.Unknown, AddressingMode.IndirectYIndexed, Category.Write),
		new(Instruction.Sty, AddressingMode.ZeropageXIndexed, Category.Write),
		new(Instruction.Sta, AddressingMode.ZeropageXIndexed, Category.Write),
		new(Instruction.Stx, AddressingMode.ZeropageYIndexed, Category.Write),
		new(Instruction.IllSax, AddressingMode.ZeropageYIndexed, Category.Write),
		new(Instruction.Tya, AddressingMode.Implied, Category.Implied),
		new(Instruction.Sta, AddressingMode.AbsoluteYIndexed, Category.Write),
		new(Instruction.Txs, AddressingMode.Implied, Category.Implied),
		new(Instruction.Unknown, AddressingMode.AbsoluteXIndexed, Category.Write),
		new(Instruction.Unknown, AddressingMode.AbsoluteXIndexed, Category.Write),
		new(Instruction.Sta, AddressingMode.AbsoluteXIndexed, Category.Write),
		new(Instruction.Unknown, AddressingMode.AbsoluteYIndexed, Category.Write),
		new(Instruction.Unknown, AddressingMode.AbsoluteYIndexed, Category.Write),
		// 0xA0 - 0xAF
		new(Instruction.Ldy, AddressingMode.Immediate),
		new(Instruction.Lda, AddressingMode.XIndexedIndirect, Category.Read),
		new(Instruction.Ldx, AddressingMode.Immediate),
		new(Instruction.IllLax, AddressingMode.XIndexedIndirect, Category.Read),
		new(Instruction.Ldy, AddressingMode.Zeropage, Category.Read),
		new(Instruction.Lda, AddressingMode.Zeropage, Category.Read),
		new(Instruction.Ldx, AddressingMode.Zeropage, Category.Read),
		new(Instruction.IllLax, AddressingMode.Zeropage, Category.Read),
		new(Instruction.Tay, AddressingMode.Implied, Category.Implied),
		new(Instruction.Lda, AddressingMode.Immediate),
		new(Instruction.Tax, AddressingMode.Implied, Category.Implied),
		new(Instruction.Unknown, AddressingMode.Immediate),
		new(Instruction.Ldy, AddressingMode.Absolute, Category.Read),
		new(Instruction.Lda, AddressingMode.Absolute, Category.Read),
		new(Instruction.Ldx, AddressingMode.Absolute, Category.Read),
		new(Instruction.IllLax, AddressingMode.Absolute, Category.Read),
		// 0xB0 - 0xBF
		new(Instruction.Bcs, AddressingMode.Relative),
		new(Instruction.Lda, AddressingMode.IndirectYIndexed, Category.Read),
		new(Instruction.Jam, AddressingMode.Implied, Category.Implied),
		new(Instruction.IllLax, AddressingMode.IndirectYIndexed, Category.Read),
		new(Instruction.Ldy, AddressingMode.ZeropageXIndexed, Category.Read),
		new(Instruction.Lda, AddressingMode.ZeropageXIndexed, Category.Read),
		new(Instruction.Ldx, AddressingMode.ZeropageYIndexed, Category.Read),
		new(Instruction.IllLax, AddressingMode.ZeropageYIndexed, Category.Read),
		new(Instruction.Clv, AddressingMode.Implied, Category.Implied),
		new(Instruction.Lda, AddressingMode.AbsoluteYIndexed, Category.Read),
		new(Instruction.Tsx, AddressingMode.Implied, Category.Implied),
		new(Instruction.Unknown, AddressingMode.AbsoluteYIndexed, Category.Read),
		new(Instruction.Ldy, AddressingMode.AbsoluteXIndexed, Category.Read),
		new(Instruction.Lda, AddressingMode.AbsoluteXIndexed, Category.Read),
		new(Instruction.Ldx, AddressingMode.AbsoluteYIndexed, Category.Read),
		new(Instruction.IllLax, AddressingMode.AbsoluteYIndexed, Category.Read),
		// 0xC0 - 0xCF
		new(Instruction.Cpy, AddressingMode.Immediate),
		new(Instruction.Cmp, AddressingMode.XIndexedIndirect, Category.Read),
		new(Instruction.Nop, AddressingMode.Immediate),
		new(Instruction.IllDcp, AddressingMode.XIndexedIndirect, Category.ReadModifyWrite),
		new(Instruction.Cpy, AddressingMode.Zeropage, Category.Read),
		new(Instruction.Cmp, AddressingMode.Zeropage, Category.Read),
		new(Instruction.Dec, AddressingMode.Zeropage, Category.ReadModifyWrite),
		new(Instruction.IllDcp, AddressingMode.Zeropage, Category.ReadModifyWrite),
		new(Instruction.Iny, AddressingMode.Implied, Category.Implied),
		new(Instruction.Cmp, AddressingMode.Immediate),
		new(Instruction.Dex, AddressingMode.Implied, Category.Implied),
		new(Instruction.Unknown, AddressingMode.Immediate),
		new(Instruction.Cpy, AddressingMode.Absolute, Category.Read),
		new(Instruction.Cmp, AddressingMode.Absolute, Category.Read),
		new(Instruction.Dec, AddressingMode.Absolute, Category.ReadModifyWrite),
		new(Instruction.IllDcp, AddressingMode.Absolute, Category.ReadModifyWrite),
		// 0xD0 - 0xDF
		new(Instruction.Bne, AddressingMode.Relative),
		new(Instruction.Cmp, AddressingMode.IndirectYIndexed, Category.Read),
		new(Instruction.Jam, AddressingMode.Implied, Category.Implied),
		new(Instruction.IllDcp, AddressingMode.IndirectYIndexed, Category.ReadModifyWrite),
		new(Instruction.Nop, AddressingMode.ZeropageXIndexed, Category.Read),
		new(Instruction.Cmp, AddressingMode.ZeropageXIndexed, Category.Read),
		new(Instruction.Dec, AddressingMode.ZeropageXIndexed, Category.ReadModifyWrite),
		new(Instruction.IllDcp, AddressingMode.ZeropageXIndexed, Category.ReadModifyWrite),
		new(Instruction.Cld, AddressingMode.Implied, Category.Implied),
		new(Instruction.Cmp, AddressingMode.AbsoluteYIndexed, Category.Read),
		new(Instruction.Nop, AddressingMode.Implied, Category.Implied),
		new(Instruction.IllDcp, AddressingMode.AbsoluteYIndexed, Category.ReadModifyWrite),
		new(Instruction.Nop, AddressingMode.AbsoluteXIndexed, Category.Read),
		new(Instruction.Cmp, AddressingMode.AbsoluteXIndexed, Category.Read),
		new(Instruction.Dec, AddressingMode.AbsoluteXIndexed, Category.ReadModifyWrite),
		new(Instruction.IllDcp, AddressingMode.AbsoluteXIndexed, Category.ReadModifyWrite),
		// 0xE0 - 0xEF
		new(Instruction.Cpx, AddressingMode.Immediate),
		new(Instruction.Sbc, AddressingMode.XIndexedIndirect, Category.Read),
		new(Instruction.Nop, AddressingMode.Immediate),
		new(Instruction.IllIsc, AddressingMode.XIndexedIndirect, Category.ReadModifyWrite),
		new(Instruction.Cpx, AddressingMode.Zeropage, Category.Read),
		new(Instruction.Sbc, AddressingMode.Zeropage, Category.Read),
		new(Instruction.Inc, AddressingMode.Zeropage, Category.ReadModifyWrite),
		new(Instruction.IllIsc, AddressingMode.Zeropage, Category.ReadModifyWrite),
		new(Instruction.Inx, AddressingMode.Implied, Category.Implied),
		new(Instruction.Sbc, AddressingMode.Immediate),
		new(Instruction.Nop, AddressingMode.Implied, Category.Implied),
		new(Instruction.Sbc, AddressingMode.Immediate),
		new(Instruction.Cpx, AddressingMode.Absolute, Category.Read),
		new(Instruction.Sbc, AddressingMode.Absolute, Category.Read),
		new(Instruction.Inc, AddressingMode.Absolute, Category.ReadModifyWrite),
		new(Instruction.IllIsc, AddressingMode.Absolute, Category.ReadModifyWrite),
		// 0xF0 - 0xFF
		new(Instruction.Beq, AddressingMode.Relative),
		new(Instruction.Sbc, AddressingMode.IndirectYIndexed, Category.Read),
		new(Instruction.Jam, AddressingMode.Implied, Category.Implied),
		new(Instruction.IllIsc, AddressingMode.IndirectYIndexed, Category.ReadModifyWrite),
		new(Instruction.Nop, AddressingMode.ZeropageXIndexed, Category.Read),
		new(Instruction.Sbc, AddressingMode.ZeropageXIndexed, Category.Read),
		new(Instruction.Inc, AddressingMode.ZeropageXIndexed, Category.ReadModifyWrite),
		new(Instruction.IllIsc, AddressingMode.ZeropageXIndexed, Category.ReadModifyWrite),
		new(Instruction.Sed, AddressingMode.Implied, Category.Implied),
		new(Instruction.Sbc, AddressingMode.AbsoluteYIndexed, Category.Read),
		new(Instruction.Nop, AddressingMode.Implied, Category.Implied),
		new(Instruction.IllIsc, AddressingMode.AbsoluteYIndexed, Category.ReadModifyWrite),
		new(Instruction.Nop, AddressingMode.AbsoluteXIndexed, Category.Read),
		new(Instruction.Sbc, AddressingMode.AbsoluteXIndexed, Category.Read),
		new(Instruction.Inc, AddressingMode.AbsoluteXIndexed, Category.ReadModifyWrite),
		new(Instruction.IllIsc, AddressingMode.AbsoluteXIndexed, Category.ReadModifyWrite)
	];

	#endregion

	#region Registers

	private ushort _regPc;
	private byte _regSpLo;
	private const byte RegSpHi = 0x01;
	private byte _regA;
	private byte _regX;
	private byte _regY;
	private ushort RegSp => (ushort)((RegSpHi << 8) | _regSpLo);

	#endregion

	#region Flags

	private bool _flagCarry;
	private bool _flagZero;
	public bool FlagInterruptDisable { get; private set; }
	private bool _flagDecimal; // Disabled but can still be set/cleared
	private bool _flagB;
	private bool _flagOverflow;
	private bool _flagNegative;

	#endregion

	public readonly CpuBus Bus;

	private byte _currentOpcode = 0;
	private int _step = 0;
	private int _cycles = 7;

	private ushort _fetchAddress = 0;
	private byte _fetchLow;
	private byte _fetchHigh;
	private byte _fetchOperand;
	private bool _pageBoundaryCrossed;
	private bool _requestNmi = false;
	private bool _requestIrq = false;
	private bool _inIrq = false;

	private Operation CurrentOperation => _operations[_currentOpcode];

	private byte RegStatus
	{
		get => (byte)
		(
			((_flagNegative ? 1 : 0) << 7) |
			((_flagOverflow ? 1 : 0) << 6) |
			(1 << 5) | // Unused, always 1
			((_flagB ? 1 : 0) << 4) | // Unused, 0 if pushed by interrupt, 1 if pushed by instruction
			((_flagDecimal ? 1 : 0) << 3) |
			((FlagInterruptDisable ? 1 : 0) << 2) |
			((_flagZero ? 1 : 0) << 1) |
			((_flagCarry ? 1 : 0) << 0)
		);

		set
		{
			_flagNegative = ((value >> 7) & 1) != 0;
			_flagOverflow = ((value >> 6) & 1) != 0;
			_flagB = ((value >> 4) & 1) != 0;
			_flagDecimal = ((value >> 3) & 1) != 0;
			FlagInterruptDisable = ((value >> 2) & 1) != 0;
			_flagZero = ((value >> 1) & 1) != 0;
			_flagCarry = (value & 1) != 0;
		}
	}

	public Cpu(Emu emu)
	{
		Bus = new(emu);
	}

	public void Reset()
	{
		_regPc = (ushort)((Bus.ReadByte(0xFFFD) << 8) | Bus.ReadByte(0xFFFC));
		_regSpLo = 0xFD;

		_flagNegative = false;
		_flagOverflow = false;
		_flagB = false;
		_flagDecimal = false;
		FlagInterruptDisable = true;
		_flagZero = false;
		_flagCarry = false;
	}

	public void RequestNmi() => _requestNmi = true;
	public void RequestIrq() => _requestIrq = true;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private byte ReadByte(ushort address) => Bus.ReadByte(address);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void WriteByte(ushort address, byte value) => Bus.WriteByte(address, value);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private byte FetchByte() => ReadByte(_regPc++);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void PushByte(byte value)
	{
		WriteByte(RegSp, value);
		_regSpLo--;
	}

	public void Tick()
	{
		if (_step == 0)
		{
			if (_requestNmi)
			{
				_requestNmi = false;
				PushByte((byte)(_regPc >> 8));
				PushByte((byte)(_regPc & 0xFF));
				PushByte(RegStatus);
				_fetchLow = ReadByte(0xFFFA);
				_fetchHigh = ReadByte(0xFFFB);
				_regPc = (ushort)((_fetchHigh << 8) | _fetchLow);
				FlagInterruptDisable = true;
			}

			if (_requestIrq)
			{
				_requestIrq = false;
				_currentOpcode = 0;
				_inIrq = true;
			}
			else
				_currentOpcode = FetchByte();
		}

		//Console.WriteLine($"IP={_regPc:X4}");
		
		switch (CurrentOperation.AddressingMode)
		{
			case AddressingMode.Indirect:
				ExecuteAddrIndirect();
				break;
			case AddressingMode.Accumulator:
				ExecuteAddrAccumulator();
				break;
			case AddressingMode.Implied:
				ExecuteAddrImplied();
				break;
			case AddressingMode.Immediate:
				ExecuteAddrImmediate();
				break;
			case AddressingMode.Absolute:
				ExecuteAddrAbsolute();
				break;
			case AddressingMode.Zeropage:
				ExecuteAddrZeropage();
				break;
			case AddressingMode.ZeropageXIndexed:
				ExecuteAddrZeropageIndexed();
				break;
			case AddressingMode.ZeropageYIndexed:
				ExecuteAddrZeropageIndexed();
				break;
			case AddressingMode.AbsoluteXIndexed:
				ExecuteAddrAbsoluteIndexed();
				break;
			case AddressingMode.AbsoluteYIndexed:
				ExecuteAddrAbsoluteIndexed();
				break;
			case AddressingMode.Relative:
				ExecuteAddrRelative();
				break;
			case AddressingMode.IndirectYIndexed:
				ExecuteAddrIndirectYIndexed();
				break;
			case AddressingMode.XIndexedIndirect:
				ExecuteAddrXIndexedIndirect();
				break;
			default:
				throw new NotImplementedException($"Opcode 0x{_currentOpcode:X2} not recognized.");
		}

		_cycles++;
	}

	#region Addressing modes

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteAddrIndirect()
	{
		if (CurrentOperation.Instruction != Instruction.Jmp)
			throw new UnreachableException();

		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch pointer address low, increment PC
				_fetchLow = FetchByte();
				_step++;
				break;
			case 2: // fetch pointer address high, increment PC
				_fetchHigh = FetchByte();
				_step++;
				break;
			case 3: // fetch low address to latch
				_fetchAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
				_fetchLow = ReadByte(_fetchAddress);
				_step++;
				break;
			case 4: // fetch PCH, copy latch to PCL
				_fetchAddress++;
				_fetchAddress &= 0x00FF;
				_fetchAddress |= (ushort)(_fetchHigh << 8);
				_fetchHigh = ReadByte(_fetchAddress);
				_regPc = (ushort)((_fetchHigh << 8) | _fetchLow);
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteAddrAccumulator()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // read next instruction byte (and throw it away)
				ReadByte(_regPc);
				switch (CurrentOperation.Instruction)
				{
					case Instruction.Asl:
						ExecuteOpAsl(ref _regA);
						break;
					case Instruction.Lsr:
						ExecuteOpLsr(ref _regA);
						break;
					case Instruction.Rol:
						ExecuteOpRol(ref _regA);
						break;
					case Instruction.Ror:
						ExecuteOpRor(ref _regA);
						break;
					default: throw new UnreachableException();
				}
				_step = 0;
				break;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteAddrImplied()
	{
		if (CurrentOperation.Category == Category.Stack)
		{
			switch (CurrentOperation.Instruction)
			{
				case Instruction.Brk:
					ExecuteInstBrk();
					break;
				case Instruction.Rti:
					ExecuteInstRti();
					break;
				case Instruction.Rts:
					ExecuteInstRts();
					break;
				case Instruction.Pha:
					ExecuteInstPha();
					break;
				case Instruction.Php:
					ExecuteInstPhp();
					break;
				case Instruction.Pla:
					ExecuteInstPla();
					break;
				case Instruction.Plp:
					ExecuteInstPlp();
					break;
				default: throw new UnreachableException();
			}
			return;
		}

		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // read next instruction byte (and throw it away)
				ReadByte(_regPc);
				switch (CurrentOperation.Instruction)
				{
					case Instruction.Clc:
						ExecuteOpClc();
						break;
					case Instruction.Cld:
						ExecuteOpCld();
						break;
					case Instruction.Cli:
						ExecuteOpCli();
						break;
					case Instruction.Clv:
						ExecuteOpClv();
						break;
					case Instruction.Dex:
						ExecuteOpDex();
						break;
					case Instruction.Dey:
						ExecuteOpDey();
						break;
					case Instruction.Inx:
						ExecuteOpInx();
						break;
					case Instruction.Iny:
						ExecuteOpIny();
						break;
					case Instruction.Nop: break;
					case Instruction.Sec:
						ExecuteOpSec();
						break;
					case Instruction.Sed:
						ExecuteOpSed();
						break;
					case Instruction.Sei:
						ExecuteOpSei();
						break;
					case Instruction.Tax:
						ExecuteOpTax();
						break;
					case Instruction.Tay:
						ExecuteOpTay();
						break;
					case Instruction.Tsx:
						ExecuteOpTsx();
						break;
					case Instruction.Txa:
						ExecuteOpTxa();
						break;
					case Instruction.Txs:
						ExecuteOpTxs();
						break;
					case Instruction.Tya:
						ExecuteOpTya();
						break;
					default: throw new UnreachableException();
				}
				_step = 0;
				break;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteAddrImmediate()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch value, increment PC
				{
					var value = FetchByte();
					switch (CurrentOperation.Instruction)
					{
						case Instruction.Adc:
							ExecuteOpAdc(value);
							break;
						case Instruction.And:
							ExecuteOpAnd(value);
							break;
						case Instruction.Cmp:
							ExecuteOpCmp(value);
							break;
						case Instruction.Cpx:
							ExecuteOpCpx(value);
							break;
						case Instruction.Cpy:
							ExecuteOpCpy(value);
							break;
						case Instruction.Eor:
							ExecuteOpEor(value);
							break;
						case Instruction.Lda:
							ExecuteOpLda(value);
							break;
						case Instruction.Ldx:
							ExecuteOpLdx(value);
							break;
						case Instruction.Ldy:
							ExecuteOpLdy(value);
							break;
						case Instruction.Nop: break;
						case Instruction.Ora:
							ExecuteOpOra(value);
							break;
						case Instruction.Sbc:
							ExecuteOpSbc(value);
							break;
						default: throw new UnreachableException();
					}
					_step = 0;
					break;
				}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteAddrAbsolute()
	{
		switch (CurrentOperation.Category)
		{
			case Category.InstJmp:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch low address byte, increment PC
						_fetchLow = FetchByte();
						_step++;
						break;
					case 2: // copy low address byte to PCL, fetch high address byte to PCH
						_regPc = (ushort)((FetchByte() << 8) | _fetchLow);
						_step = 0;
						break;
				}
				break;
			case Category.InstJsr:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch low address byte, increment PC
						_fetchLow = FetchByte();
						_step++;
						break;
					case 2: // internal operation (predecrement S?)
						_step++;
						break;
					case 3: // push PCH on stack, decrement S
						WriteByte(RegSp, (byte)(_regPc >> 8));
						_regSpLo--;
						_step++;
						break;
					case 4: // push PCL on stack, decrement S
						WriteByte(RegSp, (byte)(_regPc & 0xFF));
						_regSpLo--;
						_step++;
						break;
					case 5: // copy low address byte to PCL, fetch high address byte to PCH
						_regPc = (ushort)((FetchByte() << 8) | _fetchLow);
						_step = 0;
						break;
					default:
						throw new UnreachableException();
				}
				break;
			case Category.Read:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch low byte of address, increment PC
						_fetchLow = FetchByte();
						_step++;
						break;
					case 2: // fetch high byte of address, increment PC
						_fetchHigh = FetchByte();
						_step++;
						break;
					case 3: // read from effective address
						_fetchAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						var value = ReadByte(_fetchAddress);
						switch (CurrentOperation.Instruction)
						{
							case Instruction.Adc:
								ExecuteOpAdc(value);
								break;
							case Instruction.And:
								ExecuteOpAnd(value);
								break;
							case Instruction.Bit:
								ExecuteOpBit(value);
								break;
							case Instruction.Cmp:
								ExecuteOpCmp(value);
								break;
							case Instruction.Cpx:
								ExecuteOpCpx(value);
								break;
							case Instruction.Cpy:
								ExecuteOpCpy(value);
								break;
							case Instruction.Eor:
								ExecuteOpEor(value);
								break;
							case Instruction.Lda:
								ExecuteOpLda(value);
								break;
							case Instruction.Ldx:
								ExecuteOpLdx(value);
								break;
							case Instruction.Ldy:
								ExecuteOpLdy(value);
								break;
							case Instruction.Nop: break;
							case Instruction.Ora:
								ExecuteOpOra(value);
								break;
							case Instruction.Sbc:
								ExecuteOpSbc(value);
								break;
							case Instruction.IllLax:
								ExecuteOpIllLax(value);
								break;
							default: throw new UnreachableException();
						}
						_step = 0;
						break;
				}
				break;
			case Category.ReadModifyWrite:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch low byte of address, increment PC
						_fetchLow = FetchByte();
						_step++;
						break;
					case 2: // fetch high byte of address, increment PC
						_fetchHigh = FetchByte();
						_step++;
						break;
					case 3: // read from effective address
						_fetchAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						_fetchOperand = ReadByte(_fetchAddress);
						_step++;
						break;
					case 4: // write the value back to effective address, and do the operation on it
						WriteByte(_fetchAddress, _fetchOperand);
						switch (CurrentOperation.Instruction)
						{
							case Instruction.Asl:
								ExecuteOpAsl(ref _fetchOperand);
								break;
							case Instruction.Dec:
								ExecuteOpDec(ref _fetchOperand);
								break;
							case Instruction.Inc:
								ExecuteOpInc(ref _fetchOperand);
								break;
							case Instruction.Lsr:
								ExecuteOpLsr(ref _fetchOperand);
								break;
							case Instruction.Rol:
								ExecuteOpRol(ref _fetchOperand);
								break;
							case Instruction.Ror:
								ExecuteOpRor(ref _fetchOperand);
								break;
							case Instruction.IllDcp:
								ExecuteOpIllDcp(ref _fetchOperand);
								break;
							case Instruction.IllIsc:
								ExecuteOpIllIsc(ref _fetchOperand);
								break;
							case Instruction.IllRla:
								ExecuteOpIllRla(ref _fetchOperand);
								break;
							case Instruction.IllRra:
								ExecuteOpIllRra(ref _fetchOperand);
								break;
							case Instruction.IllSlo:
								ExecuteOpIllSlo(ref _fetchOperand);
								break;
							case Instruction.IllSre:
								ExecuteOpIllSre(ref _fetchOperand);
								break;
							default: throw new UnreachableException();
						}
						_step++;
						break;
					case 5: // write the new value to effective address
						WriteByte(_fetchAddress, _fetchOperand);
						_step = 0;
						break;
				}
				break;
			case Category.Write:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch low byte of address, increment PC
						_fetchLow = FetchByte();
						_step++;
						break;
					case 2: // fetch high byte of address, increment PC
						_fetchHigh = FetchByte();
						_step++;
						break;
					case 3: // write register to effective address
						_fetchAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						switch (CurrentOperation.Instruction)
						{
							case Instruction.Sta:
								WriteByte(_fetchAddress, ExecuteOpSta());
								break;
							case Instruction.Stx:
								WriteByte(_fetchAddress, ExecuteOpStx());
								break;
							case Instruction.Sty:
								WriteByte(_fetchAddress, ExecuteOpSty());
								break;
							case Instruction.IllSax:
								WriteByte(_fetchAddress, ExecuteOpIllSax());
								break;
							default: throw new UnreachableException();
						}
						_step = 0;
						break;
				}
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteAddrZeropage()
	{
		switch (CurrentOperation.Category)
		{
			case Category.Read:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch address, increment PC
						_fetchAddress = FetchByte();
						_step++;
						break;
					case 2: // read from effective address
						var value = ReadByte(_fetchAddress);
						switch (CurrentOperation.Instruction)
						{
							case Instruction.Adc:
								ExecuteOpAdc(value);
								break;
							case Instruction.And:
								ExecuteOpAnd(value);
								break;
							case Instruction.Bit:
								ExecuteOpBit(value);
								break;
							case Instruction.Cmp:
								ExecuteOpCmp(value);
								break;
							case Instruction.Cpx:
								ExecuteOpCpx(value);
								break;
							case Instruction.Cpy:
								ExecuteOpCpy(value);
								break;
							case Instruction.Eor:
								ExecuteOpEor(value);
								break;
							case Instruction.Lda:
								ExecuteOpLda(value);
								break;
							case Instruction.Ldx:
								ExecuteOpLdx(value);
								break;
							case Instruction.Ldy:
								ExecuteOpLdy(value);
								break;
							case Instruction.Nop: break;
							case Instruction.Ora:
								ExecuteOpOra(value);
								break;
							case Instruction.Sbc:
								ExecuteOpSbc(value);
								break;
							case Instruction.IllLax:
								ExecuteOpIllLax(value);
								break;
							default: throw new UnreachableException();
						}
						_step = 0;
						break;
				}
				break;
			case Category.ReadModifyWrite:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch address, increment PC
						_fetchAddress = FetchByte();
						_step++;
						break;
					case 2: // read from effective address
						_fetchOperand = ReadByte(_fetchAddress);
						_step++;
						break;
					case 3: // write the value back to effective address, and do the operation on it
						WriteByte(_fetchAddress, _fetchOperand);
						switch (CurrentOperation.Instruction)
						{
							case Instruction.Asl:
								ExecuteOpAsl(ref _fetchOperand);
								break;
							case Instruction.Dec:
								ExecuteOpDec(ref _fetchOperand);
								break;
							case Instruction.Inc:
								ExecuteOpInc(ref _fetchOperand);
								break;
							case Instruction.Lsr:
								ExecuteOpLsr(ref _fetchOperand);
								break;
							case Instruction.Rol:
								ExecuteOpRol(ref _fetchOperand);
								break;
							case Instruction.Ror:
								ExecuteOpRor(ref _fetchOperand);
								break;
							case Instruction.IllDcp:
								ExecuteOpIllDcp(ref _fetchOperand);
								break;
							case Instruction.IllIsc:
								ExecuteOpIllIsc(ref _fetchOperand);
								break;
							case Instruction.IllRla:
								ExecuteOpIllRla(ref _fetchOperand);
								break;
							case Instruction.IllRra:
								ExecuteOpIllRra(ref _fetchOperand);
								break;
							case Instruction.IllSlo:
								ExecuteOpIllSlo(ref _fetchOperand);
								break;
							case Instruction.IllSre:
								ExecuteOpIllSre(ref _fetchOperand);
								break;
							default: throw new UnreachableException();
						}
						_step++;
						break;
					case 4: // write the new value to effective address
						WriteByte(_fetchAddress, _fetchOperand);
						_step = 0;
						break;
				}
				break;
			case Category.Write:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch address, increment PC
						_fetchAddress = FetchByte();
						_step++;
						break;
					case 2: // write register to effective address
						switch (CurrentOperation.Instruction)
						{
							case Instruction.Sta:
								WriteByte(_fetchAddress, ExecuteOpSta());
								break;
							case Instruction.Stx:
								WriteByte(_fetchAddress, ExecuteOpStx());
								break;
							case Instruction.Sty:
								WriteByte(_fetchAddress, ExecuteOpSty());
								break;
							case Instruction.IllSax:
								WriteByte(_fetchAddress, ExecuteOpIllSax());
								break;
							default: throw new UnreachableException();
						}
						_step = 0;
						break;
				}
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteAddrZeropageIndexed()
	{
		var index = CurrentOperation.AddressingMode switch
		{
			AddressingMode.ZeropageXIndexed => _regX,
			AddressingMode.ZeropageYIndexed => _regY,
			_ => throw new UnreachableException()
		};

		switch (CurrentOperation.Category)
		{
			case Category.Read:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch address, increment PC
						_fetchAddress = FetchByte();
						_step++;
						break;
					case 2: // read from address, add index register to it
						ReadByte(_fetchAddress);
						_fetchAddress += index;
						_fetchAddress &= 0xFF;
						_step++;
						break;
					case 3: // read from effective address
						var value = ReadByte(_fetchAddress);
						switch (CurrentOperation.Instruction)
						{
							case Instruction.Adc:
								ExecuteOpAdc(value);
								break;
							case Instruction.And:
								ExecuteOpAnd(value);
								break;
							case Instruction.Bit:
								ExecuteOpBit(value);
								break;
							case Instruction.Cmp:
								ExecuteOpCmp(value);
								break;
							case Instruction.Cpx:
								ExecuteOpCpx(value);
								break;
							case Instruction.Cpy:
								ExecuteOpCpy(value);
								break;
							case Instruction.Eor:
								ExecuteOpEor(value);
								break;
							case Instruction.Lda:
								ExecuteOpLda(value);
								break;
							case Instruction.Ldx:
								ExecuteOpLdx(value);
								break;
							case Instruction.Ldy:
								ExecuteOpLdy(value);
								break;
							case Instruction.Nop: break;
							case Instruction.Ora:
								ExecuteOpOra(value);
								break;
							case Instruction.Sbc:
								ExecuteOpSbc(value);
								break;
							case Instruction.IllLax:
								ExecuteOpIllLax(value);
								break;
							default: throw new UnreachableException();
						}
						_step = 0;
						break;
				}
				break;
			case Category.ReadModifyWrite:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch address, increment PC
						_fetchAddress = FetchByte();
						_step++;
						break;
					case 2: // read from address, add index register X to it
						ReadByte(_fetchAddress);
						_fetchAddress += _regX;
						_fetchAddress &= 0xFF;
						_step++;
						break;
					case 3: // read from effective address
						_fetchOperand = ReadByte(_fetchAddress);
						_step++;
						break;
					case 4: // write the value back to effective address, and do the operation on it
						WriteByte(_fetchAddress, _fetchOperand);
						switch (CurrentOperation.Instruction)
						{
							case Instruction.Asl:
								ExecuteOpAsl(ref _fetchOperand);
								break;
							case Instruction.Dec:
								ExecuteOpDec(ref _fetchOperand);
								break;
							case Instruction.Inc:
								ExecuteOpInc(ref _fetchOperand);
								break;
							case Instruction.Lsr:
								ExecuteOpLsr(ref _fetchOperand);
								break;
							case Instruction.Rol:
								ExecuteOpRol(ref _fetchOperand);
								break;
							case Instruction.Ror:
								ExecuteOpRor(ref _fetchOperand);
								break;
							case Instruction.IllDcp:
								ExecuteOpIllDcp(ref _fetchOperand);
								break;
							case Instruction.IllIsc:
								ExecuteOpIllIsc(ref _fetchOperand);
								break;
							case Instruction.IllRla:
								ExecuteOpIllRla(ref _fetchOperand);
								break;
							case Instruction.IllRra:
								ExecuteOpIllRra(ref _fetchOperand);
								break;
							case Instruction.IllSlo:
								ExecuteOpIllSlo(ref _fetchOperand);
								break;
							case Instruction.IllSre:
								ExecuteOpIllSre(ref _fetchOperand);
								break;
							default: throw new UnreachableException();
						}
						_step++;
						break;
					case 5: // write the new value to effective address
						WriteByte(_fetchAddress, _fetchOperand);
						_step = 0;
						break;
				}
				break;
			case Category.Write:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch address, increment PC
						_fetchAddress = FetchByte();
						_step++;
						break;
					case 2: // read from address, add index register to it
						ReadByte(_fetchAddress);
						_fetchAddress += index;
						_fetchAddress &= 0xFF;
						_step++;
						break;
					case 3: // write to effective address
						switch (CurrentOperation.Instruction)
						{
							case Instruction.Sta:
								WriteByte(_fetchAddress, ExecuteOpSta());
								break;
							case Instruction.Stx:
								WriteByte(_fetchAddress, ExecuteOpStx());
								break;
							case Instruction.Sty:
								WriteByte(_fetchAddress, ExecuteOpSty());
								break;
							case Instruction.IllSax:
								WriteByte(_fetchAddress, ExecuteOpIllSax());
								break;
							default: throw new UnreachableException();
						}
						_step = 0;
						break;
				}
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteAddrAbsoluteIndexed()
	{
		var index = CurrentOperation.AddressingMode switch
		{
			AddressingMode.AbsoluteXIndexed => _regX,
			AddressingMode.AbsoluteYIndexed => _regY,
			_ => throw new UnreachableException()
		};

		switch (CurrentOperation.Category)
		{
			case Category.Read:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch low byte of address, increment PC
						_fetchLow = FetchByte();
						_step++;
						break;
					case 2: // fetch high byte of address, add index register to low address byte, increment PC
						_fetchHigh = FetchByte();
						var oldLow = _fetchLow;
						_fetchLow += index;
						_pageBoundaryCrossed = oldLow + index != _fetchLow;
						_step++;
						break;
					case 3: // read from effective address, fix the high byte of effective address
						_fetchAddress = (ushort)((_fetchHigh << 8) | _fetchLow);

						// If page boundary was not crossed, do not incur penalty cycle, address is correct
						//  and will only be read once immediately in this cycle, finishing the instruction
						if (!_pageBoundaryCrossed)
							goto case 4;

						// If page boundary was crossed, read from incorrect address,
						//  fix the high byte and incur a penalty cycle
						ReadByte(_fetchAddress);

						_fetchHigh++;
						_fetchAddress = (ushort)((_fetchHigh << 8) | _fetchLow);

						_step++;
						break;
					case 4: // re-read from effective address
						var value = ReadByte(_fetchAddress);
						switch (CurrentOperation.Instruction)
						{
							case Instruction.Adc:
								ExecuteOpAdc(value);
								break;
							case Instruction.And:
								ExecuteOpAnd(value);
								break;
							case Instruction.Bit:
								ExecuteOpBit(value);
								break;
							case Instruction.Cmp:
								ExecuteOpCmp(value);
								break;
							case Instruction.Cpx:
								ExecuteOpCpx(value);
								break;
							case Instruction.Cpy:
								ExecuteOpCpy(value);
								break;
							case Instruction.Eor:
								ExecuteOpEor(value);
								break;
							case Instruction.Lda:
								ExecuteOpLda(value);
								break;
							case Instruction.Ldx:
								ExecuteOpLdx(value);
								break;
							case Instruction.Ldy:
								ExecuteOpLdy(value);
								break;
							case Instruction.Nop: break;
							case Instruction.Ora:
								ExecuteOpOra(value);
								break;
							case Instruction.Sbc:
								ExecuteOpSbc(value);
								break;
							// TODO: case Instruction.IllLas: ExecuteOpLas(value); break;
							case Instruction.IllLax:
								ExecuteOpIllLax(value);
								break;
							// TODO: case Instruction.IllShs: ExecuteOpShs(value); break;
							default: throw new UnreachableException();
						}
						_step = 0;
						break;
				}
				break;
			case Category.ReadModifyWrite:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch low byte of address, increment PC
						_fetchLow = FetchByte();
						_step++;
						break;
					case 2: // fetch high byte of address, add index register X to low address byte, increment PC
						_fetchHigh = FetchByte();
						var oldLow = _fetchLow;
						_fetchLow += index;
						_pageBoundaryCrossed = oldLow + index != _fetchLow;
						_step++;
						break;
					case 3: // read from effective address, fix the high byte of effective address
						_fetchAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						ReadByte(_fetchAddress);

						if (_pageBoundaryCrossed)
							_fetchHigh++;

						_step++;
						break;
					case 4:
						_fetchAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						_fetchOperand = ReadByte(_fetchAddress);
						_step++;
						break;
					case 5: // write the value back to effective address, and do the operation on it
						WriteByte(_fetchAddress, _fetchOperand);
						switch (CurrentOperation.Instruction)
						{
							case Instruction.Asl:
								ExecuteOpAsl(ref _fetchOperand);
								break;
							case Instruction.Dec:
								ExecuteOpDec(ref _fetchOperand);
								break;
							case Instruction.Inc:
								ExecuteOpInc(ref _fetchOperand);
								break;
							case Instruction.Lsr:
								ExecuteOpLsr(ref _fetchOperand);
								break;
							case Instruction.Rol:
								ExecuteOpRol(ref _fetchOperand);
								break;
							case Instruction.Ror:
								ExecuteOpRor(ref _fetchOperand);
								break;
							case Instruction.IllDcp:
								ExecuteOpIllDcp(ref _fetchOperand);
								break;
							case Instruction.IllIsc:
								ExecuteOpIllIsc(ref _fetchOperand);
								break;
							case Instruction.IllRla:
								ExecuteOpIllRla(ref _fetchOperand);
								break;
							case Instruction.IllRra:
								ExecuteOpIllRra(ref _fetchOperand);
								break;
							case Instruction.IllSlo:
								ExecuteOpIllSlo(ref _fetchOperand);
								break;
							case Instruction.IllSre:
								ExecuteOpIllSre(ref _fetchOperand);
								break;
							default: throw new UnreachableException();
						}
						_step++;
						break;
					case 6: // write the new value to effective address
						WriteByte(_fetchAddress, _fetchOperand);
						_step = 0;
						break;
				}
				break;
			case Category.Write:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch low byte of address, increment PC
						_fetchLow = FetchByte();
						_step++;
						break;
					case 2: // fetch high byte of address, add index register to low address byte, increment PC
						_fetchHigh = FetchByte();
						var oldLow = _fetchLow;
						_fetchLow += index;
						_pageBoundaryCrossed = oldLow + index != _fetchLow;
						_step++;
						break;
					case 3: // read from effective address, fix the high byte of effective address
						_fetchAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						ReadByte(_fetchAddress);

						if (_pageBoundaryCrossed)
							_fetchHigh++;

						_step++;
						break;
					case 4: // write to effective address
						_fetchAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						switch (CurrentOperation.Instruction)
						{
							case Instruction.Sta:
								WriteByte(_fetchAddress, ExecuteOpSta());
								break;
							case Instruction.Stx:
								WriteByte(_fetchAddress, ExecuteOpStx());
								break;
							case Instruction.Sty:
								WriteByte(_fetchAddress, ExecuteOpSty());
								break;
							// TODO: case Instruction.IllSha: WriteByte(_fetchedAddress, ExecuteOpIllSha()); break;
							// TODO: case Instruction.IllShx: WriteByte(_fetchedAddress, ExecuteOpIllShx()); break;
							// TODO: case Instruction.IllShy: WriteByte(_fetchedAddress, ExecuteOpIllShy()); break;
							default: throw new UnreachableException();
						}
						_step = 0;
						break;
				}
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteAddrRelative()
	{
		var condition = CurrentOperation.Instruction switch
		{
			Instruction.Bcc => !_flagCarry,
			Instruction.Bcs => _flagCarry,
			Instruction.Bmi => _flagNegative,
			Instruction.Beq => _flagZero,
			Instruction.Bne => !_flagZero,
			Instruction.Bpl => !_flagNegative,
			Instruction.Bvc => !_flagOverflow,
			Instruction.Bvs => _flagOverflow,
			_ => throw new UnreachableException()
		};

		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // fetch operand, increment PC
				_fetchOperand = FetchByte();
				if (!condition)
				{
					_step = 0;
					break;
				}
				_step++;
				break;
			case 2: // Fetch opcode of next instruction, If branch is taken, add operand to PCL. Otherwise increment PC.
				var oldPc = _regPc;
				_regPc = (ushort)(_regPc + (sbyte)_fetchOperand);

				if ((oldPc & 0xFF00) == (_regPc & 0xFF00)) // If not crossing page boundary, this is the end
				{
					_step = 0;
					break;
				}

				_step++;
				break;
			case 3: // Fetch opcode of next instruction. Fix PCH. If it did not change, increment PC.
				// PCH was never "broken", there is no point in "unfixing" it in cycle 3, just so we can fix it again here, so just do nothing
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteAddrIndirectYIndexed()
	{
		switch (CurrentOperation.Category)
		{
			case Category.Read:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch pointer address, increment PC
						_fetchAddress = FetchByte();
						_step++;
						break;
					case 2: // fetch effective address low
						_fetchLow = ReadByte(_fetchAddress);
						_fetchAddress++;
						_fetchAddress &= 0x00FF;
						_step++;
						break;
					case 3: // fetch effective address high, add Y to low byte of effective address
						_fetchHigh = ReadByte(_fetchAddress);
						_fetchAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						_pageBoundaryCrossed = (_fetchAddress & 0xFF00) != ((_fetchAddress + _regY) & 0xFF00);
						_fetchAddress += _regY;
						_step++;
						break;
					case 4: // read from effective address, fix high byte of effective address
						if (!_pageBoundaryCrossed) // If no page boundary was crossed, immediately perform the operation
							goto case 5;

						// Emulate first incorrect memory access for unfixed address
						ReadByte((ushort)(_fetchAddress - 0x0100));
						_step++;
						break;
					case 5: // read from effective address
						var value = ReadByte(_fetchAddress);
						switch (CurrentOperation.Instruction)
						{
							case Instruction.Adc:
								ExecuteOpAdc(value);
								break;
							case Instruction.And:
								ExecuteOpAnd(value);
								break;
							case Instruction.Cmp:
								ExecuteOpCmp(value);
								break;
							case Instruction.Eor:
								ExecuteOpEor(value);
								break;
							case Instruction.Lda:
								ExecuteOpLda(value);
								break;
							case Instruction.Ora:
								ExecuteOpOra(value);
								break;
							case Instruction.Sbc:
								ExecuteOpSbc(value);
								break;
							case Instruction.IllLax:
								ExecuteOpIllLax(value);
								break;
							default: throw new UnreachableException();
						}
						_step = 0;
						break;
				}
				break;
			case Category.ReadModifyWrite:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch pointer address, increment PC
						_fetchAddress = FetchByte();
						_step++;
						break;
					case 2: // fetch effective address low
						_fetchLow = ReadByte(_fetchAddress);
						_fetchAddress++;
						_fetchAddress &= 0x00FF;
						_step++;
						break;
					case 3: // fetch effective address high, add Y to low byte of effective address
						_fetchHigh = ReadByte(_fetchAddress);
						_fetchAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						_pageBoundaryCrossed = (_fetchAddress & 0xFF00) != ((_fetchAddress + _regY) & 0xFF00);
						_fetchAddress += _regY;
						_step++;
						break;
					case 4: // read from effective address, fix high byte of effective address
						ReadByte(_pageBoundaryCrossed ? (ushort)(_fetchAddress - 0x0100) : _fetchAddress);
						_step++;
						break;
					case 5: // read from effective address
						_fetchOperand = ReadByte(_fetchAddress);
						_step++;
						break;
					case 6: // write the value back to effective address, and do the operation on it
						WriteByte(_fetchAddress, _fetchOperand);
						switch (CurrentOperation.Instruction)
						{
							case Instruction.IllRla:
								ExecuteOpIllRla(ref _fetchOperand);
								break;
							case Instruction.IllRra:
								ExecuteOpIllRra(ref _fetchOperand);
								break;
							case Instruction.IllIsc:
								ExecuteOpIllIsc(ref _fetchOperand);
								break;
							case Instruction.IllSlo:
								ExecuteOpIllSlo(ref _fetchOperand);
								break;
							case Instruction.IllSre:
								ExecuteOpIllSre(ref _fetchOperand);
								break;
							case Instruction.IllDcp:
								ExecuteOpIllDcp(ref _fetchOperand);
								break;
							default: throw new UnreachableException();
						}
						_step++;
						break;
					case 7: // write the new value to effective address
						WriteByte(_fetchAddress, _fetchOperand);
						_step = 0;
						break;
				}
				break;
			case Category.Write:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch pointer address, increment PC
						_fetchAddress = FetchByte();
						_step++;
						break;
					case 2: // fetch effective address low
						_fetchLow = ReadByte(_fetchAddress);
						_fetchAddress++;
						_fetchAddress &= 0x00FF;
						_step++;
						break;
					case 3: // fetch effective address high, add Y to low byte of effective address
						_fetchHigh = ReadByte(_fetchAddress);
						_fetchAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						_pageBoundaryCrossed = (_fetchAddress & 0xFF00) != ((_fetchAddress + _regY) & 0xFF00);
						_fetchAddress += _regY;
						_step++;
						break;
					case 4: // read from effective address, fix high byte of effective address
						ReadByte(_pageBoundaryCrossed ? (ushort)(_fetchAddress - 0x0100) : _fetchAddress);
						_step++;
						break;
					case 5: // write to effective address
						switch (CurrentOperation.Instruction)
						{
							case Instruction.Sta:
								WriteByte(_fetchAddress, ExecuteOpSta());
								break;
							//case Instruction.IllSha: WriteByte(_fetchAddress, ExecuteOpIllSha()); break;
							default: throw new UnreachableException();
						}
						_step = 0;
						break;
				}
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteAddrXIndexedIndirect()
	{
		switch (CurrentOperation.Category)
		{
			case Category.Read:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch pointer address, increment PC
						_fetchAddress = FetchByte();
						_step++;
						break;
					case 2: // read from the address, add X to it
						ReadByte(_fetchAddress);
						_fetchAddress += _regX;
						_fetchAddress &= 0x00FF;
						_step++;
						break;
					case 3: // fetch effective address low
						_fetchLow = ReadByte(_fetchAddress);
						_fetchAddress++;
						_fetchAddress &= 0x00FF;
						_step++;
						break;
					case 4: // fetch effective address high
						_fetchHigh = ReadByte(_fetchAddress);
						_fetchAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						_step++;
						break;
					case 5: // read from effective address
						var value = ReadByte(_fetchAddress);
						switch (CurrentOperation.Instruction)
						{
							case Instruction.Adc:
								ExecuteOpAdc(value);
								break;
							case Instruction.And:
								ExecuteOpAnd(value);
								break;
							case Instruction.Cmp:
								ExecuteOpCmp(value);
								break;
							case Instruction.Eor:
								ExecuteOpEor(value);
								break;
							case Instruction.Lda:
								ExecuteOpLda(value);
								break;
							case Instruction.Ora:
								ExecuteOpOra(value);
								break;
							case Instruction.Sbc:
								ExecuteOpSbc(value);
								break;
							case Instruction.IllLax:
								ExecuteOpIllLax(value);
								break;
							default: throw new UnreachableException();
						}
						_step = 0;
						break;
				}
				break;
			case Category.ReadModifyWrite:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch pointer address, increment PC
						_fetchAddress = FetchByte();
						_step++;
						break;
					case 2: // read from the address, add X to it
						ReadByte(_fetchAddress);
						_fetchAddress += _regX;
						_fetchAddress &= 0x00FF;
						_step++;
						break;
					case 3: // fetch effective address low
						_fetchLow = ReadByte(_fetchAddress);
						_fetchAddress++;
						_fetchAddress &= 0x00FF;
						_step++;
						break;
					case 4: // fetch effective address high
						_fetchHigh = ReadByte(_fetchAddress);
						_fetchAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						_step++;
						break;
					case 5: // read from effective address
						_fetchOperand = ReadByte(_fetchAddress);
						_step++;
						break;
					case 6: // write the value back to effective address, and do the operation on it
						WriteByte(_fetchAddress, _fetchOperand);
						switch (CurrentOperation.Instruction)
						{
							case Instruction.IllRla:
								ExecuteOpIllRla(ref _fetchOperand);
								break;
							case Instruction.IllRra:
								ExecuteOpIllRra(ref _fetchOperand);
								break;
							case Instruction.IllIsc:
								ExecuteOpIllIsc(ref _fetchOperand);
								break;
							case Instruction.IllSlo:
								ExecuteOpIllSlo(ref _fetchOperand);
								break;
							case Instruction.IllSre:
								ExecuteOpIllSre(ref _fetchOperand);
								break;
							case Instruction.IllDcp:
								ExecuteOpIllDcp(ref _fetchOperand);
								break;
							default: throw new UnreachableException();
						}
						_step++;
						break;
					case 7: // write the new value to effective address
						WriteByte(_fetchAddress, _fetchOperand);
						_step = 0;
						break;
				}
				break;
			case Category.Write:
				switch (_step)
				{
					case 0: // fetch opcode, increment PC
						_step++;
						break;
					case 1: // fetch pointer address, increment PC
						_fetchAddress = FetchByte();
						_step++;
						break;
					case 2: // read from the address, add X to it
						ReadByte(_fetchAddress);
						_fetchAddress += _regX;
						_fetchAddress &= 0x00FF;
						_step++;
						break;
					case 3: // fetch effective address low
						_fetchLow = ReadByte(_fetchAddress);
						_fetchAddress++;
						_fetchAddress &= 0x00FF;
						_step++;
						break;
					case 4: // fetch effective address high
						_fetchHigh = ReadByte(_fetchAddress);
						_fetchAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						_step++;
						break;
					case 5: // write to effective address
						switch (CurrentOperation.Instruction)
						{
							case Instruction.Sta:
								WriteByte(_fetchAddress, ExecuteOpSta());
								break;
							case Instruction.IllSax:
								WriteByte(_fetchAddress, ExecuteOpIllSax());
								break;
							default: throw new UnreachableException();
						}
						_step = 0;
						break;
				}
				break;
			default:
				throw new UnreachableException();
		}
	}

	#endregion

	#region Special instructions

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBrk()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // read next instruction byte (and throw it away), increment PC
				if (!_inIrq)
					FetchByte();
				_step++;
				break;
			case 2: // push PCH on stack, decrement S
				WriteByte(RegSp, (byte)((_regPc) >> 8));
				_regSpLo--;
				_step++;
				break;
			case 3: // push PCL on stack, decrement S
				WriteByte(RegSp, (byte)((_regPc) & 0xFF));
				_regSpLo--;
				_step++;
				break;
			case 4: // push P on stack (with B flag set), decrement S
				_flagB = !_inIrq;
				_inIrq = false;
				WriteByte(RegSp, RegStatus);
				_regSpLo--;
				_flagB = false;
				_step++;
				break;
			case 5: // fetch PCL
				_fetchLow = ReadByte(0xFFFE);
				_step++;
				break;
			case 6: // fetch PCH
				_fetchHigh = ReadByte(0xFFFF);
				_regPc = (ushort)((_fetchHigh << 8) | _fetchLow);
				FlagInterruptDisable = true;
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstRti()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // read next instruction byte (and throw it away)
				ReadByte(_regPc);
				_step++;
				break;
			case 2: // increment S
				_regSpLo++;
				_step++;
				break;
			case 3: // pull P from stack, increment S
				RegStatus = ReadByte(RegSp);
				_regSpLo++;
				_flagB = false;
				_step++;
				break;
			case 4: // pull PCL from stack, increment S
				_regPc = ReadByte(RegSp);
				_regSpLo++;
				_step++;
				break;
			case 5: // pull PCH from stack
				_regPc |= (ushort)(ReadByte(RegSp) << 8);
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstRts()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // read next instruction byte (and throw it away)
				ReadByte(_regPc);
				_step++;
				break;
			case 2: // increment S
				_regSpLo++;
				_step++;
				break;
			case 3: // pull PCL from stack, increment S
				_regPc = ReadByte(RegSp);
				_regSpLo++;
				_step++;
				break;
			case 4: // pull PCH from stack
				_regPc |= (ushort)(ReadByte(RegSp) << 8);
				_step++;
				break;
			case 5: // increment PC
				_regPc++;
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstPha()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // read next instruction byte (and throw it away)
				ReadByte(_regPc);
				_step++;
				break;
			case 2: // push register on stack, decrement S
				WriteByte(RegSp, _regA);
				_regSpLo--;
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstPhp()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // read next instruction byte (and throw it away)
				ReadByte(_regPc);
				_step++;
				break;
			case 2: // push register on stack, decrement S
				_flagB = true;
				WriteByte(RegSp, RegStatus);
				_flagB = false;
				_regSpLo--;
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstPla()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // read next instruction byte (and throw it away)
				ReadByte(_regPc);
				_step++;
				break;
			case 2: // increment S
				_regSpLo++;
				_step++;
				break;
			case 3: // pull register from stack
				_regA = ReadByte(RegSp);
				_flagNegative = ((_regA >> 7) & 1) != 0;
				_flagZero = _regA == 0;
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstPlp()
	{
		switch (_step)
		{
			case 0: // fetch opcode, increment PC
				_step++;
				break;
			case 1: // read next instruction byte (and throw it away)
				ReadByte(_regPc);
				_step++;
				break;
			case 2: // increment S
				_regSpLo++;
				_step++;
				break;
			case 3: // pull register from stack
				RegStatus = ReadByte(RegSp);
				_flagB = false;
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	#endregion

	#region Operations

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpAdc(byte value)
	{
		var result = _regA + value + (_flagCarry ? 1 : 0);
		var overflow = (_regA & (1 << 7)) == (value & (1 << 7)) && (value & (1 << 7)) != (result & (1 << 7));

		_regA = (byte)result;

		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
		_flagCarry = result > byte.MaxValue;
		_flagOverflow = overflow;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpAnd(byte value)
	{
		_regA &= value;
		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpAsl(ref byte value)
	{
		var carry = ((value >> 7) & 1) != 0;

		value <<= 1;

		_flagNegative = ((value >> 7) & 1) != 0;
		_flagZero = value == 0;
		_flagCarry = carry;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpBit(byte value)
	{
		_flagNegative = ((value >> 7) & 1) != 0;
		_flagZero = (_regA & value) == 0;
		_flagOverflow = ((value >> 6) & 1) != 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpClc() => _flagCarry = false;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpCld() => _flagDecimal = false;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpCli() => FlagInterruptDisable = false;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpClv() => _flagOverflow = false;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpCmp(byte value)
	{
		var result = _regA - value;

		_flagNegative = ((result >> 7) & 1) != 0;
		_flagZero = _regA == value;
		_flagCarry = _regA >= value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpCpx(byte value)
	{
		var result = _regX - value;

		_flagNegative = ((result >> 7) & 1) != 0;
		_flagZero = _regX == value;
		_flagCarry = _regX >= value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpCpy(byte value)
	{
		var result = _regY - value;

		_flagNegative = ((result >> 7) & 1) != 0;
		_flagZero = _regY == value;
		_flagCarry = _regY >= value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpIllDcp(ref byte value)
	{
		value--;
		var result = _regA - value;

		_flagNegative = ((result >> 7) & 1) != 0;
		_flagZero = _regA == value;
		_flagCarry = _regA >= value;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpDec(ref byte value)
	{
		value--;

		_flagNegative = ((value >> 7) & 1) != 0;
		_flagZero = value == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpDex()
	{
		_regX--;
		_flagNegative = (_regX & (1 << 7)) != 0;
		_flagZero = _regX == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpDey()
	{
		_regY--;
		_flagNegative = (_regY & (1 << 7)) != 0;
		_flagZero = _regY == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpEor(byte value)
	{
		_regA ^= value;
		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpInc(ref byte value)
	{
		value++;

		_flagNegative = ((value >> 7) & 1) != 0;
		_flagZero = value == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpInx()
	{
		_regX++;
		_flagNegative = (_regX & (1 << 7)) != 0;
		_flagZero = _regX == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpIny()
	{
		_regY++;
		_flagNegative = (_regY & (1 << 7)) != 0;
		_flagZero = _regY == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpLda(byte value)
	{
		_regA = value;
		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpLdx(byte value)
	{
		_regX = value;
		_flagNegative = ((_regX >> 7) & 1) != 0;
		_flagZero = _regX == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpLdy(byte value)
	{
		_regY = value;
		_flagNegative = ((_regY >> 7) & 1) != 0;
		_flagZero = _regY == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpLsr(ref byte value)
	{
		var carry = (value & 1) != 0;

		value >>= 1;

		_flagNegative = ((value >> 7) & 1) != 0;
		_flagZero = value == 0;
		_flagCarry = carry;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpOra(byte value)
	{
		_regA |= value;
		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpRol(ref byte value)
	{
		var carry = ((value >> 7) & 1) != 0;

		value <<= 1;
		value |= (byte)(_flagCarry ? 1 : 0);

		_flagNegative = ((value >> 7) & 1) != 0;
		_flagZero = value == 0;
		_flagCarry = carry;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpRor(ref byte value)
	{
		var carry = (value & 1) != 0;

		value >>= 1;
		value |= (byte)((_flagCarry ? 1 : 0) << 7);

		_flagNegative = ((value >> 7) & 1) != 0;
		_flagZero = value == 0;
		_flagCarry = carry;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpSbc(byte value)
	{
		var result = _regA - value - (_flagCarry ? 0 : 1);
		var underflow = (_regA & (1 << 7)) == ((255 - value) & (1 << 7)) && ((255 - value) & (1 << 7)) != (result & (1 << 7));

		_regA = (byte)result;

		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
		_flagCarry = result >= 0;
		_flagOverflow = underflow;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpSec() => _flagCarry = true;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpSed() => _flagDecimal = true;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpSei() => FlagInterruptDisable = true;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private byte ExecuteOpSta() => _regA;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private byte ExecuteOpStx() => _regX;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private byte ExecuteOpSty() => _regY;

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpTax()
	{
		_regX = _regA;
		_flagNegative = ((_regX >> 7) & 1) != 0;
		_flagZero = _regX == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpTay()
	{
		_regY = _regA;
		_flagNegative = ((_regY >> 7) & 1) != 0;
		_flagZero = _regY == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpTsx()
	{
		_regX = _regSpLo;
		_flagNegative = ((_regX >> 7) & 1) != 0;
		_flagZero = _regX == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpTxa()
	{
		_regA = _regX;
		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpTxs()
	{
		_regSpLo = _regX;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpTya()
	{
		_regA = _regY;
		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpIllIsc(ref byte value)
	{
		value++;

		var result = _regA - value - (_flagCarry ? 0 : 1);
		var underflow = (_regA & (1 << 7)) == ((255 - value) & (1 << 7)) && ((255 - value) & (1 << 7)) != (result & (1 << 7));

		_regA = (byte)result;

		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
		_flagCarry = result >= 0;
		_flagOverflow = underflow;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpIllLax(byte value)
	{
		_regA = value;
		_regX = value;

		_flagNegative = ((value >> 7) & 1) != 0;
		_flagZero = value == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpIllRla(ref byte value)
	{
		var carry = ((value >> 7) & 1) != 0;

		value <<= 1;
		value |= (byte)(_flagCarry ? 1 : 0);

		_regA &= value;

		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
		_flagCarry = carry;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpIllRra(ref byte value)
	{
		var carry = (value & 1) != 0;

		value >>= 1;
		value |= (byte)((_flagCarry ? 1 : 0) << 7);

		var result = _regA + value + (carry ? 1 : 0);
		var overflow = (_regA & (1 << 7)) == (value & (1 << 7)) && (value & (1 << 7)) != (result & (1 << 7));

		_regA = (byte)result;

		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
		_flagCarry = result > byte.MaxValue;
		_flagOverflow = overflow;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private byte ExecuteOpIllSax() => (byte)(_regA & _regX);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpIllSlo(ref byte value)
	{
		var carry = ((value >> 7) & 1) != 0;

		value <<= 1;

		_flagNegative = ((value >> 7) & 1) != 0;
		_flagCarry = carry;

		_regA |= value;
		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteOpIllSre(ref byte value)
	{
		var carry = (value & 1) != 0;

		value >>= 1;

		_regA ^= value;

		_flagCarry = carry;
		_flagNegative = ((_regA >> 7) & 1) != 0;
		_flagZero = _regA == 0;
	}

	#endregion
}
