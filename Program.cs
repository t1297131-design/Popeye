using ConsoleWebStarter;

var builder =
    WebApplication.CreateBuilder(args);


// One CommandRunner per HTTP request.
// This allows separate requests to run commands concurrently.
builder.Services
    .AddScoped<CommandRunner>();

var app =
    builder.Build();


// Route Console output from commands to the browser
// while preserving normal console output.
Console.SetOut(
    new RoutedConsoleWriter(Console.Out));

Console.SetError(
    new RoutedConsoleWriter(Console.Error));


app.UseDefaultFiles();

app.UseStaticFiles();


app.MapGet(
    "/health",
    () => Results.Ok(
        new
        {
            status = "ok"
        }));


app.MapPost(
    "/api/commands/run",
    async (
        HttpContext context,
        CommandRunner runner) =>
    {
        var body =
            await context.Request
                .ReadFromJsonAsync<
                    Dictionary<string, string>>(
                    context.RequestAborted);


        if (body is null ||
            !body.TryGetValue(
                "menu",
                out var menu) ||
            !body.TryGetValue(
                "food",
                out var food))
        {
            context.Response.StatusCode =
                StatusCodes.Status400BadRequest;

            await context.Response.WriteAsync(
                "Menu and food are required.",
                context.RequestAborted);

            return;
        }


        if (menu is not (
                "Order Brekkie" or
                "Order Lunch" or
                "Order Now"))
        {
            context.Response.StatusCode =
                StatusCodes.Status400BadRequest;

            await context.Response.WriteAsync(
                "Invalid menu selection.",
                context.RequestAborted);

            return;
        }


        if (food is not (
                "Biscuit" or
                "Chicken Cruncher" or
                "Regular Fries"))
        {
            context.Response.StatusCode =
                StatusCodes.Status400BadRequest;

            await context.Response.WriteAsync(
                "Invalid food selection.",
                context.RequestAborted);

            return;
        }


        using var cancellation =
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    context.RequestAborted);


        // Start the command on a ThreadPool thread.
        // Because CommandRunner is scoped, another request
        // receives its own CommandRunner and can execute
        // at the same time.
        var commandOutput =
            await Task.Run(
                () => runner.TryRun(
                    "run",
                    menu,
                    food,
                    cancellation.Token),
                cancellation.Token);


        if (commandOutput is null)
        {
            context.Response.StatusCode =
                StatusCodes.Status409Conflict;

            await context.Response.WriteAsync(
                "Unable to start command.",
                context.RequestAborted);

            return;
        }


        context.Response.StatusCode =
            StatusCodes.Status200OK;

        context.Response.ContentType =
            "text/plain; charset=utf-8";

        context.Response.Headers.CacheControl =
            "no-cache, no-transform";

        context.Response.Headers[
            "X-Accel-Buffering"] =
            "no";


        try
        {
            await foreach (
                var text in
                commandOutput.ReadAllAsync(
                    context.RequestAborted))
            {
                await context.Response
                    .WriteAsync(
                        text,
                        context.RequestAborted);

                await context.Response.Body
                    .FlushAsync(
                        context.RequestAborted);
            }
        }
        catch (OperationCanceledException)
            when (
                context.RequestAborted
                    .IsCancellationRequested)
        {
            // Browser disconnected.
        }
        finally
        {
            await cancellation
                .CancelAsync();
        }
    });


app.MapGet(
    "/order/{orderNumber}",
    (string orderNumber) =>
    {
        var encodedOrderNumber =
            Uri.EscapeDataString(
                orderNumber);

        return Results.Redirect(
            $"/order-status.html?order={encodedOrderNumber}");
    });


app.Run();