using System.IO.Pipes;
using System.Text;

namespace AuraDeskService.Input;

/// <summary>
/// Envoie des commandes d'injection au AuraDeskHelper via pipe nommé.
/// Le helper tourne dans la session utilisateur interactive.
/// </summary>
public static class PipeClient
{
    private const string PIPE_NAME = "AuraDeskInputPipe";

    public static async Task SendAsync(string json)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.Out);
            await pipe.ConnectAsync(500); // timeout 500ms
            var data = Encoding.UTF8.GetBytes(json + "\n");
            await pipe.WriteAsync(data);
        }
        catch { /* helper pas encore démarré ou occupé */ }
    }

    public static void Send(string json)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PIPE_NAME, PipeDirection.Out);
            pipe.Connect(500);
            var data = Encoding.UTF8.GetBytes(json + "\n");
            pipe.Write(data);
        }
        catch { }
    }
}
