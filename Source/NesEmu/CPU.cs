using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NesEmu;

// https://www.pagetable.com/?p=410
// https://www.masswerk.at/6502/6502_instruction_set.html
// https://www.nesdev.org/obelisk-6502-guide/reference.html
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

	private readonly struct InstructionInfo(Instruction instruction, string mnemonic, AddressingMode addressingMode)
	{
		public readonly Instruction Instruction = instruction;
		public readonly string mnemonic = mnemonic;
		public readonly AddressingMode AddressingMode = addressingMode;

		public ushort Bytes => AddressingMode switch
		{
			AddressingMode.Implied => 1,
			AddressingMode.Immediate => 2,
			AddressingMode.Relative => 2,
			AddressingMode.Accumulator => 1,

			AddressingMode.Absolute => 3,
			AddressingMode.AbsoluteXIndexed => 3,
			AddressingMode.AbsoluteYIndexed => 3,

			AddressingMode.Indirect => 3,
			AddressingMode.XIndexedIndirect => 2,
			AddressingMode.IndirectYIndexed => 2,

			AddressingMode.Zeropage => 2,
			AddressingMode.ZeropageXIndexed => 2,
			AddressingMode.ZeropageYIndexed => 2,
			_ => throw new InvalidOperationException()
		};
	}

	private readonly InstructionInfo[] _instructions =
	[
		// 0x00 - 0x0F
		new(Instruction.Brk, "BRK", AddressingMode.Implied),
		new(Instruction.Ora, "ORA", AddressingMode.XIndexedIndirect),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied),
		new(Instruction.IllSlo, "*SLO", AddressingMode.XIndexedIndirect),
		new(Instruction.Nop, "*NOP", AddressingMode.Zeropage),
		new(Instruction.Ora, "ORA", AddressingMode.Zeropage),
		new(Instruction.Asl, "ASL", AddressingMode.Zeropage),
		new(Instruction.IllSlo, "*SLO", AddressingMode.Zeropage),
		new(Instruction.Php, "PHP", AddressingMode.Implied),
		new(Instruction.Ora, "ORA", AddressingMode.Immediate),
		new(Instruction.Asl, "ASL", AddressingMode.Accumulator),
		new(Instruction.Unknown, "*ANC", AddressingMode.Immediate),
		new(Instruction.Nop, "*NOP", AddressingMode.Absolute),
		new(Instruction.Ora, "ORA", AddressingMode.Absolute),
		new(Instruction.Asl, "ASL", AddressingMode.Absolute),
		new(Instruction.IllSlo, "*SLO", AddressingMode.Absolute),
		// 0x10 - 0x1F
		new(Instruction.Bpl, "BPL", AddressingMode.Relative),
		new(Instruction.Ora, "ORA", AddressingMode.IndirectYIndexed),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied),
		new(Instruction.IllSlo, "*SLO", AddressingMode.IndirectYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.ZeropageXIndexed),
		new(Instruction.Ora, "ORA", AddressingMode.ZeropageXIndexed),
		new(Instruction.Asl, "ASL", AddressingMode.ZeropageXIndexed),
		new(Instruction.IllSlo, "*SLO", AddressingMode.ZeropageXIndexed),
		new(Instruction.Clc, "CLC", AddressingMode.Implied),
		new(Instruction.Ora, "ORA", AddressingMode.AbsoluteYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.Implied),
		new(Instruction.IllSlo, "*SLO", AddressingMode.AbsoluteYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.AbsoluteXIndexed),
		new(Instruction.Ora, "ORA", AddressingMode.AbsoluteXIndexed),
		new(Instruction.Asl, "ASL", AddressingMode.AbsoluteXIndexed),
		new(Instruction.IllSlo, "*SLO", AddressingMode.AbsoluteXIndexed),
		// 0x20 - 0x2F
		new(Instruction.Jsr, "JSR", AddressingMode.Absolute),
		new(Instruction.And, "AND", AddressingMode.XIndexedIndirect),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied),
		new(Instruction.IllRla, "*RLA", AddressingMode.XIndexedIndirect),
		new(Instruction.Bit, "BIT", AddressingMode.Zeropage),
		new(Instruction.And, "AND", AddressingMode.Zeropage),
		new(Instruction.Rol, "ROL", AddressingMode.Zeropage),
		new(Instruction.IllRla, "*RLA", AddressingMode.Zeropage),
		new(Instruction.Plp, "PLP", AddressingMode.Implied),
		new(Instruction.And, "AND", AddressingMode.Immediate),
		new(Instruction.Rol, "ROL", AddressingMode.Accumulator),
		new(Instruction.Unknown, "*ANC", AddressingMode.Immediate),
		new(Instruction.Bit, "BIT", AddressingMode.Absolute),
		new(Instruction.And, "AND", AddressingMode.Absolute),
		new(Instruction.Rol, "ROL", AddressingMode.Absolute),
		new(Instruction.IllRla, "*RLA", AddressingMode.Absolute),
		// 0x30 - 0x3F
		new(Instruction.Bmi, "BMI", AddressingMode.Relative),
		new(Instruction.And, "AND", AddressingMode.IndirectYIndexed),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied),
		new(Instruction.IllRla, "*RLA", AddressingMode.IndirectYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.ZeropageXIndexed),
		new(Instruction.And, "AND", AddressingMode.ZeropageXIndexed),
		new(Instruction.Rol, "ROL", AddressingMode.ZeropageXIndexed),
		new(Instruction.IllRla, "*RLA", AddressingMode.ZeropageXIndexed),
		new(Instruction.Sec, "SEC", AddressingMode.Implied),
		new(Instruction.And, "AND", AddressingMode.AbsoluteYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.Implied),
		new(Instruction.IllRla, "*RLA", AddressingMode.AbsoluteYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.AbsoluteXIndexed),
		new(Instruction.And, "AND", AddressingMode.AbsoluteXIndexed),
		new(Instruction.Rol, "ROL", AddressingMode.AbsoluteXIndexed),
		new(Instruction.IllRla, "*RLA", AddressingMode.AbsoluteXIndexed),
		// 0x40 - 0x4F
		new(Instruction.Rti, "RTI", AddressingMode.Implied),
		new(Instruction.Eor, "EOR", AddressingMode.XIndexedIndirect),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied),
		new(Instruction.IllSre, "*SRE", AddressingMode.XIndexedIndirect),
		new(Instruction.Nop, "*NOP", AddressingMode.Zeropage),
		new(Instruction.Eor, "EOR", AddressingMode.Zeropage),
		new(Instruction.Lsr, "LSR", AddressingMode.Zeropage),
		new(Instruction.IllSre, "*SRE", AddressingMode.Zeropage),
		new(Instruction.Pha, "PHA", AddressingMode.Implied),
		new(Instruction.Eor, "EOR", AddressingMode.Immediate),
		new(Instruction.Lsr, "LSR", AddressingMode.Accumulator),
		new(Instruction.Unknown, "ALR", AddressingMode.Immediate),
		new(Instruction.Jmp, "JMP", AddressingMode.Absolute),
		new(Instruction.Eor, "EOR", AddressingMode.Absolute),
		new(Instruction.Lsr, "LSR", AddressingMode.Absolute),
		new(Instruction.IllSre, "*SRE", AddressingMode.Absolute),
		// 0x50 - 0x5F
		new(Instruction.Bvc, "BVC", AddressingMode.Relative),
		new(Instruction.Eor, "EOR", AddressingMode.IndirectYIndexed),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied),
		new(Instruction.IllSre, "*SRE", AddressingMode.IndirectYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.ZeropageXIndexed),
		new(Instruction.Eor, "EOR", AddressingMode.ZeropageXIndexed),
		new(Instruction.Lsr, "LSR", AddressingMode.ZeropageXIndexed),
		new(Instruction.IllSre, "*SRE", AddressingMode.ZeropageXIndexed),
		new(Instruction.Cli, "CLI", AddressingMode.Implied),
		new(Instruction.Eor, "EOR", AddressingMode.AbsoluteYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.Implied),
		new(Instruction.IllSre, "*SRE", AddressingMode.AbsoluteYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.AbsoluteXIndexed),
		new(Instruction.Eor, "EOR", AddressingMode.AbsoluteXIndexed),
		new(Instruction.Lsr, "LSR", AddressingMode.AbsoluteXIndexed),
		new(Instruction.IllSre, "*SRE", AddressingMode.AbsoluteXIndexed),
		// 0x60 - 0x6F
		new(Instruction.Rts, "RTS", AddressingMode.Implied),
		new(Instruction.Adc, "ADC", AddressingMode.XIndexedIndirect),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied),
		new(Instruction.IllRra, "*RRA", AddressingMode.XIndexedIndirect),
		new(Instruction.Nop, "*NOP", AddressingMode.Zeropage),
		new(Instruction.Adc, "ADC", AddressingMode.Zeropage),
		new(Instruction.Ror, "ROR", AddressingMode.Zeropage),
		new(Instruction.IllRra, "*RRA", AddressingMode.Zeropage),
		new(Instruction.Pla, "PLA", AddressingMode.Implied),
		new(Instruction.Adc, "ADC", AddressingMode.Immediate),
		new(Instruction.Ror, "ROR", AddressingMode.Accumulator),
		new(Instruction.Unknown, "*ARR", AddressingMode.Immediate),
		new(Instruction.Jmp, "JMP", AddressingMode.Indirect),
		new(Instruction.Adc, "ADC", AddressingMode.Absolute),
		new(Instruction.Ror, "ROR", AddressingMode.Absolute),
		new(Instruction.IllRra, "*RRA", AddressingMode.Absolute),
		// 0x70 - 0x7F
		new(Instruction.Bvs, "BVS", AddressingMode.Relative),
		new(Instruction.Adc, "ADC", AddressingMode.IndirectYIndexed),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied),
		new(Instruction.IllRra, "*RRA", AddressingMode.IndirectYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.ZeropageXIndexed),
		new(Instruction.Adc, "ADC", AddressingMode.ZeropageXIndexed),
		new(Instruction.Ror, "ROR", AddressingMode.ZeropageXIndexed),
		new(Instruction.IllRra, "*RRA", AddressingMode.ZeropageXIndexed),
		new(Instruction.Sei, "SEI", AddressingMode.Implied),
		new(Instruction.Adc, "ADC", AddressingMode.AbsoluteYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.Implied),
		new(Instruction.IllRra, "*RRA", AddressingMode.AbsoluteYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.AbsoluteXIndexed),
		new(Instruction.Adc, "ADC", AddressingMode.AbsoluteXIndexed),
		new(Instruction.Ror, "ROR", AddressingMode.AbsoluteXIndexed),
		new(Instruction.IllRra, "*RRA", AddressingMode.AbsoluteXIndexed),
		// 0x80 - 0x8F
		new(Instruction.Nop, "*NOP", AddressingMode.Immediate),
		new(Instruction.Sta, "STA", AddressingMode.XIndexedIndirect),
		new(Instruction.Nop, "*NOP", AddressingMode.Immediate),
		new(Instruction.IllSax, "*SAX", AddressingMode.XIndexedIndirect),
		new(Instruction.Sty, "STY", AddressingMode.Zeropage),
		new(Instruction.Sta, "STA", AddressingMode.Zeropage),
		new(Instruction.Stx, "STX", AddressingMode.Zeropage),
		new(Instruction.IllSax, "*SAX", AddressingMode.Zeropage),
		new(Instruction.Dey, "DEY", AddressingMode.Implied),
		new(Instruction.Nop, "*NOP", AddressingMode.Immediate),
		new(Instruction.Txa, "TXA", AddressingMode.Implied),
		new(Instruction.Unknown, "*ANE", AddressingMode.Immediate),
		new(Instruction.Sty, "STY", AddressingMode.Absolute),
		new(Instruction.Sta, "STA", AddressingMode.Absolute),
		new(Instruction.Stx, "STX", AddressingMode.Absolute),
		new(Instruction.IllSax, "*SAX", AddressingMode.Absolute),
		// 0x90 - 0x9F
		new(Instruction.Bcc, "BCC", AddressingMode.Relative),
		new(Instruction.Sta, "STA", AddressingMode.IndirectYIndexed),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied),
		new(Instruction.Unknown, "*SHA", AddressingMode.IndirectYIndexed),
		new(Instruction.Sty, "STY", AddressingMode.ZeropageXIndexed),
		new(Instruction.Sta, "STA", AddressingMode.ZeropageXIndexed),
		new(Instruction.Stx, "STX", AddressingMode.ZeropageYIndexed),
		new(Instruction.IllSax, "*SAX", AddressingMode.ZeropageYIndexed),
		new(Instruction.Tya, "TYA", AddressingMode.Implied),
		new(Instruction.Sta, "STA", AddressingMode.AbsoluteYIndexed),
		new(Instruction.Txs, "TXS", AddressingMode.Implied),
		new(Instruction.Unknown, "*TAS", AddressingMode.AbsoluteXIndexed),
		new(Instruction.Unknown, "*SHY", AddressingMode.AbsoluteXIndexed),
		new(Instruction.Sta, "STA", AddressingMode.AbsoluteXIndexed),
		new(Instruction.Unknown, "*SHX", AddressingMode.AbsoluteYIndexed),
		new(Instruction.Unknown, "*SHA", AddressingMode.AbsoluteYIndexed),
		// 0xA0 - 0xAF
		new(Instruction.Ldy, "LDY", AddressingMode.Immediate),
		new(Instruction.Lda, "LDA", AddressingMode.XIndexedIndirect),
		new(Instruction.Ldx, "LDX", AddressingMode.Immediate),
		new(Instruction.IllLax, "*LAX", AddressingMode.XIndexedIndirect),
		new(Instruction.Ldy, "LDY", AddressingMode.Zeropage),
		new(Instruction.Lda, "LDA", AddressingMode.Zeropage),
		new(Instruction.Ldx, "LDX", AddressingMode.Zeropage),
		new(Instruction.IllLax, "*LAX", AddressingMode.Zeropage),
		new(Instruction.Tay, "TAY", AddressingMode.Implied),
		new(Instruction.Lda, "LDA", AddressingMode.Immediate),
		new(Instruction.Tax, "TAX", AddressingMode.Implied),
		new(Instruction.Unknown, "*LXA", AddressingMode.Immediate),
		new(Instruction.Ldy, "LDY", AddressingMode.Absolute),
		new(Instruction.Lda, "LDA", AddressingMode.Absolute),
		new(Instruction.Ldx, "LDX", AddressingMode.Absolute),
		new(Instruction.IllLax, "*LAX", AddressingMode.Absolute),
		// 0xB0 - 0xBF
		new(Instruction.Bcs, "BCS", AddressingMode.Relative),
		new(Instruction.Lda, "LDA", AddressingMode.IndirectYIndexed),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied),
		new(Instruction.IllLax, "*LAX", AddressingMode.IndirectYIndexed),
		new(Instruction.Ldy, "LDY", AddressingMode.ZeropageXIndexed),
		new(Instruction.Lda, "LDA", AddressingMode.ZeropageXIndexed),
		new(Instruction.Ldx, "LDX", AddressingMode.ZeropageYIndexed),
		new(Instruction.IllLax, "*LAX", AddressingMode.ZeropageYIndexed),
		new(Instruction.Clv, "CLV", AddressingMode.Implied),
		new(Instruction.Lda, "LDA", AddressingMode.AbsoluteYIndexed),
		new(Instruction.Tsx, "TSX", AddressingMode.Implied),
		new(Instruction.Unknown, "*LAS", AddressingMode.AbsoluteYIndexed),
		new(Instruction.Ldy, "LDY", AddressingMode.AbsoluteXIndexed),
		new(Instruction.Lda, "LDA", AddressingMode.AbsoluteXIndexed),
		new(Instruction.Ldx, "LDX", AddressingMode.AbsoluteYIndexed),
		new(Instruction.IllLax, "*LAX", AddressingMode.AbsoluteYIndexed),
		// 0xC0 - 0xCF
		new(Instruction.Cpy, "CPY", AddressingMode.Immediate),
		new(Instruction.Cmp, "CMP", AddressingMode.XIndexedIndirect),
		new(Instruction.Nop, "*NOP", AddressingMode.Implied),
		new(Instruction.IllDcp, "*DCP", AddressingMode.XIndexedIndirect),
		new(Instruction.Cpy, "CPY", AddressingMode.Zeropage),
		new(Instruction.Cmp, "CMP", AddressingMode.Zeropage),
		new(Instruction.Dec, "DEC", AddressingMode.Zeropage),
		new(Instruction.IllDcp, "*DCP", AddressingMode.Zeropage),
		new(Instruction.Iny, "INY", AddressingMode.Implied),
		new(Instruction.Cmp, "CMP", AddressingMode.Immediate),
		new(Instruction.Dex, "DEX", AddressingMode.Implied),
		new(Instruction.Unknown, "*SBX", AddressingMode.Immediate),
		new(Instruction.Cpy, "CPY", AddressingMode.Absolute),
		new(Instruction.Cmp, "CMP", AddressingMode.Absolute),
		new(Instruction.Dec, "DEC", AddressingMode.Absolute),
		new(Instruction.IllDcp, "*DCP", AddressingMode.Absolute),
		// 0xD0 - 0xDF
		new(Instruction.Bne, "BNE", AddressingMode.Relative),
		new(Instruction.Cmp, "CMP", AddressingMode.IndirectYIndexed),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied),
		new(Instruction.IllDcp, "*DCP", AddressingMode.IndirectYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.ZeropageXIndexed),
		new(Instruction.Cmp, "CMP", AddressingMode.ZeropageXIndexed),
		new(Instruction.Dec, "DEC", AddressingMode.ZeropageXIndexed),
		new(Instruction.IllDcp, "*DCP", AddressingMode.ZeropageXIndexed),
		new(Instruction.Cld, "CLD", AddressingMode.Implied),
		new(Instruction.Cmp, "CMP", AddressingMode.AbsoluteYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.Implied),
		new(Instruction.IllDcp, "*DCP", AddressingMode.AbsoluteYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.AbsoluteXIndexed),
		new(Instruction.Cmp, "CMP", AddressingMode.AbsoluteXIndexed),
		new(Instruction.Dec, "DEC", AddressingMode.AbsoluteXIndexed),
		new(Instruction.IllDcp, "*DCP", AddressingMode.AbsoluteXIndexed),
		// 0xE0 - 0xEF
		new(Instruction.Cpx, "CPX", AddressingMode.Immediate),
		new(Instruction.Sbc, "SBC", AddressingMode.XIndexedIndirect),
		new(Instruction.Nop, "*NOP", AddressingMode.Immediate),
		new(Instruction.IllIsc, "*ISC", AddressingMode.XIndexedIndirect),
		new(Instruction.Cpx, "CPX", AddressingMode.Zeropage),
		new(Instruction.Sbc, "SBC", AddressingMode.Zeropage),
		new(Instruction.Inc, "INC", AddressingMode.Zeropage),
		new(Instruction.IllIsc, "*ISC", AddressingMode.Zeropage),
		new(Instruction.Inx, "INX", AddressingMode.Implied),
		new(Instruction.Sbc, "SBC", AddressingMode.Immediate),
		new(Instruction.Nop, "NOP", AddressingMode.Implied),
		new(Instruction.Sbc, "*SBC", AddressingMode.Immediate),
		new(Instruction.Cpx, "CPX", AddressingMode.Absolute),
		new(Instruction.Sbc, "SBC", AddressingMode.Absolute),
		new(Instruction.Inc, "INC", AddressingMode.Absolute),
		new(Instruction.IllIsc, "*ISC", AddressingMode.Absolute),
		// 0xF0 - 0xFF
		new(Instruction.Beq, "BEQ", AddressingMode.Relative),
		new(Instruction.Sbc, "SBC", AddressingMode.IndirectYIndexed),
		new(Instruction.Jam, "*JAM", AddressingMode.Implied),
		new(Instruction.IllIsc, "*ISC", AddressingMode.IndirectYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.ZeropageXIndexed),
		new(Instruction.Sbc, "SBC", AddressingMode.ZeropageXIndexed),
		new(Instruction.Inc, "INC", AddressingMode.ZeropageXIndexed),
		new(Instruction.IllIsc, "*ISC", AddressingMode.ZeropageXIndexed),
		new(Instruction.Sed, "SED", AddressingMode.Implied),
		new(Instruction.Sbc, "SBC", AddressingMode.AbsoluteYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.Implied),
		new(Instruction.IllIsc, "*ISC", AddressingMode.AbsoluteYIndexed),
		new(Instruction.Nop, "*NOP", AddressingMode.AbsoluteXIndexed),
		new(Instruction.Sbc, "SBC", AddressingMode.AbsoluteXIndexed),
		new(Instruction.Inc, "INC", AddressingMode.AbsoluteXIndexed),
		new(Instruction.IllIsc, "*ISC", AddressingMode.AbsoluteXIndexed)
	];

	private ushort _regPc;
	private byte _regSpLo;
	private const byte _regSpHi = 0x01;
	private byte _regA;
	private byte _regX;
	private byte _regY;

	private bool _flagCarry;
	private bool _flagZero;
	private bool _flagInterruptDisable;
	private bool _flagDecimal; // Disabled but can still be set/cleared
	private bool _flagB;
	private bool _flagOverflow;
	private bool _flagNegative;

	private ushort RegSp => (ushort)((_regSpHi << 8) | _regSpLo);

	private readonly CpuBus _bus;

	private byte _currentOpcode = 0;
	private int _step = 0;
	private int _fetchStep = 0;
	private int _cycles = 7;

	private ushort _fetchedAddress = 0;
	private byte _fetchLow;
	private byte _fetchHigh;
	private byte _memOperand;
	private bool _pageBoundaryCrossed;
	private bool _nmi = false;

	private AddressingMode CurrentAddressingMode => _instructions[_currentOpcode].AddressingMode;

	private byte RegStatus
	{
		get => (byte)
		(
			((_flagNegative ? 1 : 0) << 7) |
			((_flagOverflow ? 1 : 0) << 6) |
			(1 << 5) | // Unused, always 1
			((_flagB ? 1 : 0) << 4) | // Unused, 0 if pushed by interrupt, 1 if pushed by instruction
			((_flagDecimal ? 1 : 0) << 3) |
			((_flagInterruptDisable ? 1 : 0) << 2) |
			((_flagZero ? 1 : 0) << 1) |
			((_flagCarry ? 1 : 0) << 0)
		);

		set
		{
			_flagNegative = ((value >> 7) & 1) != 0;
			_flagOverflow = ((value >> 6) & 1) != 0;
			_flagB = ((value >> 4) & 1) != 0;
			_flagDecimal = ((value >> 3) & 1) != 0;
			_flagInterruptDisable = ((value >> 2) & 1) != 0;
			_flagZero = ((value >> 1) & 1) != 0;
			_flagCarry = (value & 1) != 0;
		}
	}

	public Cpu(CpuBus bus)
	{
		_bus = bus;
		Reset();
	}

	public void Reset()
	{
		_regPc = (ushort)((_bus.ReadByte(0xFFFD) << 8) | _bus.ReadByte(0xFFFC));
		_regSpLo = 0xFD;

		_flagNegative = false;
		_flagOverflow = false;
		_flagB = false;
		_flagDecimal = false;
		_flagInterruptDisable = true;
		_flagZero = false;
		_flagCarry = false;
	}

	private byte ReadByte(ushort address) => _bus.ReadByte(address);

	private void WriteByte(ushort address, byte value) => _bus.WriteByte(address, value);

	private byte FetchByte() => ReadByte(_regPc++);

	private void PushByte(byte value)
	{
		WriteByte(RegSp, value);
		_regSpLo--;
	}

	private byte PopByte()
	{
		_regSpLo++;
		return ReadByte(RegSp);
	}

	private bool FetchAddress()
	{
		switch (CurrentAddressingMode)
		{
			case AddressingMode.Absolute: // 1 added cycle
				{
					switch (_fetchStep)
					{
						case 0:
							_fetchLow = FetchByte();
							_fetchStep++;
							return false;
						case 1:
							_fetchHigh = FetchByte();
							_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
							_fetchStep = 0;
							return true;
						default:
							throw new UnreachableException();
					}
				}
			case AddressingMode.AbsoluteXIndexed: // 1 added cycle
				{
					switch (_fetchStep)
					{
						case 0:
							_fetchLow = FetchByte();
							_fetchStep++;
							return false;
						case 1:
							_fetchHigh = FetchByte();
							_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
							_pageBoundaryCrossed = (_fetchedAddress & 0xFF00) != ((_fetchedAddress + _regX) & 0xFF00);
							_fetchedAddress += _regX;
							_fetchStep = 0;
							return true;
						default:
							throw new UnreachableException();
					}
				}
			case AddressingMode.AbsoluteYIndexed: // 1 added cycle
				{
					switch (_fetchStep)
					{
						case 0:
							_fetchLow = FetchByte();
							_fetchStep++;
							return false;
						case 1:
							_fetchHigh = FetchByte();
							_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
							_pageBoundaryCrossed = (_fetchedAddress & 0xFF00) != ((_fetchedAddress + _regY) & 0xFF00);
							_fetchedAddress += _regY;
							_fetchStep = 0;
							return true;
						default:
							throw new UnreachableException();
					}
				}
			case AddressingMode.Indirect: // 3 added cycles
				{
					switch (_fetchStep)
					{
						case 0:
							_fetchLow = FetchByte();
							_fetchStep++;
							return false;
						case 1:
							_fetchHigh = FetchByte();
							_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
							_fetchStep++;
							return false;
						case 2:
							_fetchLow = ReadByte(_fetchedAddress);
							_fetchedAddress++;
							// Reuse the same high byte to reproduce the "indirect jump bug"
							_fetchedAddress &= 0x00FF;
							_fetchedAddress |= (ushort)(_fetchHigh << 8);
							_fetchStep++;
							return false;
						case 3:
							_fetchHigh = ReadByte(_fetchedAddress);
							_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
							_fetchStep = 0;
							return true;
						default:
							throw new UnreachableException();
					}
				}
			case AddressingMode.XIndexedIndirect: // 3 added cycles
				{
					switch (_fetchStep)
					{
						case 0:
							_fetchedAddress = FetchByte();
							_fetchStep++;
							return false;
						case 1:
							_fetchedAddress += _regX;
							_fetchedAddress &= 0x00FF;
							_fetchStep++;
							return false;
						case 2:
							_fetchLow = ReadByte(_fetchedAddress);
							_fetchedAddress++;
							_fetchedAddress &= 0x00FF;
							_fetchStep++;
							return false;
						case 3:
							_fetchHigh = ReadByte(_fetchedAddress);
							_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
							_fetchStep = 0;
							return true;
						default:
							throw new UnreachableException();
					}
				}
			case AddressingMode.IndirectYIndexed: // 2 added cycles
				{
					switch (_fetchStep)
					{
						case 0:
							_fetchedAddress = FetchByte();
							_fetchStep++;
							return false;
						case 1:
							_fetchLow = ReadByte(_fetchedAddress);
							_fetchedAddress++;
							_fetchedAddress &= 0x00FF;
							_fetchStep++;
							return false;
						case 2:
							_fetchHigh = ReadByte(_fetchedAddress);
							_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
							_pageBoundaryCrossed = (_fetchedAddress & 0xFF00) != ((_fetchedAddress + _regY) & 0xFF00);
							_fetchedAddress += _regY;
							_fetchStep = 0;
							return true;
						default:
							throw new UnreachableException();
					}
				}
			case AddressingMode.Zeropage: // 0 added cycles
				_fetchLow = FetchByte();
				_fetchHigh = 0;
				_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
				return true;
			case AddressingMode.ZeropageXIndexed: // 1 added cycle
				switch (_fetchStep)
				{
					case 0:
						_fetchLow = FetchByte();
						_fetchHigh = 0;
						_fetchStep++;
						return false;
					case 1:
						_fetchLow += _regX;
						_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						_fetchStep = 0;
						return true;
					default:
						throw new UnreachableException();
				}
			case AddressingMode.ZeropageYIndexed: // 1 added cycle
				switch (_fetchStep)
				{
					case 0:
						_fetchLow = FetchByte();
						_fetchHigh = 0;
						_fetchStep++;
						return false;
					case 1:
						_fetchLow += _regY;
						_fetchedAddress = (ushort)((_fetchHigh << 8) | _fetchLow);
						_fetchStep = 0;
						return true;
					default:
						throw new UnreachableException();
				}
			default:
				throw new UnreachableException($"The addressing mode {CurrentAddressingMode} was used but not implemented (opcode 0x{_currentOpcode:X2}).");
		}
	}

	private void ReadMemoryOperand()
	{
		if (CurrentAddressingMode is AddressingMode.Immediate or AddressingMode.Accumulator)
			throw new UnreachableException($"Attempted to read memory operand from immediate or accumulator (opcode 0x{_currentOpcode:X2}).");

		_memOperand = ReadByte(_fetchedAddress);
	}

	private void WriteMemoryOperand() => WriteByte(_fetchedAddress, _memOperand);

	private string DisassembleNext()
	{
		var inst = _instructions[_currentOpcode];
		var b1 = ReadByte(_regPc);
		var b2 = ReadByte((ushort)(_regPc + 1));
		return inst.AddressingMode switch
		{
			AddressingMode.Relative => inst.mnemonic + $" ${_regPc + 1 + (sbyte)b1:X4}",
			AddressingMode.Immediate => inst.mnemonic + $" #${b1:X2}",

			AddressingMode.Absolute => inst.mnemonic + $" ${b2:X2}{b1:X2}",
			AddressingMode.AbsoluteXIndexed => inst.mnemonic + $" ${b2:X2}{b1:X2},X",
			AddressingMode.AbsoluteYIndexed => inst.mnemonic + $" ${b2:X2}{b1:X2},Y",

			AddressingMode.Zeropage => inst.mnemonic + $" ${b1:X2} = {ReadByte(b1):X2}",
			AddressingMode.ZeropageXIndexed => inst.mnemonic + $" ${b1:X2},X",
			AddressingMode.ZeropageYIndexed => inst.mnemonic + $" ${b1:X2},Y",

			AddressingMode.Indirect => inst.mnemonic + $" (${b2:X2}{b1:X2})",
			AddressingMode.XIndexedIndirect => inst.mnemonic + $" (${b1:X2},X)",
			AddressingMode.IndirectYIndexed => inst.mnemonic + $" (${b1:X2}),Y",

			_ => inst.mnemonic
		};
	}

	public void Tick()
	{
		if (_step == 0)
		{
			if (_nmi)
			{
				_nmi = false;
				PushByte((byte)((_regPc + 2) >> 8));
				PushByte((byte)((_regPc + 2) & 0xFF));
				PushByte(RegStatus);
				_fetchLow = ReadByte(0xFFFA);
				_fetchHigh = ReadByte(0xFFFB);
				_regPc = (ushort)((_fetchHigh << 8) | _fetchLow);
				_flagInterruptDisable = true;
			}

			_currentOpcode = FetchByte();

			/*var sb = new StringBuilder();
			sb.Append($"{_regPc - 1:X4}  {_currentOpcode:X2}");
			for (var i = 0; i < _instructions[_currentOpcode].Bytes - 1; i++)
				sb.Append($" {ReadByte((ushort)(_regPc + i)):X2}");
			sb.Append(new string(' ', 16 - sb.Length));
			sb.Append(DisassembleNext());
			sb.Append(new string(' ', 48 - sb.Length));
			sb.Append($"A:{_regA:X2} X:{_regX:X2} Y:{_regY:X2} P:{RegStatus:X2} SP:{_regSpLo:X2} CYC:{_cycles}");
			Console.WriteLine(sb);*/
		}

		switch (_instructions[_currentOpcode].Instruction)
		{
			case Instruction.Adc: ExecuteInstAdc(); break;
			case Instruction.And: ExecuteInstAnd(); break;
			case Instruction.Asl: ExecuteInstAsl(); break;
			case Instruction.Bcc: ExecuteInstBcc(); break;
			case Instruction.Bcs: ExecuteInstBcs(); break;
			case Instruction.Beq: ExecuteInstBeq(); break;
			case Instruction.Bit: ExecuteInstBit(); break;
			case Instruction.Bmi: ExecuteInstBmi(); break;
			case Instruction.Bne: ExecuteInstBne(); break;
			case Instruction.Bpl: ExecuteInstBpl(); break;
			case Instruction.Brk: ExecuteInstBrk(); break;
			case Instruction.Bvc: ExecuteInstBvc(); break;
			case Instruction.Bvs: ExecuteInstBvs(); break;
			case Instruction.Clc: ExecuteInstClc(); break;
			case Instruction.Cld: ExecuteInstCld(); break;
			case Instruction.Cli: ExecuteInstCli(); break;
			case Instruction.Clv: ExecuteInstClv(); break;
			case Instruction.Cmp: ExecuteInstCmp(); break;
			case Instruction.Cpx: ExecuteInstCpx(); break;
			case Instruction.Cpy: ExecuteInstCpy(); break;
			case Instruction.Dec: ExecuteInstDec(); break;
			case Instruction.Dex: ExecuteInstDex(); break;
			case Instruction.Dey: ExecuteInstDey(); break;
			case Instruction.Eor: ExecuteInstEor(); break;
			case Instruction.Inc: ExecuteInstInc(); break;
			case Instruction.Inx: ExecuteInstInx(); break;
			case Instruction.Iny: ExecuteInstIny(); break;
			case Instruction.Jmp: ExecuteInstJmp(); break;
			case Instruction.Jsr: ExecuteInstJsr(); break;
			case Instruction.Lda: ExecuteInstLda(); break;
			case Instruction.Ldx: ExecuteInstLdx(); break;
			case Instruction.Ldy: ExecuteInstLdy(); break;
			case Instruction.Lsr: ExecuteInstLsr(); break;
			case Instruction.Nop: ExecuteInstNop(); break;
			case Instruction.Ora: ExecuteInstOra(); break;
			case Instruction.Pha: ExecuteInstPha(); break;
			case Instruction.Php: ExecuteInstPhp(); break;
			case Instruction.Pla: ExecuteInstPla(); break;
			case Instruction.Plp: ExecuteInstPlp(); break;
			case Instruction.Rol: ExecuteInstRol(); break;
			case Instruction.Ror: ExecuteInstRor(); break;
			case Instruction.Rti: ExecuteInstRti(); break;
			case Instruction.Rts: ExecuteInstRts(); break;
			case Instruction.Sbc: ExecuteInstSbc(); break;
			case Instruction.Sec: ExecuteInstSec(); break;
			case Instruction.Sed: ExecuteInstSed(); break;
			case Instruction.Sei: ExecuteInstSei(); break;
			case Instruction.Sta: ExecuteInstSta(); break;
			case Instruction.Stx: ExecuteInstStx(); break;
			case Instruction.Sty: ExecuteInstSty(); break;
			case Instruction.Tax: ExecuteInstTax(); break;
			case Instruction.Tay: ExecuteInstTay(); break;
			case Instruction.Tsx: ExecuteInstTsx(); break;
			case Instruction.Txa: ExecuteInstTxa(); break;
			case Instruction.Txs: ExecuteInstTxs(); break;
			case Instruction.Tya: ExecuteInstTya(); break;
			case Instruction.IllDcp: ExecuteInstIllDcp(); break;
			case Instruction.IllIsc: ExecuteInstIllIsc(); break;
			case Instruction.IllLax: ExecuteInstIllLax(); break;
			case Instruction.IllRla: ExecuteInstIllRla(); break;
			case Instruction.IllRra: ExecuteInstIllRra(); break;
			case Instruction.IllSax: ExecuteInstIllSax(); break;
			case Instruction.IllSlo: ExecuteInstIllSlo(); break;
			case Instruction.IllSre: ExecuteInstIllSre(); break;
			default:
				throw new NotImplementedException();
		}

		_cycles++;
	}

	public void RequestNmi()
	{
		_nmi = true;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstAdc()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_memOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				ReadMemoryOperand();
			immediate:

				var result = _regA + _memOperand + (_flagCarry ? 1 : 0);
				var overflow = (_regA & (1 << 7)) == (_memOperand & (1 << 7)) && (_memOperand & (1 << 7)) != (result & (1 << 7));

				_regA = (byte)result;

				_flagNegative = ((_regA >> 7) & 1) != 0;
				_flagZero = _regA == 0;
				_flagCarry = result > byte.MaxValue;
				_flagOverflow = overflow;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstAnd()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_memOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				ReadMemoryOperand();
			immediate:

				_regA &= _memOperand;
				_flagNegative = ((_regA >> 7) & 1) != 0;
				_flagZero = _regA == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstAsl()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (CurrentAddressingMode == AddressingMode.Accumulator)
				{
					var carry = ((_regA >> 7) & 1) != 0;

					_regA <<= 1;

					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;
					_flagCarry = carry;

					_step = 0;
					break;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed)
					goto case 3;

				break;
			case 3:
				ReadMemoryOperand();

				_step++;
				break;
			case 4:
				{
					var carry = ((_memOperand >> 7) & 1) != 0;

					_memOperand <<= 1;

					_flagNegative = ((_memOperand >> 7) & 1) != 0;
					_flagZero = _memOperand == 0;
					_flagCarry = carry;

					_step++;
					break;
				}
			case 5:
				WriteMemoryOperand();

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBcc() => ExecuteConditionalBranch(!_flagCarry);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBcs() => ExecuteConditionalBranch(_flagCarry);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBeq() => ExecuteConditionalBranch(_flagZero);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBit()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Fetch address
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				ReadMemoryOperand();

				_flagNegative = ((_memOperand >> 7) & 1) != 0;
				_flagZero = (_regA & _memOperand) == 0;
				_flagOverflow = ((_memOperand >> 6) & 1) != 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBmi() => ExecuteConditionalBranch(_flagNegative);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBne() => ExecuteConditionalBranch(!_flagZero);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBpl() => ExecuteConditionalBranch(!_flagNegative);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBrk()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Push PC hi
				PushByte((byte)((_regPc + 2) >> 8));
				_step++;
				break;
			case 2: // Push PC lo
				PushByte((byte)((_regPc + 2) & 0xFF));
				_step++;
				break;
			case 3: // Push status
				_flagB = true;
				PushByte(RegStatus);
				_flagB = false;
				_step++;
				break;
			case 4:
				_fetchLow = ReadByte(0xFFFE);
				_step++;
				break;
			case 5:
				_fetchHigh = ReadByte(0xFFFF);
				_step++;
				break;
			case 6:
				_regPc = (ushort)((_fetchHigh << 8) | _fetchLow);
				_flagInterruptDisable = true;
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBvc() => ExecuteConditionalBranch(!_flagOverflow);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstBvs() => ExecuteConditionalBranch(_flagOverflow);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstCmp()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_memOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				ReadMemoryOperand();
			immediate:

				var result = _regA - _memOperand;

				_flagNegative = ((result >> 7) & 1) != 0;
				_flagZero = _regA == _memOperand;
				_flagCarry = _regA >= _memOperand;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstCpx()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_memOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				ReadMemoryOperand();
			immediate:

				var result = _regX - _memOperand;

				_flagNegative = ((result >> 7) & 1) != 0;
				_flagZero = _regX == _memOperand;
				_flagCarry = _regX >= _memOperand;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstCpy()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_memOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				ReadMemoryOperand();
			immediate:

				var result = _regY - _memOperand;

				_flagNegative = ((result >> 7) & 1) != 0;
				_flagZero = _regY == _memOperand;
				_flagCarry = _regY >= _memOperand;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstDec()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed)
					goto case 3;

				break;
			case 3:
				ReadMemoryOperand();

				_step++;
				break;
			case 4:
				{
					_memOperand--;

					_flagNegative = ((_memOperand >> 7) & 1) != 0;
					_flagZero = _memOperand == 0;

					_step++;
					break;
				}
			case 5:
				WriteMemoryOperand();

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstDex()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				_regX--;
				_flagNegative = (_regX & (1 << 7)) != 0;
				_flagZero = _regX == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstDey()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				_regY--;
				_flagNegative = (_regY & (1 << 7)) != 0;
				_flagZero = _regY == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstEor()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_memOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				ReadMemoryOperand();
			immediate:

				_regA ^= _memOperand;
				_flagNegative = ((_regA >> 7) & 1) != 0;
				_flagZero = _regA == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstInc()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed)
					goto case 3;

				break;
			case 3:
				ReadMemoryOperand();

				_step++;
				break;
			case 4:
				{
					_memOperand++;

					_flagNegative = ((_memOperand >> 7) & 1) != 0;
					_flagZero = _memOperand == 0;

					_step++;
					break;
				}
			case 5:
				WriteMemoryOperand();

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstInx()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1:
				_regX++;
				_flagNegative = (_regX & (1 << 7)) != 0;
				_flagZero = _regX == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstIny()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1:
				_regY++;
				_flagNegative = (_regY & (1 << 7)) != 0;
				_flagZero = _regY == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstJmp()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1:
				if (!FetchAddress())
					break;

				_regPc = _fetchedAddress;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstJsr()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Push PC hi
				PushByte((byte)((_regPc + 1) >> 8));
				_step++;
				break;
			case 2: // Push PC lo
				PushByte((byte)((_regPc + 1) & 0xFF));
				_step++;
				break;
			case 3:
				if (!FetchAddress())
					break;
				_step++;
				break;
			case 4:
				_regPc = _fetchedAddress;
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstLda()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Fetch address
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_memOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				if (!_pageBoundaryCrossed || CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed and not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
				{
					_step = 3;
					goto case 3;
				}
				_step++;
				break;
			case 3:
				ReadMemoryOperand();
			immediate:
				_regA = _memOperand;
				_flagNegative = ((_regA >> 7) & 1) != 0;
				_flagZero = _regA == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstLdx()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Fetch address
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_memOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (!_pageBoundaryCrossed || CurrentAddressingMode is not AddressingMode.AbsoluteYIndexed)
					goto case 3;

				break;
			case 3:
				ReadMemoryOperand();
			immediate:
				_regX = _memOperand;
				_flagNegative = ((_regX >> 7) & 1) != 0;
				_flagZero = _regX == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstLdy()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Fetch address
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_memOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (!_pageBoundaryCrossed || CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed)
					goto case 3;

				break;
			case 3:
				ReadMemoryOperand();
			immediate:
				_regY = _memOperand;
				_flagNegative = ((_regY >> 7) & 1) != 0;
				_flagZero = _regY == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstLsr()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (CurrentAddressingMode == AddressingMode.Accumulator)
				{
					var carry = (_regA & 1) != 0;

					_regA >>= 1;

					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;
					_flagCarry = carry;
					_step = 0;
					break;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;
				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed)
					goto case 3;
				break;
			case 3:
				ReadMemoryOperand();
				_step++;
				break;
			case 4:
				{
					var carry = (_memOperand & 1) != 0;

					_memOperand >>= 1;

					_flagNegative = ((_memOperand >> 7) & 1) != 0;
					_flagZero = _memOperand == 0;
					_flagCarry = carry;

					WriteMemoryOperand();

					_step++;
					break;
				}
			case 5:
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstNop()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1:
				if (CurrentAddressingMode == AddressingMode.Implied)
				{
					_step = 0;
					break;
				}

				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					FetchByte();
					_step = 0;
					break;
				}

				_step++;
				break;
			case 2:
				if (!FetchAddress())
					break;

				if (CurrentAddressingMode == AddressingMode.AbsoluteXIndexed && _pageBoundaryCrossed)
				{
					_step++;
					break;
				}

				_step = 0;
				break;
			case 3:
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstOra()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_memOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				ReadMemoryOperand();
			immediate:

				_regA |= _memOperand;
				_flagNegative = ((_regA >> 7) & 1) != 0;
				_flagZero = _regA == 0;

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
			case 0:
				_step++;
				break;
			case 1:
				PushByte(_regA);
				_step++;
				break;
			case 2:
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
			case 0:
				_step++;
				break;
			case 1:
				_flagB = true;
				PushByte(RegStatus);
				_flagB = false;
				_step++;
				break;
			case 2:
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
			case 0:
				_step++;
				break;
			case 1:
				_step++;
				break;
			case 2:
				_regA = PopByte();
				_flagNegative = ((_regA >> 7) & 1) != 0;
				_flagZero = _regA == 0;
				_step++;
				break;
			case 3:
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
			case 0:
				_step++;
				break;
			case 1:
				_step++;
				break;
			case 2:
				RegStatus = PopByte();
				_flagB = false;
				_step++;
				break;
			case 3:
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstRol()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (CurrentAddressingMode == AddressingMode.Accumulator)
				{
					var carry = ((_regA >> 7) & 1) != 0;

					_regA <<= 1;
					_regA |= (byte)(_flagCarry ? 1 : 0);

					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;
					_flagCarry = carry;

					_step = 0;
					break;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed)
					goto case 3;

				break;
			case 3:
				ReadMemoryOperand();

				_step++;
				break;
			case 4:
				{
					var carry = ((_memOperand >> 7) & 1) != 0;

					_memOperand <<= 1;
					_memOperand |= (byte)(_flagCarry ? 1 : 0);

					_flagNegative = ((_memOperand >> 7) & 1) != 0;
					_flagZero = _memOperand == 0;
					_flagCarry = carry;

					_step++;
					break;
				}
			case 5:
				WriteMemoryOperand();

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstRor()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (CurrentAddressingMode == AddressingMode.Accumulator)
				{
					var carry = (_regA & 1) != 0;

					_regA >>= 1;
					_regA |= (byte)((_flagCarry ? 1 : 0) << 7);

					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;
					_flagCarry = carry;

					_step = 0;
					break;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed)
					goto case 3;

				break;
			case 3:
				ReadMemoryOperand();

				_step++;
				break;
			case 4:
				{
					var carry = (_memOperand & 1) != 0;

					_memOperand >>= 1;
					_memOperand |= (byte)((_flagCarry ? 1 : 0) << 7);

					_flagNegative = ((_memOperand >> 7) & 1) != 0;
					_flagZero = _memOperand == 0;
					_flagCarry = carry;

					_step++;
					break;
				}
			case 5:
				WriteMemoryOperand();

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
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Pop status
				RegStatus = PopByte();
				_flagB = false;
				_step++;
				break;
			case 2:
				_fetchLow = PopByte();
				_step++;
				break;
			case 3:
				_fetchHigh = PopByte();
				_step++;
				break;
			case 4:
				_regPc = _fetchLow;
				_step++;
				break;
			case 5:
				_regPc |= (ushort)(_fetchHigh << 8);
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
			case 0: // Fetch opcode
				_step++;
				break;
			case 1:
				_fetchLow = PopByte();
				_step++;
				break;
			case 2:
				_fetchHigh = PopByte();
				_step++;
				break;
			case 3:
				_regPc = _fetchLow;
				_step++;
				break;
			case 4:
				_regPc |= (ushort)(_fetchHigh << 8);
				_step++;
				break;
			case 5:
				_regPc++;
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstSbc()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (CurrentAddressingMode == AddressingMode.Immediate)
				{
					_memOperand = FetchByte();
					goto immediate;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				ReadMemoryOperand();
			immediate:

				var result = _regA - _memOperand - (_flagCarry ? 0 : 1);
				var underflow = (_regA & (1 << 7)) == ((255 - _memOperand) & (1 << 7)) && ((255 - _memOperand) & (1 << 7)) != (result & (1 << 7));

				_regA = (byte)result;

				_flagNegative = ((_regA >> 7) & 1) != 0;
				_flagZero = _regA == 0;
				_flagCarry = result >= 0;
				_flagOverflow = underflow;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstSec() => ExecuteSetFlag(ref _flagCarry);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstSed() => ExecuteSetFlag(ref _flagDecimal);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstSei() => ExecuteSetFlag(ref _flagInterruptDisable);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstClc() => ExecuteClearFlag(ref _flagCarry);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstCld() => ExecuteClearFlag(ref _flagDecimal);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstCli() => ExecuteClearFlag(ref _flagInterruptDisable);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstClv() => ExecuteClearFlag(ref _flagOverflow);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstSta()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Fetch address
				if (!FetchAddress())
					break;

				_step++;
				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed and not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
					_step++;

				break;
			case 2:
				_step++;
				break;
			case 3:
				_memOperand = _regA;
				WriteMemoryOperand();
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstStx()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Fetch address
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_memOperand = _regX;
				WriteMemoryOperand();
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstSty()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Fetch address
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_memOperand = _regY;
				WriteMemoryOperand();
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstTax()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1:
				_regX = _regA;
				_flagNegative = ((_regX >> 7) & 1) != 0;
				_flagZero = _regX == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstTay()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1:
				_regY = _regA;
				_flagNegative = ((_regY >> 7) & 1) != 0;
				_flagZero = _regY == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstTsx()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1:
				_regX = _regSpLo;
				_flagNegative = ((_regX >> 7) & 1) != 0;
				_flagZero = _regX == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstTxa()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1:
				_regA = _regX;
				_flagNegative = ((_regA >> 7) & 1) != 0;
				_flagZero = _regA == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstTxs()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1:
				_regSpLo = _regX;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstTya()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1:
				_regA = _regY;
				_flagNegative = ((_regA >> 7) & 1) != 0;
				_flagZero = _regA == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstIllDcp()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed and not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
					goto case 3;

				break;
			case 3:
				ReadMemoryOperand();

				_step++;
				break;
			case 4:
				{
					_memOperand--;
					var result = _regA - _memOperand;

					_flagNegative = ((result >> 7) & 1) != 0;
					_flagZero = _regA == _memOperand;
					_flagCarry = _regA >= _memOperand;

					_step++;
					break;
				}
			case 5:
				WriteMemoryOperand();

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstIllIsc()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed and not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
					goto case 3;

				break;
			case 3:
				ReadMemoryOperand();

				_step++;
				break;
			case 4:
				{
					_memOperand++;

					var result = _regA - _memOperand - (_flagCarry ? 0 : 1);
					var underflow = (_regA & (1 << 7)) == ((255 - _memOperand) & (1 << 7)) && ((255 - _memOperand) & (1 << 7)) != (result & (1 << 7));

					_regA = (byte)result;

					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;
					_flagCarry = result >= 0;
					_flagOverflow = underflow;

					_step++;
					break;
				}
			case 5:
				WriteMemoryOperand();

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstIllLax()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Fetch address
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				if (!_pageBoundaryCrossed || CurrentAddressingMode is not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
				{
					_step = 3;
					goto case 3;
				}
				_step++;
				break;
			case 3:
				ReadMemoryOperand();
				_regA = _memOperand;
				_regX = _memOperand;

				_flagNegative = ((_regX >> 7) & 1) != 0;
				_flagZero = _regX == 0;

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstIllRla()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed and not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
					goto case 3;

				break;
			case 3:
				ReadMemoryOperand();

				_step++;
				break;
			case 4:
				{
					var carry = ((_memOperand >> 7) & 1) != 0;

					_memOperand <<= 1;
					_memOperand |= (byte)(_flagCarry ? 1 : 0);

					_regA &= _memOperand;

					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;
					_flagCarry = carry;

					_step++;
					break;
				}
			case 5:
				WriteMemoryOperand();

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstIllRra()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed and not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
					goto case 3;

				break;
			case 3:
				ReadMemoryOperand();

				_step++;
				break;
			case 4:
				{
					var carry = (_memOperand & 1) != 0;

					_memOperand >>= 1;
					_memOperand |= (byte)((_flagCarry ? 1 : 0) << 7);

					var result = _regA + _memOperand + (carry ? 1 : 0);
					var overflow = (_regA & (1 << 7)) == (_memOperand & (1 << 7)) && (_memOperand & (1 << 7)) != (result & (1 << 7));

					_regA = (byte)result;

					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;
					_flagCarry = result > byte.MaxValue;
					_flagOverflow = overflow;

					_step++;
					break;
				}
			case 5:
				WriteMemoryOperand();

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstIllSax()
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Fetch address
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				if (!_pageBoundaryCrossed || CurrentAddressingMode is not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
				{
					_step = 3;
					goto case 3;
				}
				_step++;
				break;
			case 3:
				ReadMemoryOperand();
				_memOperand = (byte)(_regA & _regX);

				_step = 0;
				WriteMemoryOperand();
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstIllSlo()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (CurrentAddressingMode == AddressingMode.Accumulator)
				{
					var carry = ((_regA >> 7) & 1) != 0;

					_regA <<= 1;

					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;
					_flagCarry = carry;

					_step = 0;
					break;
				}

				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed and not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
					goto case 3;

				break;
			case 3:
				ReadMemoryOperand();

				_step++;
				break;
			case 4:
				{
					var carry = ((_memOperand >> 7) & 1) != 0;

					_memOperand <<= 1;

					_flagNegative = ((_memOperand >> 7) & 1) != 0;
					_flagCarry = carry;

					_regA |= _memOperand;
					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;

					_step++;
					break;
				}
			case 5:
				WriteMemoryOperand();

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteInstIllSre()
	{
		switch (_step)
		{
			case 0:
				_step++;
				break;
			case 1:
				if (!FetchAddress())
					break;

				_step++;
				break;
			case 2:
				_step++;

				if (CurrentAddressingMode is not AddressingMode.AbsoluteXIndexed and not AddressingMode.AbsoluteYIndexed and not AddressingMode.IndirectYIndexed)
					goto case 3;

				break;
			case 3:
				ReadMemoryOperand();

				_step++;
				break;
			case 4:
				{
					var carry = (_memOperand & 1) != 0;

					_memOperand >>= 1;

					_regA ^= _memOperand;

					_flagCarry = carry;
					_flagNegative = ((_regA >> 7) & 1) != 0;
					_flagZero = _regA == 0;
					_step++;
					break;
				}
			case 5:
				WriteMemoryOperand();

				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteConditionalBranch(bool condition)
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Check condition, stop if false
				_memOperand = FetchByte(); // Fetch the offset

				if (!condition)
				{
					_step = 0;
					break;
				}

				_step++;
				break;
			case 2:
				var page = _regPc >> 8;
				_regPc = (ushort)(_regPc + (sbyte)_memOperand);
				var newPage = _regPc >> 8;
				if (page == newPage)
					goto case 3;
				_step++;
				break;
			case 3: // An additional step that simply wastes a cycle if branching to different page
				_step = 0;
				break;
			default:
				throw new UnreachableException();

		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteSetFlag(ref bool flag)
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Check condition, stop if false
				flag = true;
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ExecuteClearFlag(ref bool flag)
	{
		switch (_step)
		{
			case 0: // Fetch opcode
				_step++;
				break;
			case 1: // Check condition, stop if false
				flag = false;
				_step = 0;
				break;
			default:
				throw new UnreachableException();
		}
	}
}
