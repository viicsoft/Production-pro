using System;
using System.Reflection;
using System.Linq;

class Program {
    static void Main() {
        var asm = Assembly.LoadFile(System.IO.Path.GetFullPath(@"publish\desktop\Interop.BMDSwitcherAPI.dll"));
        string[] targets = { "IBMDSwitcherFairlightAudioDynamicsProcessor", "IBMDSwitcherFairlightAudioCompressor", "IBMDSwitcherFairlightAudioExpander", "IBMDSwitcherFairlightAudioLimiter" };
        foreach(var t in targets) {
            var type = asm.GetTypes().FirstOrDefault(x => x.Name == t);
            if(type != null) {
                Console.WriteLine("--- " + type.Name + " ---");
                foreach(var m in type.GetMethods().Where(m => m.Name.StartsWith("Get"))) {
                    var pars = m.GetParameters();
                    var pstr = string.Join(", ", pars.Select(p => (p.IsOut ? "out " : (p.ParameterType.IsByRef ? "ref " : "")) + p.ParameterType.Name.Replace("&", "") + " " + p.Name));
                    Console.WriteLine(m.Name + "(" + pstr + ")");
                }
            }
        }
    }
}
