using System;
using System.Drawing;
using System.Drawing.Imaging;

class Program
{
    static void Main()
    {
        string path = @""C:\Users\USER\.gemini\antigravity\brain\a420663b-f942-4219-8d2e-f851d4c8b9db\.user_uploaded\media_1789755888494.png"";
        using (Bitmap bmp = new Bitmap(path))
        {
            Console.WriteLine($""{bmp.Width}x{bmp.Height}"");
        }
    }
}
