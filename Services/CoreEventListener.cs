using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Serialization;
using MicMuteTool.Ipc;

namespace MicMuteTool.Services;

public sealed class CoreEventListener : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly CancellationTokenSource _shutdown = new();
    private readonly Action<bool> _onMuteChanged;

    public CoreEventListener(Action<bool> onMuteChanged)
    {
        _onMuteChanged = onMuteChanged;
    }

    public void Start()
    {
        _ = Task.Run(ListenLoopAsync);
    }

    private async Task ListenLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(CoreIpc.UiEventPipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(_shutdown.Token);
                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var coreEvent = JsonSerializer.Deserialize<CoreEvent>(line, JsonOptions);
                if (coreEvent?.Type == "muteChanged")
                {
                    _onMuteChanged(coreEvent.MuteState);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Keep listening after malformed or interrupted event messages.
            }
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }
}
