using System.Text;
using System.Threading.Channels;

namespace ConsoleWebStarter;


public sealed class CommandRunner
{
    private int running;


    public ChannelReader<string>? TryRun(
        string command,
        string menu,
        string food,
        CancellationToken cancellationToken)
    {
        if (command != "run")
        {
            throw new ArgumentException(
                "Unknown command.",
                nameof(command));
        }


        if (Interlocked.CompareExchange(
                ref running,
                1,
                0) != 0)
        {
            return null;
        }


        var channel =
            Channel.CreateBounded<string>(
                new BoundedChannelOptions(256)
                {
                    FullMode =
                        BoundedChannelFullMode.Wait,

                    SingleReader =
                        true,

                    SingleWriter =
                        true
                });


        _ = Task.Run(
            async () =>
            {
                RoutedConsoleWriter.Target.Value =
                    new ChannelConsoleWriter(
                        channel.Writer,
                        cancellationToken);


                try
                {
                    Console.WriteLine(
                        $"> {command}");

                    Console.WriteLine(
                        $"Menu: {menu}");

                    Console.WriteLine(
                        $"Food: {food}");

                    Console.WriteLine(
                        $"Started at " +
                        $"{DateTimeOffset.UtcNow:O}");


                    await RunAsync(
                        menu,
                        food,
                        cancellationToken);


                    Console.WriteLine(
                        "Command completed successfully.");
                }
                catch (OperationCanceledException)
                    when (
                        cancellationToken
                            .IsCancellationRequested)
                {
                    // Request/browser cancelled.
                }
                catch (Exception exception)
                {
                    if (!cancellationToken
                            .IsCancellationRequested)
                    {
                        await channel.Writer
                            .WriteAsync(
                                $"ERROR: " +
                                $"{exception.Message}\n",
                                CancellationToken.None);
                    }
                }
                finally
                {
                    RoutedConsoleWriter.Target.Value =
                        null;


                    Interlocked.Exchange(
                        ref running,
                        0);


                    channel.Writer
                        .TryComplete();
                }
            },
            CancellationToken.None);


        return channel.Reader;
    }


    private static async Task RunAsync(
        string menu,
        string food,
        CancellationToken cancellationToken)
    {
        cancellationToken
            .ThrowIfCancellationRequested();


        await RegisterFlow
            .MockRegisterFlow
            .Run(
                menu,
                food);
    }
}


internal sealed class ChannelConsoleWriter(
    ChannelWriter<string> writer,
    CancellationToken cancellationToken)
    : TextWriter
{
    public override Encoding Encoding =>
        Encoding.UTF8;


    public override void Write(
        char value)
    {
        Write(
            value.ToString());
    }


    public override void Write(
        string? value)
    {
        if (value is null)
        {
            return;
        }


        writer.WriteAsync(
                value,
                cancellationToken)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }


    public override void WriteLine(
        string? value)
    {
        Write(
            value + NewLine);
    }
}


internal sealed class RoutedConsoleWriter(
    TextWriter fallback)
    : TextWriter
{
    internal static readonly
        AsyncLocal<TextWriter?>
        Target =
            new();


    public override Encoding Encoding =>
        Encoding.UTF8;


    public override void Write(
        char value)
    {
        (Target.Value ?? fallback)
            .Write(value);
    }


    public override void Write(
        string? value)
    {
        (Target.Value ?? fallback)
            .Write(value);
    }


    public override void WriteLine(
        string? value)
    {
        (Target.Value ?? fallback)
            .WriteLine(value);
    }


    public override void Flush()
    {
        (Target.Value ?? fallback)
            .Flush();
    }
}