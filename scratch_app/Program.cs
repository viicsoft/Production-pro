using System;
using System.Reflection;
using BMDSwitcherAPI;

class Program {
    static void Main() {
        var type = typeof(IBMDSwitcherMixEffectBlock);
        foreach (var m in type.GetMethods()) {
            if (m.Name.Contains("Transition") || m.Name.Contains("Cut")) {
                Console.WriteLine(m.Name);
                foreach (var p in m.GetParameters()) {
                    Console.WriteLine("  " + p.ParameterType.Name + " " + p.Name);
                }
            }
        }
    }
}
