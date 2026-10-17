using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Naravel.Mail.Smtp.Tests;

/// <summary>A minimal plain-text SMTP server that records one session, so the real MailKit client can be tested without a container.</summary>
internal sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public FakeSmtpServer()
    {
        _listener.Start();
        _loop = Task.Run(AcceptLoopAsync);
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public List<string> EnvelopeFrom { get; } = new();

    public List<string> EnvelopeRecipients { get; } = new();

    public List<string> Data { get; } = new();

    /// <summary>When set, the server answers <c>RCPT TO</c> with this reply line (for example a 550 rejection).</summary>
    public string? RejectRecipientWith { get; set; }

    public async Task<bool> WaitForMessageAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            lock (Data)
            {
                if (Data.Count > 0) return true;
            }

            await Task.Delay(10);
        }

        return false;
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                await ServeAsync(client);
            }
        }
        catch (Exception) when (_stop.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        using var writer = new StreamWriter(stream, new ASCIIEncoding()) { NewLine = "\r\n", AutoFlush = true };
        await writer.WriteLineAsync("220 localhost fake ready");
        while (await reader.ReadLineAsync() is { } line)
        {
            var upper = line.ToUpperInvariant();
            if (upper.StartsWith("EHLO", StringComparison.Ordinal) || upper.StartsWith("HELO", StringComparison.Ordinal))
            {
                await writer.WriteLineAsync("250 localhost");
            }
            else if (upper.StartsWith("MAIL FROM:", StringComparison.Ordinal))
            {
                EnvelopeFrom.Add(line[10..].Trim());
                await writer.WriteLineAsync("250 ok");
            }
            else if (upper.StartsWith("RCPT TO:", StringComparison.Ordinal))
            {
                if (RejectRecipientWith is { } reject)
                {
                    await writer.WriteLineAsync(reject);
                    continue;
                }

                EnvelopeRecipients.Add(line[8..].Trim());
                await writer.WriteLineAsync("250 ok");
            }
            else if (upper == "DATA")
            {
                await writer.WriteLineAsync("354 go ahead");
                var data = new StringBuilder();
                while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                {
                    data.AppendLine(dataLine);
                }

                lock (Data) Data.Add(data.ToString());
                await writer.WriteLineAsync("250 queued");
            }
            else if (upper == "QUIT")
            {
                await writer.WriteLineAsync("221 bye");
                return;
            }
            else
            {
                await writer.WriteLineAsync("250 ok");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        try
        {
            await _loop;
        }
        catch (Exception)
        {
        }

        _stop.Dispose();
    }
}
