using System;
using System.Drawing;
using System.IO;

class Program
{
    static void Main()
    {
        string pngPath = @"C:\Users\Hp\.gemini\antigravity\brain\3bbe714a-ec3e-4b80-aa0c-32a1f402de85\atemdirector_icon_1769726008515.png";
        string icoPath = @"c:\worker\AtemDirector\installer\icon.ico";
        
        if (!File.Exists(pngPath)) {
            Console.WriteLine("PNG not found: " + pngPath);
            return;
        }

        using (Bitmap bmp = new Bitmap(pngPath))
        {
            IntPtr hIcon = bmp.GetHicon();
            using (Icon icon = Icon.FromHandle(hIcon))
            {
                using (Stream stream = File.Create(icoPath))
                {
                    icon.Save(stream);
                }
            }
        }
        Console.WriteLine("Successfully created icon: " + icoPath);
    }
}
