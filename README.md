# Console Web Starter

.NET 10 ASP.NET Core app with a small HTML/JavaScript frontend. No external NuGet packages required.

## Run locally

Install the .NET 10 SDK, then from this folder:

```sh
dotnet run --urls http://localhost:8080
```

Open http://localhost:8080 and click **Run**. The browser sends the fixed command `run` to the C# dispatcher; it does not execute a shell command or `dotnet run` on the server. Output streams to the page as the command executes.

## Add your code

- `Program.cs`: web server and streaming endpoint (top-level statements generate the Program class).
- `GuerillaMail.cs`: class `GuerillaMail`, with a placeholder `RunAsync` method.
- `models.cs`: class `models`, named exactly as requested.
- `OrderingFile.cs`: class `OrderingFile`, with a placeholder `RunAsync` method.
- `CommandRunner.cs`: handles `run`, calls the placeholder classes and captures `Console.WriteLine` / `Console.Error.WriteLine` from that async execution.
- `wwwroot/index.html`: Run button, status and live output.

Replace the two placeholder method bodies with your own logic. Replace `Console.ReadLine()` with web form inputs: this starter has no interactive stdin. Await all child tasks and honor the cancellation token. Disconnecting cancels the current command cooperatively. One command is allowed at a time per app process; concurrent requests receive HTTP 409. Exceptions appear as `ERROR:` in the output. History is not persisted, and browser output is capped at 200,000 characters. Output from separate child processes is not automatically captured.

## Docker on your VPS

Copy this entire folder to the VPS, then run:

```sh
docker compose up -d --build
```

Open http://YOUR_VPS_IP:8080. Port 8080 must be accessible. Store persistent files under `/app/data`, which is backed by a named volume.

## Dockhand

For a stack created by pasting YAML, use `compose.dockhand.yml`. First publish the app image to a registry, for example from this folder:

```sh
docker login ghcr.io
docker buildx build --platform linux/amd64 -t ghcr.io/YOUR_USERNAME/console-web-starter:1.0.0 --push .
```

Use `linux/arm64` instead if your VPS is ARM. Create the Dockhand stack, paste `compose.dockhand.yml`, set its `APP_IMAGE` environment variable to your published image name and deploy. Configure registry credentials in Dockhand if the image is private. A pasted YAML file alone does not contain the C# source or build context.

This starter has no login. Before exposing application controls publicly, configure authentication and HTTPS through your preferred reverse proxy or private access system. When using a reverse proxy, disable buffering for the streaming endpoint. The app itself does not need access to the Docker socket.
