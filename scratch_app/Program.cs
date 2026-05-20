using System;
using System.IO;
using System.Threading.Tasks;
using ScratchApp;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Ensuring Ollama is installed...");
        await OllamaService.EnsureOllamaInstalledAndRunningAsync();
        Console.WriteLine("Ollama is running!");

        Console.WriteLine("Pulling gemma3:4b model...");
        await OllamaService.PullModelAsync("gemma3:4b", progress => {
            Console.Write($"\rProgress: {progress,-50}");
        });
        Console.WriteLine("\nModel pulled successfully!");

        Console.WriteLine("Testing generation...");
        // Use a tiny 1x1 black pixel base64 to save prompt tokens for the test
        var dummyImage = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=";
        string result = await OllamaService.GenerateAsync("What color is this image? Reply in JSON format: {\"color\": \"<color>\"}", new[] { dummyImage });
        Console.WriteLine($"Result: {result}");
    }
}
