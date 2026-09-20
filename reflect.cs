using System;
using System.Reflection;
using BMDSwitcherAPI;

class Program {
    static void Main() {
        foreach (var m in typeof(IBMDSwitcherFairlightAudioSourceCallback).GetMethods()) {
            Console.WriteLine(m.Name);
        }
    }
}
