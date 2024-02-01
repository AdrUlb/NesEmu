using NesEmu;


Cartridge cartridge;
using (var fs = File.OpenRead(@"C:\Users\Adrian\Desktop\nestest.nes"))
	cartridge = new(fs);

var bus = new Bus
{
	Cartridge = cartridge
};

var cpu = new CPU(bus);

while (true)
	cpu.ExecuteCycle();
